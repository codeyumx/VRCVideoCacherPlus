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
}
