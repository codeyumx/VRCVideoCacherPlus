using VRCVideoCacher.YTDL.SiteHandlers.Sites;
using Xunit;

namespace VRCVideoCacher.Tests;

// The parsed duration and the presence of #EXT-X-ENDLIST are what gate HLS caching: get
// either wrong and yt-dlp is either pointed at a live stream that never ends, or a
// perfectly cacheable video is skipped.
public class HlsManifestTests
{
    private const string CompleteManifest = """
        #EXTM3U
        #EXT-X-VERSION:3
        #EXT-X-TARGETDURATION:10
        #EXTINF:9.009,
        seg0.ts
        #EXTINF:9.009,
        seg1.ts
        #EXTINF:3.003,
        seg2.ts
        #EXT-X-ENDLIST
        """;

    private const string LiveManifest = """
        #EXTM3U
        #EXT-X-VERSION:3
        #EXT-X-MEDIA-SEQUENCE:1740
        #EXTINF:9.009,
        seg1740.ts
        #EXTINF:9.009,
        seg1741.ts
        """;

    [Fact]
    public void SumsSegmentDurationsAndSeesTheEndList()
    {
        var (duration, isComplete) = HlsHandler.ParseMediaPlaylist(CompleteManifest);

        Assert.True(isComplete);
        Assert.NotNull(duration);
        Assert.Equal(21.021, duration!.Value, precision: 3);
    }

    [Fact]
    public void ReportsALivePlaylistAsIncomplete()
    {
        var (duration, isComplete) = HlsHandler.ParseMediaPlaylist(LiveManifest);

        Assert.False(isComplete);
        // A duration is still parsed; it is the completeness flag that gates caching.
        Assert.NotNull(duration);
    }

    [Fact]
    public void ReturnsNoDurationWhenThereAreNoSegments()
    {
        var (duration, isComplete) = HlsHandler.ParseMediaPlaylist("#EXTM3U\n#EXT-X-VERSION:3\n");

        Assert.Null(duration);
        Assert.False(isComplete);
    }

    [Fact]
    public void ParsesDurationsWithAnInvariantDecimalPoint()
    {
        // Manifests always use '.', so parsing must not follow a comma-decimal culture.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var (duration, _) = HlsHandler.ParseMediaPlaylist(CompleteManifest);
            Assert.Equal(21.021, duration!.Value, precision: 3);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void HandlesCrLfLineEndings()
    {
        var (duration, isComplete) = HlsHandler.ParseMediaPlaylist(CompleteManifest.Replace("\n", "\r\n"));

        Assert.True(isComplete);
        Assert.Equal(21.021, duration!.Value, precision: 3);
    }

    [Fact]
    public void ReadsAnOptionalSessionTitle()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-SESSION-DATA:DATA-ID="com.apple.hls.title",VALUE="Some Dance Video"
            #EXTINF:9.0,
            seg0.ts
            #EXT-X-ENDLIST
            """;

        Assert.Equal("Some Dance Video", HlsHandler.ParseSessionTitle(manifest));
    }

    [Fact]
    public void ReturnsNoTitleWhenTheTagIsAbsent() =>
        Assert.Null(HlsHandler.ParseSessionTitle(CompleteManifest));

    [Theory]
    [InlineData("https://cdn.example.com/v/clip.mp4", true)]
    [InlineData("https://cdn.example.com/v/clip.WEBM", true)]
    [InlineData("https://cdn.example.com/v/thumb.jpg", true)]
    [InlineData("https://cdn.example.com/v/clip.m3u8", false)]
    [InlineData("https://cdn.example.com/v/stream", false)]
    public void LooksObviouslyNotHls_SkipsTheProbeForKnownMediaExtensions(string url, bool expected) =>
        Assert.Equal(expected, HlsHandler.LooksObviouslyNotHls(new Uri(url)));

    [Theory]
    [InlineData("https://cdn.example.com/v/clip.m3u8", true)]
    [InlineData("https://cdn.example.com/v/clip.M3U8", true)]
    [InlineData("https://cdn.example.com/v/clip.mp4", false)]
    public void CanHandle_FastPathsOnTheM3u8Extension(string url, bool expected) =>
        Assert.Equal(expected, new HlsHandler().CanHandle(new Uri(url)));
}
