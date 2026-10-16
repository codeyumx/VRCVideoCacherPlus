using VRCVideoCacher.Integrations.YouTube;
using VRCVideoCacher.Models;
using Xunit;

namespace VRCVideoCacher.Tests;

public class YouTubeIntegrationTests
{
    private static Task<VideoInfo?> ResolveAsync(string url) =>
        new YouTubeIntegration().GetVideoInfo(url, new Uri(url), avPro: false);

    [Theory]
    [InlineData("https://www.youtube.com/@LofiGirl/live", "https://www.youtube.com/@LofiGirl/live")]
    [InlineData("https://m.youtube.com/@LofiGirl/live/?si=abc#t", "https://www.youtube.com/@LofiGirl/live")]
    [InlineData("https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow/live", "https://www.youtube.com/channel/UCSJ4gkVC6NrvII8umztf0Ow/live")]
    public async Task ChannelLiveUrl_KeepsAUrlYtdlpCanResolve(string url, string expectedUrl)
    {
        // watch?v=live is not a video; yt-dlp can only resolve the channel's /live page.
        var info = await ResolveAsync(url);

        Assert.NotNull(info);
        Assert.Equal(YouTubeIntegration.LiveVideoId, info!.VideoId);
        Assert.Equal(expectedUrl, info.VideoUrl);
    }

    [Fact]
    public async Task BareLivePath_IsNotAChannelStream() =>
        Assert.Null(await ResolveAsync("https://www.youtube.com/live"));

    [Theory]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ/")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ/extra")]
    public async Task Shorts_TakesTheSegmentAfterShorts(string url)
    {
        var info = await ResolveAsync(url);

        Assert.NotNull(info);
        Assert.Equal("dQw4w9WgXcQ", info!.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", info.VideoUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/shorts/")]
    [InlineData("https://www.youtube.com/shorts/tooShort/")]
    public async Task Shorts_WithoutAValidId_ReturnsNull(string url) =>
        Assert.Null(await ResolveAsync(url));

    [Theory]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtube-nocookie.com/embed/dQw4w9WgXcQ?start=30")]
    public async Task NoCookieEmbed_IsHandledAndParsed(string url)
    {
        Assert.True(new YouTubeIntegration().CanHandle(new Uri(url)));

        var info = await ResolveAsync(url);

        Assert.NotNull(info);
        Assert.Equal("dQw4w9WgXcQ", info!.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", info.VideoUrl);
    }
}
