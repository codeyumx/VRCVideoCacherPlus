using VRCVideoCacher.Models;
using Xunit;

namespace VRCVideoCacher.Tests;

// The per-site settings the rule engine replaced are gone from ConfigModel, so their JSON keys
// are ignored on load and erased on the next save. These tests pin the one-time translation of
// those values into rules: without it an upgrade silently drops a user's blocked URLs and turns
// their cache opt-outs back on, because the seeded defaults cache.
public class LegacyConfigMigrationTests
{
    private static ConfigModel Migrated(string json)
    {
        var config = new ConfigModel();
        PlusConfigManager.ApplyLegacyRuleSettings(json, config);
        return config;
    }

    private static UriRule Rule(ConfigModel config, string name) =>
        Assert.Single(config.UriRules, r => r.Name == name);

    [Fact]
    public void BlockedUrlsBecomeRulesThatStillHitTheOldRedirectTarget()
    {
        var config = Migrated("""
            {
              "BlockedUrls": ["https://example.com/bad", "https://na2.vrdancing.club/sampleurl.mp4"],
              "BlockRedirect": "https://www.youtube.com/watch?v=byv2bKekeWQ"
            }
            """);

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
        var config = Migrated("""{ "BlockedUrls": ["https://example.com/bad"] }""");

        Assert.Equal(RuleAction.Block, Rule(config, "Block https://example.com/bad").Action);
    }

    [Fact]
    public void CacheOptOutsSurviveInsteadOfBeingInvertedByTheSeededDefaults()
    {
        var config = Migrated("""{ "CacheVrDancing": false, "CachePyPyDance": false, "CacheYouTube": false }""");

        Assert.False(Rule(config, "VRDancing").Cache);
        Assert.False(Rule(config, "PyPyDance").Cache);
        Assert.False(Rule(config, "YouTube").Cache);
    }

    [Fact]
    public void ResolutionCapsAndTheDancingRedirectKeepTheirValues()
    {
        var config = Migrated("""
            { "CacheYouTubeMaxResolution": 2160, "CacheYouTubeMaxLength": 45, "RedirectVRDancing": true }
            """);

        var youTube = Rule(config, "YouTube");
        Assert.Equal(2160, youTube.MaxResolution);
        Assert.Equal(45, youTube.MaxDurationMinutes);
        Assert.True(Rule(config, "VRDancing EU to NA Redirect").Enabled);
    }

    [Fact]
    public void RunningTwiceDoesNotDuplicateMigratedRules()
    {
        const string json = """{ "BlockedUrls": ["https://example.com/bad"] }""";

        var config = Migrated(json);
        var afterFirst = config.UriRules.Count;
        PlusConfigManager.ApplyLegacyRuleSettings(json, config);

        Assert.Equal(afterFirst, config.UriRules.Count);
    }

    [Fact]
    public void AConfigWithNothingToMigrateLeavesTheRuleListAlone()
    {
        var expected = new ConfigModel().UriRules.Count;

        var config = Migrated("""{ "Language": "ja", "CacheOnly": true }""");

        Assert.Equal(expected, config.UriRules.Count);
    }

    [Fact]
    public void AMalformedConfigIsIgnoredRatherThanFatal()
    {
        var expected = new ConfigModel().UriRules.Count;

        var config = Migrated("{ \"BlockedUrls\": [ \"https://example.com/bad\" ");

        Assert.Equal(expected, config.UriRules.Count);
    }

    // Builds up to 2026.8.14 kept these three in PlusConfig.json; dropping them on upgrade would
    // reset a user's rate limit and codec choice without any sign it had happened.
    [Fact]
    public void PlusConfigFileValuesLandOnTheMainConfig()
    {
        var config = new ConfigModel();

        PlusConfigManager.ApplyPlusConfigFile(
            """{ "CacheDownloadRateLimitMBs": 5, "CacheDownloadIdleSeconds": 0, "CacheYouTubePreferVp9": false }""",
            config);

        Assert.Equal(5, config.CacheDownloadRateLimitMBs);
        Assert.Equal(0, config.CacheDownloadIdleSeconds);
        Assert.False(config.CacheYouTubePreferVp9);
    }

    [Fact]
    public void PlusConfigFileKeysThatAreMissingKeepTheirDefaults()
    {
        var config = new ConfigModel();

        PlusConfigManager.ApplyPlusConfigFile("""{ "CacheDownloadRateLimitMBs": 5 }""", config);

        Assert.Equal(5, config.CacheDownloadRateLimitMBs);
        Assert.Equal(new ConfigModel().CacheDownloadIdleSeconds, config.CacheDownloadIdleSeconds);
        Assert.Equal(new ConfigModel().CacheYouTubePreferVp9, config.CacheYouTubePreferVp9);
    }
}
