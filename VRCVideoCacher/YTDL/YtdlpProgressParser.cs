using System.Globalization;
using System.Text.RegularExpressions;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Reads yt-dlp's --newline progress output.
///
/// yt-dlp already computes the transfer rate and the time remaining, and it sees every byte
/// — including the ones we never touch, since it writes the file itself. Taking its numbers
/// is both more accurate and far simpler than re-deriving them from the lines we happen to
/// sample. Lines look like:
///
///   [download]   0.0% of   12.34MiB at  Unknown B/s ETA Unknown
///   [download]  45.2% of ~ 12.34MiB at    1.23MiB/s ETA 00:12
///   [download]  45.2% of 12.34MiB at 1.23MiB/s ETA 00:12 (frag 10/22)
///   [download] 100% of 12.34MiB in 00:05
/// </summary>
internal static partial class YtdlpProgressParser
{
    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)%")]
    private static partial Regex PercentPattern();

    // "Unknown B/s" deliberately does not match: the rate is genuinely not known yet.
    [GeneratedRegex(@"\bat\s+(?<value>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?i?B)/s", RegexOptions.IgnoreCase)]
    private static partial Regex RatePattern();

    // Accepts MM:SS and HH:MM:SS. "ETA Unknown" does not match, for the same reason.
    [GeneratedRegex(@"\bETA\s+(?<eta>(?:\d+:)?\d{1,2}:\d{2})\b")]
    private static partial Regex EtaPattern();

    public static bool TryParse(string line, out DownloadProgress progress)
    {
        progress = default;

        if (string.IsNullOrEmpty(line))
            return false;

        var percentMatch = PercentPattern().Match(line);
        if (!percentMatch.Success ||
            !double.TryParse(percentMatch.Groups["value"].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return false;

        progress = new DownloadProgress(percent, ParseRate(line), ParseEta(line));
        return true;
    }

    private static double? ParseRate(string line)
    {
        var match = RatePattern().Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        // yt-dlp uses binary units (KiB/MiB); treat a bare K/M/G the same way.
        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024,
            "GB" or "GIB" => 1024d * 1024 * 1024,
            "TB" or "TIB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };

        return value * multiplier;
    }

    private static TimeSpan? ParseEta(string line)
    {
        var match = EtaPattern().Match(line);
        if (!match.Success)
            return null;

        var parts = match.Groups["eta"].Value.Split(':');

        // Validate the fields rather than relying on the TimeSpan constructor to reject
        // them: it normalises out-of-range values instead of throwing, so a malformed
        // "99:99" would silently become 1h40m39s. Showing no estimate beats a wrong one.
        var seconds = int.Parse(parts[^1]);
        if (seconds >= 60)
            return null;

        var minutes = int.Parse(parts[^2]);
        if (parts.Length == 3 && minutes >= 60)
            return null;

        return parts.Length switch
        {
            2 => new TimeSpan(0, minutes, seconds),
            3 => new TimeSpan(int.Parse(parts[0]), minutes, seconds),
            _ => null
        };
    }
}
