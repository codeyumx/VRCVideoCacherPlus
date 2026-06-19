using System.ComponentModel.DataAnnotations;

namespace VRCVideoCacher.Database.Models;

public class VRDancingTitle
{
    [Key]
    public required string Code { get; set; }
    public string Song { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Instructor { get; set; } = string.Empty;
}
