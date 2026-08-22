using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;
using VRCVideoCacher.Integrations;
using VRCVideoCacher.Integrations.Hls;

namespace VRCVideoCacher.API;

public class ApiController : WebApiController
{
    // ponytail: fixed local default; this fork takes no remote config from an upstream server.
    private const int YoutubePrefetchMaxRetries = 7;

    // Defaults true on process start, so the first app launch asks the extension to push
    // fresh cookies. Cleared once valid cookies are received (see ReceiveYoutubeCookies).
    private static volatile bool _cookieRefreshRequested = true;

    // Signal the long-poll endpoint waits on, so a UI refresh request wakes the extension
    // immediately instead of waiting for its next poll. Swapped out atomically each fire.
    private static volatile TaskCompletionSource _refreshSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Lets the UI (Cookies panel) ask the new extension to push fresh cookies right now.
    public static void RequestCookieRefresh()
    {
        _cookieRefreshRequested = true;
        var pending = Interlocked.Exchange(ref _refreshSignal,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        pending.TrySetResult();
    }

    // We support two extensions: the original EllyVR/upstream one (unchanged) and our new
    // one (still in development). App-driven refresh is a NEW-extension-only feature — the
    // new extension identifies itself with this header. The old extension never sends it, so
    // its behaviour is completely unaffected: it just POSTs cookies on YouTube visits as before.
    private const string NewExtensionHeader = "X-VRCVideoCacher-Ext";
    private const string NewExtensionId = "VRCVideoCacherPlus";
    private bool IsNewExtension =>
        HttpContext.Request.Headers[NewExtensionHeader] == NewExtensionId;

    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<ApiController>();

    // [OLD + NEW EXTENSION] CORS preflight for the youtube-cookies POST. Used by BOTH the
    // old/upstream (EllyVR) extension and the new one — do not gate this behind IsNewExtension.
    // Chromium's "Private Network Access" (PNA) policy requires a public origin (youtube.com)
    // to receive Access-Control-Allow-Private-Network: true before it can POST to localhost.
    // Without this OPTIONS handler the preflight is rejected and the extension never sends
    // cookies, regardless of which Chromium-based browser the user has the extension in.
    [Route(HttpVerbs.Options, "/youtube-cookies")]
    public Task ReceiveYoutubeCookiesOptions()
    {
        ApplyCorsHeaders();
        HttpContext.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        HttpContext.Response.Headers["Access-Control-Allow-Headers"] = $"Content-Type, {NewExtensionHeader}";
        HttpContext.Response.Headers["Access-Control-Max-Age"] = "86400";
        HttpContext.Response.StatusCode = 204;
        return Task.CompletedTask;
    }

    // [NEW EXTENSION ONLY] New extension polls this; "1" means the app wants fresh cookies
    // pushed (e.g. just started). Only the new extension is answered "1" — anything without
    // the header (including the old extension) gets "0", so the old extension is unaffected.
    [Route(HttpVerbs.Get, "/youtube-cookies/refresh-needed")]
    public async Task CookieRefreshNeeded()
    {
        ApplyCorsHeaders();
        var needed = _cookieRefreshRequested && IsNewExtension;
        await HttpContext.SendStringAsync(needed ? "1" : "0", "text/plain", Encoding.UTF8);
    }

    // [NEW EXTENSION ONLY] Long-poll: the new extension holds this open; it returns "1" the
    // instant the app requests a refresh, or "0" after a timeout so the connection is
    // periodically renewed. This is what makes the UI "Request fresh cookies" button act
    // immediately. The old extension never calls this and gets "0" if it somehow does.
    [Route(HttpVerbs.Get, "/youtube-cookies/refresh-wait")]
    public async Task CookieRefreshWait()
    {
        ApplyCorsHeaders();
        if (!IsNewExtension)
        {
            await HttpContext.SendStringAsync("0", "text/plain", Encoding.UTF8);
            return;
        }

        if (!_cookieRefreshRequested)
        {
            var signal = _refreshSignal;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var fired = await Task.WhenAny(signal.Task, Task.Delay(Timeout.Infinite, timeout.Token));
            // signal.Task completing means a refresh was requested; otherwise the delay won.
            if (fired != signal.Task)
            {
                await HttpContext.SendStringAsync("0", "text/plain", Encoding.UTF8);
                return;
            }
        }

        await HttpContext.SendStringAsync("1", "text/plain", Encoding.UTF8);
    }

    // [OLD + NEW EXTENSION] The core cookie-receive endpoint. The old/upstream extension POSTs
    // here with no special header (its behaviour must stay identical); the new extension POSTs
    // the same way plus the X-VRCVideoCacher-Ext header to opt into the refresh-flag lifecycle.
    [Route(HttpVerbs.Post, "/youtube-cookies")]
    public async Task ReceiveYoutubeCookies()
    {
        ApplyCorsHeaders();

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
        // Only the new extension participates in the refresh-flag lifecycle; the old
        // extension's POST is handled identically to before (it never touches this flag).
        if (IsNewExtension)
            _cookieRefreshRequested = false;

        HttpContext.Response.StatusCode = 200;
        await HttpContext.SendStringAsync("Cookies received.", "text/plain", Encoding.UTF8);

        Log.Information("Received Youtube cookies from browser extension.");
        Program.NotifyCookiesUpdated();
        if (!ConfigManager.Config.YtdlpUseCookies)
            Log.Warning("Config is NOT set to use cookies from browser extension.");
    }

    // Origins that legitimately talk to the cookie endpoints: a YouTube page (where the
    // extension's content script runs) or an extension context. Note that an extension
    // holding host_permissions for localhost bypasses CORS entirely, so the extension
    // schemes here are belt-and-braces for the ones that don't.
    private static readonly string[] AllowedCookieOrigins =
    [
        "https://www.youtube.com",
        "https://youtube.com",
        "https://m.youtube.com",
        "https://music.youtube.com",
        "https://studio.youtube.com"
    ];

    private static bool IsAllowedCookieOrigin(string origin) =>
        AllowedCookieOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ||
        origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);

    private void ApplyCorsHeaders()
    {
        var requestOrigin = HttpContext.Request.Headers["Origin"] ?? string.Empty;

        // Previously echoed back whatever Origin arrived, so any page the user happened to
        // be visiting could POST a cookie file at us and overwrite the real one. Only the
        // origins that actually host the extension are answered now.
        if (string.IsNullOrEmpty(requestOrigin) || !IsAllowedCookieOrigin(requestOrigin))
            return;

        HttpContext.Response.Headers["Access-Control-Allow-Origin"] = requestOrigin;
        HttpContext.Response.Headers["Vary"] = "Origin";

        // Required for Chromium's Private Network Access (PNA) policy: allows a
        // public origin (youtube.com) to POST to a private address (localhost).
        HttpContext.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
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

    // An empty 200 means "nothing to resolve, play the URL as-is" — which is precisely what
    // the Direct and bypass paths want, and precisely wrong for a block: the game happily
    // plays a direct .mp4 without any help from us, so an empty body blocks nothing. Only a
    // non-2xx makes yt-dlp-stub exit non-zero, which is the signal that actually stops
    // playback. Callers that mean "bypass" must keep sending an empty 200.
    private async Task SendBlockedAsync(string reason)
    {
        HttpContext.Response.StatusCode = 403;
        await HttpContext.SendStringAsync(reason, "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// The only legitimate caller of /getvideo is yt-dlp-stub, a plain HTTP client with no
    /// browser context. A cross-origin GET from a web page needs no preflight, so without
    /// this check any site the user visits could drive the resolver: make the application
    /// fetch arbitrary URLs, queue downloads and write into the cache. It could not read
    /// the responses, but it would not need to.
    ///
    /// Sec-Fetch-Site: none means the user typed the URL themselves, which is deliberate
    /// and cannot be triggered by a third party, so that case is allowed through.
    /// </summary>
    private bool IsBrowserOriginatedRequest()
    {
        if (!string.IsNullOrEmpty(HttpContext.Request.Headers["Origin"]))
            return true;

        var fetchSite = HttpContext.Request.Headers["Sec-Fetch-Site"];
        return !string.IsNullOrEmpty(fetchSite) &&
               !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase);
    }

    [Route(HttpVerbs.Get, "/getvideo")]
    public async Task GetVideo()
    {
        if (IsBrowserOriginatedRequest())
        {
            Log.Warning("Rejecting browser-originated request to /getvideo.");
            HttpContext.Response.StatusCode = 403;
            await HttpContext.SendStringAsync("This endpoint is not callable from a browser.", "text/plain", Encoding.UTF8);
            return;
        }

        // No quote-mangling here any more: yt-dlp arguments go through ArgumentList, so a
        // quote in a URL can no longer break out of the command line. The old
        // Replace("\"", "%22") was the single guard for the whole application and it also
        // corrupted any URL that legitimately contained one.
        var requestUrl = Request.QueryString["url"]?.Trim();
        var avPro = string.Compare(Request.QueryString["avpro"], "true", StringComparison.OrdinalIgnoreCase) == 0;
        var source = Request.QueryString["source"];

        if (string.IsNullOrEmpty(requestUrl))
        {
            Log.Warning("No URL provided.");
            await HttpContext.SendStringAsync("No URL provided.", "text/plain", Encoding.UTF8);
            return;
        }

        Log.Information("Request URL: {URL}", requestUrl);



        // Evaluate URL against Rules Engine
        var evalResult = RuleEngine.EvaluateUrl(requestUrl);
        if (evalResult.MatchedRule != null)
        {
            Log.Information("Rule matched '{RuleName}' -> Action: {Action}, FinalURL: '{FinalUrl}'",
                evalResult.MatchedRule.Name, evalResult.Action, evalResult.FinalUrl);

            switch (evalResult.Action)
            {
                case RuleAction.Block:
                    Log.Warning("URL blocked by rule '{RuleName}': {URL}", evalResult.MatchedRule.Name, requestUrl);
                    await SendBlockedAsync($"Blocked by VRCVideoCacher rule '{evalResult.MatchedRule.Name}'.");
                    return;

                case RuleAction.Direct:
                    ActiveStreamTracker.AssociateUrlInfo(evalResult.FinalUrl, requestUrl, requestUrl, null, null);
                    Log.Information("URL set to Direct (bypass caching) by rule '{RuleName}': {URL}", evalResult.MatchedRule.Name, evalResult.FinalUrl);
                    await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
                    return;

                case RuleAction.Redirect:
                    ActiveStreamTracker.AssociateUrlInfo(evalResult.RedirectUrl, requestUrl, requestUrl, null, null);
                    Log.Information("URL redirected by rule '{RuleName}' to: {RedirectUrl}", evalResult.MatchedRule.Name, evalResult.RedirectUrl);
                    await HttpContext.SendStringAsync(evalResult.RedirectUrl, "text/plain", Encoding.UTF8);
                    return;

                case RuleAction.Resolve:
                case RuleAction.Rewrite:
                default:
                    requestUrl = evalResult.FinalUrl;
                    break;
            }
        }

        var videoInfo = await VideoId.GetVideoId(requestUrl, avPro);
        if (videoInfo == null)
        {
            Log.Information("Failed to get Video Info for URL: {URL}", requestUrl);
            return;
        }
        DatabaseManager.AddPlayHistory(videoInfo);
        var dbCache = DatabaseManager.GetVideoInfoCache(videoInfo.VideoId);

        if (source == "resonite")
        {
            Log.Information("Request sent from resonite sending json.");
            await HttpContext.SendStringAsync(await VideoId.GetURLResonite(videoInfo.VideoUrl), "text/plain", Encoding.UTF8);
            return;
        }

        var (isCached, filePath, fileName) = GetCachedFile(videoInfo.VideoId, avPro);
        if (isCached)
        {
            // LRU bookkeeping only — the file may be open for serving or mid-eviction, and
            // failing to stamp it is not a reason to fail the request.
            try { File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow); }
            catch (Exception ex) { Log.Debug("Could not stamp {File}: {Err}", fileName, ex.Message); }
            DatabaseManager.UpdateVideoWatchStats(videoInfo.VideoId);
            var url = $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}";
            ActiveStreamTracker.AssociateUrlInfo(url, videoInfo.VideoUrl, dbCache?.Title ?? videoInfo.VideoUrl, videoInfo.VideoId, dbCache?.Duration);
            Log.Information("Responding with Cached URL: {URL}", url);
            await HttpContext.SendStringAsync(url, "text/plain", Encoding.UTF8);
            return;
        }

        if (string.IsNullOrEmpty(videoInfo.VideoId))
        {
            ActiveStreamTracker.AssociateUrlInfo(videoInfo.VideoUrl, videoInfo.VideoUrl, dbCache?.Title ?? videoInfo.VideoUrl, videoInfo.VideoId, dbCache?.Duration);
            Log.Information("Failed to get Video ID: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        if (ConfigManager.Config.CacheOnly)
        {
            ActiveStreamTracker.AssociateUrlInfo(videoInfo.VideoUrl, videoInfo.VideoUrl, dbCache?.Title ?? videoInfo.VideoUrl, videoInfo.VideoId, dbCache?.Duration);
            Log.Information("Cache Only Mode Enabled: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        // HLS manifests play natively in AVPro / VRChat's video player — yt-dlp's generic
        // extractor would just return the same URL (or fail), so skip the extra process.
        // We still queue the download below so it gets cached in the background.
        if (videoInfo.UrlType == UrlType.Hls)
        {
            var hlsDuration = DatabaseManager.GetVideoInfoCache(videoInfo.VideoId)?.Duration;
            ActiveStreamTracker.AssociateUrlInfo(videoInfo.VideoUrl, videoInfo.VideoUrl, dbCache?.Title ?? videoInfo.VideoUrl, videoInfo.VideoId, hlsDuration);
            Log.Information("HLS URL: passing through without yt-dlp resolution.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
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

        // The StartsWith("https://manifest.googlevideo.com") that used to sit here was
        // subsumed by the Contains check on the same line.
        if (videoInfo.UrlType == UrlType.YouTube ||
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

        var cachedDuration = DatabaseManager.GetVideoInfoCache(videoInfo.VideoId)?.Duration;
        var finalUrl = string.IsNullOrEmpty(response) ? videoInfo.VideoUrl : response;
        ActiveStreamTracker.AssociateUrlInfo(finalUrl, videoInfo.VideoUrl, dbCache?.Title ?? videoInfo.VideoUrl, videoInfo.VideoId, cachedDuration);
        Log.Information("Responding with URL: {URL}", response);
        await HttpContext.SendStringAsync(response, "text/plain", Encoding.UTF8);

        // Don't attempt to cache if its a livestream
        if (videoInfo.VideoId.Equals("live"))
            return;

        // Record activity immediately with whatever duration we already have cached,
        // so download deferral and queueing are never blocked by a slow yt-dlp call.
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
        if (!isCached && evalResult.MatchedRule?.Cache == true)
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
        var probe = HlsIntegration.TryGetCachedProbe(videoInfo.VideoUrl);
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
        var isCached = File.Exists(filePath) && EnsureValidOrEvict(filePath, fileName);
        if (avPro && !isCached)
        {
            // retry with .mp4
            fileName = $"{videoId}.mp4";
            filePath = Path.Join(CacheManager.CachePath, fileName);
            isCached = File.Exists(filePath) && EnsureValidOrEvict(filePath, fileName);
        }
        return (isCached, filePath, fileName);
    }

    // If a cache file fails sanity checks (size/magic), drop it so the next play
    // re-resolves and re-downloads instead of serving the same broken bytes forever.
    private static bool EnsureValidOrEvict(string filePath, string fileName)
    {
        if (VideoFileValidator.IsLikelyValidVideo(filePath))
            return true;

        Log.Warning("Evicting invalid cache entry {File} on read.", fileName);
        try { CacheManager.DeleteCacheItem(fileName); }
        catch (Exception ex) { Log.Warning("Failed to evict {File}: {Err}", fileName, ex.Message); }
        return false;
    }
}