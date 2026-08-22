using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using SharpCompress.Readers;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public class YtdlManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<YtdlManager>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly HttpClient DownloadHttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromMinutes(10)
    };
    public static readonly string CookiesPath;

    public static readonly string YtdlPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
    public static readonly string DenoPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "deno.exe" : "deno");
    public static readonly string FfmpegPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    private const string YtdlpApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest";
    private const string FfmpegNightlyApiUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";
    private const string FfmpegApiUrl = "https://api.github.com/repos/GyanD/codexffmpeg/releases/latest";
    private const string DenoApiUrl = "https://api.github.com/repos/denoland/deno/releases/latest";
    private const string DenoFallBackVersionURL = "https://dl.deno.land/release-latest.txt";
    private const string DenoFallBackDownloadURL = "https://dl.deno.land/release/";

    // Guards the "Deno missing" error so it is reported on transition, not per invocation.
    private static int _denoMissingReported;


    static YtdlManager()
    {
        CookiesPath = Path.Join(Program.DataPath, "youtube_cookies.txt");

        // try to locate in PATH
        if (LaunchArgs.UseGlobalPath)
        {
            YtdlPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp") ??
                       throw new FileNotFoundException("Unable to find yt-dlp");
            DenoPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "deno.exe" : "deno") ??
                       throw new FileNotFoundException("Unable to find Deno runtime");
            FfmpegPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg") ??
                         string.Empty;
        }

        Log.Debug("Using ytdl path: {YtdlPath}", YtdlPath);
    }

    /// <summary>
    /// Downloads an archive to a temporary file and verifies its GitHub digest before the
    /// caller extracts it. Returns null when the download or the check fails.
    ///
    /// Buffering to disk first is the point: the archive contents get marked executable and
    /// run, so they have to be verified before extraction rather than streamed straight out
    /// of the socket into the utils directory.
    /// </summary>
    private static async Task<string?> DownloadArchiveAsync(string url, string? digest, string label)
    {
        var tempPath = Path.Join(Program.UtilsPath, $"_{label}.download");
        try
        {
            using var response = await DownloadHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("{Label}: download failed with {StatusCode}.", label, response.StatusCode);
                return null;
            }

            await using (var responseStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await responseStream.CopyToAsync(fileStream);
            }

            if (await FileHash.VerifyGitHubDigestAsync(tempPath, digest, label))
                return tempPath;
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }

        TryDeleteTemp(tempPath);
        return null;
    }

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Debug("Could not remove temporary download {Path}: {Error}", path, ex.Message);
        }
    }

    /// <summary>
    /// Resolves an archive entry to a destination inside <see cref="Program.UtilsPath"/>,
    /// flattening any directory component and refusing anything that still escapes.
    ///
    /// Entry names come from a remote archive, and joining one straight onto a destination
    /// directory is the classic zip-slip: an entry called "../../something" writes wherever
    /// the process can reach. Returns null when the entry should be skipped.
    /// </summary>
    private static string? ResolveUtilsDestination(string entryKey)
    {
        var fileName = Path.GetFileName(entryKey);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var utilsRoot = Path.GetFullPath(Program.UtilsPath);
        var destination = Path.GetFullPath(Path.Join(utilsRoot, fileName));

        // GetFileName already strips directories; this also catches an entry literally
        // named "..", which would otherwise resolve to the parent directory.
        if (!destination.StartsWith(utilsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            Log.Warning("Rejecting archive entry {Entry}: resolves outside {UtilsPath}.", entryKey, utilsRoot);
            return null;
        }

        return destination;
    }

    /// <summary>
    /// Builds the complete yt-dlp argument list. Every element is exactly one argv entry
    /// and must NOT be pre-quoted — the result goes to ProcessStartInfo.ArgumentList, which
    /// applies the correct platform quoting itself.
    ///
    /// This used to return a single concatenated command line with hand-written quotes,
    /// which made the URL's own quoting the caller's problem. Exactly one call site escaped
    /// it (a lone Replace("\"", "%22") in ApiController), so a URL arriving by any other
    /// route — the pre-cache list, playlist expansion, the UI — could close the quote and
    /// append flags of its own. yt-dlp has --exec, so that is arbitrary code execution.
    /// </summary>
    public static List<string> GenerateYtdlArgs(List<string> args, IEnumerable<string> trailingArgs)
    {
        args.AddRange([
            "--encoding", "utf-8",
            "--ignore-config",
            "--no-playlist",
            "--no-warnings",
            "--no-mtime",
            "--no-progress"
        ]);

        if (File.Exists(FfmpegPath))
        {
            args.Add("--ffmpeg-location");
            args.Add(FfmpegPath);
        }

        if (File.Exists(DenoPath))
        {
            args.Add("--js-runtimes");
            args.Add($"deno:{DenoPath}");
            Interlocked.Exchange(ref _denoMissingReported, 0);
        }
        else if (Interlocked.Exchange(ref _denoMissingReported, 1) == 0)
        {
            // Reported once per transition to missing, not once per invocation. This method
            // runs for every single video request, and an error-level log raises a modal
            // dialog — which, mid-session in VR, is not a small thing to do repeatedly.
            Log.Error("Deno runtime not found at path: {DenoPath}", DenoPath);
        }

        if (Program.IsCookiesEnabledAndValid())
        {
            args.Add("--cookies");
            args.Add(CookiesPath);
        }

        args.AddRange(SplitArguments(ConfigManager.Config.YtdlpAdditionalArgs));
        args.AddRange(trailingArgs);
        return args;
    }

    /// <summary>
    /// Splits the user's free-form "additional arguments" setting into individual argv
    /// entries, honouring double and single quotes the way a shell would. The setting is a
    /// single config string, but ArgumentList needs one token per element — appending it
    /// whole would pass e.g. `--retries 3` as one argument named "--retries 3".
    /// </summary>
    internal static List<string> SplitArguments(string? value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        var current = new System.Text.StringBuilder();
        var quote = '\0';
        var hasToken = false;

        foreach (var c in value)
        {
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else
                    current.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    hasToken = true;
                    break;
                case ' ':
                case '\t':
                    if (hasToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                    break;
                default:
                    current.Append(c);
                    hasToken = true;
                    break;
            }
        }

        if (hasToken)
            result.Add(current.ToString());

        return result;
    }

    public static void StartYtdlUpdaterThread()
    {
        Task.Run(YtdlUpdaterTask);
    }

    private static async Task YtdlUpdaterTask()
    {
        var interval = TimeSpan.FromHours(1);
        var token = Program.ShutdownToken;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                await TryDownloadYtdlp();
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "YT-DLP update check failed, will retry next interval.");
            }
        }
    }

    public static async Task TryDownloadYtdlp()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new Exception("Failed to get Utils path");

        Log.Information("Checking for YT-DLP updates...");
        try
        {
            using var response = await HttpClient.GetAsync(YtdlpApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Failed to check for YT-DLP updates.");
                return;
            }
            var data = await response.Content.ReadAsStringAsync();
            var json = Json.Deserialize<GitHubRelease>(data);
            if (json == null)
            {
                Log.Error("Failed to parse YT-DLP update response.");
                return;
            }

            var currentYtdlVersion = Versions.CurrentVersion.Ytdlp;
            if (!File.Exists(YtdlPath))
            {
                currentYtdlVersion = "Not Installed";
            }
            else if (!await CheckIfProcessStarts(YtdlPath))
            {
                currentYtdlVersion = "Not Working";
            }
            else
            {
                currentYtdlVersion = await GetYtdlpVersionFromBinary() ?? string.Empty;
            }

            var latestVersion = json.tag_name;
            Log.Information("YT-DLP Current: {Installed} Latest: {Latest}", currentYtdlVersion, latestVersion);
            if (string.IsNullOrEmpty(latestVersion))
            {
                Log.Warning("Failed to check for YT-DLP updates.");
                return;
            }
            if (currentYtdlVersion == latestVersion)
            {
                Log.Information("YT-DLP is up to date.");
                return;
            }
            Log.Information("YT-DLP is outdated. Updating...");

            await DownloadYtdl(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning(ex, "YT-DLP update failed due to a network error.");
            Log.Information("You can manually place yt-dlp.exe (or yt-dlp on Linux) in: {UtilsPath}", Program.UtilsPath);
        }
    }

    private static async Task<string?> GetYtdlpVersionFromBinary()
    {
        try
        {
            var result = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = YtdlPath,
                Arguments = "--version"
            });
            return result.Output;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect yt-dlp version from binary.");
        }
        return null;
    }

    private static async Task<string?> GetFfmpegVersionFromBinary()
    {
        try
        {
            var result = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = "-version"
            });
            // Output: "ffmpeg version 7.1.1-full_build-www.gyan.dev ..."
            var firstLine = result.Output.Split('\n')[0].Trim();
            var parts = firstLine.Split(' ');
            if (parts.Length >= 3 && parts[0] == "ffmpeg" && parts[1] == "version")
                return parts[2].Split('-')[0]; // strip "-full_build-..." suffix
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect FFmpeg version from binary.");
        }
        return null;
    }

    private static async Task<string?> GetDenoVersionFromBinary()
    {
        try
        {
            var result = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = DenoPath,
                Arguments = "--version"
            });
            // Output: "deno 2.8.0\n..."
            var firstLine = result.Output.Split('\n')[0].Trim();
            var parts = firstLine.Split(' ');
            if (parts.Length >= 2 && parts[0] == "deno")
                return $"v{parts[1]}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect Deno version from binary.");
        }
        return null;
    }

    public static async Task TryDownloadDeno()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new Exception("Failed to get Utils path");

        try
        {
            await TryDownloadDenoInner();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning(ex, "Deno update failed due to a network error.");
            Log.Information("You can manually place deno.exe (or deno on Linux) in: {UtilsPath}", Program.UtilsPath);
        }
    }

    private static async Task TryDownloadDenoInner()
    {
        using var apiResponse = await HttpClient.GetAsync(DenoApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }
        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = Json.Deserialize<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse deno release response.");
            return;
        }

        var currentDenoVersion = Versions.CurrentVersion.Deno;
        if (!File.Exists(DenoPath))
        {
            currentDenoVersion = "Not Installed";
        }
        else if (!await CheckIfProcessStarts(DenoPath))
        {
            currentDenoVersion = "Not Working";
        }
        else
        {
            currentDenoVersion = await GetDenoVersionFromBinary() ?? string.Empty;
        }

        var latestVersion = json.tag_name;
        Log.Information("Deno Current: {Installed} Latest: {Latest}", currentDenoVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for Deno updates.");
            return;
        }
        if (currentDenoVersion == latestVersion)
        {
            Log.Information("Deno is up to date.");
            return;
        }
        Log.Information("Deno is outdated. Updating...");

        string assetName;
        if (OperatingSystem.IsWindows())
        {
            assetName = "deno-x86_64-pc-windows-msvc.zip";
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetName = "deno-x86_64-unknown-linux-gnu.zip";
                    break;
                case Architecture.Arm64:
                    assetName = "deno-aarch64-unknown-linux-gnu.zip";
                    break;
                default:
                    Log.Error("Unsupported architecture {OSArchitecture}", RuntimeInformation.OSArchitecture);
                    return;
            }
        }
        else
        {
            Log.Error("Unsupported operating system {OperatingSystem}", Environment.OSVersion);
            return;
        }
        // deno-x86_64-pc-windows-msvc.zip -> deno-x86_64-pc-windows-msvc
        var assets = json.assets.Where(asset => asset.name == assetName).ToList();
        if (assets.Count < 1)
        {
            Log.Error("Unable to find Deno asset {AssetName} for this platform.", assetName);
            return;
        }

        Log.Information("Downloading Deno...");
        var asset = assets.First();

        var archivePath = await DownloadArchiveAsync(asset.browser_download_url, asset.digest, "Deno");
        if (archivePath == null)
        {
            Log.Information("Failed to download deno from github attempting fallback download.");
            await TryDownloadDenoFallback(assetName);
            return;
        }

        await using var responseStream = File.OpenRead(archivePath);
        var reader = await ReaderFactory.OpenAsyncReader(responseStream);
        try
        {
            while (await reader.MoveToNextEntryAsync())
            {
                if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                    continue;

                Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
                var path = ResolveUtilsDestination(reader.Entry.Key);
                if (path == null)
                    continue;

                await using (var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var entryStream = await reader.OpenEntryStreamAsync())
                {
                    await entryStream.CopyToAsync(outputStream);
                }

                FileTools.MarkFileExecutable(path);
                Versions.CurrentVersion.Deno = json.tag_name;
                Versions.Save();
                Log.Information("Deno downloaded and extracted.");
                return;
            }
        }
        finally
        {
            await reader.DisposeAsync();
            await responseStream.DisposeAsync();
            TryDeleteTemp(archivePath);
        }

        Log.Error("Failed to extract Deno files.");
    }

    private static async Task TryDownloadDenoFallback(string assetName)
    {
        Log.Warning("Falling back to Deno version check via text file.");
        using var response = await HttpClient.GetAsync(DenoFallBackVersionURL);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest Deno version: {ResponseStatusCode}", response.StatusCode);
            return;
        }
        var latestVersion = (await response.Content.ReadAsStringAsync()).Trim();
        var url = $"{DenoFallBackDownloadURL}{latestVersion}/{assetName}";

        // dl.deno.land publishes no digest, so this path is TLS-only. DownloadArchiveAsync
        // logs that fact rather than letting it pass unnoticed.
        var archivePath = await DownloadArchiveAsync(url, digest: null, "Deno (fallback)");
        if (archivePath == null)
        {
            Log.Error("Failed to download Deno from fallback URL.");
            return;
        }

        await using var responseStream = File.OpenRead(archivePath);
        var reader = await ReaderFactory.OpenAsyncReader(responseStream);
        try
        {
            while (await reader.MoveToNextEntryAsync())
            {
                if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                    continue;

                Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
                var path = ResolveUtilsDestination(reader.Entry.Key);
                if (path == null)
                    continue;

                await using (var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var entryStream = await reader.OpenEntryStreamAsync())
                {
                    await entryStream.CopyToAsync(outputStream);
                }

                FileTools.MarkFileExecutable(path);
                Versions.CurrentVersion.Deno = latestVersion;
                Versions.Save();
                Log.Information("Deno downloaded and extracted.");
                return;
            }
        }
        finally
        {
            await reader.DisposeAsync();
            await responseStream.DisposeAsync();
            TryDeleteTemp(archivePath);
        }

        Log.Error("Failed to extract Deno files from fallback download.");
    }

    public static async Task TryDownloadFfmpeg()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new Exception("Failed to get Utils path");

        try
        {
            await TryDownloadFfmpegInner();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning(ex, "FFmpeg update failed due to a network error.");
            Log.Information("You can manually place ffmpeg.exe (or ffmpeg on Linux) in: {UtilsPath}", Program.UtilsPath);
        }
    }

    private static async Task TryDownloadFfmpegInner()
    {
        using var apiResponse = await HttpClient.GetAsync(OperatingSystem.IsWindows() ? FfmpegApiUrl : FfmpegNightlyApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }
        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = Json.Deserialize<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse ffmpeg release response.");
            return;
        }

        var currentffmpegVersion = Versions.CurrentVersion.Ffmpeg;
        if (!File.Exists(FfmpegPath))
        {
            currentffmpegVersion = "Not Installed";
        }
        else if (!await CheckIfProcessStarts(FfmpegPath, "-version"))
        {
            currentffmpegVersion = "Not Working";
        }
        else
        {
            currentffmpegVersion = await GetFfmpegVersionFromBinary() ?? string.Empty;
        }

        var latestVersion = OperatingSystem.IsWindows() ? json.tag_name : json.name;
        Log.Information("FFmpeg Current: {Installed} Latest: {Latest}", currentffmpegVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for FFmpeg updates.");
            return;
        }
        if (currentffmpegVersion == latestVersion)
        {
            Log.Information("FFmpeg is up to date.");
            return;
        }
        Log.Information("FFmpeg is outdated. Updating...");

        string assetSuffix;
        if (OperatingSystem.IsWindows())
        {
            assetSuffix = "full_build-shared.zip";
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetSuffix = "master-latest-linux64-gpl.tar.xz";
                    break;
                case Architecture.Arm64:
                    assetSuffix = "master-latest-linuxarm64-gpl.tar.xz";
                    break;
                default:
                    Log.Error("Unsupported architecture {OSArchitecture}", RuntimeInformation.OSArchitecture);
                    return;
            }
        }
        else
        {
            Log.Error("Unsupported operating system {OperatingSystem}", Environment.OSVersion);
            return;
        }
        var asset = json.assets
            .FirstOrDefault(assetVersion => assetVersion.name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase));
        if (asset == null || string.IsNullOrEmpty(asset.browser_download_url))
        {
            Log.Error("Unable to find ffmpeg asset for this platform.");
            return;
        }
        Log.Information("Downloading FFmpeg...");

        // Previously streamed straight into the archive reader without even checking the
        // status code, so a 404 body was handed to SharpCompress as if it were an archive.
        var archivePath = await DownloadArchiveAsync(asset.browser_download_url, asset.digest, "FFmpeg");
        if (archivePath == null)
            return;

        await using var responseStream = File.OpenRead(archivePath);
        var reader = await ReaderFactory.OpenAsyncReader(responseStream);
        var success = false;
        try
        {
            while (await reader.MoveToNextEntryAsync())
            {
                if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                    continue;

                if (!reader.Entry.Key.Contains("/bin/"))
                    continue;

                Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
                var path = ResolveUtilsDestination(reader.Entry.Key);
                if (path == null)
                    continue;

                await using (var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var entryStream = await reader.OpenEntryStreamAsync())
                {
                    await entryStream.CopyToAsync(outputStream);
                }

                FileTools.MarkFileExecutable(path);
                success = true;
            }
        }
        finally
        {
            await reader.DisposeAsync();
            await responseStream.DisposeAsync();
            TryDeleteTemp(archivePath);
        }

        if (!success)
        {
            Log.Error("Failed to extract ffmpeg files.");
            return;
        }

        Versions.CurrentVersion.Ffmpeg = latestVersion;
        Versions.Save();
        Log.Information("FFmpeg downloaded and extracted.");
    }

    private static async Task DownloadYtdl(GitHubRelease json)
    {
        if (File.Exists(YtdlPath) && File.GetAttributes(YtdlPath).HasFlag(FileAttributes.ReadOnly))
        {
            Log.Warning("Skipping yt-dlp download because location is unwritable.");
            return;
        }

        string assetName;
        if (OperatingSystem.IsWindows())
        {
            assetName = "yt-dlp.exe";
        }
        else if (OperatingSystem.IsLinux())
        {
            assetName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "yt-dlp_linux",
                Architecture.Arm64 => "yt-dlp_linux_aarch64",
                _ => throw new Exception($"Unsupported architecture {RuntimeInformation.OSArchitecture}"),
            };
        }
        else
        {
            throw new Exception($"Unsupported operating system {Environment.OSVersion}");
        }

        foreach (var assetVersion in json.assets)
        {
            if (assetVersion.name != assetName)
                continue;

            if (string.IsNullOrEmpty(Program.UtilsPath))
                throw new Exception("Failed to get YT-DLP path");

            // Ensure directory exists
            var ytdlDir = Path.GetDirectoryName(YtdlPath);
            if (!string.IsNullOrEmpty(ytdlDir))
                Directory.CreateDirectory(ytdlDir);

            // Stage, verify, then move into place. This binary is marked executable and run
            // on every video request, so an unverified body must never occupy the final
            // path — not even briefly, and not even if the verification then fails.
            var tempPath = YtdlPath + ".download";
            try
            {
                await using (var stream = await DownloadHttpClient.GetStreamAsync(assetVersion.browser_download_url))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream);
                }

                if (!await FileHash.VerifyGitHubDigestAsync(tempPath, assetVersion.digest, "yt-dlp"))
                    throw new Exception("yt-dlp download failed its digest check.");

                File.Move(tempPath, YtdlPath, overwrite: true);
            }
            finally
            {
                TryDeleteTemp(tempPath);
            }

            Log.Information("Downloaded YT-DLP.");
            FileTools.MarkFileExecutable(YtdlPath);
            Versions.CurrentVersion.Ytdlp = json.tag_name;
            Versions.Save();
            return;
        }
        throw new Exception("Failed to download YT-DLP");
    }

    private static async Task<bool> CheckIfProcessStarts(string path, string arg = "--version")
    {
        var processName = Path.GetFileNameWithoutExtension(path);
        try
        {
            var (output, error, exitCode) = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = path,
                Arguments = arg
            });
            if (exitCode != 0)
            {
                Log.Error("Error starting {ProcessName}: {Output} {Error}", processName, output, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception while starting {ProcessName}: {Message}", processName, ex.Message);
            return false;
        }
        return true;
    }
}