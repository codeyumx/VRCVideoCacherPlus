using System.Text.Json;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

/// <summary>
/// The PlusPlus-only settings, and the rule seeding that goes with them.
///
/// These used to live nested under the "Plus" key of the shared Config.json or in a separate file.
/// They are now flat top-level fields inside the main ConfigModel.
/// </summary>
public static class PlusConfigManager
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(PlusConfigManager));

    private static readonly string LegacyConfigFilePath = Path.Join(Program.DataPath, "PlusConfig.json");

    public static ConfigModel Config => ConfigManager.Config;

    /// <summary>Saves config via ConfigManager.</summary>
    public static void TrySaveConfig() => ConfigManager.TrySaveConfig();

    public static List<UriRule> GetDefaultRules() => DefaultRules.Create();

    /// <summary>
    /// Runs once from ConfigManager's initialiser, after the file is loaded and before it
    /// is saved back.
    /// </summary>
    internal static void Initialize(ConfigModel config, string rawJsonText)
    {
        // TODO: Remove later - Migrating legacy nested Plus block from Config.json
        if (!string.IsNullOrEmpty(rawJsonText))
        {
            try
            {
                using var document = JsonDocument.Parse(rawJsonText);
                var root = document.RootElement;
                if (root.TryGetProperty("Plus", out var plusElement) && plusElement.ValueKind == JsonValueKind.Object)
                {
                    if (plusElement.TryGetProperty("CacheDownloadRateLimitMBs", out var rate) && rate.TryGetInt32(out var rateValue))
                        config.CacheDownloadRateLimitMBs = rateValue;

                    if (plusElement.TryGetProperty("CacheDownloadIdleSeconds", out var idle) && idle.TryGetInt32(out var idleValue))
                        config.CacheDownloadIdleSeconds = idleValue;

                    if (plusElement.TryGetProperty("CacheYouTubePreferVp9", out var vp9) && vp9.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        config.CacheYouTubePreferVp9 = vp9.GetBoolean();

                    if (plusElement.TryGetProperty("UriRules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                    {
                        var migratedRules = Utils.Json.Deserialize<List<UriRule>>(rules.GetRawText());
                        if (migratedRules is { Count: > 0 })
                            config.UriRules = migratedRules;
                    }

                    if (plusElement.TryGetProperty("SeededDefaultRules", out var seeded) && seeded.ValueKind == JsonValueKind.Array)
                    {
                        var migratedSeeded = Utils.Json.Deserialize<List<string>>(seeded.GetRawText());
                        if (migratedSeeded is { Count: > 0 })
                            config.SeededDefaultRules = migratedSeeded;
                    }

                    Log.Information("Migrated settings from the legacy nested Plus block in Config.json.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to migrate settings from legacy nested Plus block.");
            }
        }

        // TODO: Remove later - Migrating legacy separate PlusConfig.json file
        MigrateFromLegacyPlusConfigFile(config);

        MigrateBrokenDefaultRules(config);
        EnsureDefaultRules(config);
    }

    // TODO: Remove later - Migrating legacy separate PlusConfig.json file
    private static void MigrateFromLegacyPlusConfigFile(ConfigModel config)
    {
        if (!File.Exists(LegacyConfigFilePath))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(LegacyConfigFilePath));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("CacheDownloadRateLimitMBs", out var rate) && rate.TryGetInt32(out var rateValue))
                    config.CacheDownloadRateLimitMBs = rateValue;

                if (root.TryGetProperty("CacheDownloadIdleSeconds", out var idle) && idle.TryGetInt32(out var idleValue))
                    config.CacheDownloadIdleSeconds = idleValue;

                if (root.TryGetProperty("CacheYouTubePreferVp9", out var vp9) && vp9.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    config.CacheYouTubePreferVp9 = vp9.GetBoolean();

                if (root.TryGetProperty("UriRules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                {
                    var migratedRules = Utils.Json.Deserialize<List<UriRule>>(rules.GetRawText());
                    if (migratedRules is { Count: > 0 })
                        config.UriRules = migratedRules;
                }

                if (root.TryGetProperty("SeededDefaultRules", out var seeded) && seeded.ValueKind == JsonValueKind.Array)
                {
                    var migratedSeeded = Utils.Json.Deserialize<List<string>>(seeded.GetRawText());
                    if (migratedSeeded is { Count: > 0 })
                        config.SeededDefaultRules = migratedSeeded;
                }

                Log.Information("Merged legacy PlusConfig.json into the shared config.");
            }

            var backupPath = LegacyConfigFilePath + ".bak";
            File.Copy(LegacyConfigFilePath, backupPath, overwrite: true);
            File.Delete(LegacyConfigFilePath);
            Log.Information("Kept a copy of the previous settings at {Backup}.", backupPath);
        }
        catch (Exception ex)
        {
            // Leaving the file in place is the safe failure: the merge is retried next
            // launch rather than the settings being lost.
            Log.Error(ex, "Failed to migrate PlusConfig.json; it has been left in place.");
        }
    }

    /// <summary>
    /// Repairs default rules that shipped with a broken pattern.
    /// </summary>
    private static void MigrateBrokenDefaultRules(ConfigModel config)
    {
        if (config.UriRules == null)
            return;

        foreach (var rule in config.UriRules)
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

    /// <summary>
    /// Returns true if all PlusPlus settings are unmodified defaults.
    /// </summary>
    public static bool IsDefault(ConfigModel config)
    {
        if (config.CacheDownloadRateLimitMBs != 0) return false;
        if (config.CacheDownloadIdleSeconds != 30) return false;
        if (config.CacheYouTubePreferVp9 != true) return false;

        var defaults = DefaultRules.Create();
        if (config.UriRules == null || config.UriRules.Count != defaults.Count) return false;
        for (int i = 0; i < defaults.Count; i++)
        {
            var r = config.UriRules[i];
            var d = defaults[i];
            if (r.Name != d.Name || r.Pattern != d.Pattern || r.RedirectTarget != d.RedirectTarget || r.Action != d.Action || r.Enabled != d.Enabled)
                return false;
        }

        return true;
    }
}
