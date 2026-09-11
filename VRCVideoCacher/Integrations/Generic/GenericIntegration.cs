using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Integrations.Generic;

/// <summary>
/// The fallback for any URL nothing else claims: hash the URL for a stable cache id and let
/// yt-dlp's generic extractor deal with the rest. Must stay last in the registry.
/// </summary>
public class GenericIntegration : Integration
{
    private static readonly ILogger Log = Program.Logger.ForContext<GenericIntegration>();

    public override bool CanHandle(Uri uri) => true;

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        Log.Information("No specific integration for URL, using generic: {URL}", url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = VideoId.HashUrl(url),
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }
}
