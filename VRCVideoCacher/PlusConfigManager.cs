using System.Text.RegularExpressions;
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
            MigrateLegacyRuleSettings(json, Config);
            Log.Information("Migrated Plus settings from main Config.json.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate Plus settings from main Config.json, using defaults.");
        }
    }

    /// <summary>
    /// One-time migration of the per-site settings the rule engine replaced. Those fields were
    /// deleted from ConfigModel, so their JSON keys are ignored on load and erased on the next
    /// save: without this, a user's BlockedUrls list disappears, their cache opt-outs
    /// (CacheVrDancing / CachePyPyDance / CacheYouTube = false) are inverted into the seeded
    /// rules' Cache = true, and RedirectVRDancing silently stops redirecting.
    /// </summary>
    internal static void MigrateLegacyRuleSettings(Dictionary<string, object> json, PlusConfigModel config)
    {
        var migrated = 0;

        if (FindRule(config, "YouTube") is { } youTube)
        {
            if (TryRead(json, "CacheYouTubeMaxResolution", out var resolution))
                youTube.MaxResolution = Convert.ToInt32(resolution);
            if (TryRead(json, "CacheYouTubeMaxLength", out var length))
                youTube.MaxDurationMinutes = Convert.ToInt32(length);
            if (TryRead(json, "CacheYouTube", out var cacheYouTube))
                youTube.Cache = Convert.ToBoolean(cacheYouTube);
        }

        if (FindRule(config, "PyPyDance") is { } pyPyDance && TryRead(json, "CachePyPyDance", out var cachePyPyDance))
            pyPyDance.Cache = Convert.ToBoolean(cachePyPyDance);

        if (FindRule(config, "VRDancing") is { } vrDancing && TryRead(json, "CacheVrDancing", out var cacheVrDancing))
            vrDancing.Cache = Convert.ToBoolean(cacheVrDancing);

        if (FindRule(config, "VRDancing EU to NA Redirect") is { } vrRedirect && TryRead(json, "RedirectVRDancing", out var redirectVrDancing))
            vrRedirect.Enabled = Convert.ToBoolean(redirectVrDancing);

        TryRead(json, "BlockRedirect", out var blockRedirect);
        var redirectTarget = blockRedirect as string ?? string.Empty;

        if (TryRead(json, "BlockedUrls", out var blockedUrls) && blockedUrls is Newtonsoft.Json.Linq.JArray entries)
        {
            foreach (var entry in entries)
            {
                var url = entry?.ToString();
                if (string.IsNullOrWhiteSpace(url) || url == LegacySampleBlockedUrl)
                    continue;

                var pattern = "^" + Regex.Escape(url);
                if (config.UriRules.Any(r => r.Pattern == pattern))
                    continue;

                // Pre-rules builds swapped a blocked URL for BlockRedirect and played that, so a
                // configured redirect is preserved as a Redirect rule; without one the request is
                // refused outright, which is what Block means in the engine.
                var rule = new UriRule
                {
                    Name = $"Block {url}",
                    Pattern = pattern,
                    Enabled = true,
                    Action = string.IsNullOrWhiteSpace(redirectTarget) ? RuleAction.Block : RuleAction.Redirect,
                    RedirectTarget = redirectTarget
                };

                var catchAllIndex = config.UriRules.FindIndex(r => r.Name == CatchAllRuleName);
                if (catchAllIndex >= 0)
                    config.UriRules.Insert(catchAllIndex, rule);
                else
                    config.UriRules.Add(rule);

                migrated++;
                Log.Information("Migrated blocked URL '{Url}' into a {Action} rule.", url, rule.Action);
            }
        }

        if (migrated > 0)
            Log.Information("Migrated {Count} pre-rules setting(s) into rules.", migrated);
    }

    private static UriRule? FindRule(PlusConfigModel config, string name) => config.UriRules.FirstOrDefault(r => r.Name == name);

    private static bool TryRead(Dictionary<string, object> json, string key, out object? value) =>
        json.TryGetValue(key, out value) && value != null;

    // The one entry every pre-rules Config.json ships with; blocking it would be noise.
    private const string LegacySampleBlockedUrl = "https://na2.vrdancing.club/sampleurl.mp4";

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
