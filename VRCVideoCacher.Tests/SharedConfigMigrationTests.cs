using VRCVideoCacher;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

public class SharedConfigMigrationTests
{
    [Fact]
    public void PlusSettingsRoundTripInsideTheSharedConfig()
    {
        var config = new ConfigModel
        {
            YtdlpDubLanguage = "de",
            CacheDownloadRateLimitMBs = 50,
            CacheDownloadIdleSeconds = 30,
            CacheYouTubePreferVp9 = true,
            UriRules = [new UriRule { Name = "Mine", Pattern = "^https://x/", Action = RuleAction.Block }],
            SeededDefaultRules = ["Mine"]
        };

        var restored = Json.Deserialize<ConfigModel>(Json.Serialize(config))!;

        Assert.Equal("de", restored.YtdlpDubLanguage);
        Assert.Equal(50, restored.CacheDownloadRateLimitMBs);
        Assert.Equal(30, restored.CacheDownloadIdleSeconds);
        Assert.True(restored.CacheYouTubePreferVp9);
        Assert.Equal(["Mine"], restored.SeededDefaultRules);

        var rule = Assert.Single(restored.UriRules);
        Assert.Equal("Mine", rule.Name);
        Assert.Equal(RuleAction.Block, rule.Action);
    }

    [Fact]
    public void PlusSettingsSerialiseAtTopLevel()
    {
        var json = Json.Serialize(new ConfigModel());

        Assert.Contains("\"UriRules\": [", json);
        Assert.DoesNotContain("\"Plus\":", json);
    }

    [Fact]
    public void AConfigWrittenByUpstreamLoadsWithDefaultPlusSettings()
    {
        const string strippedByUpstream = """
            {
              "YtdlpWebServerUrl": "http://localhost:9696",
              "YtdlpUseCookies": true,
              "CacheMaxSizeInGb": 10.0,
              "Language": "en"
            }
            """;

        var config = Json.Deserialize<ConfigModel>(strippedByUpstream);

        Assert.NotNull(config);
        Assert.Equal(30, config!.CacheDownloadIdleSeconds);
        
        PlusConfigManager.Initialize(config, strippedByUpstream);
        Assert.NotEmpty(config.UriRules);
    }

    [Fact]
    public void ALegacyNestedPlusBlockMigratesToTopLevel()
    {
        const string legacyJson = """
            {
              "YtdlpWebServerUrl": "http://localhost:9696",
              "Plus": {
                "CacheDownloadRateLimitMBs": 50,
                "CacheDownloadIdleSeconds": 15,
                "CacheYouTubePreferVp9": false,
                "UriRules": [
                  { "Enabled": true, "Name": "Kept", "Pattern": "^https://kept/", "Action": 4 }
                ],
                "SeededDefaultRules": ["Kept"]
              }
            }
            """;

        var config = Json.Deserialize<ConfigModel>(legacyJson)!;
        PlusConfigManager.Initialize(config, legacyJson);

        Assert.Equal(50, config.CacheDownloadRateLimitMBs);
        Assert.Equal(15, config.CacheDownloadIdleSeconds);
        Assert.False(config.CacheYouTubePreferVp9);
        Assert.Contains("Kept", config.SeededDefaultRules);

        var rule = Assert.Single(config.UriRules, r => r.Name == "Kept");
        Assert.Equal("Kept", rule.Name);
        Assert.Equal(RuleAction.Block, rule.Action);
    }

    [Fact]
    public void IsDefaultReturnsTrueForDefaultConfigAndFalseForCustomized()
    {
        var config = new ConfigModel();
        // Fresh config should be default (after rules seeding/initialization)
        PlusConfigManager.Initialize(config, string.Empty);
        Assert.True(PlusConfigManager.IsDefault(config));

        // Change a primitive setting
        config.CacheDownloadIdleSeconds = 45;
        Assert.False(PlusConfigManager.IsDefault(config));
        config.CacheDownloadIdleSeconds = 30; // Restore
        Assert.True(PlusConfigManager.IsDefault(config));

        config.CacheDownloadRateLimitMBs = 10;
        Assert.False(PlusConfigManager.IsDefault(config));
        config.CacheDownloadRateLimitMBs = 0; // Restore
        Assert.True(PlusConfigManager.IsDefault(config));

        config.CacheYouTubePreferVp9 = false;
        Assert.False(PlusConfigManager.IsDefault(config));
        config.CacheYouTubePreferVp9 = true; // Restore
        Assert.True(PlusConfigManager.IsDefault(config));

        // Modify rules
        config.UriRules[0].Enabled = !config.UriRules[0].Enabled;
        Assert.False(PlusConfigManager.IsDefault(config));
    }
}
