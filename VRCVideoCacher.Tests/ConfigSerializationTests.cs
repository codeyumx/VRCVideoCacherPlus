using VRCVideoCacher;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// These files already exist on every user's disk, written by Newtonsoft. The move to
// System.Text.Json must read them back identically — a silent reset here means everyone
// loses their settings and their entire rule list on upgrade, with only a log line to say
// so, because both config loaders fall back to defaults when parsing fails.
public class ConfigSerializationTests
{
    // A Config.json exactly as Newtonsoft wrote it, including the "10.0" float form.
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
        // Every field, because STJ ignores fields entirely without IncludeFields — the
        // failure mode is not an exception, it is every value silently being the default.
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
        // RedirectVRDancing was removed from the model; an existing file still has it.
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
        // RuleAction is stored as a number by both serializers; 2 is Redirect.
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

        var config = Json.Deserialize<PlusConfigModel>(json);

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
        // The regex must come back exactly; a mangled pattern silently stops matching.
        Assert.Contains(@"music\.youtube\.com", rule.Pattern);
    }

    [Fact]
    public void DoesNotEscapeUrlsOrRegexPatternsIntoUnreadableText()
    {
        // The default STJ encoder emits \u0026 for & and \u002B for +. Config files are
        // meant to be hand-editable, and the rule list is full of both.
        var config = new PlusConfigModel
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
        var json = Json.Serialize(new PlusConfigModel
        {
            UriRules = [new UriRule { Action = RuleAction.Block }]
        });

        // RuleAction.Block is 4. A string here would break every config already on disk.
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

        Assert.Equal(45, Json.Deserialize<PlusConfigModel>(json)!.CacheDownloadIdleSeconds);
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
        // Snake-case names on the model match the API exactly; digest drives the update's
        // integrity check and is absent on older assets.
        const string json = """
            {
              "tag_name": "2026.8.21",
              "html_url": "https://github.com/owner/repo/releases/tag/2026.8.21",
              "name": "Release",
              "assets": [
                {
                  "name": "VRCVideoCacher",
                  "browser_download_url": "https://example.com/VRCVideoCacher",
                  "digest": "sha256:abc123",
                  "size": 52000000
                },
                { "name": "VRCVideoCacher.exe", "browser_download_url": "https://example.com/x.exe" }
              ]
            }
            """;

        var release = Json.Deserialize<GitHubRelease>(json);

        Assert.NotNull(release);
        Assert.Equal("2026.8.21", release!.tag_name);
        Assert.Equal(2, release.assets.Count);
        Assert.Equal("sha256:abc123", release.assets[0].digest);
        Assert.Null(release.assets[1].digest);
    }
}
