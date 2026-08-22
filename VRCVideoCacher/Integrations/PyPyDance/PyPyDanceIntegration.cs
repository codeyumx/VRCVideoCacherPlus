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

    public override string Name => "PyPyDance";

    public override bool CanHandle(Uri uri) =>
        Prefixes.Any(prefix => uri.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public override async Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var result = await HttpUtil.HttpClient.SendAsync(request);
            var videoUrl = result.RequestMessage?.RequestUri?.ToString();
            if (string.IsNullOrEmpty(videoUrl))
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL} Response: {Response} - {Data}",
                    url, result.StatusCode, await result.Content.ReadAsStringAsync());
                return null;
            }

            var finalUri = new Uri(videoUrl);
            var fileName = Path.GetFileName(finalUri.LocalPath);
            var videoId = !fileName.Contains('.') ? fileName : fileName.Split('.')[0];

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
