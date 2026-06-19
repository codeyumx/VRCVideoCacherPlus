using System.ComponentModel.DataAnnotations;

namespace VRCVideoCacher.Database.Models;

public class VideoWatchStats
{
    [Key]
    public required string VideoId { get; set; }
    public DateTime LastWatchedAt { get; set; }
    public int WatchCount { get; set; }
}
