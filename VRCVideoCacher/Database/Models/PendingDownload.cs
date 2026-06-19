using System.ComponentModel.DataAnnotations;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Database.Models;

public class PendingDownload
{
    [Key]
    public int Key { get; set; }
    public required DateTime QueuedAt { get; set; }
    public required string VideoUrl { get; set; }
    public required string VideoId { get; set; }
    public required UrlType UrlType { get; set; }
    public required DownloadFormat DownloadFormat { get; set; }
}
