using Serilog;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Models;

public class Versions
{
    private static readonly ILogger Log = Program.Logger.ForContext<Versions>();
    private static readonly string VersionPath = Path.Join(Program.DataPath, "version.json");
    public static readonly VersionJson CurrentVersion = new();

    static Versions()
    {
        if (File.Exists(VersionPath))
        {
            try
            {
                CurrentVersion = Json.Deserialize<VersionJson>(File.ReadAllText(VersionPath)) ??
                                 new VersionJson();
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse version file, it may be corrupted. Recreating...");
            }
        }

        Save();
    }

    public static void Save()
    {
        AtomicFile.WriteAllText(VersionPath, Json.Serialize(CurrentVersion));
    }
}

public class VersionJson
{
    public string Ytdlp { get; set; } = string.Empty;
    public string Ffmpeg { get; set; } = string.Empty;
    public string Deno { get; set; } = string.Empty;
}