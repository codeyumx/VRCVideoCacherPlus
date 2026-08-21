using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.YTDL;

public enum DownloadState { Idle, WaitingForIdle, Downloading, Paused }

public class VideoDownloader
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    // Real browser UA — some CDNs gate raw-stream endpoints on UA and return an HTML
    // stub for unknown clients, which then fails validation. Matches what AVPro/VRChat
    // would look like to a permissive server.
    private const string DownloadUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Safari/537.36";

    // A cache download is an arbitrarily large file over an arbitrarily slow link, so the
    // operation as a whole must not be time-boxed. HttpClient.Timeout covers reading the
    // body as well as the headers even with ResponseHeadersRead, so the 100s default was
    // cancelling any transfer that ran longer than that. Liveness is enforced per-read
    // instead, via the stall watchdog in CopyWithStallGuardAsync.
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(30),
        // Downloads whatever URL the resolver settled on, which ultimately came from a
        // world; same address guard as the probe path.
        ConnectCallback = Utils.UrlPolicy.GuardedConnectAsync,
    })
    {
        DefaultRequestHeaders = { { "User-Agent", DownloadUserAgent } },
        Timeout = Timeout.InfiniteTimeSpan
    };

    // How long a transfer may deliver no bytes at all before we give up on it. Generous
    // enough to survive a CDN hiccup, short enough that a dead socket can't wedge the queue.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    // Per-videoId temp paths so a leftover from one download can never be glued onto
    // a different video's resume request. Format: _tempVideo.<videoId>.<ext>
    private const string TempPrefix = "_tempVideo.";
    private static string TempPathFor(string videoId, string ext) =>
        Path.Join(CacheManager.CachePath, $"{TempPrefix}{videoId}.{ext}");
    private static string TempMp4For(string videoId) => TempPathFor(videoId, "mp4");
    private static string TempWebmFor(string videoId) => TempPathFor(videoId, "webm");

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool, string?>? OnDownloadCompleted;
    public static event Action<VideoInfo>? OnDownloadPaused;
    public static event Action? OnQueueChanged;
    public static event Action<double>? OnDownloadProgress; // 0.0 to 100.0

    // Current state (read from UI thread via Get* methods — volatile for cross-thread visibility)
    private static volatile VideoInfo? _currentDownload;
    private static volatile DownloadState _state = DownloadState.Idle;

    // Pause/resume — all mutations under StateLock
    private static readonly object StateLock = new();
    private static VideoInfo? _pausedDownload;
    private static volatile bool _pauseRequested;
    private static volatile bool _forceDownloadNext;
    private static Process? _currentProcess;
    private static CancellationTokenSource? _downloadCts;

    private static int _started;

    static VideoDownloader()
    {
        SweepStaleTempFiles();
        ActiveStreamTracker.OnStreamingActivity += OnStreamingActivity;
    }

    /// <summary>
    /// Starts the download loop. Called once from startup rather than from the static
    /// constructor: a type initialiser that spawns a background worker means merely
    /// subscribing to one of the events on this class — which the dashboard view model does
    /// while building the UI — silently starts the queue running.
    /// </summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        Task.Run(DownloadThread);
    }

    // On startup, wipe any leftover _tempVideo.* (including .part) from a prior crash.
    // Resume across process restarts isn't worth the risk of cross-video glue-ups.
    private static void SweepStaleTempFiles()
    {
        try
        {
            if (!Directory.Exists(CacheManager.CachePath)) return;
            foreach (var path in Directory.EnumerateFiles(CacheManager.CachePath, $"{TempPrefix}*"))
            {
                try { File.Delete(path); }
                catch (Exception ex) { Log.Warning("Failed to remove stale temp {Path}: {Err}", path, ex.Message); }
            }
        }
        catch (Exception ex) { Log.Warning("Stale temp sweep failed: {Err}", ex.Message); }
    }

    private static void OnStreamingActivity()
    {
        lock (StateLock)
        {
            if (_currentDownload == null || _pauseRequested) return;

            _pauseRequested = true;
            _pausedDownload = _currentDownload;
            Log.Information("Streaming started — pausing cache download for {VideoId}.", _currentDownload.VideoId);

            try { _currentProcess?.Kill(); }
            catch { /* process may have already exited */ }
            _downloadCts?.Cancel();
        }
        OnQueueChanged?.Invoke();
    }

    private static async Task DownloadThread()
    {
        var shutdown = Program.ShutdownToken;

        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, shutdown);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var idleSeconds = PlusConfigManager.Config.CacheDownloadIdleSeconds;

            if (idleSeconds > 0 && !ActiveStreamTracker.IsIdle(idleSeconds) && !_forceDownloadNext)
            {
                var waitState = _state is DownloadState.Paused ? DownloadState.Paused : DownloadState.WaitingForIdle;
                if (_state != waitState)
                {
                    var pending = DatabaseManager.GetPendingDownloads();
                    var queueCount = pending.Count + (_pausedDownload != null ? 1 : 0);
                    Log.Information("Deferring {Count} cache download(s) — waiting for {IdleSeconds}s of idle time.", queueCount, idleSeconds);
                    _state = waitState;
                    _currentDownload = null;
                    OnQueueChanged?.Invoke();
                }
                continue;
            }

            // Log when idle threshold is met and we're about to resume
            if ((_state is DownloadState.WaitingForIdle or DownloadState.Paused) && idleSeconds > 0 && !_forceDownloadNext)
            {
                Log.Information("Idle threshold reached ({IdleSeconds}s) — resuming cache downloads.", idleSeconds);
            }

            VideoInfo? queueItem;
            bool isResume;
            lock (StateLock)
            {
                _pauseRequested = false;

                if (_pausedDownload != null && !_forceDownloadNext)
                {
                    queueItem = _pausedDownload;
                    _pausedDownload = null;
                    isResume = true;
                    Log.Information("Resuming paused download for {VideoId}.", queueItem.VideoId);
                }
                else
                {
                    // If force-downloading, re-queue the paused download back to DB
                    // so it isn't silently lost.
                    if (_forceDownloadNext && _pausedDownload != null)
                    {
                        Log.Information("Re-queuing paused download for {VideoId} due to force download.", _pausedDownload.VideoId);
                        DatabaseManager.AddPendingDownload(_pausedDownload);
                        _pausedDownload = null;
                    }
                    // Dequeue from persistent DB
                    var pending = DatabaseManager.GetPendingDownloads();
                    if (pending.Count == 0)
                    {
                        _forceDownloadNext = false;
                        if (_state != DownloadState.Idle)
                        {
                            Log.Information("Download queue is empty — returning to idle.");
                            _state = DownloadState.Idle;
                            _currentDownload = null;
                            OnQueueChanged?.Invoke();
                        }
                        continue;
                    }

                    var next = pending[0];
                    queueItem = new VideoInfo
                    {
                        VideoUrl = next.VideoUrl,
                        VideoId = next.VideoId,
                        UrlType = next.UrlType,
                        DownloadFormat = next.DownloadFormat
                    };
                    isResume = false;
                }

                // Set _currentDownload inside the lock so OnStreamingActivity
                // cannot fire between dequeue and assignment and miss the pause.
                _currentDownload = queueItem;
                _forceDownloadNext = false;
            }

            if (isResume)
                Log.Information("Resuming cache download for {VideoId}.", queueItem.VideoId);
            _state = DownloadState.Downloading;
            OnDownloadStarted?.Invoke(queueItem);

            var success = false;
            string? failReason = null;
            try
            {
                // VRDancing's mpegts-beta host serves HLS .m3u8 manifests, not raw MP4.
                // Route those through yt-dlp's HLS path instead of raw HTTP GET.
                var isVrdHls = queueItem.UrlType == UrlType.VRDancing
                    && queueItem.VideoUrl.StartsWith("https://mpegts-beta.vrdancing.club", StringComparison.OrdinalIgnoreCase);

                (success, failReason) = queueItem.UrlType switch
                {
                    UrlType.YouTube => await DownloadYouTubeVideo(queueItem),
                    UrlType.VRDancing when isVrdHls => await DownloadHlsVideo(queueItem),
                    UrlType.PyPyDance or UrlType.VRDancing => await DownloadVideoWithId(queueItem),
                    UrlType.Hls => await DownloadHlsVideo(queueItem),
                    _ => (false, "SkipReasonUnsupportedUrl")
                };
            }
            catch (OperationCanceledException) { /* paused via CTS, handled below */ }
            catch (Exception ex)
            {
                Log.Error("Exception during download: {Ex}", ex.ToString());
                failReason = ex.Message;
            }

            bool wasPaused;
            lock (StateLock)
            {
                wasPaused = _pauseRequested;
                _currentDownload = null;
                _currentProcess = null;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }

            if (wasPaused)
            {
                _state = DownloadState.Paused;
                OnDownloadPaused?.Invoke(queueItem);
                OnQueueChanged?.Invoke();
                continue;
            }

            // Remove from persistent queue on completion (success or failure)
            DatabaseManager.RemovePendingDownload(queueItem.VideoId, queueItem.DownloadFormat);

            _state = DownloadState.Idle;
            OnDownloadCompleted?.Invoke(queueItem, success, failReason);
            OnQueueChanged?.Invoke();
        }
    }

    public static void QueueDownload(VideoInfo videoInfo)
    {
        lock (StateLock)
        {
            if (_currentDownload?.VideoId == videoInfo.VideoId && _currentDownload?.DownloadFormat == videoInfo.DownloadFormat)
                return;
            if (_pausedDownload?.VideoId == videoInfo.VideoId && _pausedDownload?.DownloadFormat == videoInfo.DownloadFormat)
                return;
        }

        // AddPendingDownload handles duplicate checks internally
        DatabaseManager.AddPendingDownload(videoInfo);
        Log.Information("Queued download for {VideoId} ({UrlType}, {Format}).",
            videoInfo.VideoId, videoInfo.UrlType, videoInfo.DownloadFormat);
        OnQueueChanged?.Invoke();
    }

    public static void RemoveFromQueue(string videoId, DownloadFormat format)
    {
        DatabaseManager.RemovePendingDownload(videoId, format);
        Log.Information("Removed {VideoId} from download queue.", videoId);
        OnQueueChanged?.Invoke();
    }

    public static void RemoveFromQueueByKey(int key)
    {
        DatabaseManager.RemovePendingDownloadByKey(key);
        OnQueueChanged?.Invoke();
    }

    public static void ForceDownloadNext()
    {
        _forceDownloadNext = true;
        Log.Information("Force download requested — skipping idle delay for next item.");
        OnQueueChanged?.Invoke();
    }

    public static void BumpToTopOfQueue(int key)
    {
        DatabaseManager.BumpToTopOfQueue(key);
        Log.Information("Bumped item {Key} to top of download queue.", key);
        OnQueueChanged?.Invoke();
    }

    public static void ClearQueue()
    {
        lock (StateLock)
        {
            _pausedDownload = null;
        }
        DatabaseManager.ClearPendingDownloads();
        Log.Information("Download queue cleared.");
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public static IReadOnlyList<Database.Models.PendingDownload> GetQueueSnapshot() => DatabaseManager.GetPendingDownloads();
    public static int GetQueueCount()
    {
        var count = DatabaseManager.GetPendingDownloads().Count;
        return _currentDownload != null ? Math.Max(0, count - 1) : count;
    }
    public static VideoInfo? GetCurrentDownload() => _currentDownload;
    public static VideoInfo? GetPausedDownload() { lock (StateLock) return _pausedDownload; }
    public static DownloadState GetDownloadState() => _state;

    private static async Task<(bool Success, string? FailReason)> DownloadYouTubeVideo(VideoInfo videoInfo)
    {
        if (_pauseRequested) return (false, null);

        var url = videoInfo.VideoUrl;
        string? videoId;
        try
        {
            var (id, skipReason) = await VideoId.TryGetYouTubeVideoId(url);
            if (string.IsNullOrEmpty(id))
            {
                Log.Warning("Skipping download for {URL}: {Reason}", url, skipReason);
                return (false, skipReason);
            }
            videoId = id;
        }
        catch (Exception ex)
        {
            Log.Error("Skipping download for {URL}: {ex}", url, ex.Message);
            return (false, ex.Message);
        }

        // Re-check after the yt-dlp metadata call — streaming may have started during it
        if (_pauseRequested) return (false, null);

        var tempMp4 = TempMp4For(videoId);
        var tempWebm = TempWebmFor(videoId);

        // Only delete completed temp files; .part files are preserved for -c to resume
        if (File.Exists(tempMp4))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(tempMp4);
        }
        if (File.Exists(tempWebm))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(tempWebm);
        }

        var args = new List<string> { "-c", "--newline", "--no-warnings" }; // -c resumes from .part file if killed, --newline for progress parsing

        var rateLimitMBs = PlusConfigManager.Config.CacheDownloadRateLimitMBs;
        if (rateLimitMBs > 0)
        {
            args.Add("--limit-rate");
            args.Add($"{rateLimitMBs}M");
        }

        using var process = new Process
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

        var evalResult = Services.RuleEngine.EvaluateUrl(videoInfo.VideoUrl);
        var maxRes = evalResult.MaxResolution ?? 1080;

        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";
            args.Add("-o");
            args.Add(tempWebm);
            args.Add("-f");
            args.Add($"bv*[height<={maxRes}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={maxRes}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}");
        }
        else
        {
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add("-o");
            args.Add(tempMp4);
            args.Add("-f");
            if (PlusConfigManager.Config.CacheYouTubePreferVp9)
            {
                // VP9+aac in mp4 — best compression, universal compatibility (20-50% smaller than h264)
                args.Add($"bv*[height<={maxRes}][vcodec~='^vp9']{audioArgPotato}/bv*[height<={maxRes}][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<={maxRes}][vcodec~='^av01'][dynamic_range='SDR']");
            }
            else
            {
                // h264+aac — fastest decode, widest hardware support
                args.Add($"bv*[height<={maxRes}][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<={maxRes}][vcodec~='^av01'][dynamic_range='SDR']");
            }
            args.Add("--remux-video");
            args.Add("mp4");
        }

        // "--" so a video id starting with a dash can never be read as a flag.
        var arguments = YtdlManager.GenerateYtdlArgs(args, ["--", videoId]);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        Log.Information("Downloading YouTube Video: {Args}", string.Join(' ', arguments));

        lock (StateLock) { _currentProcess = process; }

        if (_pauseRequested)
        {
            lock (StateLock) { _currentProcess = null; }
            return (false, null);
        }

        process.Start();
        var errorBuilder = new StringBuilder();
        var errorTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                errorBuilder.AppendLine(line);
        });
        var outputTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                ParseYtdlpProgress(line);
        });
        await process.WaitForExitAsync();
        await Task.WhenAll(errorTask, outputTask);
        var error = errorBuilder.ToString().Trim();

        lock (StateLock) { _currentProcess = null; }

        // If killed due to pause, don't treat non-zero exit as an error
        if (_pauseRequested) return (false, null);
        if (process.ExitCode != 0)
        {
            // Known non-fatal yt-dlp failures: log as Warning (no dialog popup) and
            // return a clean skip reason for the status bar.
            if (error.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("YouTube format unavailable for {VideoId} — no matching stream for the configured format/resolution. {Error}", videoId, error);
                return (false, "SkipReasonFormatUnavailable");
            }
            if (error.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("YouTube bot-check for {VideoId} — install VRCVideoCacherBrowserExtension to fix.", videoId);
                return (false, "SkipReasonBotCheck");
            }
            if (error.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("YouTube video unavailable: {VideoId}. {Error}", videoId, error);
                return (false, "SkipReasonVideoUnavailable");
            }
            if (error.Contains("Private video", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("YouTube video is private: {VideoId}.", videoId);
                return (false, "SkipReasonPrivateVideo");
            }

            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            return (false, $"yt-dlp exited with code {process.ExitCode}");
        }

        // Let yt-dlp's final rename settle before looking for the output file. Was
        // Thread.Sleep, which blocks a thread-pool thread for no reason in an async method
        // — the sibling HLS path already used Task.Delay here.
        await Task.Delay(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Warning("File already exists, canceling...");
            VideoFileValidator.TryDelete(tempMp4);
            VideoFileValidator.TryDelete(tempWebm);
            return (false, "SkipReasonFileExists");
        }

        string sourceTemp;
        if (File.Exists(tempMp4)) sourceTemp = tempMp4;
        else if (File.Exists(tempWebm)) sourceTemp = tempWebm;
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return (false, "SkipReasonFileNotFound");
        }

        if (!VideoFileValidator.IsLikelyValidVideo(sourceTemp))
        {
            // Warning, not Error — the user-facing notification is the queue StatusMessage
            // surfaced via OnDownloadCompleted. Log.Error here would pop a modal dialog.
            Log.Warning("YouTube download for {VideoId} failed validation; discarding.", videoId);
            VideoFileValidator.TryDelete(sourceTemp);
            return (false, "SkipReasonInvalidDownload");
        }

        // Pinned across the publish so AddToCache's own size-budget flush cannot evict the
        // file it was just told about.
        using (CacheManager.PinFile(fileName))
        {
            File.Move(sourceTemp, filePath, overwrite: true);
            CacheManager.AddToCache(fileName);
        }
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return (true, null);
    }

    private static void ParseYtdlpProgress(string line)
    {
        // yt-dlp progress lines look like: [download]  45.2% of 12.34MiB at 1.23MiB/s
        if (!line.Contains('%')) return;
        var idx = line.IndexOf('%');
        if (idx < 1) return;
        // Walk backwards to find the start of the number
        var start = idx - 1;
        while (start > 0 && (char.IsDigit(line[start - 1]) || line[start - 1] == '.'))
            start--;
        if (double.TryParse(line[start..idx], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            OnDownloadProgress?.Invoke(percent);
    }

    /// <summary>
    /// Copies the response body to disk, optionally rate-limited, under a stall watchdog.
    /// The deadline is pushed forward on every read, so a transfer that keeps making
    /// progress runs as long as it needs to while one that goes silent for
    /// <see cref="StallTimeout"/> is cancelled. HttpClient.Timeout is disabled for this
    /// client, so this is the only thing between a dead socket and a wedged queue.
    /// </summary>
    /// <param name="bytesPerSecond">Rate limit, or 0 for unlimited.</param>
    /// <param name="totalBytes">Expected final size on disk, including any resumed prefix.</param>
    /// <param name="alreadyOnDisk">
    /// Bytes already present from a previous attempt. Progress is reported against the file
    /// as a whole, so this has to be counted: without it a download resuming at 90% restarted
    /// its progress bar from 0 and crept back up, which reads as the transfer having reset.
    /// </param>
    private static async Task CopyWithStallGuardAsync(
        Stream source, Stream destination, long bytesPerSecond, long totalBytes, long alreadyOnDisk, CancellationTokenSource cts)
    {
        var buffer = new byte[81920];
        var stopwatch = Stopwatch.StartNew();
        long totalBytesRead = 0;
        var lastReportedPercent = -1.0;

        while (true)
        {
            cts.CancelAfter(StallTimeout);
            var bytesRead = await source.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            totalBytesRead += bytesRead;

            if (totalBytes > 0)
            {
                // Only raise the event when the rounded percentage actually moves: this
                // fired once per 80 KiB chunk, so a large file pushed thousands of
                // redundant updates at the UI thread.
                var percent = Math.Round((alreadyOnDisk + totalBytesRead) / (double)totalBytes * 100.0, 1);
                if (percent > lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    OnDownloadProgress?.Invoke(percent);
                }
            }

            if (bytesPerSecond <= 0)
                continue;

            var expectedMs = (double)totalBytesRead / bytesPerSecond * 1000;
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            if (elapsedMs < expectedMs)
                await Task.Delay(TimeSpan.FromMilliseconds(expectedMs - elapsedMs), cts.Token);
        }

        // Disarm, so the validation and move that follow can't trip the watchdog.
        cts.CancelAfter(Timeout.InfiniteTimeSpan);
    }

    // A cancelled transfer is either the user's pause — which the caller reports as a pause,
    // with no error — or the stall watchdog firing, which is a genuine failure and needs to
    // reach the queue's status line instead of masquerading as a pause.
    private static (bool Success, string? FailReason) CancelledOutcome(string url)
    {
        if (_pauseRequested)
            return (false, null);

        Log.Warning("Download for {URL} stalled for {Seconds}s with no data; giving up.", url, StallTimeout.TotalSeconds);
        return (false, "SkipReasonStalled");
    }


    private static async Task<(bool Success, string? FailReason)> DownloadVideoWithId(VideoInfo videoInfo)
    {
        if (_pauseRequested) return (false, null);

        var url = videoInfo.VideoUrl;
        var tempMp4 = TempMp4For(videoInfo.VideoId);

        // Check for a partial download to resume via HTTP Range. Per-videoId temp paths
        // mean we can only ever resume our OWN partial — not bytes from a different video.
        long resumeFrom = 0;
        if (File.Exists(tempMp4))
        {
            resumeFrom = new FileInfo(tempMp4).Length;
            if (resumeFrom > 0)
                Log.Information("Resuming direct download from byte {Offset}.", resumeFrom);
        }

        if (_pauseRequested) return (false, null);

        var cts = new CancellationTokenSource();
        lock (StateLock) { _downloadCts = cts; }

        // Re-check after assigning CTS — streaming may have fired in the gap and found _downloadCts null
        if (_pauseRequested) return (false, null);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // Add headers that some CDNs expect to avoid rate limiting / auth checks
            request.Headers.Add("Accept", "video/*");
            if (resumeFrom > 0)
                request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

            // Arm the watchdog for the headers too: ConnectTimeout only covers the TCP
            // handshake, so a server that accepts and then says nothing would hang here.
            cts.CancelAfter(StallTimeout);
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            cts.CancelAfter(Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException) { return CancelledOutcome(url); }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Failed to start download for {URL}", url);
            return (false, "network error");
        }

        // HttpClient follows redirects automatically by default; manual handling is unnecessary.

        long expectedTotalBytes = 0;
        string? responseContentType = null;

        // Scoped `using` rather than a Dispose call on each of the six exit paths — one of
        // which had to be added every time a new early return appeared.
        using (response)
        {
            // 416 = server says our range is beyond the file — treat as already complete
            if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    Log.Warning("Failed to download video: {URL} {Status}", url, response.StatusCode);
                    return (false, $"HTTP {(int)response.StatusCode}");
                }

                // Reject obvious non-video bodies (HTML error pages, JSON errors served as 200, etc.)
                // before they ever hit disk. The magic-byte check after download catches the rest.
                responseContentType = response.Content.Headers.ContentType?.MediaType;
                if (!VideoFileValidator.IsAcceptableContentType(responseContentType))
                {
                    Log.Warning("Skipping cache for {URL}: unexpected Content-Type {Ct}", url, responseContentType);
                    VideoFileValidator.TryDelete(tempMp4);
                    return (false, $"unexpected content-type {responseContentType}");
                }

                var contentLength = response.Content.Headers.ContentLength ?? 0;
                if (contentLength > 0)
                    expectedTotalBytes = resumeFrom + contentLength;

                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    var fileMode = resumeFrom > 0 ? FileMode.Append : FileMode.Create;
                    await using var fileStream = new FileStream(tempMp4, fileMode, FileAccess.Write, FileShare.None);

                    var rateLimitMBs = PlusConfigManager.Config.CacheDownloadRateLimitMBs;
                    var bytesPerSecond = rateLimitMBs > 0 ? rateLimitMBs * 1024L * 1024L : 0L;
                    await CopyWithStallGuardAsync(stream, fileStream, bytesPerSecond, expectedTotalBytes, resumeFrom, cts);
                }
                catch (OperationCanceledException)
                {
                    return CancelledOutcome(url);
                }
                catch (IOException ex)
                {
                    Log.Warning("Download stream failed for {URL}: {Err}", url, ex.Message);
                    return (false, "network error");
                }
                catch (HttpRequestException ex)
                {
                    Log.Warning("Download stream failed for {URL}: {Err}", url, ex.Message);
                    return (false, "network error");
                }
            }
        }

        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (!File.Exists(tempMp4))
        {
            Log.Error("Failed to download Video: {URL}", url);
            return (false, "SkipReasonFileNotFound");
        }

        // If the server told us how big the body should be, enforce it. Catches the case
        // where the connection drops mid-stream after enough bytes were written to fool
        // the size+magic-byte check (e.g. several MB of MPEG-TS that happens to start
        // with 0x47, but ends mid-frame). Skip when expected size is unknown — many
        // mpegts endpoints omit Content-Length and we don't want to refuse those.
        if (expectedTotalBytes > 0)
        {
            var actual = new FileInfo(tempMp4).Length;
            if (actual < expectedTotalBytes)
            {
                Log.Warning("Download for {VideoId} truncated: {Got}/{Expected} bytes; discarding.",
                    videoInfo.VideoId, actual, expectedTotalBytes);
                VideoFileValidator.TryDelete(tempMp4);
                return (false, "SkipReasonTruncated");
            }
        }

        // If the body turned out to be an HLS manifest (Content-Type lied or wasn't set),
        // fall back to the HLS download path instead of discarding — the URL is still valid.
        if (VideoFileValidator.LooksLikeHlsManifest(tempMp4, responseContentType))
        {
            Log.Information("Detected HLS manifest at {URL}; switching to HLS download path.", url);
            VideoFileValidator.TryDelete(tempMp4);
            return await DownloadHlsVideo(videoInfo);
        }

        if (!VideoFileValidator.IsLikelyValidVideo(tempMp4, responseContentType))
        {
            Log.Warning("Download for {VideoId} ({URL}) failed validation; discarding.", videoInfo.VideoId, url);
            VideoFileValidator.TryDelete(tempMp4);
            return (false, "SkipReasonInvalidDownload");
        }

        using (CacheManager.PinFile(fileName))
        {
            File.Move(tempMp4, filePath, overwrite: true);
            CacheManager.AddToCache(fileName);
        }
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return (true, null);
    }
    /// <summary>
    /// Downloads a finished HLS playlist by letting yt-dlp's generic extractor pull all
    /// segments and remux them into a single MP4 via the bundled ffmpeg. Honors the
    /// existing pause/resume and rate-limit plumbing.
    /// </summary>
    private static async Task<(bool Success, string? FailReason)> DownloadHlsVideo(VideoInfo videoInfo)
    {
        if (_pauseRequested) return (false, null);

        var tempMp4 = TempMp4For(videoInfo.VideoId);
        if (File.Exists(tempMp4))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(tempMp4);
        }

        var args = new List<string>
        {
            "--newline",
            "-o", tempMp4,
            "--remux-video", "mp4",
            // Let yt-dlp pick native vs. ffmpeg HLS downloader — native chokes on
            // fMP4/CMAF (#EXT-X-MAP) playlists, ffmpeg handles those.
            "--concurrent-fragments", "4",
            // CDN segment fetches can be flaky; retry the manifest a few times and
            // each individual segment more aggressively before giving up.
            "--retries", "3",
            "--fragment-retries", "5"
        };

        var rateLimitMBs = PlusConfigManager.Config.CacheDownloadRateLimitMBs;
        if (rateLimitMBs > 0)
        {
            args.Add("--limit-rate");
            args.Add($"{rateLimitMBs}M");
        }

        using var process = new Process
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
            },
        };

        var arguments = YtdlManager.GenerateYtdlArgs(args, ["--", videoInfo.VideoUrl]);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        Log.Information("Downloading HLS Video: {Args}", string.Join(' ', arguments));

        lock (StateLock) { _currentProcess = process; }

        if (_pauseRequested)
        {
            lock (StateLock) { _currentProcess = null; }
            return (false, null);
        }

        process.Start();
        var errorBuilder = new StringBuilder();
        var errorTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                errorBuilder.AppendLine(line);
        });
        var outputTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                ParseYtdlpProgress(line);
        });
        await process.WaitForExitAsync();
        await Task.WhenAll(errorTask, outputTask);
        var error = errorBuilder.ToString().Trim();

        lock (StateLock) { _currentProcess = null; }

        if (_pauseRequested) return (false, null);
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download HLS Video: {exitCode} {URL} {error}", process.ExitCode, videoInfo.VideoUrl, error);
            return (false, $"yt-dlp exited with code {process.ExitCode}");
        }

        await Task.Delay(100);

        var ext = videoInfo.DownloadFormat.ToString().ToLower();
        var fileName = $"{videoInfo.VideoId}.{ext}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Warning("File already exists, canceling...");
            VideoFileValidator.TryDelete(tempMp4);
            return (false, "SkipReasonFileExists");
        }

        if (!File.Exists(tempMp4))
        {
            Log.Error("HLS download produced no output file: {URL}", videoInfo.VideoUrl);
            return (false, "SkipReasonFileNotFound");
        }

        if (!VideoFileValidator.IsLikelyValidVideo(tempMp4))
        {
            Log.Warning("HLS download for {VideoId} failed validation; discarding.", videoInfo.VideoId);
            VideoFileValidator.TryDelete(tempMp4);
            return (false, "SkipReasonInvalidDownload");
        }

        using (CacheManager.PinFile(fileName))
        {
            File.Move(tempMp4, filePath, overwrite: true);
            CacheManager.AddToCache(fileName);
        }
        Log.Information("HLS Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");

        // HLS has no remote thumbnail source — extract a frame from the cached MP4 so
        // history rows show a real image instead of the gray placeholder.
        _ = Task.Run(async () =>
        {
            var thumb = await ThumbnailManager.TryExtractFromVideoAsync(videoInfo.VideoId, filePath);
            if (!string.IsNullOrEmpty(thumb))
                Log.Information("HLS thumbnail extracted: {Path}", thumb);
        });

        return (true, null);
    }

}