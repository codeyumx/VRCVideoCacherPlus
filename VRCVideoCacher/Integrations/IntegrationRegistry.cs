using VRCVideoCacher.Integrations.Generic;
using VRCVideoCacher.Integrations.Hls;
using VRCVideoCacher.Integrations.PyPyDance;
using VRCVideoCacher.Integrations.Rewriters;
using VRCVideoCacher.Integrations.VRDancing;
using VRCVideoCacher.Integrations.YouTube;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Integrations;

/// <summary>
/// Every integration and rewriter, and the order they are consulted in.
///
/// Adding a site means adding a class in its own folder and one line here.
/// </summary>
public static class IntegrationRegistry
{
    private static readonly List<Integration> Integrations =
    [
        new YouTubeIntegration(),
        new PyPyDanceIntegration(),
        new VRDancingIntegration(),
        new HlsIntegration(),
        // Claims everything, so it must stay last.
        new GenericIntegration(),
    ];

    /// <summary>
    /// Run in order before any integration sees the URL, each seeing the previous one's
    /// output.
    ///
    /// Cloud share-link rewriting (Dropbox, Google Drive) is deliberately absent: it lives
    /// in the default rules, where the user can see and change it.
    /// </summary>
    private static readonly List<UrlRewriter> Rewriters =
    [
        new NicoVideoRewriter(),          // nicovideo.jp / nico.ms → nicovideo.life
        new YtsRewriter(),                // dmn.moe /sr/ → /yt/
        new ThirdPartyYouTubeRewriter(),  // follow redirects to the real YouTube URL
    ];

    public static async Task<string> ApplyRewrites(string url)
    {
        foreach (var rewriter in Rewriters)
        {
            var uri = VideoId.ToUri(url);
            if (uri == null)
                return url;

            url = await rewriter.RewriteAsync(url, uri);
        }

        return url;
    }

    /// <summary>
    /// Picks the integration for a URL. A rule naming one wins over shape matching, which
    /// is how a rule forces an otherwise-unrecognised host through a specific integration.
    /// </summary>
    public static Integration? Resolve(Uri uri, UriRule? matchedRule = null)
    {
        if (matchedRule != null && !string.IsNullOrEmpty(matchedRule.Integration))
        {
            var named = Integrations.FirstOrDefault(i => i.Name == matchedRule.Integration);
            if (named != null)
                return named;
        }

        return Integrations.FirstOrDefault(i => i.CanHandle(uri));
    }

    /// <summary>
    /// Like <see cref="Resolve"/>, but when only the generic fallback would match it runs
    /// a content probe to detect HLS manifests served under arbitrary URLs — no .m3u8
    /// extension, unknown host.
    /// </summary>
    public static async Task<Integration?> ResolveAsync(string url, Uri uri, UriRule? matchedRule = null)
    {
        var integration = Resolve(uri, matchedRule);

        if (integration is not null and not GenericIntegration)
            return integration;

        // Skip the probe for URLs that clearly aren't HLS (plain .mp4, images, etc).
        if (HlsIntegration.LooksObviouslyNotHls(uri))
            return integration;

        // Skip it entirely when HLS caching is off — detection is only worth the up-to-5s
        // GET if we are going to do something with the answer. Checked per call, not
        // memoised, so enabling the setting takes effect on the next play.
        if (!ConfigManager.Config.CacheHlsPlaylists)
            return integration;

        // True for HLS manifests and for raw progressive MPEG-TS; both route through
        // HlsIntegration, which exposes the distinction via TryGetCachedProbe.
        if (await HlsIntegration.LooksLikeStreamable(url))
            return Integrations.OfType<HlsIntegration>().First();

        return integration;
    }

    /// <summary>
    /// Returns the integration that produced a <see cref="VideoInfo"/>, identified by its
    /// <see cref="UrlType"/>.
    ///
    /// Preferred over re-resolving from the URL once a VideoInfo exists: GetVideoInfo
    /// canonicalises as it goes — a /shorts/ link and a bare video id both come back as
    /// /watch?v=... — so re-running rule evaluation against the new form can pick a
    /// different integration than the one that created the record.
    /// </summary>
    public static Integration? ResolveByUrlType(UrlType urlType) => urlType switch
    {
        UrlType.YouTube => Integrations.OfType<YouTubeIntegration>().FirstOrDefault(),
        UrlType.PyPyDance => Integrations.OfType<PyPyDanceIntegration>().FirstOrDefault(),
        UrlType.VRDancing => Integrations.OfType<VRDancingIntegration>().FirstOrDefault(),
        UrlType.Hls => Integrations.OfType<HlsIntegration>().FirstOrDefault(),
        _ => Integrations.OfType<GenericIntegration>().FirstOrDefault(),
    };

    /// <summary>Whether anything other than the generic fallback claims this URL.</summary>
    public static bool HasSpecificIntegration(Uri uri) =>
        Integrations.Any(i => i is not GenericIntegration && i.CanHandle(uri));

    /// <summary>Names selectable from a rule's Integration field.</summary>
    public static IReadOnlyList<string> AvailableIntegrationNames() =>
        Integrations.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n)).ToList()!;
}
