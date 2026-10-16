using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// The Cookies panel warns the user before their YouTube login lapses. If this parsing is
// wrong the warning is silently absent or wrong, and playback breaks with no notice —
// so these tests pin the column layout and the "no expiry" cases, not just the happy path.
public class CookieFileTests
{
    // domain, includeSubdomains, path, secure, expiry, name, value
    private static string Line(string name, string expiry, string domain = ".youtube.com") =>
        string.Join('\t', domain, "TRUE", "/", "TRUE", expiry, name, "somevalue");

    private const long Expiry2030 = 1893456000; // 2030-01-01T00:00:00Z

    [Fact]
    public void ReadsLoginInfoExpiry()
    {
        var result = CookieFile.ParseLoginExpiryUtc([Line("LOGIN_INFO", Expiry2030.ToString())]);

        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void IgnoresOtherCookiesAndPicksLoginInfo()
    {
        // Regression guard: an earlier non-login cookie must not be mistaken for the
        // login cookie just because it appears first in the file.
        var result = CookieFile.ParseLoginExpiryUtc([
            Line("SID", "1600000000"),
            Line("HSID", "1600000000"),
            Line("LOGIN_INFO", Expiry2030.ToString())
        ]);

        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void SkipsCommentsAndBlankLines()
    {
        var result = CookieFile.ParseLoginExpiryUtc([
            "# Netscape HTTP Cookie File",
            "",
            "   ",
            Line("LOGIN_INFO", Expiry2030.ToString())
        ]);

        Assert.NotNull(result);
    }

    [Fact]
    public void ReadsHttpOnlyLoginInfoRow()
    {
        // "#HttpOnly_" prefixes the domain of an HttpOnly cookie; it is a row, not a comment.
        var result = CookieFile.ParseLoginExpiryUtc([
            "# Netscape HTTP Cookie File",
            Line("LOGIN_INFO", Expiry2030.ToString(), domain: "#HttpOnly_.youtube.com")
        ]);

        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void AsCookieRowStripsHttpOnlyPrefixAndSkipsComments()
    {
        Assert.Equal(".youtube.com\tTRUE", CookieFile.AsCookieRow("#HttpOnly_.youtube.com\tTRUE"));
        Assert.Equal(".youtube.com\tTRUE", CookieFile.AsCookieRow(".youtube.com\tTRUE"));
        Assert.Null(CookieFile.AsCookieRow("# Netscape HTTP Cookie File"));
        Assert.Null(CookieFile.AsCookieRow("   "));
    }

    [Fact]
    public void ReturnsNullForSessionCookie()
    {
        // Expiry 0 means "dies with the browser session" — there is no date to warn about,
        // so the panel must show nothing rather than 1970.
        Assert.Null(CookieFile.ParseLoginExpiryUtc([Line("LOGIN_INFO", "0")]));
    }

    [Fact]
    public void ReturnsNullWhenLoginCookieAbsent()
    {
        Assert.Null(CookieFile.ParseLoginExpiryUtc([Line("SID", Expiry2030.ToString())]));
    }

    [Fact]
    public void ReturnsNullForUnparsableExpiry()
    {
        Assert.Null(CookieFile.ParseLoginExpiryUtc([Line("LOGIN_INFO", "not-a-number")]));
    }

    [Fact]
    public void SkipsTruncatedLines()
    {
        // Fewer than 7 columns: indexing column 5 would throw and take the whole panel down.
        var truncated = string.Join('\t', ".youtube.com", "TRUE", "/", "TRUE");

        Assert.Null(CookieFile.ParseLoginExpiryUtc([truncated]));
    }

    [Fact]
    public void ReturnsNullForEmptyFile()
    {
        Assert.Null(CookieFile.ParseLoginExpiryUtc([]));
    }
}
