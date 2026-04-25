using VRCVideoCacher.YTDL.SiteHandlers.Sites;

namespace VRCVideoCacher.YTDL.SiteHandlers;

public static class SiteHandlerRegistry
{
    private static readonly List<ISiteHandler> Handlers =
    [
        new YouTubeHandler(),
        new PyPyDanceHandler(),
        new VRDancingHandler(),
        new HlsHandler(),
        // fallthrough last
        new GenericHandler(),
    ];
    
    // Rewriters run first, in order, before handler resolution
    private static readonly List<ISiteHandler> Rewriters =
    [
        new NicoVideoHandler(),   // rewrites nico.ms → nicovideo.life
        new YTSHandler(),      // rewrites /sr/ → /yt/
        new ThirdPartyYTResolver(), // dmn.moe, u2b.cx etc → real YT url
        new HlsHandler(),         // Dropbox ?dl=0 → ?dl=1, GDrive /file/d/<id>/view → /uc?...
    ];
    
    public static async Task<string> ApplyRewrites(string url)
    {
        foreach (var rewriter in Rewriters)
        {
            var uri = VideoId.ToUri(url);
            if (uri == null) return url;
            url = await rewriter.RewriteUrl(url, uri);
        }
        return url;
    }

    public static ISiteHandler? Resolve(Uri uri) => Handlers.FirstOrDefault(h => h.CanHandle(uri));

    /// <summary>
    /// Like <see cref="Resolve"/>, but when only the GenericHandler would match,
    /// performs an async content probe to detect HLS manifests served under
    /// arbitrary URLs (no .m3u8 extension, unknown hosts).
    /// </summary>
    public static async Task<ISiteHandler?> ResolveAsync(string url, Uri uri)
    {
        var handler = Handlers.FirstOrDefault(h => h.CanHandle(uri));
        if (handler is not null and not GenericHandler)
            return handler;

        // Skip the network probe for URLs that clearly aren't HLS (plain .mp4, images, etc.).
        if (HlsHandler.LooksObviouslyNotHls(uri))
            return handler;

        // Skip the probe entirely when HLS caching is off — detection is only worth the
        // up-to-5s GET if we're actually going to do something special with the URL.
        // The flag is checked per-call (not memoized), so flipping it on takes effect on
        // the next play of any URL that wasn't probed while it was off.
        if (!ConfigManager.Config.CacheHlsPlaylists)
            return handler;

        // LooksLikeStreamable returns true for HLS manifests AND raw progressive MPEG-TS
        // streams — both are routed through HlsHandler, which surfaces the distinction
        // via TryGetCachedProbe so cache-gating can apply the right rule.
        if (await HlsHandler.LooksLikeStreamable(url))
            return Handlers.OfType<HlsHandler>().First();

        return handler; // GenericHandler or null
    }

    public static bool HasSpecificHandler(Uri uri) =>
        Handlers.Any(h => h is not GenericHandler && h.CanHandle(uri));
}
