using Newtonsoft.Json;
using VRCVideoCacher.Models;
using Xunit;

namespace VRCVideoCacher.Tests;

// The per-site settings the rule engine replaced are gone from ConfigModel, so their JSON
// keys are ignored on load and erased on the next save. These tests pin the one-time
// translation of those values into rules: without it an upgrade silently drops a user's
// blocked URLs and turns their cache opt-outs back on, because the seeded defaults cache.
public class LegacyConfigMigrationTests
{
    private static Dictionary<string, object> Legacy(string json) =>
        JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;

    private static UriRule Rule(PlusConfigModel config, string name) =>
        Assert.Single(config.UriRules, r => r.Name == name);

    [Fact]
    public void BlockedUrlsBecomeRulesThatStillHitTheOldRedirectTarget()
    {
        var config = new PlusConfigModel();

        PlusConfigManager.MigrateLegacyRuleSettings(Legacy("""
            {
              "BlockedUrls": ["https://example.com/bad", "https://na2.vrdancing.club/sampleurl.mp4"],
              "BlockRedirect": "https://www.youtube.com/watch?v=byv2bKekeWQ"
            }
            """), config);

        var rule = Rule(config, "Block https://example.com/bad");
        Assert.Equal(RuleAction.Redirect, rule.Action);
        Assert.Equal("https://www.youtube.com/watch?v=byv2bKekeWQ", rule.RedirectTarget);
        Assert.True(rule.Enabled);

        // Pre-rules builds matched with StartsWith, so a sub-path or query still hits the rule.
        Assert.Matches(rule.Pattern, "https://example.com/bad?token=1");

        // The entry every default Config.json ships with must not turn into a rule.
        Assert.DoesNotContain(config.UriRules, r => r.Pattern.Contains("sampleurl"));
    }

    [Fact]
    public void WithoutARedirectTargetBlockedUrlsAreBlocked()
    {
        var config = new PlusConfigModel();

        PlusConfigManager.MigrateLegacyRuleSettings(Legacy("""{ "BlockedUrls": ["https://example.com/bad"] }"""), config);

        Assert.Equal(RuleAction.Block, Rule(config, "Block https://example.com/bad").Action);
    }

    [Fact]
    public void CacheOptOutsSurviveInsteadOfBeingInvertedByTheSeededDefaults()
    {
        var config = new PlusConfigModel();
        Assert.True(Rule(config, "VRDancing").Cache); // the default this has to override

        PlusConfigManager.MigrateLegacyRuleSettings(Legacy("""
            { "CacheVrDancing": false, "CachePyPyDance": false, "CacheYouTube": false }
            """), config);

        Assert.False(Rule(config, "VRDancing").Cache);
        Assert.False(Rule(config, "PyPyDance").Cache);
        Assert.False(Rule(config, "YouTube").Cache);
    }

    [Fact]
    public void ResolutionCapsAndTheDancingRedirectKeepTheirValues()
    {
        var config = new PlusConfigModel();

        PlusConfigManager.MigrateLegacyRuleSettings(Legacy("""
            { "CacheYouTubeMaxResolution": 2160, "CacheYouTubeMaxLength": 45, "RedirectVRDancing": true }
            """), config);

        var youTube = Rule(config, "YouTube");
        Assert.Equal(2160, youTube.MaxResolution);
        Assert.Equal(45, youTube.MaxDurationMinutes);
        Assert.True(Rule(config, "VRDancing EU to NA Redirect").Enabled);
    }

    [Fact]
    public void RunningTwiceDoesNotDuplicateMigratedRules()
    {
        var config = new PlusConfigModel();
        var json = Legacy("""{ "BlockedUrls": ["https://example.com/bad"] }""");

        PlusConfigManager.MigrateLegacyRuleSettings(json, config);
        var afterFirst = config.UriRules.Count;
        PlusConfigManager.MigrateLegacyRuleSettings(json, config);

        Assert.Equal(afterFirst, config.UriRules.Count);
    }

    [Fact]
    public void AConfigWithNothingToMigrateLeavesTheRuleListAlone()
    {
        var config = new PlusConfigModel();
        var before = config.UriRules.Count;

        PlusConfigManager.MigrateLegacyRuleSettings(Legacy("""{ "Language": "ja", "CacheOnly": true }"""), config);

        Assert.Equal(before, config.UriRules.Count);
    }
}
