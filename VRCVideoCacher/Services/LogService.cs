using System.Collections.Concurrent;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;
using VRCVideoCacher.Models;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.Services;

public static class LogService
{
    public static event Action<LogEntry>? OnLogEntry;

    // Buffer to store logs before UI subscribes
    private static readonly ConcurrentQueue<LogEntry> LogBuffer = new();
    private const int MaxBufferSize = 500;

    public static void EmitLogEntry(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };

        var source = "Unknown";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var sourceStr = sourceContext.ToString().Trim('"');
            var lastDot = sourceStr.LastIndexOf('.');
            source = lastDot >= 0 ? sourceStr[(lastDot + 1)..] : sourceStr;
        }

        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = level,
            Source = source,
            Message = logEvent.RenderMessage()
        };

        // Add to buffer
        LogBuffer.Enqueue(entry);
        while (LogBuffer.Count > MaxBufferSize)
            LogBuffer.TryDequeue(out _);

        // Emit to subscribers
        OnLogEntry?.Invoke(entry);
    }

    // Get all buffered logs (for UI initialization)
    public static IEnumerable<LogEntry> GetBufferedLogs() => LogBuffer.ToArray();
}

public class UiLogSink : ILogEventSink
{
    private static PopupWindow? _currentPopup;

    // The same error usually repeats — a failing CDN retried once per request, a tool that
    // stays missing. A modal for each occurrence buries the user in dialogs, mid-session,
    // in VR. Identical text is shown at most once per window.
    private static readonly TimeSpan RepeatSuppression = TimeSpan.FromMinutes(1);
    private static readonly object PopupLock = new();
    private static string? _lastMessage;
    private static DateTime _lastShownAt = DateTime.MinValue;

    public void Emit(LogEvent logEvent)
    {
        // Feed the log viewer first, so an entry still reaches it when the popup below is
        // suppressed or the config isn't loaded yet.
        LogService.EmitLogEntry(logEvent);

        if (ConfigManager.Config is not { ErrorPopups: true } || logEvent.Level < LogEventLevel.Error)
            return;

        var message = logEvent.RenderMessage();
        lock (PopupLock)
        {
            var now = DateTime.UtcNow;
            if (message == _lastMessage && now - _lastShownAt < RepeatSuppression)
                return;

            _lastMessage = message;
            _lastShownAt = now;
        }

        var source = logEvent.Properties.TryGetValue("SourceContext", out var sourceContext)
            ? sourceContext.ToString()
            : "Unknown";

        Dispatcher.UIThread.Post(() =>
        {
            // No window yet — an error logged during startup, before the UI exists. The old
            // code force-unwrapped App.MainWindow here, so that threw on the UI thread and
            // brought the application down over a log line.
            var owner = App.MainWindow;
            if (owner == null)
                return;

            if (!owner.IsVisible)
                owner.Show();

            _currentPopup?.Close();
            _currentPopup = new PopupWindow(message)
            {
                Title = $"Error from {source}"
            };
            if (source.Contains("YtdlManager"))
                _currentPopup.SetFolderHint(
                    "You can manually place deno.exe / yt-dlp.exe / ffmpeg.exe here:",
                    Program.UtilsPath);
            _ = _currentPopup.ShowDialog(owner);
        });
    }
}
