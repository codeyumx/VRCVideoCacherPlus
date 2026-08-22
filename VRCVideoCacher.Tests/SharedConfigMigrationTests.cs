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
        
        PlusConfigManager.Initialize(config);
        Assert.NotEmpty(config.UriRules);
    }

    [Fact]
    public void TheSharedConfigNoticeFlagDefaultsToUnshown()
    {
        Assert.False(new ConfigModel().HasShownSharedConfigNotice);
        var restored = Json.Deserialize<ConfigModel>(
            Json.Serialize(new ConfigModel { HasShownSharedConfigNotice = true }))!;
        Assert.True(restored.HasShownSharedConfigNotice);
    }

    [Fact]
    public void NoticeTextNamesTheFileTheOldSettingsAreActuallyIn()
    {
        using var stream = typeof(ConfigModel).Assembly
            .GetManifestResourceStream("VRCVideoCacher.Languages.en.loc.json");
        Assert.NotNull(stream);

        using var document = System.Text.Json.JsonDocument.Parse(stream!);
        var notice = document.RootElement.GetProperty("SharedConfigNotice").GetString();

        Assert.NotNull(notice);
        // Migrations were removed deliberately, so nothing writes a .bak; the previous
        // PlusConfig.json is simply left in place. The message must name that file, not
        // a backup that never gets created.
        Assert.Contains("PlusConfig.json", notice!);
        Assert.DoesNotContain(".bak", notice!);
        Assert.Contains("Config.json", notice!);
    }

    [Fact]
    public void EveryLanguageDefinesTheSameKeysAsEnglish()
    {
        // English is the fallback, so a key missing from a translation degrades gracefully —
        // but it degrades silently, which is how ko.loc.json ended up one key short without
        // anyone noticing. Comparing key sets makes that visible at build time.
        var assembly = typeof(ConfigModel).Assembly;

        HashSet<string> KeysOf(string resource)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            using var document = System.Text.Json.JsonDocument.Parse(stream!);
            return document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        }

        var english = KeysOf("VRCVideoCacher.Languages.en.loc.json");

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("VRCVideoCacher.Languages.") && n.EndsWith(".loc.json")))
        {
            var keys = KeysOf(resource);
            Assert.True(english.SetEquals(keys),
                $"{resource} differs from English: missing [{string.Join(", ", english.Except(keys))}], " +
                $"unexpected [{string.Join(", ", keys.Except(english))}]");
        }
    }

    [Fact]
    public void EveryLanguageHasTheNoticeStrings()
    {
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
