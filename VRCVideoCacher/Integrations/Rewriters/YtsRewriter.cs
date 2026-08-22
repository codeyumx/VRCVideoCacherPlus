using Serilog;

namespace VRCVideoCacher.Integrations.Rewriters;

/// <summary>
/// dmn.moe serves its player under /sr/ and the resolvable stream under /yt/.
/// </summary>
public class YtsRewriter : UrlRewriter
{
    private static readonly ILogger Log = Program.Logger.ForContext<YtsRewriter>();

    private const string Host = "https://dmn.moe";

    public override Task<string> RewriteAsync(string url, Uri uri)
    {
        if (!url.StartsWith(Host, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(url);

        var rewritten = url.Replace("/sr/", "/yt/");
        if (rewritten != url)
            Log.Information("YTS URL detected, modified to: {URL}", rewritten);

        return Task.FromResult(rewritten);
    }
}
