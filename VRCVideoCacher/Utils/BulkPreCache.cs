using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher.Utils;

public class BulkPreCache
{
    private static readonly ILogger Log = Program.Logger.ForContext<BulkPreCache>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

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
            if (string.IsNullOrEmpty(file.FileName))
                continue;

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
        using var response = await HttpClient.GetAsync(fileInfo.Url);
        if (!response.IsSuccessStatusCode)
        {
            Log.Information("Failed to download {Url}: {ResponseStatusCode}", fileInfo.Url, response.StatusCode);
            return;
        }
        var fileStream = new FileStream(fileInfo.FilePath, FileMode.Create, FileAccess.Write);
        await response.Content.CopyToAsync(fileStream);
        fileStream.Close();
        if (fileInfo.LastModified > 0)
        {
            await Task.Delay(10);
            File.SetLastWriteTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
            File.SetCreationTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
            File.SetLastAccessTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
        }
    }
}