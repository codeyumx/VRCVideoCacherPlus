using System.Text.RegularExpressions;
using Serilog;

namespace VRCVideoCacher.Integrations.Rewriters;

/// <summary>
/// Redirects niconico links to nicovideo.life, a third-party mirror that yt-dlp can
/// actually resolve.
///
/// That mirror is unofficial and unaffiliated with this project or with niconico, so
/// playing a niconico link tells it what is being watched. Documented under "What does it
/// connect to?" in the README; inherited from upstream VRCVideoCacher.
/// </summary>
public class NicoVideoRewriter : UrlRewriter
{
    private static readonly ILogger Log = Program.Logger.ForContext<NicoVideoRewriter>();

    private const string MirrorHost = "nicovideo.life";

    // Full nicovideo/niconico watch URLs, and the nico.ms short form.
    private static readonly Regex WatchUrl = new(@"^(https?)://(live|www)\.nicovideo\.jp/watch/(.+)$", RegexOptions.Compiled);
    private static readonly Regex ShortUrl = new(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled);

    public override Task<string> RewriteAsync(string url, Uri uri)
    {
        if (!uri.Host.EndsWith("nicovideo.jp") && !uri.Host.EndsWith("nico.ms"))
            return Task.FromResult(url);

        var (match, idGroup) = new[]
        {
            (WatchUrl.Match(url), 3),
            (ShortUrl.Match(url), 2),
        }.FirstOrDefault(candidate => candidate.Item1.Success);

        if (match?.Success != true)
            return Task.FromResult(url);

        var rewritten = $"https://www.{MirrorHost}/watch/{match.Groups[idGroup].Value}";
        Log.Information("Incompatible URL, passing to third-party resolver {Host}: {URL}", MirrorHost, rewritten);
        return Task.FromResult(rewritten);
    }
}
