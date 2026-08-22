using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Avalonia;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.Services;
using VRCVideoCacher.Integrations.VRDancing;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;
#if STEAMRELEASE
using Steamworks;
#endif

namespace VRCVideoCacher;

internal sealed class Program
{
    public static string YtdlpHash = string.Empty;
    // Versioning is YEAR.MONTH.RELEASE — set in the .csproj <Version> property
    public static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
    public const string Creator_Elly = "Elly";
    public const string Creator_Natsumi = "Natsumi";
    public const string Creator_Haxy = "Haxy";
    public const string Creator_Hauskaz = "Hauskaz";
    public const string Creator_DubyaDude = "DubyaDude";

    // Single source of truth for this fork's identity. The updater downloads and swaps in a
    // release asset from here, so pointing it at the wrong repo silently replaces the user's
    // install with a different build — keep every repo reference derived from these two.
    public const string RepoOwner = "Bluscream";
    public const string RepoName = "VRCVideoCacherPlusPlus";
    public const string RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";
    public const string LatestReleaseUrl = $"{RepoUrl}/releases/latest";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    public static ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher");
    public static readonly string UtilsPath = Path.Join(DataPath, "Utils");
    private static readonly string LogsPath = Path.Join(DataPath, "Logs");
    public static event Action? OnCookiesUpdated;
    private const string SingleInstanceMutexName = @"Local\VRCVideoCacher_SingleInstance";
    private static Mutex? _singleInstanceMutex;

    private static readonly CancellationTokenSource ShutdownCts = new();

    /// <summary>
    /// Cancelled when the application is shutting down. Long-running background loops
    /// should await on this so they unwind promptly instead of being terminated mid-step by
    /// the Environment.Exit at the end of Main.
    /// </summary>
    public static CancellationToken ShutdownToken => ShutdownCts.Token;

    public static void SignalShutdown()
    {
        try
        {
            if (!ShutdownCts.IsCancellationRequested)
                ShutdownCts.Cancel();
        }
        catch (Exception ex)
        {
            Logger.Debug("Error signalling shutdown: {Error}", ex.Message);
        }
    }

    private static bool TryAcquireSingleInstanceMutex()
    {
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);
        try
        {
            return _singleInstanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Previous holder exited without releasing; we now own the mutex.
            return true;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        LaunchArgs.SetupArguments(args);

        // Logging comes first so that everything below is actually recorded. It used to be
        // initialised further down, which meant HostsManager.TryRun, WaitForPreviousInstance
        // and the kill-existing-instance branch all logged into Serilog's silent default
        // sink — a failed elevated hosts edit produced no output anywhere at all.
        InitializeLogger();

        // Must run before Steam API init — this process may be a privileged subprocess
        // invoked by ElevatorManager, in which case it does its one job and exits.
        HostsManager.TryRun();
        Utils.ConnectionSevering.TryRunElevatedCommand();

#if STEAMRELEASE
        if (LaunchArgs.SteamSdk)
        {
            if (SteamAPI.RestartAppIfNecessary(new AppId_t(4296960)))
            {
                Environment.Exit(0);
                return;
            }

            if (!SteamAPI.Init())
            {
                Console.Error.WriteLine("SteamAPI.Init() failed. Make sure Steam is running.");
                Environment.Exit(1);
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => SteamAPI.Shutdown();
        }
#endif

        Updater.WaitForPreviousInstance();

        // Atomic single-instance guard. Held for the lifetime of this process so
        // simultaneous launches (Steam + VRCX, etc.) can't both win the race.
        if (!TryAcquireSingleInstanceMutex())
        {
            if (LaunchArgs.KillExistingInstance)
            {
                // Documented-as-destructive escape hatch; corrupts history when triggered by auto-launchers.
                foreach (var process in Process.GetProcessesByName("VRCVideoCacher"))
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        process.Dispose();
                        continue;
                    }
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                        Logger.Information("Killed existing instance with PID {Pid} due to kill existing instance argument.", process.Id);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to kill existing instance with PID {Pid}.", process.Id);
                    }
                    process.Dispose();
                }
                // Re-acquire after the holder has fully exited and released its mutex handle.
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
                if (!TryAcquireSingleInstanceMutex())
                {
                    Console.WriteLine("Could not acquire single-instance lock after kill. Exiting...");
                    Environment.Exit(0);
                }
            }
            else
            {
                Console.WriteLine("Application is already running, Exiting...");
                Environment.Exit(0);
            }
        }

        Updater.Cleanup();
        SetupGlobalExceptionHandlers();

        if (!LaunchArgs.HasGui)
        {
            // Run backend only (console mode)
            InitVrcVideoCacher().GetAwaiter().GetResult();
            return;
        }

        OpenVRService.Start(CurrentProcessPath);

        // Start the UI — blocks until Avalonia shuts down
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // Ask the background loops to unwind first, then force-exit as a backstop for
        // anything still holding the process open (web server, OpenVR).
        SignalShutdown();
        Environment.Exit(0);
    }

    public static void InitializeUIBackend()
    {
        Task.Run(async () =>
        {
            try
            {
                await InitVrcVideoCacher();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Backend error: {Message}", ex.Message);
            }
        });
    }

    private static void InitializeLogger()
    {
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}" + Environment.NewLine + "{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: Path.Combine(LogsPath, "VRCVideoCacher.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5);

        // The hosts-edit subprocess has no UI, and UiLogSink reads ConfigManager.Config —
        // which under elevation would create a second config under the admin's profile.
        if (LaunchArgs.HasGui && !LaunchArgs.IsPrivilegedHelper)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sink(new UiLogSink());
        }

        Log.Logger = loggerConfiguration.CreateLogger();
        Logger = Log.ForContext("SourceContext", "Core");

        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}, {Hauskaz}, {DubyaDude}", Version, Creator_Elly, Creator_Natsumi, Creator_Haxy, Creator_Hauskaz, Creator_DubyaDude);
    }

    private static async Task InitVrcVideoCacher()
    {
        try { Console.Title = $"VRCVideoCacherPlus v{Version}"; } catch { /* GUI mode, no console */ }

        Directory.CreateDirectory(UtilsPath);
#if !STEAMRELEASE
        await Updater.CheckForUpdates();
#endif
        if (Environment.CommandLine.Contains("--Reset"))
        {
            FileTools.RestoreAllYtdl();
            Environment.Exit(0);
        }
        if (Environment.CommandLine.Contains("--Hash"))
        {
            Console.WriteLine(GetOurYtdlpHash());
            Environment.Exit(0);
        }
        Console.CancelKeyPress += (_, _) => Environment.Exit(0);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnAppQuit();

        YtdlpHash = GetOurYtdlpHash();

        // Nothing below needs the message of the day, so don't hold the application behind
        // a network round trip to fetch it.
        RunDetached(VvcConfigService.GetConfig(), "Config API fetch");

        if (ConfigManager.Config.YtdlpAutoUpdate && !LaunchArgs.UseGlobalPath)
        {
            // Awaited deliberately: yt-dlp and Deno have to be on disk before the web
            // server starts answering resolve requests with them.
            await Task.WhenAll(
                YtdlManager.TryDownloadYtdlp(),
                YtdlManager.TryDownloadDeno()
            );
            YtdlManager.StartYtdlUpdaterThread();
            RunDetached(YtdlManager.TryDownloadFfmpeg(), "FFmpeg download");
        }

        if (OperatingSystem.IsWindows())
            AutoStartShortcut.TryUpdateShortcutPath();
        WebServer.Init();
        FileTools.ApplyPatchSettings();

        // Mirrors listed in PreCacheUrls can run to gigabytes. Awaiting them meant the
        // cache index and the download queue did not come up until every one had finished.
        RunDetached(BulkPreCache.DownloadFileList(), "Bulk pre-cache");

        // Console mode has no window to put a dialog on, so the same one-time notice the UI
        // shows goes to the log instead.
        if (!LaunchArgs.HasGui && !ConfigManager.Config.HasShownSharedConfigNotice)
        {
            Logger.Warning("{Notice}", Jeek.Avalonia.Localization.Localizer.Get("SharedConfigNotice"));
            ConfigManager.Config.HasShownSharedConfigNotice = true;
            ConfigManager.TrySaveConfig();
        }

        if (ConfigManager.Config.YtdlpUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"ytdlUseCookies\" in config.");

        CacheManager.Init();
        VideoDownloader.Start();
        // Runs after CacheManager.Init so already-cached videos are skipped; not awaited
        // because resolving each URL hits the network and must not delay startup.
        RunDetached(VideoPreCache.QueueConfiguredVideos(), "Video pre-cache");
        VRDancingSheetService.StartBackgroundSync();
        VrcLogMonitor.Start();

        // run after init to avoid text spam blocking user input
        if (OperatingSystem.IsWindows())
            _ = WinGet.TryInstallPackages();

        await Task.Delay(-1);
    }

    /// <summary>
    /// Starts a task without waiting for it, logging a failure rather than letting it
    /// surface later as an unobserved task exception at an arbitrary GC.
    /// </summary>
    private static void RunDetached(Task task, string description)
    {
        _ = task.ContinueWith(
            completed => Logger.Warning(completed.Exception?.GetBaseException(), "{Description} failed.", description),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void DeleteCookieFile()
    {
        if (File.Exists(YtdlManager.CookiesPath))
        {
            File.Delete(YtdlManager.CookiesPath);
            Logger.Information("Deleted cookie file.");
        }
    }

    public static bool DoesCookieFileExist()
    {
        return File.Exists(YtdlManager.CookiesPath);
    }

    public static bool IsCookiesEnabledAndValid()
    {
        if (!ConfigManager.Config.YtdlpUseCookies)
            return false;

        if (!File.Exists(YtdlManager.CookiesPath))
            return false;

        var cookies = File.ReadAllText(YtdlManager.CookiesPath);
        return IsCookiesValid(cookies);
    }

    public static bool IsCookiesValid(string cookies)
    {
        if (string.IsNullOrEmpty(cookies))
            return false;

        if (cookies.Contains("youtube.com") && cookies.Contains("LOGIN_INFO"))
            return true;

        return false;
    }

    // Expiry of the login cookie on disk, or null when there is no cookie file or no
    // expiring LOGIN_INFO line. Parsing rules live in CookieFile.
    public static DateTime? GetCookiesExpiryUtc()
    {
        if (!DoesCookieFileExist())
            return null;

        try
        {
            return CookieFile.ParseLoginExpiryUtc(File.ReadLines(YtdlManager.CookiesPath));
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to read cookie expiry: {Error}", ex.Message);
            return null;
        }
    }

    public static async Task<bool?> ValidateCookiesAsync()
    {
        if (!IsCookiesEnabledAndValid())
            return null;

        try
        {
            var cookieContainer = new CookieContainer();
            var lines = await File.ReadAllLinesAsync(YtdlManager.CookiesPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length < 7)
                    continue;

                try
                {
                    var domain = parts[0];
                    var path = parts[2];
                    var secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    var name = parts[5];
                    var value = parts[6];

                    cookieContainer.Add(new Cookie(name, value, path, domain) { Secure = secure });
                }
                catch
                {
                    // Skip malformed cookie lines
                }
            }

            using var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            handler.CookieContainer = cookieContainer;
            handler.UseCookies = true;
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync("https://www.youtube.com/", cts.Token);
            return (int)response.StatusCode >= 200 && (int)response.StatusCode < 400;
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to validate cookies online: {Error}", ex.ToString());
            return null;
        }
    }

    public static Stream GetYtDlpStub()
    {
        return GetEmbeddedResource("VRCVideoCacher.yt-dlp-stub.exe");
    }

    public static Stream GetEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new Exception($"{resourceName} not found in resources.");

        return stream;
    }

    private static string GetOurYtdlpHash()
    {
        var stream = GetYtDlpStub();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Dispose();
        return ComputeBinaryContentHash(ms.ToArray());
    }

    public static string ComputeBinaryContentHash(byte[] content)
    {
        return Convert.ToBase64String(SHA256.HashData(content));
    }

    private static void OnAppQuit()
    {
        SignalShutdown();
        VrcLogMonitor.Stop();
        API.WebServer.Stop();
        FileTools.RestoreAllYtdl();
        Logger.Information("Exiting...");
        Log.CloseAndFlush();
    }

    public static void NotifyCookiesUpdated()
    {
        OnCookiesUpdated?.Invoke();
    }

    private static void SetupGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error(ex, "Unhandled AppDomain Exception");
                SaveCrashReport(ex, "AppDomain.CurrentDomain.UnhandledException");
            }
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (e.Exception != null)
            {
                Logger.Warning(e.Exception, "Unobserved Task Exception");
                SaveCrashReport(e.Exception, "TaskScheduler.UnobservedTaskException");
            }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Logger.Information("ProcessExit signal received, performing shutdown tasks...");
            Log.CloseAndFlush();
        };
    }

    public static void SaveCrashReport(Exception ex, string source)
    {
        try
        {
            var reportPath = Path.Combine(DataPath, "CRASH_REPORT.txt");
            Directory.CreateDirectory(DataPath);
            var reportContent = $@"==================================================
VRCVideoCacher Crash Report
Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
Source: {source}
Version: {Version}
OS: {Environment.OSVersion} (64-bit: {Environment.Is64BitOperatingSystem})
Process ID: {Environment.ProcessId}
Command Line: {Environment.CommandLine}
==================================================
Exception Type: {ex.GetType().FullName}
Message: {ex.Message}
--------------------------------------------------
Stack Trace:
{ex.StackTrace}
==================================================
";
            if (ex.InnerException != null)
            {
                reportContent += $@"
Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}
{ex.InnerException.StackTrace}
==================================================
";
            }

            File.WriteAllText(reportPath, reportContent);
        }
        catch { /* Ignore errors writing crash report */ }
    }
}
