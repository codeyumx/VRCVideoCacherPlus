using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;
using VRCVideoCacher.YTDL.SiteHandlers.Sites;

namespace VRCVideoCacher.API;

public class ApiController : WebApiController
{
    private static int YoutubePrefetchMaxRetries => VvcConfigService.CurrentConfig.RetryCount;

    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<ApiController>();
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(30),
    };

    [Route(HttpVerbs.Post, "/youtube-cookies")]
    public async Task ReceiveYoutubeCookies()
    {
        HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        using var reader = new StreamReader(HttpContext.OpenRequestStream(), Encoding.UTF8);
        var cookies = await reader.ReadToEndAsync();
        cookies = FilterCookies(cookies);
        if (!Program.IsCookiesValid(cookies))
        {
            Log.Error("Invalid cookies received, maybe you haven't logged in yet, not saving.");
            HttpContext.Response.StatusCode = 400;
            await HttpContext.SendStringAsync("Invalid cookies.", "text/plain", Encoding.UTF8);
            return;
        }

        await File.WriteAllTextAsync(YtdlManager.CookiesPath, cookies);

        HttpContext.Response.StatusCode = 200;
        await HttpContext.SendStringAsync("Cookies received.", "text/plain", Encoding.UTF8);

        Log.Information("Received Youtube cookies from browser extension.");
        Program.NotifyCookiesUpdated();
        if (!ConfigManager.Config.YtdlpUseCookies)
            Log.Warning("Config is NOT set to use cookies from browser extension.");
    }

    private static string FilterCookies(string cookies)
    {
        var lines = cookies.Split('\n');
        var filtered = lines.Where(line =>
        {
            var parts = line.Split('\t');
            // Netscape cookie format: domain flag path secure expiration name value
            // Skip lines where the cookie name (index 5) starts with "ST-"
            // Breaks YT cookie checks otherwise, seems to be a mostly firefox issue.
            return parts.Length < 6 || !parts[5].StartsWith("ST-", StringComparison.Ordinal);
        });
        return string.Join('\n', filtered);
    }

    [Route(HttpVerbs.Get, "/getvideo")]
    public async Task GetVideo()
    {
        // escape double quotes for our own safety
        var requestUrl = Request.QueryString["url"]?.Replace("\"", "%22").Trim();
        var avPro = string.Compare(Request.QueryString["avpro"], "true", StringComparison.OrdinalIgnoreCase) == 0;
        var source = Request.QueryString["source"];

        if (string.IsNullOrEmpty(requestUrl))
        {
            Log.Warning("No URL provided.");
            await HttpContext.SendStringAsync("No URL provided.", "text/plain", Encoding.UTF8);
            return;
        }

        Log.Information("Request URL: {URL}", requestUrl);

        if (requestUrl.StartsWith("https://eu2.vrdancing.club/weekend/") && ConfigManager.Config.RedirectVRDancing)
        {
            await HttpContext.SendStringAsync(requestUrl.Replace("eu2", "na2"), "text/plain", Encoding.UTF8);
            return;
        }
        
        if (ConfigManager.Config.BlockedUrls.Any(blockedUrl => requestUrl.StartsWith(blockedUrl)))
        {
            Log.Warning("URL Is Blocked: {URL}", requestUrl);
            requestUrl = ConfigManager.Config.BlockRedirect;
        }

        if (requestUrl.StartsWith("https://mightygymcdn.nyc3.cdn.digitaloceanspaces.com"))
        {
            Log.Information("URL Is Mighty Gym: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        // pls no villager
        if (requestUrl.StartsWith("https://anime.illumination.media"))
            avPro = true;
        else if (requestUrl.Contains(".imvrcdn.com") ||
                 (requestUrl.Contains(".illumination.media") && !requestUrl.StartsWith("https://yt.illumination.media")))
        {
            Log.Information("URL Is Illumination media: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        // bypass vfi - cinema
        if (requestUrl.StartsWith("https://virtualfilm.institute"))
        {
            Log.Information("URL Is VFI - Cinema: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        var videoInfo = await VideoId.GetVideoId(requestUrl, avPro);
        if (videoInfo == null)
        {
            Log.Information("Failed to get Video Info for URL: {URL}", requestUrl);
            return;
        }
        DatabaseManager.AddPlayHistory(videoInfo);

        if (source == "resonite")
        {
            Log.Information("Request sent from resonite sending json.");
            await HttpContext.SendStringAsync(await VideoId.GetURLResonite(videoInfo.VideoUrl), "text/plain", Encoding.UTF8);
            return;
        }

        var (isCached, filePath, fileName) = GetCachedFile(videoInfo.VideoId, avPro);
        if (isCached)
        {
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
            DatabaseManager.UpdateVideoWatchStats(videoInfo.VideoId);
            var url = $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}";
            Log.Information("Responding with Cached URL: {URL}", url);
            await HttpContext.SendStringAsync(url, "text/plain", Encoding.UTF8);
            return;
        }

        if (string.IsNullOrEmpty(videoInfo.VideoId))
        {
            Log.Information("Failed to get Video ID: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        if (ConfigManager.Config.CacheOnly)
        {
            Log.Information("Cache Only Mode Enabled: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        // HLS manifests play natively in AVPro / VRChat's video player — yt-dlp's generic
        // extractor would just return the same URL (or fail), so skip the extra process.
        // We still queue the download below so it gets cached in the background.
        if (videoInfo.UrlType == UrlType.Hls)
        {
            Log.Information("HLS URL: passing through without yt-dlp resolution.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            var hlsDuration = DatabaseManager.GetVideoInfoCache(videoInfo.VideoId)?.Duration;
            ActiveStreamTracker.RecordActivity(videoInfo.VideoId, hlsDuration);
            if (ConfigManager.Config.CacheHlsPlaylists && IsHlsCacheable(videoInfo, hlsDuration))
                VideoDownloader.QueueDownload(videoInfo);
            return;
        }

        var (response, success) = await VideoId.GetUrl(videoInfo, avPro);
        if (!success)
        {
            Log.Warning("Get URL: {Error}", response);
            // only send the error back if it's for YouTube, otherwise let it play the request URL normally
            if (videoInfo.UrlType == UrlType.YouTube)
            {
                HttpContext.Response.StatusCode = 500;
                await HttpContext.SendStringAsync(response, "text/plain", Encoding.UTF8);
                return;
            }
            response = string.Empty;
        }

        if (videoInfo.UrlType == UrlType.YouTube ||
            videoInfo.VideoUrl.StartsWith("https://manifest.googlevideo.com") ||
            videoInfo.VideoUrl.Contains("googlevideo.com"))
        {
            var isPrefetchSuccessful = await VideoTools.Prefetch(response, YoutubePrefetchMaxRetries);

            if (!isPrefetchSuccessful && avPro)
            {
                Log.Warning("Prefetch failed with AVPro, retrying without AVPro.");
                avPro = false;
                (response, success) = await VideoId.GetUrl(videoInfo, avPro);
                await VideoTools.Prefetch(response, YoutubePrefetchMaxRetries);
            }
        }

        Log.Information("Responding with URL: {URL}", response);
        await HttpContext.SendStringAsync(response, "text/plain", Encoding.UTF8);

        // Record activity immediately with whatever duration we already have cached,
        // so download deferral and queueing are never blocked by a slow yt-dlp call.
        var cachedDuration = DatabaseManager.GetVideoInfoCache(videoInfo.VideoId)?.Duration;
        ActiveStreamTracker.RecordActivity(videoInfo.VideoId, cachedDuration);

        // If we don't have duration yet for a YouTube video, fetch it in the background
        // with a timeout so the tracker gets updated when it's available.
        if (videoInfo.UrlType == UrlType.YouTube && cachedDuration is not > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var duration = await VideoId.FetchAndCacheYouTubeMetadataAsync(videoInfo.VideoId)
                        .WaitAsync(cts.Token);
                    if (duration is > 0)
                        ActiveStreamTracker.RecordActivity(videoInfo.VideoId, duration);
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("Metadata fetch for {VideoId} timed out, using fallback duration.", videoInfo.VideoId);
                }
                catch (Exception ex)
                {
                    Log.Warning("Background metadata fetch for {VideoId} failed: {Error}", videoInfo.VideoId, ex.Message);
                }
            });
        }

        // check if file is cached again to handle race condition
        (isCached, _, _) = GetCachedFile(videoInfo.VideoId, avPro);
        if (!isCached && (
                (videoInfo.UrlType == UrlType.YouTube && ConfigManager.Config.CacheYouTube) ||
                (videoInfo.UrlType == UrlType.PyPyDance && ConfigManager.Config.CachePyPyDance) ||
                (videoInfo.UrlType == UrlType.VRDancing && ConfigManager.Config.CacheVrDancing)))
        {
            VideoDownloader.QueueDownload(videoInfo);
        }
    }

    // HLS playlists are only cacheable if the probe observed an #EXT-X-ENDLIST and parsed a
    // finite duration under the configured max — otherwise yt-dlp may sit on a live stream.
    // Raw MPEG-TS streams are progressive single-file downloads: a finite Content-Length is
    // the equivalent "this is a complete video, not a live feed" signal.
    private static bool IsHlsCacheable(VideoInfo videoInfo, double? cachedDuration)
    {
        var probe = HlsHandler.TryGetCachedProbe(videoInfo.VideoUrl);
        if (probe is null)
        {
            Log.Information("HLS {VideoId}: skipping cache — probe result unavailable.", videoInfo.VideoId);
            return false;
        }
        if (probe.Value.IsTransportStream)
        {
            if (probe.Value.ContentLength is not > 0)
            {
                Log.Information("MPEG-TS {VideoId}: skipping cache — no Content-Length (likely a live stream).", videoInfo.VideoId);
                return false;
            }
            return true;
        }
        if (!probe.Value.IsComplete)
        {
            Log.Information("HLS {VideoId}: skipping cache — manifest is live or incomplete (no #EXT-X-ENDLIST).", videoInfo.VideoId);
            return false;
        }
        var duration = probe.Value.Duration ?? cachedDuration;
        if (duration is not > 0)
        {
            Log.Information("HLS {VideoId}: skipping cache — no parsed duration.", videoInfo.VideoId);
            return false;
        }
        var maxMinutes = ConfigManager.Config.CacheHlsMaxLength;
        if (maxMinutes > 0 && duration > maxMinutes * 60)
        {
            Log.Information("HLS {VideoId}: skipping cache — {Min:F1}min exceeds max {Max}min.",
                videoInfo.VideoId, duration.Value / 60.0, maxMinutes);
            return false;
        }
        return true;
    }

    private static (bool isCached, string filePath, string fileName) GetCachedFile(string videoId, bool avPro)
    {
        var ext = avPro ? "webm" : "mp4";
        var fileName = $"{videoId}.{ext}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        var isCached = File.Exists(filePath);
        if (avPro && !isCached)
        {
            // retry with .mp4
            fileName = $"{videoId}.mp4";
            filePath = Path.Join(CacheManager.CachePath, fileName);
            isCached = File.Exists(filePath);
        }
        return (isCached, filePath, fileName);
    }
}