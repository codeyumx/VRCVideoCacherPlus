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
}
