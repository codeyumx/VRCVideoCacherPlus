using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// The Cookies panel tells the user which browser their YouTube login came from. A wrong
// name sends them to refresh cookies in the wrong browser, so these pin the precedence and
// the sidecar round-trip.
public class CookieSourceTests
{
    private const string ChromeUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    [Theory]
    [InlineData(ChromeUa, "Chrome")]
    [InlineData(ChromeUa + " Edg/131.0.0.0", "Microsoft Edge")]
    [InlineData(ChromeUa + " OPR/115.0.0.0", "Opera")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0", "Firefox")]
    public void NamesBrowserFromUserAgent(string userAgent, string expected)
    {
        // Chromium forks carry "Chrome/" too; reading it first would mislabel every one of them.
        Assert.Equal(expected, CookieSource.FromUserAgent(userAgent));
    }

    [Fact]
    public void ExtensionHeaderWinsOverUserAgent()
    {
        // Brave sends a plain Chrome User-Agent; only the extension's own name tells it apart.
        Assert.Equal("Brave 131", CookieSource.Identify("Brave 131", ChromeUa));
    }

    [Fact]
    public void FallsBackToUserAgentWhenHeaderIsBlank()
    {
        Assert.Equal("Chrome", CookieSource.Identify("   ", ChromeUa));
        Assert.Null(CookieSource.Identify(null, "curl/8.0"));
    }

    [Fact]
    public void SanitizeKeepsHeaderToOneShortLine()
    {
        Assert.Equal("EvilBrowser", CookieSource.Sanitize("Evil\r\nBrowser"));
        Assert.Equal(48, CookieSource.Sanitize(new string('x', 500))!.Length);
    }

    [Fact]
    public void ManualAndUnknownExtensionStayDistinct()
    {
        // An unidentified extension upload must not read back as a manual paste, or vice versa.
        var manual = CookieSource.Parse(CookieSource.Serialize(CookieOrigin.Manual));
        var unknown = CookieSource.Parse(CookieSource.Serialize(CookieOrigin.Extension(null)));

        Assert.Equal(CookieOrigin.Manual, manual);
        Assert.Equal(CookieOrigin.Extension(null), unknown);
    }

    [Fact]
    public void RoundTripsBrowserName()
    {
        var origin = CookieOrigin.Extension("Google Chrome 131");

        Assert.Equal(origin, CookieSource.Parse(CookieSource.Serialize(origin)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void UnrecognisedSidecarIsUnknown(string? text)
    {
        Assert.Null(CookieSource.Parse(text));
    }
}
