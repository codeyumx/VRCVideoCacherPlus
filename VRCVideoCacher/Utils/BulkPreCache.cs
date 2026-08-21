using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Utils;

public class BulkPreCache
{
    private static readonly ILogger Log = Program.Logger.ForContext<BulkPreCache>();

    // Generous but bounded: these are whole video files from a user-configured mirror, and
    // the 100s default cancelled anything large. Unlike the main download path there is no
    // per-read stall guard here, so an overall ceiling is what stops a dead mirror from
    // hanging the startup pre-cache indefinitely.
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromMinutes(30)
    };

    /// <summary>
    /// Manifest-supplied names are untrusted. Joining one straight onto the cache directory
    /// let an entry like "../../Startup/evil.exe" write anywhere the process can reach.
    /// Every legitimate entry is a bare "&lt;videoId&gt;.mp4", so require exactly that.
    /// </summary>
    internal static bool IsSafeCacheFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // Rejects any directory component, absolute paths, and drive-relative forms.
        if (fileName != Path.GetFileName(fileName))
            return false;

        if (fileName is "." or "..")
            return false;

        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    // FileName and Url are required
    // LastModified and Size are optional
    // e.g. JSON response
    // [{"fileName":"--QOnlGckhs.mp4","url":"https:\/\/example.com\/--QOnlGckhs.mp4","lastModified":1631653260,"size":124029113},...]
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class DownloadInfo(string fileName, string url, double lastModified, long size)
    {
        public string FileName { get; set; } = fileName;
        public string Url { get; set; } = url;
        public double LastModified { get; set; } = lastModified;
        public long Size { get; set; } = size;

        public DateTime LastModifiedDate => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(LastModified);
        public string FilePath => Path.Join(CacheManager.CachePath, FileName);
    }

    public static Task DownloadFileList() =>
        ProcessManifests(ConfigManager.Config.PreCacheUrls, FetchManifest, DownloadVideos);

    // Returns null when the manifest could not be fetched.
    private static async Task<string?> FetchManifest(string url)
    {
        using var response = await HttpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();

        Log.Information("Failed to download {Url}: {ResponseStatusCode}", url, response.StatusCode);
        return null;
    }

    // Every failure mode here is per-manifest: one unreachable host, one 404, or one
    // corrupt payload must not stop the manifests after it in the list.
    // Fetch and download are injected so that loop behaviour can be tested without network.
    internal static async Task ProcessManifests(
        IEnumerable<string> urls,
        Func<string, Task<string?>> fetchManifest,
        Func<List<DownloadInfo>, Task> downloadFiles)
    {
        foreach (var url in urls)
        {
            List<DownloadInfo>? files;
            try
            {
                var content = await fetchManifest(url);
                if (content == null)
                    continue;

                files = JsonConvert.DeserializeObject<List<DownloadInfo>>(content);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to read manifest {Url}: {Error}", url, ex.Message);
                continue;
            }

            if (files == null || files.Count == 0)
            {
                Log.Information("No files to download for {URL}", url);
                continue;
            }

            try
            {
                await downloadFiles(files);
                Log.Information("All {Count} files for {URL} are up to date.", files.Count, url);
            }
            catch (Exception ex)
            {
                // DownloadFileList is awaited during startup, and in console mode there is
                // nothing above it to catch — one unreachable mirror must not be fatal.
                Log.Warning("Failed while downloading files for {Url}: {Error}", url, ex.Message);
            }
        }
    }

    private static async Task DownloadVideos(List<DownloadInfo> files)
    {
        var fileCount = files.Count;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (!IsSafeCacheFileName(file.FileName))
            {
                Log.Warning("Skipping manifest entry with an unsafe file name: {FileName}", file.FileName);
                continue;
            }

            try
            {
                if (File.Exists(file.FilePath))
                {
                    var fileInfo = new FileInfo(file.FilePath);
                    var lastWriteTime = File.GetLastWriteTimeUtc(file.FilePath);
                    if ((file.LastModified > 0 && file.LastModifiedDate != lastWriteTime) ||
                        (file.Size > 0 && file.Size != fileInfo.Length))
                    {
                        var percentage = Math.Round((double)index / fileCount * 100, 2);
                        Log.Information("Progress: {Percentage}%", percentage);
                        Log.Information("Updating {FileName}", file.FileName);
                        await DownloadFile(file);
                    }
                }
                else
                {
                    var percentage = Math.Round((double)index / fileCount * 100, 2);
                    Log.Information("Progress: {Percentage}%", percentage);
                    Log.Information("Downloading {FileName}", file.FileName);
                    await DownloadFile(file);
                }
            }
            catch (Exception ex)
            {
                // Was HttpRequestException only, so a timeout (TaskCanceledException) or a
                // disk error aborted the whole manifest instead of skipping one file.
                Log.Warning("Error downloading {FileName}: {ExMessage}", file.FileName, ex.Message);
            }
        }
    }

    private static async Task DownloadFile(DownloadInfo fileInfo)
    {
        // ResponseHeadersRead so the body streams to disk instead of being buffered whole
        // in memory — these entries are full video files.
        using var response = await HttpClient.GetAsync(fileInfo.Url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            Log.Information("Failed to download {Url}: {ResponseStatusCode}", fileInfo.Url, response.StatusCode);
            return;
        }

        // Stage under the _tempVideo. prefix that BuildCache skips and SweepStaleTempFiles
        // cleans up, then validate before publishing. Writing straight to the final name
        // meant a mirror answering 200 with an HTML error page put that page into the cache
        // under a .mp4 name, to be served to VRChat until someone noticed.
        var tempPath = Path.Join(CacheManager.CachePath, $"_tempVideo.{fileInfo.FileName}");
        try
        {
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!VideoFileValidator.IsLikelyValidVideo(tempPath, contentType))
            {
                Log.Warning("Discarding {FileName}: the downloaded body does not look like a video.", fileInfo.FileName);
                VideoFileValidator.TryDelete(tempPath);
                return;
            }

            File.Move(tempPath, fileInfo.FilePath, overwrite: true);
        }
        catch
        {
            VideoFileValidator.TryDelete(tempPath);
            throw;
        }

        // Pinned across the timestamp rewrite and the publish. These files deliberately
        // carry the manifest's own mtime, which can be years old, so the moment they are
        // indexed they are the least-recently-used entry in the cache — without the pin,
        // AddToCache's size-budget flush would delete what was just downloaded.
        using (CacheManager.PinFile(fileInfo.FileName))
        {
            if (fileInfo.LastModified > 0)
            {
                File.SetLastWriteTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
                File.SetCreationTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
                File.SetLastAccessTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
            }

            // Register with the cache, so these files count against the size budget and take
            // part in LRU eviction like every other cached video. They used to be invisible to
            // CacheManager entirely until the next restart rebuilt the index.
            CacheManager.AddToCache(fileInfo.FileName);
        }
    }
}