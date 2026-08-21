using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

public enum CacheChangeType
{
    Added,
    Removed,
    Cleared
}

public class CacheManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<CacheManager>();
    private static readonly ConcurrentDictionary<string, VideoCache> CachedAssets = new();
    public static readonly string CachePath;

    // Events for UI
    public static event Action<string, CacheChangeType>? OnCacheChanged;

    static CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Config.CachedAssetPath))
            CachePath = Path.Join(GetSystemCacheFolder(), "CachedAssets");
        else if (Path.IsPathRooted(ConfigManager.Config.CachedAssetPath))
            CachePath = ConfigManager.Config.CachedAssetPath;
        else
            CachePath = Path.Join(Program.CurrentProcessPath, ConfigManager.Config.CachedAssetPath);

        Log.Debug("Using cache path {CachePath}", CachePath);

        // Re-check the size budget whenever the config changes. ConfigManager used to call
        // TryFlushCache directly at the end of its own initialiser, which re-entered this
        // type before CachePath had been assigned.
        ConfigManager.OnConfigChanged += TryFlushCache;

        // A failure in here surfaces as a TypeInitializationException from whatever
        // happened to touch CacheManager first, which is close to undebuggable. An
        // unreadable cache directory should degrade to an empty index, not that.
        try
        {
            BuildCache();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to index the cache directory {CachePath}", CachePath);
        }
    }

    private static string GetSystemCacheFolder()
    {
        if (OperatingSystem.IsWindows())
            return Program.DataPath;

        var cachePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cachePath))
            cachePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

        return Path.Join(cachePath, "VRCVideoCacher");
    }

    public static void Init()
    {
        TryFlushCache();
    }

    // The only file names this directory ever owns are "<videoId>.mp4" and "<videoId>.webm".
    // Anything else in there belongs to someone else — the web server writes index.html into
    // it, and users put things in it — so it must not be treated as a cache entry, validated
    // as a video, or deleted.
    private static readonly string[] CacheFileExtensions = [".mp4", ".webm"];

    private static bool IsCacheFile(string fileName) =>
        CacheFileExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    private static void BuildCache()
    {
        CachedAssets.Clear();
        Directory.CreateDirectory(CachePath);
        var files = Directory.GetFiles(CachePath);
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);

            // Skip the downloader's per-videoId scratch files; the downloader sweeps these.
            if (file.StartsWith("_tempVideo.", StringComparison.Ordinal))
                continue;

            // Previously every file here was validated as a video and deleted if it failed,
            // which meant index.html was destroyed and recreated on every single launch —
            // with a "Removed invalid cache entry" warning each time — and anything a user
            // had put in the folder was deleted without asking.
            if (!IsCacheFile(file))
                continue;

            // Self-heal: if a previous session committed a tiny error body or otherwise
            // corrupt file into the cache, drop it so we re-download instead of serving
            // 166-byte garbage to VRChat forever.
            if (!VideoFileValidator.IsLikelyValidVideo(path))
            {
                try
                {
                    File.Delete(path);
                    Log.Warning("Removed invalid cache entry on startup: {File}", file);
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to remove invalid cache entry {File}: {Err}", file, ex.Message);
                }
                continue;
            }

            // Index without flushing per file: AddToCache calls TryFlushCache, which walks
            // and sorts the whole dictionary, so using it here made startup quadratic in
            // the number of cached videos. One flush at the end does the same job.
            IndexCacheFile(file);
        }

        TryFlushCache();
    }

    public static void TryFlushCache()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;

        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var recentPlayHistory = DatabaseManager.GetPlayHistory();

        // LRU eviction — LastModified is updated on every cache hit, so it acts as "last accessed"
        var lru = CachedAssets
            .OrderBy(kvp => kvp.Value.LastModified)
            .ToList();

        foreach (var kvp in lru)
        {
            if (cacheSize < maxCacheSize)
                break;

            var videoId = Path.GetFileNameWithoutExtension(kvp.Value.FileName);
            var filePath = Path.Join(CachePath, kvp.Value.FileName);
            if (File.Exists(filePath))
            {
                // A cache file can be open for serving or being written by the downloader.
                // ClearCache already tolerated that; eviction did not, and the exception
                // propagated out through AddToCache into the download-completion path.
                try
                {
                    File.Delete(filePath);
                    cacheSize -= kvp.Value.Size;
                }
                catch (Exception ex)
                {
                    Log.Warning("Could not evict {FileName}: {Error}", kvp.Value.FileName, ex.Message);
                    continue;
                }

                // delete thumbnail if not in recent history
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    try
                    {
                        if (File.Exists(thumbnailPath))
                            File.Delete(thumbnailPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Could not delete thumbnail {Path}: {Error}", thumbnailPath, ex.Message);
                    }
                }
            }
            CachedAssets.TryRemove(kvp.Key, out _);
        }
    }

    public static void AddToCache(string fileName)
    {
        if (!IndexCacheFile(fileName))
            return;

        OnCacheChanged?.Invoke(fileName, CacheChangeType.Added);
        TryFlushCache();
    }

    /// <summary>
    /// Records a file's size and timestamp in the index. Returns false if it has gone.
    /// Split out from <see cref="AddToCache"/> so bulk indexing can skip the per-file
    /// event and size-budget check.
    /// </summary>
    private static bool IndexCacheFile(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return false;

        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = fileName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        var existingCache = CachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;
        return true;
    }

    private static long GetCacheSize()
    {
        var totalSize = 0L;
        foreach (var cache in CachedAssets)
        {
            totalSize += cache.Value.Size;
        }

        return totalSize;
    }

    // Public accessors for UI
    public static IReadOnlyDictionary<string, VideoCache> GetCachedAssets()
        => CachedAssets.ToDictionary(k => k.Key, v => v.Value);

    public static long GetTotalCacheSize() => GetCacheSize();

    public static int GetCachedVideoCount() => CachedAssets.Count;

    public static void DeleteCacheItem(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return;

        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            // Reached from the request path via EnsureValidOrEvict, where the file may
            // still be open. Leave the entry in place and try again on the next read.
            Log.Warning("Could not delete cached video {FileName}: {Error}", fileName, ex.Message);
            return;
        }

        CachedAssets.TryRemove(fileName, out _);
        OnCacheChanged?.Invoke(fileName, CacheChangeType.Removed);
        Log.Information("Deleted cached video: {FileName}", fileName);
    }

    public static void ClearCache()
    {
        var recentPlayHistory = DatabaseManager.GetPlayHistory();
        var files = CachedAssets.Keys.ToList();
        foreach (var fileName in files)
        {
            var filePath = Path.Join(CachePath, fileName);
            if (!File.Exists(filePath))
                continue;

            try
            {
                File.Delete(filePath);

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(fileName);
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    if (File.Exists(thumbnailPath))
                        File.Delete(thumbnailPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete {FileName}: {Error}", fileName, ex.ToString());
            }
        }
        CachedAssets.Clear();
        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }
}