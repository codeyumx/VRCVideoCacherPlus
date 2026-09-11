using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Integrations.VRDancing;

/// <summary>
/// VRDancing serves each track from a regional host under a short code. The code drives the
/// metadata lookup; the cache id is a hash of the URL, since there is no id in it to use.
/// </summary>
public class VRDancingIntegration : Integration
{
    private static readonly string[] Prefixes =
    [
        "https://na2.vrdancing.club",
        "https://eu2.vrdancing.club",
        "https://mpegts-beta.vrdancing.club"
    ];

    public override string Name => "VRDancing";

    public override bool CanHandle(Uri uri) =>
        Prefixes.Any(prefix => uri.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var code = url.TrimEnd('/').Split('/').Last();
        var videoId = VideoId.HashUrl(url);

        _ = Task.Run(async () => await VRDancingApiService.DownloadMetadata(code, videoId));

        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.VRDancing,
            DownloadFormat = DownloadFormat.MP4
        });
    }
}
