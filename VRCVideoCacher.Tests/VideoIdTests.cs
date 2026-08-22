using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;
using VRCVideoCacher.YTDL.SiteHandlers.Sites;
using Xunit;

namespace VRCVideoCacher.Tests;

// Video id extraction decides the cache file name, so a change here silently invalidates
// every previously cached file for the affected URL shape.
public class VideoIdTests
{
    private static VideoInfo? Resolve(string url) =>
        new YouTubeHandler().GetVideoInfo(url, new Uri(url), avPro: false).GetAwaiter().GetResult();

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&list=PL123")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/v/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    public void ExtractsTheVideoIdFromEveryCommonUrlShape(string url)
    {
        var info = Resolve(url);
        Assert.NotNull(info);
        Assert.Equal("dQw4w9WgXcQ", info!.VideoId);
        Assert.Equal(UrlType.YouTube, info.UrlType);
    }

    [Fact]
    public void CanonicalisesToAWatchUrl()
    {
        // History and the "open source" button must never end up pointing at a playlist.
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Resolve("https://youtube.com/watch?v=dQw4w9WgXcQ&list=PL123")!.VideoUrl);
    }

    [Fact]
    public void PicksTheFormatFromAvPro()
    {
        var handler = new YouTubeHandler();
        const string url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

        Assert.Equal(DownloadFormat.Webm, handler.GetVideoInfo(url, new Uri(url), true).GetAwaiter().GetResult()!.DownloadFormat);
        Assert.Equal(DownloadFormat.MP4, handler.GetVideoInfo(url, new Uri(url), false).GetAwaiter().GetResult()!.DownloadFormat);
    }

    [Fact]
    public void ReturnsNullWhenNoVideoIdIsPresent() =>
        Assert.Null(Resolve("https://www.youtube.com/results?search_query=test"));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", true)]
    [InlineData("https://www.youtube.com/playlist?list=PL123", true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://example.com/watch?list=PL123", false)]
    [InlineData("not a url", false)]
    public void IsYouTubePlaylist_DetectsAListParameterOnAYouTubeHost(string url, bool expected) =>
        Assert.Equal(expected, VideoId.IsYouTubePlaylist(url));

    [Fact]
    public void HashUrl_IsStableAndFileNameSafe()
    {
        const string url = "https://cdn.example.com/path/to/video.mp4?token=abc";
        var hash = VideoId.HashUrl(url);

        Assert.Equal(hash, VideoId.HashUrl(url));
        Assert.NotEqual(hash, VideoId.HashUrl(url + "x"));
        // Becomes a file name, so base64's /, + and = are stripped.
        Assert.DoesNotContain('/', hash);
        Assert.DoesNotContain('+', hash);
        Assert.DoesNotContain('=', hash);
        Assert.True(hash.Length > 0);
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com/a", true)]
    [InlineData("relative/path", false)]
    [InlineData("", false)]
    public void ToUri_AcceptsOnlyAbsoluteUris(string url, bool expected) =>
        Assert.Equal(expected, VideoId.ToUri(url) != null);
}
