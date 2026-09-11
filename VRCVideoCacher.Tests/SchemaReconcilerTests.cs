using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database;
using Xunit;

namespace VRCVideoCacher.Tests;

// SchemaReconciler backfills columns the EF model declares but the database lacks, because
// EnsureCreated never alters a database that already exists. These run against a real
// temporary SQLite file — the behaviour being tested is entirely about what SQLite does.
public class SchemaReconcilerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vvc-schema-{Guid.NewGuid():N}.db");

    private Database.Database CreateContext() =>
        new(new DbContextOptionsBuilder<Database.Database>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }
    }

    private static HashSet<string> ColumnsOf(DbContext db, string table)
    {
        // EF1002: table names are literals from these tests, not input.
#pragma warning disable EF1002
        return db.Database
            .SqlQueryRaw<string>($"SELECT name AS Value FROM pragma_table_info('{table}')")
            .AsEnumerable()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
#pragma warning restore EF1002
    }

    [Fact]
    public void Run_AddsAColumnMissingFromAnExistingTable()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();

        // Simulate a database created before a property existed.
        db.Database.ExecuteSqlRaw("ALTER TABLE VideoInfoCache DROP COLUMN Author");
        Assert.DoesNotContain("Author", ColumnsOf(db, "VideoInfoCache"));

        SchemaReconciler.Run(db);

        Assert.Contains("Author", ColumnsOf(db, "VideoInfoCache"));
    }

    [Fact]
    public void Run_PreservesExistingRowsWhenBackfilling()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE VideoInfoCache DROP COLUMN Author");
        db.Database.ExecuteSqlRaw("INSERT INTO VideoInfoCache (Id, Title, Type) VALUES ('abc', 'Some title', 0)");

        SchemaReconciler.Run(db);

        var row = db.VideoInfoCache.AsNoTracking().Single();
        Assert.Equal("abc", row.Id);
        Assert.Equal("Some title", row.Title);
        Assert.Null(row.Author);
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE VideoInfoCache DROP COLUMN Author");

        SchemaReconciler.Run(db);
        var afterFirst = ColumnsOf(db, "VideoInfoCache");
        SchemaReconciler.Run(db);

        Assert.Equal(afterFirst, ColumnsOf(db, "VideoInfoCache"));
    }

    [Fact]
    public void Run_LeavesAnUpToDateSchemaAlone()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
        var before = ColumnsOf(db, "PlayHistory");

        SchemaReconciler.Run(db);

        Assert.Equal(before, ColumnsOf(db, "PlayHistory"));
    }
}
