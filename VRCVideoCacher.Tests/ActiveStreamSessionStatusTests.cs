using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// VRChat's playback and error log lines carry no URL, so the tracker has to attribute them to
// the session that is still loading. A failed load used to match nothing and leave the card
// showing "Loading" forever.
public class ActiveStreamSessionStatusTests
{
    private static ActiveVideoSession Loading(string url) => new()
    {
        ResolvedUrl = url,
        OriginalUrl = url,
        Title = url,
        Status = "Loading",
        StartTime = DateTime.UtcNow
    };

    [Theory]
    [InlineData("Failed")]
    [InlineData("Playing")]
    public void UrllessStatusUpdateAppliesToTheMostRecentLoadingSession(string status)
    {
        ActiveStreamTracker.ClearAllSessions();
        ActiveStreamTracker.AddOrUpdateSession(Loading("https://cdn.example.test/older.mp4"));
        ActiveStreamTracker.AddOrUpdateSession(Loading("https://cdn.example.test/newer.mp4"));

        ActiveStreamTracker.UpdateSessionStatus(string.Empty, status);

        var sessions = ActiveStreamTracker.GetActiveSessions();
        Assert.Equal("Loading", sessions.Single(s => s.ResolvedUrl.EndsWith("older.mp4")).Status);
        Assert.Equal(status, sessions.Single(s => s.ResolvedUrl.EndsWith("newer.mp4")).Status);

        ActiveStreamTracker.ClearAllSessions();
    }

    // The log monitor resolves a direct stream's address on a detached task. It used to write
    // the result back with AddOrUpdateSession, which re-inserted a session that an AVPro
    // shutdown had cleared in the meantime.
    [Fact]
    public void ALateAddressLookupDoesNotResurrectAClearedSession()
    {
        const string url = "https://cdn.example.test/cleared.mp4";
        ActiveStreamTracker.ClearAllSessions();
        ActiveStreamTracker.AddOrUpdateSession(Loading(url));
        ActiveStreamTracker.ClearAllSessions();

        Assert.False(ActiveStreamTracker.TryUpdateSessionRemoteIp(url, "1.2.3.4"));
        Assert.Empty(ActiveStreamTracker.GetActiveSessions());
    }

    [Fact]
    public void AnAddressLookupUpdatesAStillTrackedSession()
    {
        const string url = "https://cdn.example.test/tracked.mp4";
        ActiveStreamTracker.ClearAllSessions();
        ActiveStreamTracker.AddOrUpdateSession(Loading(url));

        Assert.True(ActiveStreamTracker.TryUpdateSessionRemoteIp(url, "1.2.3.4"));
        var session = Assert.Single(ActiveStreamTracker.GetActiveSessions());
        Assert.Equal("1.2.3.4", session.RemoteIp);
        Assert.Equal("Loading", session.Status);

        ActiveStreamTracker.ClearAllSessions();
    }
}
