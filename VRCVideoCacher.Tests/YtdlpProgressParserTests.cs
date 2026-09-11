using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// yt-dlp's --newline progress output is the source of the download ETA, so the shapes it
// actually emits — including the "Unknown" ones early in a transfer — need pinning down.
public class YtdlpProgressParserTests
{
    private static DownloadProgress Parse(string line)
    {
        Assert.True(YtdlpProgressParser.TryParse(line, out var progress), $"failed to parse: {line}");
        return progress;
    }

    [Fact]
    public void ParsesPercentRateAndEta()
    {
        var progress = Parse("[download]  45.2% of ~ 12.34MiB at    1.23MiB/s ETA 00:12");

        Assert.Equal(45.2, progress.Percent, precision: 1);
        Assert.Equal(1.23 * 1024 * 1024, progress.BytesPerSecond!.Value, precision: 0);
        Assert.Equal(TimeSpan.FromSeconds(12), progress.Eta);
    }

    [Fact]
    public void ParsesALineWithAFragmentCounter()
    {
        var progress = Parse("[download]  45.2% of 12.34MiB at 1.23MiB/s ETA 00:12 (frag 10/22)");

        Assert.Equal(45.2, progress.Percent, precision: 1);
        Assert.Equal(TimeSpan.FromSeconds(12), progress.Eta);
    }

    [Fact]
    public void TreatsUnknownRateAndEtaAsAbsent()
    {
        // Emitted for the first moments of a transfer. Reporting a made-up estimate here
        // would be worse than reporting none.
        var progress = Parse("[download]   0.0% of   12.34MiB at  Unknown B/s ETA Unknown");

        Assert.Equal(0.0, progress.Percent);
        Assert.Null(progress.BytesPerSecond);
        Assert.Null(progress.Eta);
    }

    [Fact]
    public void ParsesAnHourLongEta()
    {
        Assert.Equal(new TimeSpan(1, 2, 3), Parse("[download]  5.0% of 4.00GiB at 1.00MiB/s ETA 01:02:03").Eta);
    }

    [Fact]
    public void ParsesTheCompletionLine()
    {
        var progress = Parse("[download] 100% of 12.34MiB in 00:05");

        Assert.Equal(100.0, progress.Percent);
        Assert.Null(progress.Eta);
    }

    [Theory]
    [InlineData("at 512.00B/s", 512d)]
    [InlineData("at 1.50KiB/s", 1.5 * 1024)]
    [InlineData("at 2.00MiB/s", 2d * 1024 * 1024)]
    [InlineData("at 1.00GiB/s", 1024d * 1024 * 1024)]
    public void ConvertsBinaryRateUnits(string fragment, double expected)
    {
        Assert.Equal(expected, Parse($"[download]  10.0% of 1.00GiB {fragment} ETA 00:30").BytesPerSecond!.Value, precision: 0);
    }

    [Theory]
    [InlineData("[download] Destination: /tmp/video.mp4")]
    [InlineData("[info] Writing video metadata")]
    [InlineData("")]
    public void RejectsLinesWithNoPercentage(string line)
    {
        Assert.False(YtdlpProgressParser.TryParse(line, out _));
    }

    [Fact]
    public void IgnoresANonsensicalEtaRatherThanThrowing()
    {
        // 99:99 is not a valid minute/second pair; the percentage still parses.
        Assert.True(YtdlpProgressParser.TryParse("[download]  10.0% of 1.00GiB at 1.00MiB/s ETA 99:99", out var progress));
        Assert.Equal(10.0, progress.Percent);
        Assert.Null(progress.Eta);
    }

    [Fact]
    public void ParsesInvariantlyUnderACommaDecimalCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(45.2, Parse("[download]  45.2% of 12.34MiB at 1.23MiB/s ETA 00:12").Percent, precision: 1);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, 45, "45s")]
    [InlineData(3, 20, "3m 20s")]
    [InlineData(0, 0, "1s")]      // never renders "0s" while still downloading
    public void FormatsEtaCompactly(int minutes, int seconds, string expected)
    {
        var progress = new DownloadProgress(50, null, new TimeSpan(0, minutes, seconds));
        Assert.Equal(expected, progress.FormatEta());
    }

    [Fact]
    public void FormatsAnHourLongEtaWithHours()
    {
        Assert.Equal("1h 04m", new DownloadProgress(50, null, new TimeSpan(1, 4, 30)).FormatEta());
    }

    [Fact]
    public void FormatsRateAsBytesPerSecond()
    {
        Assert.Equal("1.50 MB/s", new DownloadProgress(50, 1.5 * 1024 * 1024, null).FormatRate());
    }

    [Fact]
    public void ReportsNoRateOrEtaWhenUnknown()
    {
        var progress = DownloadProgress.AtPercent(50);
        Assert.Null(progress.FormatRate());
        Assert.Null(progress.FormatEta());
    }
}
