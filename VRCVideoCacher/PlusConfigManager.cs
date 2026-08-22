using System.Text.Json;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

/// <summary>
/// The PlusPlus-only settings, and the rule seeding that goes with them.
///
/// These used to live in their own PlusConfig.json, specifically so that upstream
/// VRCVideoCacher opening the shared Config.json could not overwrite them. That worked, but
/// it meant two config files, two save paths, two change events, and settings the user
/// could only find by knowing which file to open.
///
/// They now live under the "Plus" key of the shared Config.json. The upstream hazard is
/// real and unchanged — it reads that file into its own model and writes it back, dropping
/// what it does not recognise — so it is handled openly instead: the user is told once, and
/// the pre-merge settings are kept as a backup alongside.
///
/// Deliberately has no static constructor. It reads through to ConfigManager.Config, and a
/// static initialiser here would re-enter ConfigManager's while that is still running.
/// </summary>
public static class PlusConfigManager
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(PlusConfigManager));

    private static readonly string LegacyConfigFilePath = Path.Join(Program.DataPath, "PlusConfig.json");

    public static PlusConfigModel Config => ConfigManager.Config.Plus;

    /// <summary>One file now, so one save path.</summary>
    public static void TrySaveConfig() => ConfigManager.TrySaveConfig();

    public static List<UriRule> GetDefaultRules() => DefaultRules.Create();

    /// <summary>
    /// Runs once from ConfigManager's initialiser, after the file is loaded and before it
    /// is saved back.
    /// </summary>
    internal static void Initialize(ConfigModel config)
    {
        config.Plus ??= new PlusConfigModel();

        MigrateFromLegacyPlusConfigFile(config);
        MigrateFromLegacyTopLevelKeys(config);
        MigrateBrokenDefaultRules(config.Plus);
        EnsureDefaultRules(config.Plus);
    }

    /// <summary>
    /// Folds a PlusConfig.json from a previous version into the shared config, then takes it
    /// out of play.
    ///
    /// The original is copied to PlusConfig.json.bak and then removed. Removing it matters:
    /// left in place it would be re-read on every launch and would overwrite whatever the
    /// user had since changed. The .bak is never read again — it exists so the settings as
    /// they were at the moment of the merge remain recoverable by hand.
    /// </summary>
    private static void MigrateFromLegacyPlusConfigFile(ConfigModel config)
    {
        if (!File.Exists(LegacyConfigFilePath))
            return;

        try
        {
            var loaded = Utils.Json.Deserialize<PlusConfigModel>(File.ReadAllText(LegacyConfigFilePath));
            if (loaded != null)
            {
                config.Plus = loaded;
                Log.Information("Merged PlusConfig.json into the shared config.");
            }
            else
            {
                Log.Warning("PlusConfig.json could not be read; keeping current Plus settings.");
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
    /// Picks up Plus settings from an even older layout, where they sat at the top level of
    /// Config.json. Only applies when the Plus block is still untouched, so it can never
    /// undo a newer value.
    /// </summary>
    private static void MigrateFromLegacyTopLevelKeys(ConfigModel config)
    {
        var configPath = Path.Join(Program.DataPath, "Config.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("Plus", out _))
                return;

            var plus = config.Plus;
            var migrated = false;

            if (root.TryGetProperty("CacheDownloadRateLimitMBs", out var rate) && rate.TryGetInt32(out var rateValue))
            {
                plus.CacheDownloadRateLimitMBs = rateValue;
                migrated = true;
            }

            if (root.TryGetProperty("CacheDownloadIdleSeconds", out var idle) && idle.TryGetInt32(out var idleValue))
            {
                plus.CacheDownloadIdleSeconds = idleValue;
                migrated = true;
            }

            if (root.TryGetProperty("CacheYouTubePreferVp9", out var vp9) &&
                vp9.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                plus.CacheYouTubePreferVp9 = vp9.GetBoolean();
                migrated = true;
            }

            if (root.TryGetProperty("UriRules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            {
                var migratedRules = Utils.Json.Deserialize<List<UriRule>>(rules.GetRawText());
                if (migratedRules is { Count: > 0 })
                {
                    plus.UriRules = migratedRules;
                    migrated = true;
                }
            }

            if (migrated)
                Log.Information("Migrated Plus settings from the top level of Config.json.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate legacy top-level Plus settings, using defaults.");
        }
    }

    /// <summary>
    /// Repairs default rules that shipped with a broken pattern. Runs before
    /// <see cref="EnsureDefaultRules"/> so the repaired rule is recognised as already
    /// present and isn't duplicated by the corrected default.
    /// </summary>
    private static void MigrateBrokenDefaultRules(PlusConfigModel plus)
    {
        if (plus.UriRules == null)
            return;

        foreach (var rule in plus.UriRules)
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
    ///
    /// An earlier version re-added any default that was missing, every single launch — so
    /// deleting a default rule was impossible: it silently reappeared on the next start.
    /// Each default is now recorded once in SeededDefaultRules, and after that the user's
    /// decision to remove it sticks.
    /// </summary>
    private static void EnsureDefaultRules(PlusConfigModel plus)
    {
        var defaults = DefaultRules.Create();

        if (plus.UriRules == null || plus.UriRules.Count == 0)
        {
            plus.UriRules = defaults;
            plus.SeededDefaultRules = defaults.Select(rule => rule.Name).ToList();
            return;
        }

        // Upgrading from a version with no seed tracking: everything the user already has
        // has evidently been seeded. Anything missing is either a rule they deleted or a
        // genuinely new default; both get offered exactly once here, and are then recorded.
        if (plus.SeededDefaultRules.Count == 0)
        {
            plus.SeededDefaultRules = defaults
                .Where(d => plus.UriRules.Any(r => r.Name == d.Name || r.Pattern == d.Pattern))
                .Select(d => d.Name)
                .ToList();
        }

        foreach (var defRule in defaults)
        {
            if (plus.SeededDefaultRules.Contains(defRule.Name))
                continue;

            if (plus.UriRules.Any(r => r.Name == defRule.Name || r.Pattern == defRule.Pattern))
            {
                plus.SeededDefaultRules.Add(defRule.Name);
                continue;
            }

            var catchAllIndex = plus.UriRules.FindIndex(r => r.Name == CatchAllRuleName);
            if (catchAllIndex >= 0)
                plus.UriRules.Insert(catchAllIndex, defRule);
            else
                plus.UriRules.Add(defRule);

            plus.SeededDefaultRules.Add(defRule.Name);
            Log.Information("Added new default rule '{RuleName}'.", defRule.Name);
        }

        // Defensive: an earlier version could insert the same rule more than once. Keyed on
        // a tuple rather than a "Name + \"|\" + Pattern" string, which could collide across
        // differently-split name/pattern pairs.
        plus.UriRules = plus.UriRules.DistinctBy(r => (r.Name, r.Pattern)).ToList();
    }
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
