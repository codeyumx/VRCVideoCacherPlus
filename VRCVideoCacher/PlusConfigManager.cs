using System.Text.Json;
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

    static PlusConfigManager()
    {
        ConfigFilePath = Path.Join(Program.DataPath, "PlusConfig.json");
        Log.Information("Loading Plus config from {Path}...", ConfigFilePath);

        PlusConfigModel? loaded = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                loaded = Utils.Json.Deserialize<PlusConfigModel>(File.ReadAllText(ConfigFilePath));
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
            if (rule.Pattern != DefaultRules.LegacyDropboxPattern)
                continue;

            Log.Information("Repairing broken default rule '{RuleName}' (Dropbox share rewrite).", rule.Name);
            rule.Pattern = DefaultRules.DropboxForceDownloadPattern;
            rule.RedirectTarget = DefaultRules.DropboxForceDownloadTarget;
        }
    }

    // The catch-all rule stays last; new defaults are inserted above it.
    private const string CatchAllRuleName = "Everything else";

    /// <summary>
    /// Seeds default rules that this installation has not been offered before.
    ///
    /// The previous version re-added any default that was missing, every single launch —
    /// so deleting a default rule was impossible: it silently reappeared on the next start.
    /// Each default is now recorded once in SeededDefaultRules, and after that the user's
    /// decision to remove it sticks.
    /// </summary>
    public static void EnsureDefaultRules()
    {
        var defaults = ConfigModel.GetDefaultRules();

        if (Config.UriRules == null || Config.UriRules.Count == 0)
        {
            Config.UriRules = defaults;
            Config.SeededDefaultRules = defaults.Select(rule => rule.Name).ToList();
            return;
        }

        // Upgrading from a version with no seed tracking: everything the user already has
        // has evidently been seeded. Anything missing is either a rule they deleted or a
        // genuinely new default; both get offered exactly once here, and are then recorded.
        if (Config.SeededDefaultRules.Count == 0)
        {
            Config.SeededDefaultRules = defaults
                .Where(d => Config.UriRules.Any(r => r.Name == d.Name || r.Pattern == d.Pattern))
                .Select(d => d.Name)
                .ToList();
        }

        foreach (var defRule in defaults)
        {
            if (Config.SeededDefaultRules.Contains(defRule.Name))
                continue;

            if (Config.UriRules.Any(r => r.Name == defRule.Name || r.Pattern == defRule.Pattern))
            {
                Config.SeededDefaultRules.Add(defRule.Name);
                continue;
            }

            var catchAllIndex = Config.UriRules.FindIndex(r => r.Name == CatchAllRuleName);
            if (catchAllIndex >= 0)
                Config.UriRules.Insert(catchAllIndex, defRule);
            else
                Config.UriRules.Add(defRule);

            Config.SeededDefaultRules.Add(defRule.Name);
            Log.Information("Added new default rule '{RuleName}'.", defRule.Name);
        }

        // Defensive: an earlier version could insert the same rule more than once. Keyed on
        // a tuple rather than the old "Name + \"|\" + Pattern" string, which could collide
        // across differently-split name/pattern pairs.
        Config.UriRules = Config.UriRules.DistinctBy(r => (r.Name, r.Pattern)).ToList();
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
            // Read as a document rather than into a model: these keys no longer exist on
            // ConfigModel, so there is nothing to deserialize into. (This was
            // Dictionary<string, object>, which under Newtonsoft yielded boxed primitives
            // that Convert.ToInt32 accepted. System.Text.Json yields JsonElement instead,
            // which Convert would throw on, so each value is read through its own accessor.)
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (root.TryGetProperty("CacheDownloadRateLimitMBs", out var rate) && rate.TryGetInt32(out var rateValue))
                Config.CacheDownloadRateLimitMBs = rateValue;

            if (root.TryGetProperty("CacheDownloadIdleSeconds", out var idle) && idle.TryGetInt32(out var idleValue))
                Config.CacheDownloadIdleSeconds = idleValue;

            if (root.TryGetProperty("CacheYouTubePreferVp9", out var vp9) &&
                vp9.ValueKind is JsonValueKind.True or JsonValueKind.False)
                Config.CacheYouTubePreferVp9 = vp9.GetBoolean();

            if (root.TryGetProperty("UriRules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            {
                var migratedRules = Utils.Json.Deserialize<List<UriRule>>(rules.GetRawText());
                if (migratedRules is { Count: > 0 })
                    Config.UriRules = migratedRules;
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
        var newConfig = Utils.Json.Serialize(Config);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Plus config changed, saving...");
        Utils.AtomicFile.WriteAllText(ConfigFilePath, newConfig);
        Log.Information("Plus config saved.");
        OnConfigChanged?.Invoke();
    }
    public static List<UriRule> GetDefaultRules() => DefaultRules.Create();
}

public class PlusConfigModel
{
    public int CacheDownloadRateLimitMBs { get; set; } // 0 = unlimited
    public int CacheDownloadIdleSeconds { get; set; } = 30; // 0 = disabled
    public bool CacheYouTubePreferVp9 { get; set; } = true; // VP9+aac in mp4 instead of h264+aac
    public List<UriRule> UriRules { get; set; } = DefaultRules.Create();

    /// <summary>
    /// Names of the default rules this installation has already been offered. A default is
    /// seeded once; once it is in here, deleting the rule keeps it deleted instead of
    /// having it reappear on the next launch.
    /// </summary>
    public List<string> SeededDefaultRules { get; set; } = [];
}
