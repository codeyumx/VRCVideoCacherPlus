using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Utils;

public static class VrcLogMonitor
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(VrcLogMonitor));

    private static CancellationTokenSource? _cts;
    private static Task? _monitorTask;

    // Compiled Regular Expressions for parsing VRChat log lines
    private static readonly Regex ResolveUrlRegex = new(
        @"^[\d\.:\s\w\-]+\[Video Playback\]\s+(?:Attempting to resolve|Resolving) URL\s+'(?<url>[^']+)'",
        RegexOptions.Compiled);

    private static readonly Regex ResolvedUrlRegex = new(
        @"^[\d\.:\s\w\-]+\[Video Playback\]\s+URL\s+'(?<url>[^']+)'\s+resolved to\s+'(?<resolvedUrl>[^']+)'",
        RegexOptions.Compiled);

    private static readonly Regex AvProOpeningRegex = new(
        @"^[\d\.:\s\w\-]+\[AVProVideo\]\s+Opening\s+(?<url>https?://[^\s\)]+)",
        RegexOptions.Compiled);

    private static readonly Regex AvProPlayingRegex = new(
        @"^[\d\.:\s\w\-]+\[AVProVideo\]\s+Using playback path",
        RegexOptions.Compiled);

    private static readonly Regex AvProErrorRegex = new(
        @"^[\d\.:\s\w\-]+\[AVProVideo\]\s+Error:\s+(?<error>.+)",
        RegexOptions.Compiled);

    private static readonly Regex AvProShutdownRegex = new(
        @"^[\d\.:\s\w\-]+\[AVProVideo\]\s+Shutdown",
        RegexOptions.Compiled);

    public static void Start()
    {
        if (_monitorTask != null) return;

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
        Log.Information("VRChat Log Monitor service started.");
    }

    public static void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                _monitorTask?.Wait(2000);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error stopping log monitor task.");
            }
            _cts.Dispose();
            _cts = null;
            _monitorTask = null;
            Log.Information("VRChat Log Monitor service stopped.");
        }
    }

    private static async Task MonitorLoop(CancellationToken ct)
    {
        string? currentFile = null;
        CancellationTokenSource? tailCts = null;
        Task? tailTask = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var latestFile = FindLatestLogFile();
                if (latestFile != currentFile)
                {
                    // If we were tailing a file, stop it
                    if (tailCts != null)
                    {
                        tailCts.Cancel();
                        if (tailTask != null)
                        {
                            try { await tailTask; } catch { /* ignore task cancellation exception */ }
                        }
                        tailCts.Dispose();
                        tailCts = null;
                        tailTask = null;
                    }

                    currentFile = latestFile;

                    if (currentFile != null)
                    {
                        Log.Information("Now monitoring VRChat log file: {File}", Path.GetFileName(currentFile));
                        tailCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        tailTask = Task.Run(() => TailFileAsync(currentFile, tailCts.Token));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in log monitor directory polling loop.");
            }

            // Check for log file rollover every 5 seconds
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        // Clean up tail task on shutdown
        if (tailCts != null)
        {
            tailCts.Cancel();
            if (tailTask != null)
            {
                try { await tailTask; } catch { }
            }
            tailCts.Dispose();
        }
    }

    private static string? FindLatestLogFile()
    {
        if (string.IsNullOrEmpty(FileTools.YtdlPathVrc))
            return null;

        try
        {
            // Logs are in the parent directory of Tools (which is LocalLow/VRChat/VRChat)
            var parent = Path.GetDirectoryName(FileTools.YtdlPathVrc);
            if (string.IsNullOrEmpty(parent)) return null;

            var logDir = Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
                return null;

            var files = Directory.GetFiles(logDir, "output_log_*.txt");
            if (files.Length == 0) return null;

            // Sort by write time descending to find the latest
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return files[0];
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to resolve latest log file path.");
            return null;
        }
    }

    private static async Task TailFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);

            // Seek to the end of the file so we only tail new output log entries in the current session
            fs.Seek(0, SeekOrigin.End);

            while (!ct.IsCancellationRequested)
            {
                var line = await sr.ReadLineAsync(ct);
                if (line != null)
                {
                    ParseLogLine(line);
                }
                else
                {
                    await Task.Delay(250, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error tailing VRChat log file: {File}", Path.GetFileName(filePath));
        }
    }

    private static void ParseLogLine(string line)
    {
        try
        {
            // 1. Check for attempt to resolve URL
            var match = ResolveUrlRegex.Match(line);
            if (match.Success)
            {
                var url = match.Groups["url"].Value;
                Log.Debug("VRChat log: resolve attempt for {Url}", url);
                return;
            }

            // 2. Check for URL resolved
            match = ResolvedUrlRegex.Match(line);
            if (match.Success)
            {
                var url = match.Groups["url"].Value;
                var resolvedUrl = match.Groups["resolvedUrl"].Value;
                ActiveStreamTracker.AssociateUrlInfo(resolvedUrl, url, url, null, null);
                return;
            }

            // 3. Check for AVPro Opening URL
            match = AvProOpeningRegex.Match(line);
            if (match.Success)
            {
                var url = match.Groups["url"].Value;
                Log.Information("VRChat log: AVPro opening {Url}", url);

                var session = new ActiveVideoSession
                {
                    ResolvedUrl = url,
                    StartTime = DateTime.UtcNow,
                    Status = "Loading"
                };

                if (ActiveStreamTracker.TryGetUrlInfo(url, out var info))
                {
                    session.Title = info.Title;
                    session.OriginalUrl = info.OriginalUrl;
                    session.VideoId = info.VideoId;
                    session.Duration = info.Duration;
                }
                else
                {
                    session.Title = url;
                    session.OriginalUrl = url;
                }

                // Resolve IP address in background
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !url.Contains("localhost") && !url.Contains("127.0.0.1"))
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host);
                            if (addresses.Length > 0)
                            {
                                session.RemoteIp = addresses[0].ToString();
                                ActiveStreamTracker.AddOrUpdateSession(session);
                            }
                        }
                        catch { }
                    });
                }

                ActiveStreamTracker.AddOrUpdateSession(session);
                return;
            }

            // 4. Check for AVPro Playback Started
            if (AvProPlayingRegex.IsMatch(line))
            {
                Log.Information("VRChat log: AVPro playback started.");
                ActiveStreamTracker.UpdateSessionStatus(string.Empty, "Playing", DateTime.UtcNow);
                return;
            }

            // 5. Check for AVPro Loading Error
            match = AvProErrorRegex.Match(line);
            if (match.Success)
            {
                var error = match.Groups["error"].Value;
                Log.Warning("VRChat log: AVPro load error: {Error}", error);
                ActiveStreamTracker.UpdateSessionStatus(string.Empty, "Failed");
                return;
            }

            // 6. Check for AVPro Shutdown (e.g. world change or app close)
            if (AvProShutdownRegex.IsMatch(line))
            {
                Log.Information("VRChat log: AVPro shutdown. Clearing active sessions.");
                ActiveStreamTracker.ClearAllSessions();
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error parsing log line: {Line}", line);
        }
    }
}
