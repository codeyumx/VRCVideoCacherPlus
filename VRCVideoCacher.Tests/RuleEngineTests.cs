using System.Text.RegularExpressions;
using VRCVideoCacher.Services;
using Xunit;

namespace VRCVideoCacher.Tests;

// ExpandTemplate is what turns a matched rule into the URL actually requested, so its
// substitution rules are worth pinning down. GetRegex is the shared compile-and-cache used
// by both the request path and the Rules tab's live matcher.
public class RuleEngineTests
{
    private static Match MatchOf(string pattern, string input) =>
        RuleEngine.GetRegex(pattern).Match(input);

    [Fact]
    public void ExpandTemplate_SubstitutesNumberedGroups()
    {
        var match = MatchOf(@"^https?://example\.com/(\w+)/(\w+)$", "https://example.com/a/b");
        Assert.Equal("https://cdn.example.com/b-a", RuleEngine.ExpandTemplate("https://cdn.example.com/$2-$1", "https://example.com/a/b", match));
    }

    [Fact]
    public void ExpandTemplate_SubstitutesAnUnmatchedOptionalGroupAsEmpty()
    {
        // The Dropbox rule depends on this: its trailing (&[^#]*)? group usually does not
        // participate, and must expand to nothing rather than the literal "${2}".
        var match = MatchOf(@"^(a)(b)?$", "a");
        Assert.Equal("a", RuleEngine.ExpandTemplate("${1}${2}", "a", match));
    }

    [Theory]
    [InlineData("{url.scheme}", "https")]
    [InlineData("{url.host}", "example.com")]
    [InlineData("{url.domain}", "example.com")]
    [InlineData("{url.path}", "/videos/clip.mp4")]
    [InlineData("{url.query}", "?id=7&x=y")]
    [InlineData("{url.port}", "443")]
    [InlineData("{url.fragment}", "top")]
    [InlineData("{url.query.id}", "7")]
    [InlineData("{url.query.missing}", "")]
    public void ExpandTemplate_SubstitutesUrlTokens(string template, string expected)
    {
        const string url = "https://example.com/videos/clip.mp4?id=7&x=y#top";
        Assert.Equal(expected, RuleEngine.ExpandTemplate(template, url, MatchOf(".*", url)));
    }

    [Fact]
    public void ExpandTemplate_ReturnsOriginalUrlForAnEmptyTemplate()
    {
        const string url = "https://example.com/a";
        Assert.Equal(url, RuleEngine.ExpandTemplate("", url, MatchOf(".*", url)));
    }

    [Fact]
    public void ExpandTemplate_LeavesTokensAloneForANonAbsoluteUrl()
    {
        // Nothing to parse, so the host token cannot be resolved — it must not throw.
        var exception = Record.Exception(() => RuleEngine.ExpandTemplate("{url.host}", "not a url", MatchOf(".*", "not a url")));
        Assert.Null(exception);
    }

    [Fact]
    public void GetRegex_ReturnsTheSameInstanceForTheSamePattern()
    {
        // Caching is the point: the request path compiles every rule for every URL otherwise.
        Assert.Same(RuleEngine.GetRegex(@"^https://cached\.example/"), RuleEngine.GetRegex(@"^https://cached\.example/"));
    }

    [Fact]
    public void GetRegex_IsCaseInsensitiveAndCultureInvariant()
    {
        var regex = RuleEngine.GetRegex(@"^https://YOUTUBE\.COM/");
        Assert.Matches(regex, "https://youtube.com/watch");

        // Under a Turkish culture, IgnoreCase without CultureInvariant does not fold I to i,
        // so a pattern written in upper case silently stops matching.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Matches(RuleEngine.GetRegex(@"^https://YOUTUBE\.COM/i"), "https://youtube.com/I");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
