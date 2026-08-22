using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Tracks active video streams being served to VRChat.
/// Downloads are deferred until all known streams have likely finished
/// (based on video duration) plus the configured idle buffer.
/// </summary>
public static class ActiveStreamTracker
{
    /// <summary>
    /// Fired on the thread pool whenever a new streaming URL is served.
    /// VideoDownloader subscribes to this to pause active downloads immediately.
    /// </summary>
    public static event Action? OnStreamingActivity;

    private static readonly object Lock = new();

    private static readonly HashSet<string> _activeVideoIps = new();
    private static readonly object IpsLock = new();

    /// <summary>
    /// When the stream currently being served is expected to finish. If no duration is
    /// known this is just the time it started, and the idle buffer alone governs the delay.
    ///
    /// This was a dictionary keyed by video id, but RecordActivity cleared it on every call
    /// — a new stream means the user moved on — so it never held more than one entry, and
    /// the "latest end across all active streams" scan in IsIdle could only ever see that
    /// one. A single field says the same thing without implying otherwise.
    /// </summary>
    private static DateTime _expectedEndOfCurrentStream = DateTime.MinValue;

    /// <summary>
    /// Fallback: the last time any activity was recorded, used when
    /// duration is unknown.
    /// </summary>
    private static DateTime _lastActivityAt = DateTime.MinValue;
    private static bool _hasActivity;

    /// <summary>
    /// Record that a video URL was just served to VRChat.
    /// </summary>
    /// <param name="videoId">The video ID being streamed.</param>
    /// <param name="durationSeconds">
    /// Known duration of the video in seconds, or null if unknown.
    /// </param>
    public static void RecordActivity(string? videoId = null, double? durationSeconds = null)
    {
        lock (Lock)
        {
            _lastActivityAt = DateTime.UtcNow;
            _hasActivity = true;

            if (!string.IsNullOrEmpty(videoId))
            {
                // A new stream replaces the previous one rather than stacking with it, so a
                // run of skipped videos doesn't accumulate their durations.
                _expectedEndOfCurrentStream = durationSeconds > 0
                    ? DateTime.UtcNow.AddSeconds(durationSeconds.Value)
                    : DateTime.UtcNow;
            }
        }
        Task.Run(() => OnStreamingActivity?.Invoke());
    }

    /// <summary>
    /// Returns true if all known streams have likely finished playing
    /// and the idle buffer has elapsed.
    /// </summary>
    public static bool IsIdle(int idleSeconds)
    {
        if (idleSeconds <= 0) return true;
        lock (Lock)
        {
            if (!_hasActivity) return true;

            // Idle = past the current video's expected end, plus the buffer. Falls back to
            // the last activity timestamp when that is later, or when no duration is known.
            var latestEnd = _expectedEndOfCurrentStream > _lastActivityAt
                ? _expectedEndOfCurrentStream
                : _lastActivityAt;

            return (DateTime.UtcNow - latestEnd).TotalSeconds >= idleSeconds;
        }
    }

    private static void TrackVideoUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        // Skip local server URLs since localhost/127.0.0.1 on port 9696 is always safely severed
        if (url.Contains("localhost") || url.Contains("127.0.0.1")) return;

        Task.Run(async () =>
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host;
                    if (!string.IsNullOrEmpty(host))
                    {
                        var addresses = await Dns.GetHostAddressesAsync(host);
                        lock (IpsLock)
                        {
                            foreach (var addr in addresses)
                            {
                                _activeVideoIps.Add(addr.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Failed to resolve active stream IP for URL: {Url}", url);
            }
        });
    }

    public static HashSet<string> GetActiveVideoIps()
    {
        lock (IpsLock)
        {
            return new HashSet<string>(_activeVideoIps);
        }
    }

    public static void ClearActiveVideoIps()
    {
        lock (IpsLock)
        {
            _activeVideoIps.Clear();
        }
    }

    private static readonly List<ActiveVideoSession> _activeSessions = new();
    private static readonly object SessionsLock = new();

    private static readonly Dictionary<string, (string Title, string OriginalUrl, string? VideoId, double? Duration)> _urlInfoMap = new();
    private static readonly object MapLock = new();

    public static event Action? OnSessionsChanged;

    public static void AssociateUrlInfo(string resolvedUrl, string originalUrl, string title, string? videoId, double? duration)
    {
        lock (MapLock)
        {
            var info = (title, originalUrl, videoId, duration);
            if (!string.IsNullOrEmpty(resolvedUrl))
                _urlInfoMap[resolvedUrl] = info;
            if (!string.IsNullOrEmpty(originalUrl))
                _urlInfoMap[originalUrl] = info;
        }

        TrackVideoUrl(resolvedUrl);
        TrackVideoUrl(originalUrl);
    }

    public static bool TryGetUrlInfo(string url, out (string Title, string OriginalUrl, string? VideoId, double? Duration) info)
    {
        lock (MapLock)
        {
            if (_urlInfoMap.TryGetValue(url, out info))
                return true;

            foreach (var kv in _urlInfoMap)
            {
                if (url.Contains(kv.Key) || kv.Key.Contains(url))
                {
                    info = kv.Value;
                    return true;
                }
            }
            return false;
        }
    }

    public static void AddOrUpdateSession(ActiveVideoSession session)
    {
        lock (SessionsLock)
        {
            var existing = _activeSessions.FirstOrDefault(s => s.ResolvedUrl == session.ResolvedUrl || s.OriginalUrl == session.OriginalUrl);
            if (existing != null)
            {
                existing.Title = session.Title;
                existing.VideoId = session.VideoId;
                existing.OriginalUrl = session.OriginalUrl;
                existing.ResolvedUrl = session.ResolvedUrl;
                existing.RemoteIp = session.RemoteIp;
                existing.Status = session.Status;
                existing.StartTime = session.StartTime;
                existing.PlaybackStartedTime = session.PlaybackStartedTime;
                existing.Duration = session.Duration;
            }
            else
            {
                _activeSessions.Add(session);
            }
        }
        OnSessionsChanged?.Invoke();
    }

    public static void UpdateSessionStatus(string url, string status, DateTime? playbackStartedTime = null)
    {
        lock (SessionsLock)
        {
            var existing = _activeSessions.FirstOrDefault(s => s.ResolvedUrl == url || s.OriginalUrl == url);
            if (existing == null && status == "Playing")
            {
                existing = _activeSessions.LastOrDefault(s => s.Status == "Loading");
            }

            if (existing != null)
            {
                existing.Status = status;
                if (playbackStartedTime.HasValue)
                {
                    existing.PlaybackStartedTime = playbackStartedTime.Value;
                }
                else if (status == "Playing")
                {
                    existing.PlaybackStartedTime = DateTime.UtcNow;
                }
            }
        }
        OnSessionsChanged?.Invoke();
    }

    public static void RemoveSessionByUrl(string url)
    {
        lock (SessionsLock)
        {
            _activeSessions.RemoveAll(s => s.ResolvedUrl == url || s.OriginalUrl == url);
        }
        OnSessionsChanged?.Invoke();
    }

    public static void ClearAllSessions()
    {
        lock (SessionsLock)
        {
            _activeSessions.Clear();
        }
        OnSessionsChanged?.Invoke();
    }

    public static List<ActiveVideoSession> GetActiveSessions()
    {
        lock (SessionsLock)
        {
            var now = DateTime.UtcNow;
            _activeSessions.RemoveAll(s =>
            {
                if (s.Duration.HasValue && s.PlaybackStartedTime.HasValue)
                {
                    var elapsed = (now - s.PlaybackStartedTime.Value).TotalSeconds;
                    return elapsed > s.Duration.Value + 10;
                }
                return false;
            });
            return new List<ActiveVideoSession>(_activeSessions);
        }
    }
}

public class ActiveVideoSession
{
    public string? VideoId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string ResolvedUrl { get; set; } = string.Empty;
    public string? RemoteIp { get; set; }
    public string Status { get; set; } = string.Empty; // "Loading", "Playing", "Failed"
    public DateTime StartTime { get; set; }
    public DateTime? PlaybackStartedTime { get; set; }
    public double? Duration { get; set; } // in seconds

    public string? ThumbnailUrl => string.IsNullOrEmpty(VideoId) || VideoId == "live"
        ? null
        : $"https://img.youtube.com/vi/{VideoId}/mqdefault.jpg";

    public double CurrentPosition => Status == "Playing" && PlaybackStartedTime.HasValue
        ? Math.Min(Duration ?? double.MaxValue, (DateTime.UtcNow - PlaybackStartedTime.Value).TotalSeconds)
        : 0;
}
