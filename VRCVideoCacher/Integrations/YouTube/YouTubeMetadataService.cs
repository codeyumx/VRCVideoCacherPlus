using System.Text.Json;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Integrations.YouTube;

public static class YouTubeMetadataService
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext(typeof(YouTubeMetadataService));

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<VideoInfoCache?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        try
        {
            // The watch URL is a parameter *value* and has to be escaped as one. Unescaped,
            // its "?v=" started a new parameter of the outer query, so YouTube saw
            // url=https://www.youtube.com/watch with no video id and answered 404 — and the
            // catch below swallowed it, so titles never resolved through this path at all.
            var target = Uri.EscapeDataString($"https://www.youtube.com/watch?v={videoId}");
            var response = await HttpClient.GetStringAsync($"https://www.youtube.com/oembed?url={target}&format=json");

            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("title", out var titleElement))
                return null;
            var title = titleElement.GetString();
            if (string.IsNullOrEmpty(title))
                return null;

            // author_name is optional in the oEmbed response, and a missing one is no
            // reason to throw away a perfectly good title. Reading it unconditionally also
            // threw on the default JsonElement when the property was absent.
            var author = doc.RootElement.TryGetProperty("author_name", out var authorElement)
                ? authorElement.GetString()
                : null;

            var videoInfo = new VideoInfoCache
            {
                Id = videoId,
                Title = title,
                Author = author,
                Type = UrlType.YouTube
            };
            DatabaseManager.AddVideoInfoCache(videoInfo);
            return videoInfo;
        }
        catch (Exception ex)
        {
            // Non-fatal: the UI falls back to showing the video ID. Logged at debug so the
            // next breakage here isn't invisible the way this one was.
            Log.Debug("oEmbed lookup failed for {VideoId}: {Error}", videoId, ex.Message);
        }

        return null;
    }

    public static async Task<string?> GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = ThumbnailManager.GetThumbnailPath(videoId);
        if (File.Exists(localPath))
            return localPath;

        var url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
        var thumbnailPath = await ThumbnailManager.TrySaveThumbnail(videoId, url);
        if (!string.IsNullOrEmpty(thumbnailPath))
            return thumbnailPath;

        return url;
    }

    public static async Task<VideoInfoCache?> GetVideoMetadataAsync(string videoId)
    {
        var cachedInfo = DatabaseManager.GetVideoInfoCache(videoId);

        if (videoId.Length == 11 && (cachedInfo == null || string.IsNullOrEmpty(cachedInfo?.Title)))
            cachedInfo = await GetVideoTitleAsync(videoId);

        return cachedInfo;
    }
}
