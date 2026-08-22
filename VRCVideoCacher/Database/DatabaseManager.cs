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

        var optionsBuilder = new DbContextOptionsBuilder<Database>()
            .UseSqlite($"Data Source={Database.DbPath}");
#if DEBUG
        // Puts parameter values — watch history URLs and video ids — into the log output.
        // Useful while developing, not something to ship enabled.
        optionsBuilder = optionsBuilder.EnableSensitiveDataLogging();
#endif
        var options = optionsBuilder.Options;

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

        // A queue entry is identified by (VideoId, DownloadFormat), but nothing enforced
        // that — AddPendingDownload checked for an existing row and then inserted, so two
        // callers racing both saw "absent" and both inserted. Collapse any duplicates an
        // existing database already accumulated, keeping the earliest, then constrain it.
        db.Database.ExecuteSqlRaw("""
            DELETE FROM vvc_PendingDownloads
            WHERE Key NOT IN (
                SELECT MIN(Key) FROM vvc_PendingDownloads GROUP BY VideoId, DownloadFormat
            )
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS ux_vvc_PendingDownloads_Video
                ON vvc_PendingDownloads (VideoId, DownloadFormat)
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS vvc_VideoWatchStats (
                VideoId TEXT PRIMARY KEY NOT NULL,
                LastWatchedAt TEXT NOT NULL,
                WatchCount INTEGER NOT NULL DEFAULT 0
            )
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS vvc_VRDancingTitles (
                Code TEXT PRIMARY KEY NOT NULL,
                Song TEXT NOT NULL DEFAULT '',
                Artist TEXT NOT NULL DEFAULT '',
                Instructor TEXT NOT NULL DEFAULT ''
            )
            """);

        // TODO: Remove later - EnsureCreated above builds the schema only for a database that does not yet
        // exist, and there are no migrations, so a property added to an existing entity
        // would be missing for every current user until they deleted their database.
        SchemaReconciler.Run(db);
    }

    public static string? GetLatestHistoryUrl(string videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return null;
        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory
            .AsNoTracking()
            .Where(h => h.Id == videoId)
            .OrderByDescending(h => h.Timestamp)
            .Select(h => h.Url)
            .FirstOrDefault();
    }

    public static Dictionary<string, (string Url, UrlType Type)> GetLatestHistoryUrls(IEnumerable<string> videoIds)
    {
        var ids = videoIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, (string, UrlType)>();

        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory
            .AsNoTracking()
            .Where(h => h.Id != null && ids.Contains(h.Id))
            .GroupBy(h => h.Id!)
            .Select(g => g.OrderByDescending(h => h.Timestamp).First())
            .ToDictionary(h => h.Id!, h => (h.Url, h.Type));
    }

    public static VRDancingTitle? GetVRDancingTitle(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        using var db = _contextFactory.CreateDbContext();
        return db.VRDancingTitles.AsNoTracking().FirstOrDefault(t => t.Code == code);
    }

    public static void ReplaceVRDancingTitles(IEnumerable<VRDancingTitle> rows)
    {
        using var db = _contextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw("DELETE FROM vvc_VRDancingTitles");
        db.VRDancingTitles.AddRange(rows);
        db.SaveChanges();
        tx.Commit();
    }

    private const int MaxPlayHistoryRows = 2000;

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
        PruneOldPlayHistory(db);
        OnPlayHistoryAdded?.Invoke();
    }

    private static void PruneOldPlayHistory(Database db)
    {
        var total = db.PlayHistory.Count();
        if (total <= MaxPlayHistoryRows)
            return;

        var excess = total - MaxPlayHistoryRows;
        var oldest = db.PlayHistory
            .OrderBy(h => h.Timestamp)
            .Take(excess)
            .ToList();
        db.PlayHistory.RemoveRange(oldest);
        db.SaveChanges();
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
        if (!string.IsNullOrEmpty(videoInfoCache.Title))
        {
            YTDL.ActiveStreamTracker.AssociateUrlInfo(videoInfoCache.Id, videoInfoCache.Id, videoInfoCache.Title, videoInfoCache.Id, videoInfoCache.Duration);
        }
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

        List<History> histories;

        if (distinctOnly)
        {
            histories = db.PlayHistory
                .FromSqlRaw($@"
                    SELECT ph.* FROM {nameof(Database.PlayHistory)} ph
                    INNER JOIN (
                        SELECT {nameof(History.Id)}, MAX({nameof(History.Timestamp)}) as MaxTimestamp
                        FROM {nameof(Database.PlayHistory)}
                        GROUP BY {nameof(History.Id)}
                    ) latest ON ph.{nameof(History.Id)} = latest.{nameof(History.Id)} AND ph.{nameof(History.Timestamp)} = latest.MaxTimestamp
                    ORDER BY ph.{nameof(History.Timestamp)} DESC
                    LIMIT {{0}}", limit)
                .AsNoTracking()
                .ToList();
        }
        else
        {
            histories = db.PlayHistory
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToList();
        }

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

        // Single atomic upsert rather than read-modify-write. Two plays landing at once
        // previously either lost an increment (both read the same count) or collided on the
        // primary key. The DateTime goes through a parameter so the provider writes it in
        // the same text format EF reads back.
        db.Database.ExecuteSqlRaw("""
            INSERT INTO vvc_VideoWatchStats (VideoId, LastWatchedAt, WatchCount)
            VALUES ({0}, {1}, 1)
            ON CONFLICT(VideoId) DO UPDATE SET
                WatchCount = WatchCount + 1,
                LastWatchedAt = excluded.LastWatchedAt
            """, videoId, DateTime.UtcNow);
    }

    public static Dictionary<string, VideoWatchStats> GetAllVideoWatchStats()
    {
        using var db = _contextFactory.CreateDbContext();
        return db.VideoWatchStats
            .AsNoTracking()
            .ToDictionary(v => v.VideoId);
    }

    // Every resolved request is recorded in PlayHistory, while VideoWatchStats is only
    // incremented on a cache hit — so this is the denominator for the cache hit rate.
    public static int GetPlayHistoryCount()
    {
        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory.AsNoTracking().Count();
    }

    // --- Pending Downloads ---

    public static void AddPendingDownload(VideoInfo videoInfo)
    {
        using var db = _contextFactory.CreateDbContext();

        // Insert-or-ignore against the unique (VideoId, DownloadFormat) index, rather than
        // Any() followed by Add — two callers racing both saw "not present" and both
        // inserted, so the same video was downloaded twice. The row count tells us whether
        // anything was actually queued, so the UI is not notified for a no-op.
        var inserted = db.Database.ExecuteSqlRaw("""
            INSERT INTO vvc_PendingDownloads (QueuedAt, VideoUrl, VideoId, UrlType, DownloadFormat)
            VALUES ({0}, {1}, {2}, {3}, {4})
            ON CONFLICT (VideoId, DownloadFormat) DO NOTHING
            """,
            DateTime.UtcNow,
            videoInfo.VideoUrl,
            videoInfo.VideoId,
            (int)videoInfo.UrlType,
            (int)videoInfo.DownloadFormat);

        if (inserted > 0)
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
