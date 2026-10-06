using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher;

/// <summary>
/// The Plus-only settings, and the rule seeding that goes with them.
///
/// These live as flat top-level fields inside the main ConfigModel.
/// </summary>
public static class PlusConfigManager
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(PlusConfigManager));

    public static ConfigModel Config => ConfigManager.Config;

    /// <summary>Saves config via ConfigManager.</summary>
    public static void TrySaveConfig() => ConfigManager.TrySaveConfig();

    public static List<UriRule> GetDefaultRules() => DefaultRules.Create();

    /// <summary>
    /// Runs once from ConfigManager's initialiser, after the file is loaded and before it
    /// is saved back.
    /// </summary>
    internal static void Initialize(ConfigModel config)
    {
        // TODO: Remove later - one-time move of the settings builds up to 2026.8.14 kept in PlusConfig.json.
        MigratePlusConfigFile(config);
        // TODO: Remove later - one-time migration of the settings the rule engine replaced.
        MigrateLegacyRuleSettings(config);
        // TODO: Remove later - Migrating/repairing broken Dropbox rule pattern for existing users
        MigrateBrokenDefaultRules(config);
        EnsureDefaultRules(config);
    }

    /// <summary>
    /// Builds up to 2026.8.14 kept the three Plus settings in their own PlusConfig.json. They now
    /// live in Config.json, so the file is read once and renamed to PlusConfig.json.bak: left
    /// under its own name it would be re-read every launch and overwrite later changes. A
    /// Config.json that already carries the keys was written by a build that owns them, so the
    /// file is stale and is only set aside.
    /// </summary>
    private static void MigratePlusConfigFile(ConfigModel config)
    {
        var path = Path.Join(Program.DataPath, "PlusConfig.json");
        if (!File.Exists(path))
            return;

        try
        {
            var configPath = Path.Join(Program.DataPath, "Config.json");
            var configOwnsTheKeys = File.Exists(configPath) &&
                                    File.ReadAllText(configPath).Contains("\"CacheDownloadRateLimitMBs\"");
            if (!configOwnsTheKeys)
                ApplyPlusConfigFile(File.ReadAllText(path), config);

            File.Move(path, path + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not carry the settings in PlusConfig.json over to Config.json.");
        }
    }

    internal static void ApplyPlusConfigFile(string? json, ConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (TryInt(root, "CacheDownloadRateLimitMBs", out var rate))
            config.CacheDownloadRateLimitMBs = rate;
        if (TryInt(root, "CacheDownloadIdleSeconds", out var idle))
            config.CacheDownloadIdleSeconds = idle;
        if (TryBool(root, "CacheYouTubePreferVp9", out var vp9))
            config.CacheYouTubePreferVp9 = vp9;
    }

    /// <summary>
    /// TODO: Remove later - Reads the pre-rules settings out of Config.json and turns them into
    /// rules.
    ///
    /// The fields the rule engine replaced were deleted from ConfigModel in the same change, so
    /// their JSON keys are ignored on load and erased by the first save. Without this an upgrade
    /// drops the user's BlockedUrls, and a cache opt-out is inverted into the seeded rule's
    /// Cache = true.
    /// </summary>
    private static void MigrateLegacyRuleSettings(ConfigModel config)
    {
        var configPath = Path.Join(Program.DataPath, "Config.json");
        if (!File.Exists(configPath))
            return;

        ApplyLegacyRuleSettings(File.ReadAllText(configPath), config);
    }

    /// <summary>
    /// Turns the settings a pre-rules Config.json carries into rules. Read as raw JSON rather than
    /// through ConfigModel: these keys are not on the model any more, which is the whole problem.
    /// </summary>
    internal static void ApplyLegacyRuleSettings(string? configJson, ConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Could not read the pre-rules settings from Config.json.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            var migrated = 0;

            if (FindRule(config, "YouTube") is { } youTube)
            {
                if (TryInt(root, "CacheYouTubeMaxResolution", out var resolution))
                    youTube.MaxResolution = resolution;
                if (TryInt(root, "CacheYouTubeMaxLength", out var length))
                    youTube.MaxDurationMinutes = length;
                if (TryBool(root, "CacheYouTube", out var cacheYouTube))
                    youTube.Cache = cacheYouTube;
                migrated++;
            }

            if (FindRule(config, "PyPyDance") is { } pyPyDance && TryBool(root, "CachePyPyDance", out var cachePyPyDance))
            {
                pyPyDance.Cache = cachePyPyDance;
                migrated++;
            }

            if (FindRule(config, "VRDancing") is { } vrDancing && TryBool(root, "CacheVrDancing", out var cacheVrDancing))
            {
                vrDancing.Cache = cacheVrDancing;
                migrated++;
            }

            if (FindRule(config, "VRDancing EU to NA Redirect") is { } vrRedirect && TryBool(root, "RedirectVRDancing", out var redirectVrDancing))
            {
                vrRedirect.Enabled = redirectVrDancing;
                migrated++;
            }

            TryString(root, "BlockRedirect", out var blockRedirect);
            var migratedBlockRules = 0;
            if (root.TryGetProperty("BlockedUrls", out var blockedUrls) && blockedUrls.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in blockedUrls.EnumerateArray())
                {
                    var url = entry.GetString();
                    if (string.IsNullOrWhiteSpace(url) || url == LegacySampleBlockedUrl)
                        continue;

                    var pattern = "^" + Regex.Escape(url);
                    if (config.UriRules.Any(r => r.Pattern == pattern))
                        continue;

                    // Pre-rules builds checked BlockedUrls before any site handling, then replaced a
                    // blocked URL with BlockRedirect and resolved that as usual. So the rule goes to
                    // the top, and a configured redirect is a Rewrite: evaluation carries on with the
                    // new URL instead of handing the player an unresolved page. Without a redirect
                    // the request is refused outright, which is what Block means in the engine.
                    var rule = new UriRule
                    {
                        Name = $"Block {url}",
                        Pattern = pattern,
                        Enabled = true,
                        Action = string.IsNullOrWhiteSpace(blockRedirect) ? RuleAction.Block : RuleAction.Rewrite,
                        RedirectTarget = blockRedirect ?? string.Empty
                    };

                    config.UriRules.Insert(migratedBlockRules++, rule);

                    migrated++;
                    Log.Information("Migrated blocked URL '{Url}' into a {Action} rule.", url, rule.Action);
                }
            }

            if (migrated > 0)
                Log.Information("Migrated {Count} pre-rules setting(s) into rules.", migrated);
        }
    }

    private static bool TryBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = element.GetBoolean();
        return true;
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
            return false;
        return true;
    }

    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString();
        return true;
    }

    // The one entry every pre-rules Config.json ships with; blocking it would be noise.
    private const string LegacySampleBlockedUrl = "https://na2.vrdancing.club/sampleurl.mp4";

    private static UriRule? FindRule(ConfigModel config, string name) =>
        config.UriRules.FirstOrDefault(r => r.Name == name);

    /// <summary>
    /// TODO: Remove later - Repairs default rules that shipped with a broken pattern.
    /// </summary>
    private static void MigrateBrokenDefaultRules(ConfigModel config)
    {
        if (config.UriRules == null)
            return;

        foreach (var rule in config.UriRules)
        {
            if (rule.Pattern == DefaultRules.LegacyIlluminationPattern)
            {
                Log.Information("Repairing default rule '{RuleName}' (yt.illumination.media must resolve).", rule.Name);
                rule.Pattern = DefaultRules.IlluminationPattern;
                continue;
            }

            if (rule.Pattern != DefaultRules.LegacyDropboxPattern)
                continue;

            Log.Information("Repairing broken default rule '{RuleName}' (Dropbox share rewrite).", rule.Name);
            rule.Pattern = DefaultRules.DropboxForceDownloadPattern;
            rule.RedirectTarget = DefaultRules.DropboxForceDownloadTarget;
        }
    }

    // The catch-all rule stays last; new defaults are inserted above it.
    internal const string CatchAllRuleName = "Everything else";

    public static void EnsureDefaultRules() => EnsureDefaultRules(Config);

    /// <summary>
    /// Seeds default rules that this installation has not been offered before.
    /// </summary>
    private static void EnsureDefaultRules(ConfigModel config)
    {
        var defaults = DefaultRules.Create();

        if (config.UriRules == null || config.UriRules.Count == 0)
        {
            config.UriRules = defaults;
            config.SeededDefaultRules = defaults.Select(rule => rule.Name).ToList();
            return;
        }

        if (config.SeededDefaultRules == null)
            config.SeededDefaultRules = [];

        // Upgrading from a version with no seed tracking: everything the user already has
        // has evidently been seeded. Anything missing is either a rule they deleted or a
        // genuinely new default; both get offered exactly once here, and are then recorded.
        if (config.SeededDefaultRules.Count == 0)
        {
            config.SeededDefaultRules = defaults
                .Where(d => config.UriRules.Any(r => r.Name == d.Name || r.Pattern == d.Pattern))
                .Select(d => d.Name)
                .ToList();
        }

        foreach (var defRule in defaults)
        {
            if (config.SeededDefaultRules.Contains(defRule.Name))
                continue;

            if (config.UriRules.Any(r => r.Name == defRule.Name || r.Pattern == defRule.Pattern))
            {
                config.SeededDefaultRules.Add(defRule.Name);
                continue;
            }

            var catchAllIndex = config.UriRules.FindIndex(r => r.Name == CatchAllRuleName);
            if (catchAllIndex >= 0)
                config.UriRules.Insert(catchAllIndex, defRule);
            else
                config.UriRules.Add(defRule);

            config.SeededDefaultRules.Add(defRule.Name);
            Log.Information("Added new default rule '{RuleName}'.", defRule.Name);
        }

        // Defensive: an earlier version could insert the same rule more than once. Keyed on
        // a tuple rather than a "Name + \"|\" + Pattern" string, which could collide across
        // differently-split name/pattern pairs.
        config.UriRules = config.UriRules.DistinctBy(r => (r.Name, r.Pattern)).ToList();
    }
}
