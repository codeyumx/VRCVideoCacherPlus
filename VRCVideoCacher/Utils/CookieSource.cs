namespace VRCVideoCacher.Utils;

// Where the cookie jar on disk came from. Browser is null when the extension could not be
// identified (the old extension from a client that sent no recognisable User-Agent).
public sealed record CookieOrigin(bool IsManual, string? Browser)
{
    public static readonly CookieOrigin Manual = new(true, null);
    public static CookieOrigin Extension(string? browser) => new(false, browser);
}

// Pure helpers for naming the browser a cookie upload came from, and for the one-line
// sidecar file that remembers it next to the jar. Kept free of I/O so the rules can be tested.
public static class CookieSource
{
    // Sent by the VRCVideoCacherPlus extension, e.g. "Google Chrome 131" or "Firefox 132".
    public const string BrowserHeader = "X-VRCVideoCacher-Browser";

    private const int MaxNameLength = 48;
    private const string ManualMarker = "manual";
    private const string ExtensionPrefix = "extension:";

    // The extension's own name wins; the old extension sends none, so fall back to the
    // User-Agent its fetch carries.
    public static string? Identify(string? browserHeader, string? userAgent) =>
        Sanitize(browserHeader) ?? FromUserAgent(userAgent);

    // The header is client-supplied text that ends up in the UI and a file, so keep it to a
    // short single line.
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxNameLength)
            clean = clean[..MaxNameLength].TrimEnd();

        return clean.Length == 0 ? null : clean;
    }

    // Chromium forks append their own token after "Chrome/", so they are checked first.
    // Brave deliberately sends a plain Chrome User-Agent, so it reads as Chrome here; only
    // the new extension's header can tell it apart.
    private static readonly (string Token, string Name)[] UserAgentTokens =
    [
        ("Edg/", "Microsoft Edge"),
        ("OPR/", "Opera"),
        ("Vivaldi/", "Vivaldi"),
        ("Firefox/", "Firefox"),
        ("Chrome/", "Chrome"),
    ];

    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return null;

        foreach (var (token, name) in UserAgentTokens)
        {
            if (userAgent.Contains(token, StringComparison.Ordinal))
                return name;
        }

        return null;
    }

    public static string Serialize(CookieOrigin origin) =>
        origin.IsManual ? ManualMarker : ExtensionPrefix + (origin.Browser ?? string.Empty);

    // Null for a missing or unrecognised sidecar, so the panel shows nothing rather than a guess.
    public static CookieOrigin? Parse(string? text)
    {
        var line = text?.Trim();
        if (line == ManualMarker)
            return CookieOrigin.Manual;

        if (line == null || !line.StartsWith(ExtensionPrefix, StringComparison.Ordinal))
            return null;

        return CookieOrigin.Extension(Sanitize(line[ExtensionPrefix.Length..]));
    }
}
