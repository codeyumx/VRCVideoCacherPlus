using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// Gates every retry in the resolve path. Getting it wrong is costly in both directions:
// too broad and a recoverable video stops being retried, too narrow and a deleted one
// costs four yt-dlp launches while VRChat waits.
public class TerminalFailureTests
{
    [Theory]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Video unavailable")]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access to this video")]
    [InlineData("ERROR: [youtube] abc: This video has been removed by the uploader")]
    [InlineData("ERROR: [youtube] abc: Join this channel to get access to members-only content")]
    [InlineData("ERROR: [youtube] abc: This channel does not exist.")]
    [InlineData("ERROR: [youtube] abc: This account has been terminated")]
    [InlineData("ERROR: [youtube:truncated_id] abc: Incomplete YouTube ID")]
    [InlineData("ERROR: Unsupported URL: https://example.com/thing")]
    public void RecognisesFailuresNoRetryCanFix(string error)
    {
        Assert.True(IsTerminal(error), error);
    }

    [Theory]
    // Format problems are exactly what the non-AVPro and android retries exist for.
    [InlineData("ERROR: Requested format is not available. Use --list-formats for a list of available formats")]
    // A bot check can succeed on a different client, so it stays retryable.
    [InlineData("ERROR: Sign in to confirm you're not a bot")]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden")]
    [InlineData("ERROR: [youtube] abc: Unable to extract player response")]
    [InlineData("")]
    [InlineData("   ")]
    public void LeavesRecoverableFailuresRetryable(string error)
    {
        Assert.False(IsTerminal(error), error);
    }

    [Fact]
    public void MatchesRegardlessOfCasing()
    {
        Assert.True(IsTerminal("error: video UNAVAILABLE"));
        Assert.True(IsTerminal("PRIVATE VIDEO"));
    }

    private static bool IsTerminal(string error) => VideoId.IsTerminalFailure(error);
}
