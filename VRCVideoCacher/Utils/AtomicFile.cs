using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Writes a small text file without the possibility of leaving a truncated one behind.
///
/// File.WriteAllText truncates the target and then writes into it, so a crash — or a power
/// cut, or a full disk — in between leaves a zero-length or half-written file. That matters
/// here because every reader of these files responds to a parse failure by silently falling
/// back to defaults: the user loses their settings, their rules, and the record of which
/// tool versions are installed, with only a line in the log to say why.
///
/// Writing to a sibling temp file and renaming it over the target makes the swap atomic, so
/// a reader sees either the old contents or the new ones and never something in between.
/// </summary>
public static class AtomicFile
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(AtomicFile));

    // Config saves are infrequent and small; one lock across all of them is plenty and
    // removes any chance of two writers interleaving on the same temp path.
    private static readonly object WriteLock = new();

    /// <param name="unixMode">
    /// Unix permissions for the file, or null for the defaults; ignored on Windows. The temp
    /// file is created with them, so the contents are never on disk with wider permissions,
    /// and the rename carries them over to <paramref name="path"/>.
    /// </param>
    public static void WriteAllText(string path, string contents, UnixFileMode? unixMode = null)
    {
        var tempPath = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        var useUnixMode = false;
        if (unixMode is { } mode && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = mode;
            useUnixMode = true;
        }

        lock (WriteLock)
        {
            try
            {
                // The create mode only applies to a new file; a temp left by a crash would
                // keep its old permissions.
                if (useUnixMode)
                    File.Delete(tempPath);

                using (var stream = new FileStream(tempPath, options))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(contents);
                    writer.Flush();
                    // Push it to the platter before the rename: otherwise the rename can
                    // land while the contents are still only in the page cache, which is
                    // exactly the truncated-file case this is meant to prevent.
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception cleanupError)
                {
                    Log.Debug("Could not remove temporary file {Path}: {Error}", tempPath, cleanupError.Message);
                }

                throw;
            }
        }
    }
}
