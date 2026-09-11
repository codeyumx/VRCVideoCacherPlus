using VRCVideoCacher;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

public class ConfigSerializationTests
{
    private const string LegacyConfigJson = """
        {
          "YtdlpWebServerUrl": "http://localhost.youtube.com:9696",
          "YtdlpUseCookies": true,
          "UseBetaExtension": true,
          "YtdlpAutoUpdate": true,
          "AutoUpdateVrcVideoCacher": false,
          "YtdlpAdditionalArgs": "--retries 3",
          "YtdlpDubLanguage": "de",
          "CachedAssetPath": "",
          "CacheMaxSizeInGb": 10.0,
          "CacheHlsPlaylists": true,
          "CacheHlsMaxLength": 30,
          "CacheOnly": false,
          "PreCacheUrls": [],
          "PreCacheVideos": ["https://www.youtube.com/watch?v=dQw4w9WgXcQ"],
          "PatchResonite": false,
          "ResonitePath": "",
          "PatchVrChat": true,
          "VideoPlayersEnabled": true,
          "CloseToTray": true,
          "StartMinimized": false,
          "StartWithSteamVr": true,
          "CookieSetupCompleted": true,
          "RedirectVRDancing": false,
          "ErrorPopups": true,
          "Language": "ja",
          "HasShownTrayNotice": true
        }
        """;

    [Fact]
    public void ReadsAConfigWrittenByTheOldSerializer()
    {
        var config = Json.Deserialize<ConfigModel>(LegacyConfigJson);

        Assert.NotNull(config);
        Assert.Equal("http://localhost.youtube.com:9696", config!.YtdlpWebServerUrl);
        Assert.True(config.YtdlpUseCookies);
        Assert.True(config.UseBetaExtension);
        Assert.False(config.AutoUpdateVrcVideoCacher);
        Assert.Equal("--retries 3", config.YtdlpAdditionalArgs);
        Assert.Equal("de", config.YtdlpDubLanguage);
        Assert.Equal(10f, config.CacheMaxSizeInGb);
        Assert.Equal(30, config.CacheHlsMaxLength);
        Assert.Equal(["https://www.youtube.com/watch?v=dQw4w9WgXcQ"], config.PreCacheVideos);
        Assert.True(config.CookieSetupCompleted);
        Assert.Equal("ja", config.Language);
        Assert.True(config.HasShownTrayNotice);
    }

    [Fact]
    public void IgnoresAKeyThatNoLongerExists()
    {
        var exception = Record.Exception(() => Json.Deserialize<ConfigModel>(LegacyConfigJson));
        Assert.Null(exception);
    }

    [Fact]
    public void ConfigSurvivesARoundTrip()
    {
        var original = Json.Deserialize<ConfigModel>(LegacyConfigJson)!;
        var restored = Json.Deserialize<ConfigModel>(Json.Serialize(original))!;

        Assert.Equal(original.YtdlpWebServerUrl, restored.YtdlpWebServerUrl);
        Assert.Equal(original.CacheMaxSizeInGb, restored.CacheMaxSizeInGb);
        Assert.Equal(original.Language, restored.Language);
        Assert.Equal(original.PreCacheVideos, restored.PreCacheVideos);
        Assert.Equal(original.AutoUpdateVrcVideoCacher, restored.AutoUpdateVrcVideoCacher);
    }

    [Fact]
    public void ReadsAPlusConfigWrittenByTheOldSerializer()
    {
        const string json = """
            {
              "CacheDownloadRateLimitMBs": 5,
              "CacheDownloadIdleSeconds": 30,
              "CacheYouTubePreferVp9": true,
              "UriRules": [
                {
                  "Id": "315e410c4f874e68990ce76f4ba9534a",
                  "Enabled": false,
                  "Name": "YouTube Music Redirect",
                  "Pattern": "^https?:\\/\\/music\\.youtube\\.com\\/(?:watch|playlist)?\\?(?:.*?&)?v=([^&]+).*$",
                  "Action": 2,
                  "Cache": false,
                  "MaxResolution": null,
                  "MaxDurationMinutes": null,
                  "RedirectTarget": "https://youtube.com/watch?v=$1",
                  "Integration": null
                }
              ]
            }
            """;

        var config = Json.Deserialize<ConfigModel>(json);

        Assert.NotNull(config);
        Assert.Equal(5, config!.CacheDownloadRateLimitMBs);
        Assert.Equal(30, config.CacheDownloadIdleSeconds);
        Assert.True(config.CacheYouTubePreferVp9);

        var rule = Assert.Single(config.UriRules);
        Assert.Equal("315e410c4f874e68990ce76f4ba9534a", rule.Id);
        Assert.False(rule.Enabled);
        Assert.Equal("YouTube Music Redirect", rule.Name);
        Assert.Equal(RuleAction.Redirect, rule.Action);
        Assert.Equal("https://youtube.com/watch?v=$1", rule.RedirectTarget);
        Assert.Null(rule.MaxResolution);
        Assert.Null(rule.Integration);
        Assert.Contains(@"music\.youtube\.com", rule.Pattern);
    }

    [Fact]
    public void DoesNotEscapeUrlsOrRegexPatternsIntoUnreadableText()
    {
        var config = new ConfigModel
        {
            UriRules =
            [
                new UriRule
                {
                    Name = "Ampersands & plus+signs",
                    Pattern = @"^https?://x\.com/\?a=1&b=2$",
                    RedirectTarget = "https://y.com/?a=1&b=2+3"
                }
            ]
        };

        var json = Json.Serialize(config);

        Assert.Contains("a=1&b=2", json);
        Assert.Contains("Ampersands & plus+signs", json);
        Assert.DoesNotContain("\\u0026", json);
        Assert.DoesNotContain("\\u002B", json);
    }

    [Fact]
    public void EnumsStayNumericSoExistingFilesKeepWorking()
    {
        var json = Json.Serialize(new ConfigModel
        {
            UriRules = [new UriRule { Action = RuleAction.Block }]
        });

        Assert.Contains("\"Action\": 4", json);
    }

    [Fact]
    public void ToleratesCommentsAndTrailingCommasInAHandEditedFile()
    {
        const string json = """
            {
              // a user's note
              "CacheDownloadIdleSeconds": 45,
            }
            """;

        Assert.Equal(45, Json.Deserialize<ConfigModel>(json)!.CacheDownloadIdleSeconds);
    }

    [Fact]
    public void VersionFileRoundTrips()
    {
        var restored = Json.Deserialize<VersionJson>(
            Json.Serialize(new VersionJson { Ytdlp = "2026.08.20", Ffmpeg = "7.1.1", Deno = "v2.8.0" }))!;

        Assert.Equal("2026.08.20", restored.Ytdlp);
        Assert.Equal("7.1.1", restored.Ffmpeg);
        Assert.Equal("v2.8.0", restored.Deno);
    }

    [Fact]
    public void ReadsAGitHubReleasePayload()
    {
        const string json = """
            {
              "tag_name": "2026.8.21",
              "html_url": "https://github.com/codeyumx/VRCVideoCacherPlus/releases/tag/2026.8.21",
              "assets": [
                {
                  "name": "VRCVideoCacher.exe",
                  "browser_download_url": "https://github.com/.../VRCVideoCacher.exe",
                  "digest": "sha256:abc"
                }
              ]
            }
            """;

        var payload = Json.Deserialize<GitHubRelease>(json);

        Assert.NotNull(payload);
        Assert.Equal("2026.8.21", payload!.tag_name);
        Assert.Equal("https://github.com/codeyumx/VRCVideoCacherPlus/releases/tag/2026.8.21", payload.html_url);

        var asset = Assert.Single(payload.assets);
        Assert.Equal("VRCVideoCacher.exe", asset.name);
        Assert.Equal("https://github.com/.../VRCVideoCacher.exe", asset.browser_download_url);
        Assert.Equal("sha256:abc", asset.digest);
    }
}
