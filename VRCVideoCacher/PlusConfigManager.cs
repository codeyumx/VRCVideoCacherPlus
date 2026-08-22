using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

/// <summary>
/// The PlusPlus-only settings, and the rule seeding that goes with them.
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
        // TODO: Remove later - Migrating/repairing broken Dropbox rule pattern for existing users
        MigrateBrokenDefaultRules(config);
        EnsureDefaultRules(config);
    }

    /// <summary>
    /// TODO: Remove later - Repairs default rules that shipped with a broken pattern.
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
}
