using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// Now Playing and Active Connections label rows by looking a URL up in the tracker. The lookup
// used to fall back to a bidirectional Contains(), so a URL that merely shared a prefix with a
// tracked one adopted its title and duration — two videos on one CDN path showed each other's
// names.
public class ActiveStreamTrackerTests
{
    [Fact]
    public void QueryVariantsOfTheSameUrlResolveToTheSameVideo()
    {
        ActiveStreamTracker.AssociateUrlInfo(
            "https://cdn.example.test/video/one.mp4",
            "https://cdn.example.test/video/one.mp4",
            "One", "one-id", 12);

        Assert.True(ActiveStreamTracker.TryGetUrlInfo("https://cdn.example.test/video/one.mp4?token=abc", out var info));
        Assert.Equal("One", info.Title);
        Assert.Equal("one-id", info.VideoId);
    }

    [Fact]
    public void ALongerUrlSharingThePrefixDoesNotBorrowTheTrackedVideosInfo()
    {
        ActiveStreamTracker.AssociateUrlInfo(
            "https://cdn.example.test/prefix/one.mp4",
            "https://cdn.example.test/prefix/one.mp4",
            "One", "one-id", 12);
        ActiveStreamTracker.AssociateUrlInfo(
            "https://cdn.example.test/prefix/one.mp4-extra",
            "https://cdn.example.test/prefix/one.mp4-extra",
            "Extra", "extra-id", 34);

        Assert.True(ActiveStreamTracker.TryGetUrlInfo("https://cdn.example.test/prefix/one.mp4-extra", out var extra));
        Assert.Equal("Extra", extra.Title);

        // A URL that is not tracked and only overlaps by substring must not resolve at all.
        Assert.False(ActiveStreamTracker.TryGetUrlInfo("https://cdn.example.test/prefix/one", out _));
    }
}
