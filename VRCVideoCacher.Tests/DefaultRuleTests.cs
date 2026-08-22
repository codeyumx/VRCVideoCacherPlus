using System.Text.RegularExpressions;
using VRCVideoCacher;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using Xunit;

namespace VRCVideoCacher.Tests;

// Regression tests for the shipped default rules, run through the same regex construction
// and template expansion the request path uses.
//
// The Dropbox rules in particular: the original pattern was
//   ^https?://(?:[a-zA-Z0-9-]+\.)*dropbox\.com/(.*?)(?:\?dl=0)?$   ->  ".../$1?dl=1"
// whose lazy group had to expand past the whole query before the anchor could match, so
// the optional ?dl=0 never participated for a link with more than one parameter. Dropbox's
// current share format is "?rlkey=...&st=...&dl=0", which came out as "...&dl=0?dl=1".
public class DefaultRuleTests
{
    private static UriRule Rule(string name) =>
        DefaultRules.Create().Single(r => r.Name == name);

    /// <summary>
    /// Applies the enabled Rewrite rules in order, exactly as RuleEngine does.
    /// </summary>
    private static string ApplyRewrites(string url)
    {
        foreach (var rule in DefaultRules.Create().Where(r => r.Enabled && r.Action == RuleAction.Rewrite))
        {
            var match = RuleEngine.GetRegex(rule.Pattern).Match(url);
            if (match.Success)
                url = RuleEngine.ExpandTemplate(rule.RedirectTarget, url, match);
        }

        return url;
    }

    [Theory]
    // The modern /scl/fi/ share format, which the previous pattern mangled.
    [InlineData("https://www.dropbox.com/scl/fi/abc/v.mp4?rlkey=k&st=s&dl=0",
                "https://www.dropbox.com/scl/fi/abc/v.mp4?rlkey=k&st=s&dl=1")]
    // The older /s/ format, which it handled correctly.
    [InlineData("https://www.dropbox.com/s/abc/v.mp4?dl=0",
                "https://www.dropbox.com/s/abc/v.mp4?dl=1")]
    // Already a direct link: must be left exactly as-is, not given a second query string.
    [InlineData("https://www.dropbox.com/s/abc/v.mp4?dl=1",
                "https://www.dropbox.com/s/abc/v.mp4?dl=1")]
    // raw=1 is also already direct.
    [InlineData("https://www.dropbox.com/s/abc/v.mp4?raw=1",
                "https://www.dropbox.com/s/abc/v.mp4?raw=1")]
    // Bare path with no query at all gets ?dl=1 appended.
    [InlineData("https://www.dropbox.com/scl/fi/abc/v.mp4",
                "https://www.dropbox.com/scl/fi/abc/v.mp4?dl=1")]
    // dl=0 mid-query, with another parameter after it.
    [InlineData("https://dropbox.com/s/abc/v.mp4?dl=0&raw=1",
                "https://dropbox.com/s/abc/v.mp4?dl=1&raw=1")]
    // Not Dropbox: untouched.
    [InlineData("https://example.com/s/abc/v.mp4?dl=0",
                "https://example.com/s/abc/v.mp4?dl=0")]
    public void DropboxRules_ProduceADirectDownloadUrl(string input, string expected)
    {
        Assert.Equal(expected, ApplyRewrites(input));
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/ABC123/view?usp=sharing",
                "https://drive.google.com/uc?export=download&id=ABC123")]
    [InlineData("https://drive.google.com/file/d/ABC123",
                "https://drive.google.com/uc?export=download&id=ABC123")]
    public void GoogleDriveRule_RewritesToDirectDownload(string input, string expected)
    {
        Assert.Equal(expected, ApplyRewrites(input));
    }

    [Fact]
    public void EveryDefaultRuleHasAValidPattern()
    {
        foreach (var rule in DefaultRules.Create())
        {
            var exception = Record.Exception(() => RuleEngine.GetRegex(rule.Pattern));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void DefaultRuleNamesAreUnique()
    {
        // EnsureDefaultRules tracks seeded defaults by name, so a duplicate name would make
        // one of them unseedable.
        var names = DefaultRules.Create().Select(r => r.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void CatchAllRuleIsLastAndMatchesEverything()
    {
        var rules = DefaultRules.Create();
        var last = rules[^1];

        Assert.Equal("Everything else", last.Name);
        Assert.True(last.Enabled);
        Assert.Matches(new Regex(last.Pattern), "https://anything.example/whatever");
    }

    [Fact]
    public void BlockRickrollsShipsDisabled()
    {
        // Block genuinely prevents playback now, so a fresh install must not silently
        // refuse specific videos.
        Assert.False(Rule("Block Rickrolls").Enabled);
    }

    [Fact]
    public void YouTubeRuleMatchesItsCommonHostForms()
    {
        var regex = RuleEngine.GetRegex(Rule("YouTube").Pattern);

        Assert.Matches(regex, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Matches(regex, "https://youtu.be/dQw4w9WgXcQ");
        Assert.Matches(regex, "https://music.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Matches(regex, "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ");
        Assert.DoesNotMatch(regex, "https://notyoutube.com/watch?v=dQw4w9WgXcQ");
    }
}
