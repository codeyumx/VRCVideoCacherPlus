using VRCVideoCacher;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// Plus settings moved out of their own PlusConfig.json and into a "Plus" block inside the
// shared Config.json. Every existing install has to come through that move with its rules
// intact, so the shapes involved are pinned down here.
public class SharedConfigMigrationTests
{
    [Fact]
    public void PlusSettingsRoundTripInsideTheSharedConfig()
    {
        var config = new ConfigModel
        {
            YtdlpDubLanguage = "de",
            Plus =
            {
                CacheDownloadRateLimitMBs = 50,
                CacheDownloadIdleSeconds = 30,
                CacheYouTubePreferVp9 = true,
                UriRules = [new UriRule { Name = "Mine", Pattern = "^https://x/", Action = RuleAction.Block }],
                SeededDefaultRules = ["Mine"]
            }
        };

        var restored = Json.Deserialize<ConfigModel>(Json.Serialize(config))!;

        Assert.Equal("de", restored.YtdlpDubLanguage);
        Assert.Equal(50, restored.Plus.CacheDownloadRateLimitMBs);
        Assert.Equal(30, restored.Plus.CacheDownloadIdleSeconds);
        Assert.True(restored.Plus.CacheYouTubePreferVp9);
        Assert.Equal(["Mine"], restored.Plus.SeededDefaultRules);

        var rule = Assert.Single(restored.Plus.UriRules);
        Assert.Equal("Mine", rule.Name);
        Assert.Equal(RuleAction.Block, rule.Action);
    }

    [Fact]
    public void PlusSettingsSerialiseUnderASingleNestedKey()
    {
        // One key is what upstream drops. Scattered top-level keys would be removed just as
        // surely but would be far harder to see going missing.
        var json = Json.Serialize(new ConfigModel());

        Assert.Contains("\"Plus\":", json);
        Assert.DoesNotContain("\"UriRules\": [", json.Split("\"Plus\":")[0]);
    }

    [Fact]
    public void AConfigWrittenByUpstreamLoadsWithDefaultPlusSettings()
    {
        // Exactly the failure being warned about: the original VRCVideoCacher rewrites the
        // file without the Plus block. That must load cleanly with defaults, not throw.
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
        Assert.NotNull(config!.Plus);
        Assert.Equal(30, config.Plus.CacheDownloadIdleSeconds);
        // The rule list is repopulated from defaults rather than coming back empty.
        Assert.NotEmpty(config.Plus.UriRules);
    }

    [Fact]
    public void ALegacyPlusConfigFileDeserialisesIntoThePlusBlock()
    {
        // The exact shape of a standalone PlusConfig.json, which the migration reads.
        const string legacy = """
            {
              "CacheDownloadRateLimitMBs": 50,
              "CacheDownloadIdleSeconds": 30,
              "CacheYouTubePreferVp9": true,
              "UriRules": [
                { "Id": "abc", "Enabled": true, "Name": "Kept", "Pattern": "^https://kept/", "Action": 4 }
              ]
            }
            """;

        var plus = Json.Deserialize<PlusConfigModel>(legacy);

        Assert.NotNull(plus);
        Assert.Equal(50, plus!.CacheDownloadRateLimitMBs);
        var rule = Assert.Single(plus.UriRules);
        Assert.Equal("Kept", rule.Name);
        Assert.Equal(RuleAction.Block, rule.Action);
    }

    [Fact]
    public void TheSharedConfigNoticeFlagDefaultsToUnshown()
    {
        Assert.False(new ConfigModel().HasShownSharedConfigNotice);
        // And survives a round trip once set, so the notice is shown exactly once.
        var restored = Json.Deserialize<ConfigModel>(
            Json.Serialize(new ConfigModel { HasShownSharedConfigNotice = true }))!;
        Assert.True(restored.HasShownSharedConfigNotice);
    }

    [Fact]
    public void NoticeTextNamesTheBackupFileThatIsActuallyWritten()
    {
        // The message tells the user where their old settings went, so if the file name in
        // the text and the one the migration writes ever diverge, the advice becomes wrong.
        //
        // Read straight out of the embedded resource: Localizer needs a registered
        // localizer, which only exists once the Avalonia app has started.
        using var stream = typeof(ConfigModel).Assembly
            .GetManifestResourceStream("VRCVideoCacher.Languages.en.loc.json");
        Assert.NotNull(stream);

        using var document = System.Text.Json.JsonDocument.Parse(stream!);
        var notice = document.RootElement.GetProperty("SharedConfigNotice").GetString();

        Assert.NotNull(notice);
        Assert.Contains("PlusConfig.json.bak", notice!);
        Assert.Contains("Config.json", notice!);
    }

    [Fact]
    public void EveryLanguageHasTheNoticeStrings()
    {
        // A missing translation here would fall back to English, which is fine — but a
        // missing *key* in English would surface the raw key at the user.
        foreach (var resource in typeof(ConfigModel).Assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("VRCVideoCacher.Languages.") && n.EndsWith(".loc.json")))
        {
            using var stream = typeof(ConfigModel).Assembly.GetManifestResourceStream(resource);
            using var document = System.Text.Json.JsonDocument.Parse(stream!);

            Assert.True(document.RootElement.TryGetProperty("SharedConfigNotice", out _), $"{resource} is missing SharedConfigNotice");
            Assert.True(document.RootElement.TryGetProperty("SharedConfigNoticeTitle", out _), $"{resource} is missing SharedConfigNoticeTitle");
        }
    }
}
