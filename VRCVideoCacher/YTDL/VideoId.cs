using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL.SiteHandlers;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VRCVideoCacher.YTDL;

public class VideoId
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoId>();
    private static readonly HttpClient HttpClient = new() { DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } } };
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

    private static Process GetYtdlpProcess()
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        
        return process;
    }
    
    private static async Task<(string Output, string Error, int ExitCode)> RunYtdlpAsync(List<string> args, string url)
    {
        var ytdlpProcess = GetYtdlpProcess();
        ytdlpProcess.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"\"{url}\"");
        Log.Information("Starting yt-dlp with args: {args:l}", ytdlpProcess.StartInfo.Arguments);
        ytdlpProcess.Start();
        var output = await ytdlpProcess.StandardOutput.ReadToEndAsync();
        var error = await ytdlpProcess.StandardError.ReadToEndAsync();
        await ytdlpProcess.WaitForExitAsync();
        Log.Information("Finished yt-dlp");
        return (output.Trim(), error.Trim(), ytdlpProcess.ExitCode);
    }

    public static async Task<VideoInfo?> GetVideoId(string url, bool avPro)
    {
        url = url.Trim();
        url = await SiteHandlerRegistry.ApplyRewrites(url);

        var uri = ToUri(url);
        if (uri == null) return null;

        var handler = SiteHandlerRegistry.Resolve(uri);
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
                "--impersonate=\"safari\"",
                "--extractor-args=\"youtube:player_client=web\""
            };

            var (rawData, error, exitCode) = await RunYtdlpAsync(args, url);
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

    public static async Task<string> TryGetYouTubeVideoId(string url)
    {
        var args = new List<string>();
        args.Add("-j");

        var (rawData, error, exitCode) = await RunYtdlpAsync(args, url);
        if (exitCode != 0)
            throw new Exception($"Failed to get video ID: {error.Trim()}");        
        
        if (string.IsNullOrEmpty(rawData))
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
        }
        var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
        if (data?.Id is null || data.Duration is null)
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
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
            Log.Warning("Failed to get video ID: Video is a stream");
            return string.Empty;
        }
        if (data.Duration > ConfigManager.Config.CacheYouTubeMaxLength * 60)
        {
            Log.Warning("Failed to get video ID: Video is longer than configured max length ({VideoMin}min > {MaxMin}min)", data.Duration / 60, ConfigManager.Config.CacheYouTubeMaxLength);
            return string.Empty;
        }

        return data.Id;
    }

    public static async Task<string> GetURLResonite(string url)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage))
            args.Add($"-f \"[language={ConfigManager.Config.YtdlpDubLanguage}]\"");
        args.Add("--flat-playlist");
        args.Add("-i");
        args.Add("-J"); // --dump-single-json
        args.Add("-s");
        args.Add("--impersonate=\"safari\"");
        args.Add("--extractor-args=\"youtube:player_client=web\"");
        
        
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
        var handler = uri != null ? SiteHandlerRegistry.Resolve(uri) : null;
        var args = handler?.GetYtdlpArguments(uri!, avPro) ?? [];
        args.Add("--get-url");
        
        var (output, error, exitCode) = await RunYtdlpAsync(args, url);
        
        if (exitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
                Log.Error("Fix this error by running cookie setup.");

            return new Tuple<string, bool>(error, false);
        }

        return new Tuple<string, bool>(output, true);
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
            "--encoding utf-8"
        };

        if (File.Exists(YtdlManager.FfmpegPath))
            args.Add($"--ffmpeg-location \"{YtdlManager.FfmpegPath}\"");
        if (File.Exists(YtdlManager.DenoPath))
            args.Add($"--js-runtimes deno:\"{YtdlManager.DenoPath}\"");
        if (Program.IsCookiesEnabledAndValid())
            args.Add($"--cookies \"{YtdlManager.CookiesPath}\"");
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpAdditionalArgs))
            args.Add(ConfigManager.Config.YtdlpAdditionalArgs);
        args.Add($"\"{url}\"");

        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                Arguments = string.Join(' ', args),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to get playlist entries: {Error}", error.Trim());
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

    private static async Task<string> GetRedirectUrl(string requestUrl)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, requestUrl);
        using var res = await HttpClient.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return requestUrl;

        return res.RequestMessage?.RequestUri?.ToString() ?? requestUrl;
    }
}