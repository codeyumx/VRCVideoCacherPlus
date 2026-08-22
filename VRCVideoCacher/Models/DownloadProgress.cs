namespace VRCVideoCacher.Models;

/// <summary>
/// A progress sample for the download currently in flight.
///
/// Speed and time remaining are both optional, and genuinely unknown often enough to matter:
/// yt-dlp reports "Unknown" for the first moments of a transfer, and a server that sends no
/// Content-Length leaves nothing to measure the remainder against. Callers should show the
/// percentage alone in that case rather than inventing a number.
/// </summary>
public readonly record struct DownloadProgress(double Percent, double? BytesPerSecond, TimeSpan? Eta)
{
    public static DownloadProgress AtPercent(double percent) => new(percent, null, null);

    /// <summary>
    /// "1.2 MB/s", or null when the rate isn't known yet.
    /// </summary>
    public string? FormatRate() =>
        BytesPerSecond is > 0 ? $"{Utils.CacheStats.FormatSize((long)BytesPerSecond.Value)}/s" : null;

    /// <summary>
    /// Compact, largest-unit-first time remaining: "45s", "3m 20s", "1h 04m".
    /// </summary>
    public string? FormatEta()
    {
        if (Eta is not { } eta || eta < TimeSpan.Zero)
            return null;

        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}h {eta.Minutes:D2}m";

        return eta.TotalMinutes >= 1
            ? $"{eta.Minutes}m {eta.Seconds:D2}s"
            : $"{Math.Max(eta.Seconds, 1)}s";
    }
}
