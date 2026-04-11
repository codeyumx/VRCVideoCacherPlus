using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public enum DownloadState { Idle, WaitingForIdle, Downloading, Paused }

public class VideoDownloader
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly string TempDownloadMp4Path;
    private static readonly string TempDownloadWebmPath;

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

    static VideoDownloader()
    {
        TempDownloadMp4Path = Path.Join(CacheManager.CachePath, "_tempVideo.mp4");
        TempDownloadWebmPath = Path.Join(CacheManager.CachePath, "_tempVideo.webm");
        ActiveStreamTracker.OnStreamingActivity += OnStreamingActivity;
        Task.Run(DownloadThread);
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
        while (true)
        {
            await Task.Delay(500);

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
                (success, failReason) = queueItem.UrlType switch
                {
                    UrlType.YouTube => await DownloadYouTubeVideo(queueItem),
                    UrlType.PyPyDance or UrlType.VRDancing => await DownloadVideoWithId(queueItem),
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
    public static int GetQueueCount() => DatabaseManager.GetPendingDownloads().Count;
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

        // Only delete completed temp files; .part files are preserved for -c to resume
        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        var args = new List<string> { "-c", "--newline", "--no-warnings" }; // -c resumes from .part file if killed, --newline for progress parsing

        var rateLimitMBs = PlusConfigManager.Config.CacheDownloadRateLimitMBs;
        if (rateLimitMBs > 0)
            args.Add($"--limit-rate {rateLimitMBs}M");

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

        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";
            args.Add($"-o \"{TempDownloadWebmPath}\"");
            args.Add($"-f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\"");
        }
        else
        {
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add($"-o \"{TempDownloadMp4Path}\"");
            args.Add($"-f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\"");
            args.Add("--remux-video mp4");
        }

        process.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"-- \"{videoId}\"");
        Log.Information("Downloading YouTube Video: {Args}", process.StartInfo.Arguments);

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
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you're not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
            return (false, $"yt-dlp exited with code {process.ExitCode}");
        }
        Thread.Sleep(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(TempDownloadMp4Path)) File.Delete(TempDownloadMp4Path);
                if (File.Exists(TempDownloadWebmPath)) File.Delete(TempDownloadWebmPath);
            }
            catch (Exception ex) { Log.Error("Failed to delete temp file: {ex}", ex.ToString()); }
            return (false, "SkipReasonFileExists");
        }

        if (File.Exists(TempDownloadMp4Path))
            File.Move(TempDownloadMp4Path, filePath, overwrite: true);
        else if (File.Exists(TempDownloadWebmPath))
            File.Move(TempDownloadWebmPath, filePath, overwrite: true);
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return (false, "SkipReasonFileNotFound");
        }

        CacheManager.AddToCache(fileName);
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

    private static async Task ThrottledCopyAsync(Stream source, Stream destination, long bytesPerSecond, long totalBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        var stopwatch = Stopwatch.StartNew();
        long totalBytesRead = 0;

        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;

            if (totalBytes > 0)
                OnDownloadProgress?.Invoke((double)totalBytesRead / totalBytes * 100.0);

            var expectedMs = (double)totalBytesRead / bytesPerSecond * 1000;
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            if (elapsedMs < expectedMs)
                await Task.Delay(TimeSpan.FromMilliseconds(expectedMs - elapsedMs), ct);
        }
    }

    private static async Task CopyWithProgressAsync(Stream source, Stream destination, long totalBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalBytesRead = 0;

        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;

            if (totalBytes > 0)
                OnDownloadProgress?.Invoke((double)totalBytesRead / totalBytes * 100.0);
        }
    }

    private static async Task<(bool Success, string? FailReason)> DownloadVideoWithId(VideoInfo videoInfo)
    {
        if (_pauseRequested) return (false, null);

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;

        // Check for a partial download to resume via HTTP Range
        long resumeFrom = 0;
        if (File.Exists(TempDownloadMp4Path))
        {
            resumeFrom = new FileInfo(TempDownloadMp4Path).Length;
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
            if (resumeFrom > 0)
                request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException) { return (false, null); }

        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            url = response.Headers.Location?.ToString();
            response.Dispose();
            try
            {
                using var req2 = new HttpRequestMessage(HttpMethod.Get, url);
                if (resumeFrom > 0)
                    req2.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                response = await HttpClient.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            }
            catch (OperationCanceledException) { return (false, null); }
        }

        // 416 = server says our range is beyond the file — treat as already complete
        if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
            {
                Log.Error("Failed to download video: {URL} {Status}", url, response.StatusCode);
                response.Dispose();
                return (false, $"HTTP {(int)response.StatusCode}");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                var fileMode = resumeFrom > 0 ? FileMode.Append : FileMode.Create;
                await using var fileStream = new FileStream(TempDownloadMp4Path, fileMode, FileAccess.Write, FileShare.None);

                var totalBytes = (response.Content.Headers.ContentLength ?? 0) + resumeFrom;
                var rateLimitMBs = PlusConfigManager.Config.CacheDownloadRateLimitMBs;
                if (rateLimitMBs > 0)
                    await ThrottledCopyAsync(stream, fileStream, rateLimitMBs * 1024L * 1024L, totalBytes, cts.Token);
                else
                    await CopyWithProgressAsync(stream, fileStream, totalBytes, cts.Token);
            }
            catch (OperationCanceledException)
            {
                response.Dispose();
                return (false, null);
            }
        }

        response.Dispose();
        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath, overwrite: true);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath, overwrite: true);
        }
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return (false, "SkipReasonFileNotFound");
        }

        CacheManager.AddToCache(fileName);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return (true, null);
    }
}
