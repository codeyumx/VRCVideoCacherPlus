using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Semver;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher;

public class Updater
{
    private const string UpdateUrl = "https://api.github.com/repos/codeyumx/VRCVideoCacherPlus/releases/latest";
    private static readonly string ReleaseAssetName = OperatingSystem.IsWindows() ? "VRCVideoCacher.exe" : "VRCVideoCacher";
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher.Updater" } }
    };
    private static readonly ILogger Log = Program.Logger.ForContext<Updater>();

    // Target the actually-running executable, not a hardcoded name — supports renamed exes.
    private static readonly string FilePath = Environment.ProcessPath ?? Path.Join(Program.CurrentProcessPath, ReleaseAssetName);
    private static readonly string NewFilePath = FilePath + ".new";
    private static readonly string OldFilePath = FilePath + ".old";

    public static async Task<UpdateInfo?> CheckForUpdates()
    {
        Log.Information("Checking for updates...");
        var isDebug = false;
#if DEBUG
        isDebug = true;
#endif
        if (Program.Version.Contains("-dev") || isDebug)
        {
            Log.Information("Running in dev mode. Skipping update check.");
            return null;
        }

        string data;
        try
        {
            using var response = await HttpClient.GetAsync(UpdateUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Failed to check for updates.");
                return null;
            }
            data = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Failed to reach update server.");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            Log.Warning(ex, "Update check timed out.");
            return null;
        }

        var latestRelease = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (latestRelease == null)
        {
            Log.Warning("Failed to parse update response.");
            return null;
        }
        if (!SemVersion.TryParse(latestRelease.tag_name, SemVersionStyles.Any, out var latestVersion))
        {
            Log.Warning("Failed to parse latest release version: {Tag}", latestRelease.tag_name);
            return null;
        }
        if (!SemVersion.TryParse(Program.Version, SemVersionStyles.Any, out var currentVersion))
        {
            Log.Warning("Failed to parse current version: {Version}", Program.Version);
            return null;
        }
        Log.Information("Latest release: {Latest}, Installed Version: {Installed}", latestVersion, currentVersion);
        if (SemVersion.ComparePrecedence(currentVersion, latestVersion) >= 0)
        {
            Log.Information("No updates available.");
            return null;
        }
        Log.Information("Update available: {Version}", latestVersion);
        return new UpdateInfo(latestVersion.ToString(), latestRelease);
    }

    /// <summary>
    /// Removes leftover files from a previous in-place update. Safe to call any time at startup.
    /// </summary>
    public static void Cleanup()
    {
        TryDelete(OldFilePath);
        TryDelete(NewFilePath);
    }

    /// <summary>
    /// If launched by the updater, block until the previous process has exited so the new process
    /// owns the web server port, DB, and yt-dlp stubs before it runs the single-instance check.
    /// </summary>
    public static void WaitForPreviousInstance()
    {
        if (LaunchArgs.WaitForPid is not { } pid)
            return;
        try
        {
            using var old = Process.GetProcessById(pid);
            if (!old.WaitForExit(15_000))
                Log.Warning("Previous process (pid {Pid}) did not exit within 15s. Continuing anyway.", pid);
        }
        catch (ArgumentException)
        {
            // Process already gone — nothing to wait for.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to wait for previous process (pid {Pid}).", pid);
        }
    }

    /// <summary>
    /// Downloads the new release, swaps it in place, launches it, and exits the current process.
    /// Returns false (with no exit) only if the swap could not be completed.
    /// </summary>
    public static async Task<bool> ApplyUpdate(GitHubRelease release)
    {
        var asset = release.assets.FirstOrDefault(a => a.name == ReleaseAssetName);
        if (asset == null)
        {
            Log.Warning("No matching asset ({FileName}) found in release {Tag}.", ReleaseAssetName, release.tag_name);
            return false;
        }

        try
        {
            TryDelete(NewFilePath);

            await using (var stream = await HttpClient.GetStreamAsync(asset.browser_download_url))
            await using (var fileStream = new FileStream(NewFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream);
            }

            if (!await HashCheck(NewFilePath, asset.digest))
            {
                Log.Warning("Hash check failed, aborting update.");
                TryDelete(NewFilePath);
                return false;
            }

            if (!OperatingSystem.IsWindows())
                FileTools.MarkFileExecutable(NewFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download update.");
            TryDelete(NewFilePath);
            return false;
        }

        // In-place swap: rename the running exe out of the way, then move the new file into its slot.
        // Both Windows and Linux allow renaming/unlinking a running executable on the same volume.
        try
        {
            TryDelete(OldFilePath);
            File.Move(FilePath, OldFilePath);
            File.Move(NewFilePath, FilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to swap new exe into place.");
            // Best-effort rollback so the user isn't left with a broken install.
            try
            {
                if (!File.Exists(FilePath) && File.Exists(OldFilePath))
                    File.Move(OldFilePath, FilePath);
            }
            catch { /* nothing more to do */ }
            return false;
        }

        try
        {
            var args = LaunchArgs.BuildArgs();
            args.Add($"--wait-for-pid={Environment.ProcessId}");
            var argsString = string.Join(' ', args);
            Log.Information("Launching new version and exiting.");
            Process.Start(new ProcessStartInfo
            {
                FileName = FilePath,
                UseShellExecute = true,
                WorkingDirectory = Program.CurrentProcessPath,
                Arguments = argsString
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch updated exe.");
            return false;
        }

        // Hand off — the new process waits on our PID, then cleans up the .old file on startup.
        Environment.Exit(0);
        return true; // unreachable
    }

    private static async Task<bool> HashCheck(string path, string githubHash)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var hashString = Convert.ToHexString(hashBytes);
        var expected = githubHash.Contains(':') ? githubHash.Split(':')[1] : githubHash;
        var match = string.Equals(expected, hashString, StringComparison.OrdinalIgnoreCase);
        Log.Information("FileHash: {FileHash} GitHubHash: {GitHubHash} HashMatch: {HashMatches}", hashString, expected, match);
        return match;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete {Path}", path);
        }
    }
}

public record UpdateInfo(string Version, GitHubRelease Release);
