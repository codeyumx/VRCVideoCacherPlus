using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database.Models;

namespace VRCVideoCacher.Database;

public class Database : DbContext
{
    internal static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    internal static readonly string DbPath = Path.Join(CacheDir, "database.db");

    public DbSet<History> PlayHistory { get; set; }
    public DbSet<VideoInfoCache> VideoInfoCache { get; set; }

    // Required for PooledDbContextFactory
    public Database(DbContextOptions<Database> options) : base(options) { }
    public DbSet<PendingDownload> PendingDownloads { get; set; }
    public DbSet<VideoWatchStats> VideoWatchStats { get; set; }
    public DbSet<VRDancingTitle> VRDancingTitles { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            Directory.CreateDirectory(CacheDir);
            optionsBuilder.UseSqlite($"Data Source={DbPath}");
            optionsBuilder.EnableSensitiveDataLogging();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingDownload>().ToTable("vvc_PendingDownloads");
        modelBuilder.Entity<VideoWatchStats>().ToTable("vvc_VideoWatchStats");
        modelBuilder.Entity<VRDancingTitle>().ToTable("vvc_VRDancingTitles");
    }
}