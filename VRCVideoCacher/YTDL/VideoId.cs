using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Integrations;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VRCVideoCacher.YTDL;

public class VideoId
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoId>();
    private static readonly HashSet<string> YouTubeHosts = ["youtube.com", "youtu.be", "www.youtube.com", "m.youtube.com", "music.youtube.com"];

    internal static Uri? ToUri(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    internal static string HashUrl(string url)
    {
        return Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(url)))
            .Replace("/", "")
            .Replace("+", "")
            .Replace("=", "");
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunYtdlpAsync(List<string> args, string url, bool includeCookies = true)
    {
        // "--" so a URL that happens to start with a dash is never taken for a flag.
        var arguments = YtdlManager.GenerateYtdlArgs(args, ["--", url], includeCookies);
        Log.Information("Starting yt-dlp with args: {args:l}", string.Join(' ', arguments));
        var (output, error, exitCode) = await ProcessRunner.RunAsync(YtdlManager.YtdlPath, arguments);
        Log.Information("Finished yt-dlp");
        return (output, error, exitCode);
    }

    public static async Task<VideoInfo?> GetVideoId(string url, bool avPro)
    {
        url = url.Trim();
        url = await IntegrationRegistry.ApplyRewrites(url);

        var uri = ToUri(url);
        if (uri == null) return null;

        var handler = await IntegrationRegistry.ResolveAsync(url, uri, Services.RuleEngine.EvaluateUrl(url).MatchedRule);
        return handler == null ? null : await handler.GetVideoInfo(url, uri, avPro);
    }

    /// <summary>
    /// Fetches YouTube video metadata (duration, title, etc.) via yt-dlp -j and caches it.
    /// Returns the duration in seconds, or null if it couldn't be fetched.
    /// This is called in the streaming path so that ActiveStreamTracker knows how long
    /// to defer cache downloads.
    /// </summary>
    public static async Task<double?> FetchAndCacheYouTubeMetadataAsync(string videoId)
    {
        // Fast oEmbed lookup for instant title/author resolution
        try
        {
            var oembedInfo = await Integrations.YouTube.YouTubeMetadataService.GetVideoTitleAsync(videoId);
            if (oembedInfo != null)
            {
                Log.Information("oEmbed title resolved for {VideoId}: {Title}", videoId, oembedInfo.Title);
            }
        }
        catch { }

        // Check if we already have duration cached
        var existing = DatabaseManager.GetVideoInfoCache(videoId);
        if (existing?.Duration is > 0)
            return existing.Duration;

        try
        {
            var url = $"https://www.youtube.com/watch?v={videoId}";
            var args = new List<string>
            {
                "-j",
                "--skip-download",
                "--impersonate", "safari",
                "--extractor-args", "youtube:player_client=web"
            };

            var (rawData, error, exitCode) = await RunYtdlpAsync(args, url, includeCookies: true);
            if (exitCode != 0 || string.IsNullOrEmpty(rawData))
            {
                Log.Warning("Metadata fetch with cookies failed for {VideoId} ({Error}). Retrying without cookies...", videoId, error.Trim());
                (rawData, error, exitCode) = await RunYtdlpAsync(args, url, includeCookies: false);
            }

            if (exitCode != 0 || string.IsNullOrEmpty(rawData))
            {
                Log.Warning("Failed to fetch metadata for {VideoId}: {Error}", videoId, error);
                return null;
            }

            var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
            if (data?.Duration is null)
                return null;

            DatabaseManager.AddVideoInfoCache(new VideoInfoCache
            {
                Id = data.Id ?? videoId,
                Title = data.Name,
                Author = data.Author,
                Duration = data.Duration,
                Type = UrlType.YouTube
            });

            Log.Information("Cached metadata for {VideoId}: duration={Duration}s", videoId, data.Duration);
            return data.Duration;
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to fetch metadata for {VideoId}: {Error}", videoId, ex.Message);
            return null;
        }
    }

    public static async Task<(string VideoId, string? SkipReason)> TryGetYouTubeVideoId(string url)
    {
        var args = new List<string> { "-j" };

        var (rawData, error, exitCode) = await RunYtdlpAsync(args, url, includeCookies: true);
        if (exitCode != 0 || string.IsNullOrEmpty(rawData))
        {
            Log.Warning("TryGetYouTubeVideoId with cookies failed ({Error}). Retrying without cookies...", error.Trim());
            var (fallbackData, fallbackError, fallbackExitCode) = await RunYtdlpAsync(new List<string> { "-j" }, url, includeCookies: false);
            if (fallbackExitCode != 0 || string.IsNullOrEmpty(fallbackData))
            {
                throw new Exception($"yt-dlp metadata fetch failed: {fallbackError.Trim()}");
            }
            rawData = fallbackData;
        }

        if (string.IsNullOrEmpty(rawData))
        {
            Log.Warning("Skipping video: yt-dlp returned no metadata for {URL}", url);
            return (string.Empty, "SkipReasonNoMetadata");
        }
        var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
        if (data?.Id is null || data.Duration is null)
        {
            Log.Warning("Skipping video: could not parse video ID or duration for {URL}", url);
            return (string.Empty, "SkipReasonParseFailed");
        }

        DatabaseManager.AddVideoInfoCache(new VideoInfoCache
        {
            Id = data.Id,
            Title = data.Name,
            Author = data.Author,
            Duration = data.Duration,
            Type = UrlType.YouTube
        });

        if (data.IsLive == true)
        {
            Log.Warning("Skipping video: video is a live stream");
            return (string.Empty, "SkipReasonLiveStream");
        }
        var evalResult = Services.RuleEngine.EvaluateUrl(url);
        int maxDurationMinutes = evalResult.MaxDurationMinutes ?? 120;

        if (data.Duration > maxDurationMinutes * 60)
        {
            Log.Warning("Skipping video: duration exceeds allowed duration ({VideoMin:F1}min > {MaxMin}min)", data.Duration / 60.0, maxDurationMinutes);
            return (string.Empty, string.Format("SkipReasonTooLong|{0:F0}|{1}", data.Duration / 60.0, maxDurationMinutes));
        }

        return (data.Id, null);
    }

    public static async Task<string> GetURLResonite(string url)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage))
        {
            args.Add("-f");
            args.Add($"[language={ConfigManager.Config.YtdlpDubLanguage}]");
        }
        args.Add("--flat-playlist");
        args.Add("-i");
        args.Add("-J"); // --dump-single-json
        args.Add("-s");
        args.Add("--impersonate");
        args.Add("safari");
        args.Add("--extractor-args");
        args.Add("youtube:player_client=web");

        var (output, error, exitCode) = await RunYtdlpAsync(args, url);
        if (exitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
                Log.Error("Fix this error by running cookie setup.");

            return string.Empty;
        }

        return output;
    }

    public static async Task<Tuple<string, bool>> GetUrl(VideoInfo videoInfo, bool avPro)
    {
        // if url contains "results?" then it's a search
        if (videoInfo.VideoUrl.Contains("results?") && videoInfo.UrlType == UrlType.YouTube)
        {
            const string message = "URL is a search query, cannot get video URL.";
            return new Tuple<string, bool>(message, false);
        }

        var url = videoInfo.VideoUrl;
        var uri = ToUri(url);

        // Select the handler from the type recorded on the VideoInfo rather than
        // re-evaluating the rules against its URL. GetVideoInfo has already canonicalised
        // that URL, so a second evaluation is both redundant and able to disagree with the
        // handler that produced this record in the first place.
        var handler = uri != null ? IntegrationRegistry.ResolveByUrlType(videoInfo.UrlType) : null;
        var args = handler?.GetYtdlpArguments(uri!, avPro) ?? [];
        args.Add("--get-url");

        var (output, error, exitCode) = await RunYtdlpAsync(args, url);

        if (exitCode == 0 && !string.IsNullOrEmpty(output)) // success
            return new Tuple<string, bool>(output, true);

        if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
            Log.Error("Fix this error by running cookie setup.");

        if (avPro)
        {
            Log.Warning("AVPro format request failed retrying without AVPro.");
            return await GetUrl(videoInfo, false);
        }

        // Ultimate fallback for videos with restricted DASH/AVPro formats
        Log.Warning("Standard format request failed ({Error}). Retrying with android fallback client...", error.Trim());
        var fallbackArgs = new List<string>
        {
            "--get-url",
            "--extractor-args", "youtube:player_client=android,web",
            "-f", "b[height<=?1080]/bv*+ba/best"
        };
        var (fallbackOutput, fallbackError, fallbackExitCode) = await RunYtdlpAsync(fallbackArgs, url);
        if (fallbackExitCode == 0 && !string.IsNullOrEmpty(fallbackOutput))
        {
            return new Tuple<string, bool>(fallbackOutput, true);
        }

        return new Tuple<string, bool>(!string.IsNullOrEmpty(fallbackError) ? fallbackError : error, false);
    }

    public static bool IsYouTubePlaylist(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (!YouTubeHosts.Contains(uri.Host))
                return false;
            var query = HttpUtility.ParseQueryString(uri.Query);
            var listParam = query.Get("list");
            return !string.IsNullOrEmpty(listParam);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<VideoInfo>> GetPlaylistVideoInfos(string url, bool avPro)
    {
        var results = new List<VideoInfo>();
        var args = new List<string>
        {
            "--flat-playlist",
            "-j",
            "--ignore-config",
            "--no-warnings",
            "--encoding", "utf-8"
        };

        if (File.Exists(YtdlManager.FfmpegPath))
        {
            args.Add("--ffmpeg-location");
            args.Add(YtdlManager.FfmpegPath);
        }
        if (File.Exists(YtdlManager.DenoPath))
        {
            args.Add("--js-runtimes");
            args.Add($"deno:{YtdlManager.DenoPath}");
        }
        if (Program.IsCookiesEnabledAndValid())
        {
            args.Add("--cookies");
            args.Add(YtdlManager.CookiesPath);
        }
        args.AddRange(YtdlManager.SplitArguments(ConfigManager.Config.YtdlpAdditionalArgs));
        args.Add("--");
        args.Add(url);

        var (output, error, exitCode) = await ProcessRunner.RunAsync(YtdlManager.YtdlPath, args);

        if (exitCode != 0)
        {
            Log.Error("Failed to get playlist entries: {Error}", error);
            return results;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var data = JsonSerializer.Deserialize(line.Trim(), VideoIdJsonContext.Default.YtdlpVideoInfo);
                if (data?.Id == null) continue;

                results.Add(new VideoInfo
                {
                    VideoUrl = $"https://www.youtube.com/watch?v={data.Id}",
                    VideoId = data.Id,
                    UrlType = UrlType.YouTube,
                    DownloadFormat = avPro ? DownloadFormat.Webm : DownloadFormat.MP4
                });
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to parse playlist entry: {Error}", ex.Message);
            }
        }

        Log.Information("Extracted {Count} videos from playlist: {URL}", results.Count, url);
        return results;
    }
}