using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

/// <summary>
/// Manages Plus-specific settings in a separate file so they don't get
/// overwritten when the upstream VRCVideoCacher opens the shared Config.json.
/// </summary>
public class PlusConfigManager
{
    public static PlusConfigModel Config { get; private set; }
    private static readonly ILogger Log = Program.Logger.ForContext<PlusConfigManager>();
    private static readonly string ConfigFilePath;

    public static event Action? OnConfigChanged;

    // Dropbox share links always carry a dl=0 parameter, but not always as the only one:
    // the current /scl/fi/ format is "?rlkey=...&st=...&dl=0". Flipping that one parameter
    // to dl=1 is what turns the HTML preview page into the actual file, and rlkey must be
    // preserved, so the whole query can't just be replaced.
    private const string DropboxForceDownloadPattern =
        @"^(https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/[^#]*[?&])dl=0(&[^#]*)?$";
    private const string DropboxForceDownloadTarget = "${1}dl=1${2}";

    // Separate rule for a link that has been trimmed down to a bare path: there is no
    // parameter to flip, so dl=1 has to be appended as a new query string. Deliberately
    // does not match a URL that already has a query — one that carries dl=1 or raw=1 is
    // already a direct link, and one that carries neither is left alone rather than guessed at.
    private const string DropboxAppendDownloadPattern =
        @"^(https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/[^?#]+)$";
    private const string DropboxAppendDownloadTarget = "${1}?dl=1";

    // Shipped in earlier versions and wrong: the lazy (.*?) had to expand past the whole
    // query before the anchor could match, so the optional (?:\?dl=0)? never participated
    // for any link with more than one parameter. The target then appended a second "?",
    // producing "...&dl=0?dl=1". Existing installs carry their own copy of the rule list,
    // so the corrected default only reaches them via the migration below.
    private const string LegacyDropboxPattern =
        @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/(.*?)(?:\?dl=0)?$";

    static PlusConfigManager()
    {
        ConfigFilePath = Path.Join(Program.DataPath, "PlusConfig.json");
        Log.Information("Loading Plus config from {Path}...", ConfigFilePath);

        PlusConfigModel? loaded = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                loaded = JsonConvert.DeserializeObject<PlusConfigModel>(File.ReadAllText(ConfigFilePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load Plus config, creating new one...");
        }

        if (loaded != null)
        {
            Config = loaded;
            Log.Information("Plus config loaded successfully.");
        }
        else
        {
            Log.Information("No Plus config found, creating new one...");
            Config = new PlusConfigModel();
            MigrateFromMainConfig();
        }

        MigrateBrokenDefaultRules();
        EnsureDefaultRules();

        TrySaveConfig();
    }

    /// <summary>
    /// Repairs default rules that shipped with a broken pattern. Runs before
    /// <see cref="EnsureDefaultRules"/> so the repaired rule is recognised as already
    /// present and isn't duplicated by the corrected default.
    /// </summary>
    private static void MigrateBrokenDefaultRules()
    {
        if (Config.UriRules == null)
            return;

        foreach (var rule in Config.UriRules)
        {
            if (rule.Pattern != LegacyDropboxPattern)
                continue;

            Log.Information("Repairing broken default rule '{RuleName}' (Dropbox share rewrite).", rule.Name);
            rule.Pattern = DropboxForceDownloadPattern;
            rule.RedirectTarget = DropboxForceDownloadTarget;
        }
    }

    public static void EnsureDefaultRules()
    {
        if (Config.UriRules == null || Config.UriRules.Count == 0)
        {
            Config.UriRules = ConfigModel.GetDefaultRules();
        }
        else
        {
            var defaultRules = ConfigModel.GetDefaultRules();
            foreach (var defRule in defaultRules)
            {
                if (!Config.UriRules.Any(r => r.Name == defRule.Name || r.Pattern == defRule.Pattern))
                {
                    var lastIndex = Config.UriRules.FindIndex(r => r.Name == "Everything else");
                    if (lastIndex >= 0)
                        Config.UriRules.Insert(lastIndex, defRule);
                    else
                        Config.UriRules.Add(defRule);
                }
            }

            Config.UriRules = Config.UriRules.DistinctBy(r => r.Name + "|" + r.Pattern).ToList();
        }
    }

    /// <summary>
    /// On first run, pull any existing Plus-specific values from the main Config.json
    /// so the user doesn't lose their settings.
    /// </summary>
    private static void MigrateFromMainConfig()
    {
        var configPath = Path.Join(Program.DataPath, "Config.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(configPath));
            if (json == null)
                return;

            if (json.TryGetValue("CacheDownloadRateLimitMBs", out var rate))
                Config.CacheDownloadRateLimitMBs = Convert.ToInt32(rate);
            if (json.TryGetValue("CacheDownloadIdleSeconds", out var idle))
                Config.CacheDownloadIdleSeconds = Convert.ToInt32(idle);
            if (json.TryGetValue("CacheYouTubePreferVp9", out var vp9))
                Config.CacheYouTubePreferVp9 = Convert.ToBoolean(vp9);
            if (json.TryGetValue("UriRules", out var rulesObj) && rulesObj != null)
            {
                var rulesJson = rulesObj.ToString();
                if (!string.IsNullOrWhiteSpace(rulesJson))
                {
                    var migratedRules = JsonConvert.DeserializeObject<List<UriRule>>(rulesJson);
                    if (migratedRules != null && migratedRules.Count > 0)
                        Config.UriRules = migratedRules;
                }
            }
            Log.Information("Migrated Plus settings from main Config.json.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate Plus settings from main Config.json, using defaults.");
        }
    }

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Plus config changed, saving...");
        File.WriteAllText(ConfigFilePath, newConfig);
        Log.Information("Plus config saved.");
        OnConfigChanged?.Invoke();
    }
    public static List<UriRule> GetDefaultRules()
    {
        return
        [
            new UriRule
            {
                Name = "VRDancing EU to NA Redirect",
                Pattern = @"^https?:\/\/eu2\.vrdancing\.club\/weekend\/(.*)$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://na2.vrdancing.club/weekend/$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "YouTube Music Redirect",
                Pattern = @"^https?:\/\/music\.youtube\.com\/(?:watch|playlist)?\?(?:.*?&)?v=([^&]+).*$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://youtube.com/watch?v=$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "Dropbox Share Rewrite",
                Pattern = DropboxForceDownloadPattern,
                Action = RuleAction.Rewrite,
                RedirectTarget = DropboxForceDownloadTarget,
                Enabled = true
            },
            new UriRule
            {
                Name = "Dropbox Direct Download",
                Pattern = DropboxAppendDownloadPattern,
                Action = RuleAction.Rewrite,
                RedirectTarget = DropboxAppendDownloadTarget,
                Enabled = true
            },
            new UriRule
            {
                Name = "Google Drive File Rewrite",
                Pattern = @"^https?:\/\/drive\.google\.com\/file\/d\/([^\/]+)(?:\/.*)?$",
                Action = RuleAction.Rewrite,
                RedirectTarget = "https://drive.google.com/uc?export=download&id=$1",
                Enabled = true
            },
            new UriRule
            {
                Name = "MightyGym CDN Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*mightygymcdn\.nyc3\.cdn\.digitaloceanspaces\.com(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Illumination Media Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:imvrcdn\.com|illumination\.media)(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Virtual Film Institute Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*virtualfilm\.institute(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Block Rickrolls",
                Pattern = @"^https?://(?:www\.)?youtube\.com/watch\?v=(?:dQw4w9WgXcQ|jzmz6K8K4L0|XfELJU1mRMg)",
                Action = RuleAction.Block,
                Enabled = true
            },
            new UriRule
            {
                Name = "YouTube",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                MaxResolution = 1080,
                MaxDurationMinutes = 120,
                Enabled = true,
                Integration = "YouTube"
            },
            new UriRule
            {
                Name = "PyPyDance",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*pypy\.dance(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                Enabled = true,
                Integration = "PyPyDance"
            },
            new UriRule
            {
                Name = "VRDancing",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*vrdancing\.club(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                Enabled = true,
                Integration = "VRDancing"
            },
            new UriRule
            {
                Name = "Everything else",
                Pattern = @".*",
                Action = RuleAction.Resolve,
                Cache = false,
                Enabled = true
            }
        ];
    }
}

public class PlusConfigModel
{
    public int CacheDownloadRateLimitMBs { get; set; } // 0 = unlimited
    public int CacheDownloadIdleSeconds { get; set; } = 30; // 0 = disabled
    public bool CacheYouTubePreferVp9 { get; set; } = true; // VP9+aac in mp4 instead of h264+aac
    public List<UriRule> UriRules { get; set; } = PlusConfigManager.GetDefaultRules();
}
