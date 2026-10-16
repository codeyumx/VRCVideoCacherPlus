using VRCVideoCacher.Integrations.Hls;
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
        var (duration, isComplete) = HlsIntegration.ParseMediaPlaylist(CompleteManifest);

        Assert.True(isComplete);
        Assert.NotNull(duration);
        Assert.Equal(21.021, duration!.Value, precision: 3);
    }

    [Fact]
    public void ReportsALivePlaylistAsIncomplete()
    {
        var (duration, isComplete) = HlsIntegration.ParseMediaPlaylist(LiveManifest);

        Assert.False(isComplete);
        // A duration is still parsed; it is the completeness flag that gates caching.
        Assert.NotNull(duration);
    }

    [Fact]
    public void ReturnsNoDurationWhenThereAreNoSegments()
    {
        var (duration, isComplete) = HlsIntegration.ParseMediaPlaylist("#EXTM3U\n#EXT-X-VERSION:3\n");

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
            var (duration, _) = HlsIntegration.ParseMediaPlaylist(CompleteManifest);
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
        var (duration, isComplete) = HlsIntegration.ParseMediaPlaylist(CompleteManifest.Replace("\n", "\r\n"));

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

        Assert.Equal("Some Dance Video", HlsIntegration.ParseSessionTitle(manifest));
    }

    [Fact]
    public void ReturnsNoTitleWhenTheTagIsAbsent() =>
        Assert.Null(HlsIntegration.ParseSessionTitle(CompleteManifest));

    [Theory]
    [InlineData("https://cdn.example.com/v/clip.mp4", true)]
    [InlineData("https://cdn.example.com/v/clip.WEBM", true)]
    [InlineData("https://cdn.example.com/v/thumb.jpg", true)]
    [InlineData("https://cdn.example.com/v/clip.m3u8", false)]
    [InlineData("https://cdn.example.com/v/stream", false)]
    public void LooksObviouslyNotHls_SkipsTheProbeForKnownMediaExtensions(string url, bool expected) =>
        Assert.Equal(expected, HlsIntegration.LooksObviouslyNotHls(new Uri(url)));

    [Theory]
    [InlineData("https://cdn.example.com/v/clip.m3u8", true)]
    [InlineData("https://cdn.example.com/v/clip.M3U8", true)]
    [InlineData("https://cdn.example.com/v/clip.mp4", false)]
    public void CanHandle_FastPathsOnTheM3u8Extension(string url, bool expected) =>
        Assert.Equal(expected, new HlsIntegration().CanHandle(new Uri(url)));

    // ProbeKey is both the probe-cache key and the video id source: a query that selects
    // content must split it, while rotating signatures must not.
    [Fact]
    public void ProbeKey_KeepsContentSelectingQueryParams() =>
        Assert.NotEqual(
            HlsIntegration.ProbeKey("https://cdn.example.com/manifest.m3u8?id=movie1"),
            HlsIntegration.ProbeKey("https://cdn.example.com/manifest.m3u8?id=movie2"));

    [Fact]
    public void ProbeKey_IgnoresRotatingSigningParams() =>
        Assert.Equal(
            HlsIntegration.ProbeKey("https://cdn.example.com/manifest.m3u8?id=movie1&token=aaa&Expires=100&Signature=x&Key-Pair-Id=k"),
            HlsIntegration.ProbeKey("https://cdn.example.com/manifest.m3u8?Expires=200&id=movie1&token=bbb&Signature=y&Key-Pair-Id=k"));

    [Fact]
    public void ProbeKey_IgnoresAwsSigV4Params() =>
        Assert.Equal(
            "https://bucket.s3.amazonaws.com/v/clip.m3u8",
            HlsIntegration.ProbeKey("https://bucket.s3.amazonaws.com/v/clip.m3u8?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Date=20260101T000000Z&x-amz-signature=abc"));

    [Fact]
    public void ProbeKey_IsIndependentOfParamOrder() =>
        Assert.Equal(
            HlsIntegration.ProbeKey("https://cdn.example.com/live.m3u8?b=2&a=1"),
            HlsIntegration.ProbeKey("https://cdn.example.com/live.m3u8?a=1&b=2"));

    [Theory]
    [InlineData("https://cdn.example.com/v/clip.m3u8")]
    [InlineData("https://cdn.example.com/v/clip.m3u8?")]
    [InlineData("https://cdn.example.com/v/clip.m3u8?hdnts=exp%3D1~hmac%3Dab&st=1&e=2")]
    [InlineData("https://cdn.example.com/v/clip.m3u8#frag")]
    public void ProbeKey_IsThePathWhenNoContentParamsRemain(string url) =>
        Assert.Equal("https://cdn.example.com/v/clip.m3u8", HlsIntegration.ProbeKey(url));
}
