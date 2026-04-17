using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Database;

public static class DatabaseManager
{
    public static event Action? OnPlayHistoryAdded;
    public static event Action? OnVideoInfoCacheUpdated;
    public static event Action? OnPendingDownloadsChanged;

    private static readonly PooledDbContextFactory<Database> _contextFactory;

    static DatabaseManager()
    {
        Directory.CreateDirectory(Database.CacheDir);

        var options = new DbContextOptionsBuilder<Database>()
            .UseSqlite($"Data Source={Database.DbPath}")
            .EnableSensitiveDataLogging()
            .Options;

        _contextFactory = new PooledDbContextFactory<Database>(options);

        using var db = _contextFactory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS vvc_PendingDownloads (
                Key INTEGER PRIMARY KEY AUTOINCREMENT,
                QueuedAt TEXT NOT NULL,
                VideoUrl TEXT NOT NULL,
                VideoId TEXT NOT NULL,
                UrlType INTEGER NOT NULL,
                DownloadFormat INTEGER NOT NULL
            )
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS vvc_VideoWatchStats (
                VideoId TEXT PRIMARY KEY NOT NULL,
                LastWatchedAt TEXT NOT NULL,
                WatchCount INTEGER NOT NULL DEFAULT 0
            )
            """);
    }

    public static void AddPlayHistory(VideoInfo videoInfo)
    {
        var history = new History
        {
            Timestamp = DateTime.UtcNow,
            Url = videoInfo.VideoUrl,
            Id = videoInfo.VideoId,
            Type = videoInfo.UrlType
        };
        using var db = _contextFactory.CreateDbContext();
        db.PlayHistory.Add(history);
        db.SaveChanges();
        OnPlayHistoryAdded?.Invoke();
    }

    public static void AddVideoInfoCache(VideoInfoCache videoInfoCache)
    {
        if (string.IsNullOrEmpty(videoInfoCache.Id))
            return;

        using var db = _contextFactory.CreateDbContext();
        var existingCache = db.VideoInfoCache.Find(videoInfoCache.Id);
        if (existingCache != null)
        {
            if (string.IsNullOrEmpty(existingCache.Title) &&
                !string.IsNullOrEmpty(videoInfoCache.Title))
                existingCache.Title = videoInfoCache.Title;

            if (string.IsNullOrEmpty(existingCache.Author) &&
                !string.IsNullOrEmpty(videoInfoCache.Author))
                existingCache.Author = videoInfoCache.Author;

            if (existingCache.Duration == null &&
                videoInfoCache.Duration != null)
                existingCache.Duration = videoInfoCache.Duration;
        }
        else
        {
            db.VideoInfoCache.Add(videoInfoCache);
        }
        db.SaveChanges();
        OnVideoInfoCacheUpdated?.Invoke();
    }

    public static List<History> GetPlayHistory(int limit = 50)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static IEnumerable<HistoryItemViewModel> GetVideoHistoryAsCache(int limit = 50, bool distinctOnly = false)
    {
        using var db = _contextFactory.CreateDbContext();
        var query = db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp);

        var historyItems = distinctOnly
            ? query.GroupBy(h => h.Id)
                   .Select(g => g.First())
                   .Take(limit)
            : query.Take(limit);

        // Materialize the database query first
        var histories = historyItems.ToList();

        // Fetch matching VideoInfoCache entries
        var ids = histories.Select(h => h.Id).Where(id => id != null).Distinct().ToList();
        var cacheDict = db.VideoInfoCache
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionary(v => v.Id);

        // Project to ViewModel in-memory
        return histories.Select(h => 
        {
            cacheDict.TryGetValue(h.Id ?? string.Empty, out var meta);
            return new HistoryItemViewModel(h, meta);
        }).ToList();
    }

    public static VideoInfoCache? GetVideoInfoCache(string videoId)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.VideoInfoCache.Find(videoId);
    }

    public static void UpdateVideoWatchStats(string videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return;

        using var db = _contextFactory.CreateDbContext();
        var stats = db.VideoWatchStats.Find(videoId);
        if (stats != null)
        {
            stats.WatchCount++;
            stats.LastWatchedAt = DateTime.UtcNow;
        }
        else
        {
            db.VideoWatchStats.Add(new VideoWatchStats
            {
                VideoId = videoId,
                WatchCount = 1,
                LastWatchedAt = DateTime.UtcNow
            });
        }
        db.SaveChanges();
    }

    public static Dictionary<string, VideoWatchStats> GetAllVideoWatchStats()
    {
        using var db = _contextFactory.CreateDbContext();
        return db.VideoWatchStats
            .AsNoTracking()
            .ToDictionary(v => v.VideoId);
    }

    // --- Pending Downloads ---

    public static void AddPendingDownload(VideoInfo videoInfo)
    {
        using var db = _contextFactory.CreateDbContext();
        var exists = db.PendingDownloads.Any(p =>
            p.VideoId == videoInfo.VideoId && p.DownloadFormat == videoInfo.DownloadFormat);
        if (exists) return;

        db.PendingDownloads.Add(new PendingDownload
        {
            QueuedAt = DateTime.UtcNow,
            VideoUrl = videoInfo.VideoUrl,
            VideoId = videoInfo.VideoId,
            UrlType = videoInfo.UrlType,
            DownloadFormat = videoInfo.DownloadFormat
        });
        db.SaveChanges();
        OnPendingDownloadsChanged?.Invoke();
    }

    public static void RemovePendingDownload(string videoId, DownloadFormat format)
    {
        using var db = _contextFactory.CreateDbContext();
        var item = db.PendingDownloads.FirstOrDefault(p =>
            p.VideoId == videoId && p.DownloadFormat == format);
        if (item == null) return;

        db.PendingDownloads.Remove(item);
        db.SaveChanges();
        OnPendingDownloadsChanged?.Invoke();
    }

    public static void RemovePendingDownloadByKey(int key)
    {
        using var db = _contextFactory.CreateDbContext();
        var item = db.PendingDownloads.Find(key);
        if (item == null) return;

        db.PendingDownloads.Remove(item);
        db.SaveChanges();
        OnPendingDownloadsChanged?.Invoke();
    }

    public static List<PendingDownload> GetPendingDownloads()
    {
        using var db = _contextFactory.CreateDbContext();
        return db.PendingDownloads
            .AsNoTracking()
            .OrderBy(p => p.QueuedAt)
            .ToList();
    }

    public static void BumpToTopOfQueue(int key)
    {
        using var db = _contextFactory.CreateDbContext();
        var item = db.PendingDownloads.Find(key);
        if (item == null) return;

        var earliest = db.PendingDownloads
            .Where(p => p.Key != key)
            .OrderBy(p => p.QueuedAt)
            .Select(p => (DateTime?)p.QueuedAt)
            .FirstOrDefault();

        // Use a fixed epoch so repeated bumps don't drift QueuedAt unboundedly into the past.
        var floor = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = earliest.HasValue && earliest.Value > floor
            ? earliest.Value.AddSeconds(-1)
            : DateTime.UtcNow;
        item.QueuedAt = target;
        db.SaveChanges();
        OnPendingDownloadsChanged?.Invoke();
    }

    public static void ClearPendingDownloads()
    {
        using var db = _contextFactory.CreateDbContext();
        db.PendingDownloads.RemoveRange(db.PendingDownloads);
        db.SaveChanges();
        OnPendingDownloadsChanged?.Invoke();
    }
}
