using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _serverRunning = true;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:9696";

    [ObservableProperty]
    private long _totalCacheSize;

    [ObservableProperty]
    private float _maxCacheSize;

    [ObservableProperty]
    private int _cachedVideoCount;

    [ObservableProperty]
    private int _downloadQueueCount;

    [ObservableProperty]
    private string _cookieStatus = Localizer.Get("NotSet");

    [ObservableProperty]
    private string _currentDownloadText = Localizer.Get("None");

    [ObservableProperty]
    private bool _hostState;

    [ObservableProperty]
    private bool _cookiesFileExists = false;

    [ObservableProperty]
    private string _ytdlpStatus = "Up-To-Date";

    [ObservableProperty]
    private string _denoStatus = "Up-To-Date";

    [ObservableProperty]
    private string _ffmpegStatus = "Up-To-Date";

    [ObservableProperty]
    private bool _videoPlayersEnabled = true;

    public DashboardViewModel()
    {
        VideoPlayersEnabled = ConfigManager.Config.VideoPlayersEnabled;
        ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
        MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        HostState = ElevatorManager.HasHostsLine;

        // Initial data load
        RefreshData();

        // Subscribe to language changes to refresh localized strings
        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.InvokeAsync(RefreshLocalizedStrings);

        // Subscribe to events
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        ConfigManager.OnConfigChanged += OnConfigChanged;
        Program.OnCookiesUpdated += OnCookiesUpdated;
    }

    private void RefreshLocalizedStrings()
    {
        // Force BoolToStatusConverter to re-evaluate with new language
        OnPropertyChanged(nameof(ServerRunning));

        // Refresh directly-assigned localized strings
        if (VideoDownloader.GetCurrentDownload() == null)
            CurrentDownloadText = Localizer.Get("None");
    }

    private void OnCookiesUpdated()
    {
        _ = ValidateCookiesAsync();
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCacheStats);
    }

    private void OnDownloadStarted(Models.VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = $"{video.UrlType}: {video.VideoId}";
        });
    }

    private void OnDownloadCompleted(Models.VideoInfo video, bool success, string? failReason)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = Localizer.Get("None");
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadQueueCount = VideoDownloader.GetQueueCount();
        });
    }

    private void OnConfigChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
            MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        });
        _ = ValidateCookiesAsync();
    }

    [RelayCommand]
    private void RefreshData()
    {
        RefreshCacheStats();
        RefreshUtilStatuses();
        DownloadQueueCount = VideoDownloader.GetQueueCount();

        var currentDownload = VideoDownloader.GetCurrentDownload();
        CurrentDownloadText = currentDownload != null
            ? $"{currentDownload.UrlType}: {currentDownload.VideoId}"
            : Localizer.Get("None");

        _ = ValidateCookiesAsync();
    }

    public void RefreshUtilStatuses()
    {
        // 1. yt-dlp Status
        if (!File.Exists(YtdlManager.YtdlPath))
        {
            var sysYtdlp = FileTools.LocateFile(OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
            YtdlpStatus = sysYtdlp != null ? "Shim" : "Missing";
        }
        else
        {
            try
            {
                var bytes = File.ReadAllBytes(YtdlManager.YtdlPath);
                var hash = Program.ComputeBinaryContentHash(bytes);
                if (hash == Program.YtdlpHash)
                {
                    YtdlpStatus = "Shim";
                }
                else if (Versions.CurrentVersion.Ytdlp == "Outdated")
                {
                    YtdlpStatus = "Outdated";
                }
                else
                {
                    YtdlpStatus = "Up-To-Date";
                }
            }
            catch
            {
                YtdlpStatus = "Up-To-Date";
            }
        }

        // 2. Deno Status
        if (!File.Exists(YtdlManager.DenoPath))
        {
            var systemDeno = FileTools.LocateFile(OperatingSystem.IsWindows() ? "deno.exe" : "deno");
            DenoStatus = systemDeno != null ? "Shim" : "Missing";
        }
        else if (Versions.CurrentVersion.Deno == "Outdated")
        {
            DenoStatus = "Outdated";
        }
        else
        {
            DenoStatus = "Up-To-Date";
        }

        // 3. FFmpeg Status
        if (!File.Exists(YtdlManager.FfmpegPath))
        {
            var systemFfmpeg = FileTools.LocateFile(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            FfmpegStatus = systemFfmpeg != null ? "Shim" : "Missing";
        }
        else if (Versions.CurrentVersion.Ffmpeg == "Outdated")
        {
            FfmpegStatus = "Outdated";
        }
        else
        {
            FfmpegStatus = "Up-To-Date";
        }
    }

    [RelayCommand]
    private void ToggleVideoPlayers()
    {
        VideoPlayersEnabled = !VideoPlayersEnabled;
        ConfigManager.Config.VideoPlayersEnabled = VideoPlayersEnabled;
        ConfigManager.TrySaveConfig();
    }

    [RelayCommand]
    private async Task ToggleHost()
    {
        await ElevatorManager.ToggleHostLineAsync();
        HostState = ElevatorManager.HasHostsLine;
    }

    private void RefreshCacheStats()
    {
        TotalCacheSize = CacheManager.GetTotalCacheSize();
        // The index only holds .mp4/.webm now, so index.html is never counted and no
        // longer has to be subtracted back out.
        CachedVideoCount = CacheManager.GetCachedVideoCount();
    }

    [RelayCommand]
    private void OpenCacheFolder() => OpenUrl.OpenFolder(CacheManager.CachePath);

    private async Task ValidateCookiesAsync()
    {
        CookiesFileExists = Program.DoesCookieFileExist();

        if (!Program.IsCookiesEnabledAndValid())
        {
            Dispatcher.UIThread.Post(() => CookieStatus = Localizer.Get("NotSet"));
            return;
        }

        Dispatcher.UIThread.Post(() => CookieStatus = Localizer.Get("Checking"));

        var result = await Program.ValidateCookiesAsync();
        Dispatcher.UIThread.Post(() =>
        {
            CookieStatus = result switch
            {
                true => Localizer.Get("Valid"),
                false => Localizer.Get("Expired"),
                null => Localizer.Get("Unknown")
            };
        });
    }

    [RelayCommand]
    private async Task SetupCookieExtension()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!ConfigManager.Config.YtdlpUseCookies)
            {
                await new PopupWindow(Localizer.Get("CookiesDisabledWarning")).ShowDialog(desktop.MainWindow!);
                return;
            }

            var viewModel = new CookieSetupViewModel();
            var window = new CookieSetupWindow
            {
                DataContext = viewModel
            };

            viewModel.RequestClose += () => window.Close();

            await window.ShowDialog(desktop.MainWindow!);

            // Refresh cookies status after dialog closes
            _ = ValidateCookiesAsync();
        }
    }

    [RelayCommand]
    private async Task ClearCookies()
    {
        Program.DeleteCookieFile();
        await ValidateCookiesAsync();
    }
}
