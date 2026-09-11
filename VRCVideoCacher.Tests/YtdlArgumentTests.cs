using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// The "additional yt-dlp arguments" setting is one free-form config string, but arguments
// now reach the process through ArgumentList, which needs one token per element. Appending
// the string whole would pass "--retries 3" as a single argument literally named that.
public class YtdlArgumentTests
{
    [Fact]
    public void SplitArguments_ReturnsEmpty_ForNullOrBlank()
    {
        Assert.Empty(YtdlManager.SplitArguments(null));
        Assert.Empty(YtdlManager.SplitArguments(""));
        Assert.Empty(YtdlManager.SplitArguments("   \t "));
    }

    [Fact]
    public void SplitArguments_SplitsOnWhitespace()
    {
        Assert.Equal(["--retries", "3", "--no-mtime"],
            YtdlManager.SplitArguments("--retries 3 --no-mtime"));
    }

    [Fact]
    public void SplitArguments_CollapsesRunsOfWhitespace()
    {
        Assert.Equal(["-a", "-b"], YtdlManager.SplitArguments("  -a \t\t -b  "));
    }

    [Fact]
    public void SplitArguments_KeepsQuotedValueAsOneToken()
    {
        Assert.Equal(["--ffmpeg-location", @"C:\Program Files\ffmpeg\bin"],
            YtdlManager.SplitArguments(@"--ffmpeg-location ""C:\Program Files\ffmpeg\bin"""));
    }

    [Fact]
    public void SplitArguments_SupportsSingleQuotes()
    {
        Assert.Equal(["-f", "bv*[height<=1080]+ba"],
            YtdlManager.SplitArguments("-f 'bv*[height<=1080]+ba'"));
    }

    [Fact]
    public void SplitArguments_JoinsQuotedSectionToAdjacentText()
    {
        // --key="some value" is one argv entry, not two.
        Assert.Equal(["--extractor-args=youtube:player_client=web"],
            YtdlManager.SplitArguments(@"--extractor-args=""youtube:player_client=web"""));
    }

    [Fact]
    public void SplitArguments_TreatsUnterminatedQuoteAsRunningToEnd()
    {
        Assert.Equal(["-o", "unterminated value"],
            YtdlManager.SplitArguments(@"-o ""unterminated value"));
    }

    [Fact]
    public void SplitArguments_PreservesEmptyQuotedArgument()
    {
        Assert.Equal(["-f", ""], YtdlManager.SplitArguments(@"-f """""));
    }
}
