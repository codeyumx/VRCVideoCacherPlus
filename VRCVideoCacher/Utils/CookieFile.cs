namespace VRCVideoCacher.Utils;

// Pure parsing helpers for the Netscape-format cookie file yt-dlp consumes.
// Kept free of file/static state so the parsing rules can be tested directly.
public static class CookieFile
{
    // Netscape columns: domain, includeSubdomains, path, secure, expiry, name, value
    private const int ExpiryColumn = 4;
    private const int NameColumn = 5;
    private const int MinColumns = 7;

    // LOGIN_INFO is the cookie that carries the YouTube login — it is what IsCookiesValid
    // keys on, and once it lapses the jar stops authenticating regardless of the rest.
    public const string LoginCookieName = "LOGIN_INFO";

    private const string HttpOnlyPrefix = "#HttpOnly_";

    // Returns the LOGIN_INFO expiry, or null when there is no such line, it is a session
    // cookie (expiry 0), or the value doesn't parse. Malformed lines are skipped, matching
    // how ValidateCookiesAsync tolerates them.
    public static DateTime? ParseLoginExpiryUtc(IEnumerable<string> lines)
    {
        foreach (var rawLine in lines)
        {
            var line = AsCookieRow(rawLine);
            if (line == null)
                continue;

            var parts = line.Split('\t');
            if (parts.Length < MinColumns || parts[NameColumn] != LoginCookieName)
                continue;

            if (!long.TryParse(parts[ExpiryColumn], out var unix) || unix <= 0)
                continue;

            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        }

        return null;
    }

    // Returns the line as a cookie row, or null for blank lines and comments. "#HttpOnly_"
    // is not a comment: it is how the Netscape format marks an HttpOnly cookie, prefixed onto
    // the domain column, and browsers export LOGIN_INFO that way. The prefix is stripped so
    // the domain column holds the bare domain.
    public static string? AsCookieRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (line.StartsWith(HttpOnlyPrefix, StringComparison.Ordinal))
            return line[HttpOnlyPrefix.Length..];

        return line.StartsWith('#') ? null : line;
    }
}
