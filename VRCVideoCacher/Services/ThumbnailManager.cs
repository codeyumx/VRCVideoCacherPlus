using System.Diagnostics;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

public static class ThumbnailManager
{
    public static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    public static readonly string ThumbnailCacheDir = Path.Join(CacheDir, "thumbnails");

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    static ThumbnailManager()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    public static string GetThumbnailPath(string videoId)
    {
        return Path.Join(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static string? GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = GetThumbnailPath(videoId);
        return File.Exists(localPath) ? localPath : null;
    }

    /// <summary>
    /// Extracts a single frame from a cached video file using the bundled ffmpeg.
    /// Used for sources (e.g. HLS) where no remote thumbnail is available — once the
    /// MP4 is fully cached, we just grab a frame at ~5s in.
    /// </summary>
    public static async Task<string?> TryExtractFromVideoAsync(string videoId, string videoFilePath)
    {
        if (string.IsNullOrEmpty(videoId) || !File.Exists(videoFilePath))
            return null;

        var ffmpeg = YtdlManager.FfmpegPath;
        if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
            return null;

        var thumbnailPath = GetThumbnailPath(videoId);
        if (File.Exists(thumbnailPath))
            return thumbnailPath;

        using var process = new Process
        {
            StartInfo =
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        // -ss before -i = fast seek; -frames:v 1 = single frame; scale to 320 wide.
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss"); process.StartInfo.ArgumentList.Add("5");
        process.StartInfo.ArgumentList.Add("-i"); process.StartInfo.ArgumentList.Add(videoFilePath);
        process.StartInfo.ArgumentList.Add("-frames:v"); process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-vf"); process.StartInfo.ArgumentList.Add("scale=320:-2");
        process.StartInfo.ArgumentList.Add("-q:v"); process.StartInfo.ArgumentList.Add("4");
        process.StartInfo.ArgumentList.Add(thumbnailPath);

        // Must drain both pipes concurrently; otherwise ffmpeg can block writing to
        // stderr once the pipe buffer fills and WaitForExitAsync will hang.
        Task? drainOut = null;
        Task? drainErr = null;
        try
        {
            process.Start();
            drainOut = process.StandardOutput.ReadToEndAsync();
            drainErr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode == 0 && File.Exists(thumbnailPath))
                return thumbnailPath;
        }
        catch
        {
            // Best-effort — thumbnail extraction is non-critical
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
            // Always await the drain tasks so they complete cleanly even on cancellation.
            try { if (drainOut is not null && drainErr is not null) await Task.WhenAll(drainOut, drainErr); }
            catch { /* pipes already closed by Kill */ }
        }
        return null;
    }

    public static async Task<string?> TrySaveThumbnail(string videoId, string url)
    {
        try
        {
            var thumbnailPath = GetThumbnailPath(videoId);
            if (File.Exists(thumbnailPath))
                return null;

            var data = await HttpClient.GetStreamAsync(url);
            await using var fileStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write);
            await data.CopyToAsync(fileStream);
            return thumbnailPath;
        }
        catch
        {
            // Silently fail - thumbnail is not critical
            return null;
        }
    }
}
