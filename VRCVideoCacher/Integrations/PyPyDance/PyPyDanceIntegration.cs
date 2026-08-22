using System.Web;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Integrations.PyPyDance;

/// <summary>
/// api.pypy.dance/video redirects to the real file. The cache id comes from that final
/// file name, while the song id in the original query drives the metadata lookup.
/// </summary>
public class PyPyDanceIntegration : Integration
{
    private static readonly ILogger Log = Program.Logger.ForContext<PyPyDanceIntegration>();
    private static readonly string[] Prefixes = ["http://api.pypy.dance/video", "https://api.pypy.dance/video"];

    private const int MaxRedirects = 5;

    // Redirects are followed by hand because HttpClient will not do this one.
    //
    // api.pypy.dance/video?id=N answers 302 to a plain-http CDN URL, and .NET refuses to
    // auto-follow an https -> http redirect: it stops and returns the 302 with RequestUri
    // still pointing at the original request. Reading the file name off that gave "video"
    // as the id for *every* PyPyDance video, so they all collided on a single cache entry —
    // the first one downloaded was then served for all of them.
    //
    // Upgrading the target to https is not an option: cdn.pypy.dance answers 525 there
    // (its own TLS is misconfigured) and only serves the file over http. The address guard
    // still applies, and the hop count is bounded.
    private static readonly HttpClient RedirectClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = UrlPolicy.GuardedConnectAsync,
    })
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(20)
    };

    public override string Name => "PyPyDance";

    public override bool CanHandle(Uri uri) =>
        Prefixes.Any(prefix => uri.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The cache id is the CDN file's base name, which is the YouTube id the track came
    /// from. Returns null when the URL has no usable file name — notably the un-redirected
    /// "/video" endpoint itself, which must never become an id.
    /// </summary>
    internal static string? DeriveVideoId(Uri finalUri)
    {
        var fileName = Path.GetFileName(finalUri.LocalPath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var videoId = !fileName.Contains('.') ? fileName : fileName.Split('.')[0];

        // "video" is the API endpoint's own path segment: seeing it means the redirect was
        // not followed, and using it would collide every track onto one cache file.
        if (string.IsNullOrEmpty(videoId) || videoId is "." or ".." or "video")
            return null;

        return videoId;
    }

    private static async Task<Uri?> ResolveFinalUrlAsync(string url)
    {
        var current = new Uri(url);

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, current);
            using var response = await RedirectClient.SendAsync(request);

            var location = response.Headers.Location;
            if (location == null)
            {
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("PyPyDance URL {URL} returned {Status} with no redirect.", current, response.StatusCode);
                    return null;
                }

                return current;
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!UrlPolicy.IsFetchableWebUrl(next))
            {
                Log.Warning("PyPyDance redirected to a non-web URL: {URL}", next);
                return null;
            }

            current = next;
        }

        Log.Warning("PyPyDance redirect chain for {URL} exceeded {Max} hops.", url, MaxRedirects);
        return null;
    }

    public override async Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        try
        {
            var finalUri = await ResolveFinalUrlAsync(url);
            if (finalUri == null)
                return null;

            var videoId = DeriveVideoId(finalUri);
            if (videoId == null)
            {
                Log.Error("Could not derive a video ID from the resolved PypyDance URL: {URL}", finalUri);
                return null;
            }

            var videoUrl = finalUri.ToString();

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (!int.TryParse(query.Get("id"), out var songId))
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL}", url);
                return null;
            }

            _ = Task.Run(async () => await PyPyDanceApiService.DownloadMetadata(songId, videoId));

            return new VideoInfo
            {
                VideoUrl = videoUrl,
                VideoId = videoId,
                UrlType = UrlType.PyPyDance,
                DownloadFormat = DownloadFormat.MP4
            };
        }
        catch (Exception ex)
        {
            Log.Error("Failed to get video ID from PypyDance URL {URL}: {Error}", url, ex.Message);
            return null;
        }
    }
}
