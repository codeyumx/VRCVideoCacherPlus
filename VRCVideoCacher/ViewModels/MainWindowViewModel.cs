using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusText = Localizer.Get("ServerRunning");

    [ObservableProperty]
    private string _cacheStatusText = "Cache: 0 B";

    [ObservableProperty]
    private string _title = $"VRCVideoCacherPlus v{Program.Version}";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isUpdatePending;

    [ObservableProperty]
    private string _updateVersionText = "";

    [ObservableProperty]
    private bool _isDnsFlushPromptVisible;

    private GitHubRelease? _pendingRelease;

    public DashboardViewModel Dashboard { get; }
    public RulesViewModel Rules { get; }
    public SettingsViewModel Settings { get; }
    public CookiesViewModel Cookies { get; }
    public CacheBrowserViewModel CacheBrowser { get; }
    public DownloadQueueViewModel DownloadQueue { get; }
    public LogViewerViewModel LogViewer { get; }
    public HistoryViewModel History { get; }
    public AboutViewModel About { get; }

    public MainWindowViewModel()
    {
        Dashboard = new DashboardViewModel();
        Rules = new RulesViewModel();
        Settings = new SettingsViewModel();
        Cookies = new CookiesViewModel();
        CacheBrowser = new CacheBrowserViewModel();
        DownloadQueue = new DownloadQueueViewModel();
        LogViewer = new LogViewerViewModel();
        History = new HistoryViewModel();
        About = new AboutViewModel();

        _currentView = Dashboard;

        // Subscribe to cache changes for status bar
        CacheManager.OnCacheChanged += (_, _) => UpdateCacheStatus();
        UpdateCacheStatus();

        // Refresh localized strings when language changes
        Localizer.LanguageChanged += (_, _) => StatusText = Localizer.Get("ServerRunning");
    }

    private void UpdateCacheStatus()
    {
        var size = CacheManager.GetTotalCacheSize();
        var maxSize = ConfigManager.Config.CacheMaxSizeInGb;

        if (maxSize > 0)
        {
            var maxBytes = (long)(maxSize * 1024 * 1024 * 1024);
            CacheStatusText = $"Cache: {FormatSize(size)} / {FormatSize(maxBytes)}";
        }
        else
        {
            CacheStatusText = $"Cache: {FormatSize(size)}";
        }
    }

    // Second copy of this lived here; CacheStats.FormatSize is the one covered by tests.
    private static string FormatSize(long bytes) => Utils.CacheStats.FormatSize(bytes);

    private async Task NavigateToAsync(ViewModelBase targetView)
    {
        if (CurrentView == targetView) return;

        if (CurrentView == Rules && Rules.HasChanges)
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parentWindow = lifetime?.MainWindow;
            var canProceed = await Rules.CheckUnsavedChangesAsync(parentWindow);
            if (!canProceed) return;
        }

        CurrentView = targetView;
    }

    [RelayCommand]
    private async Task NavigateToDashboard() => await NavigateToAsync(Dashboard);

    [RelayCommand]
    private async Task NavigateToRules() => await NavigateToAsync(Rules);

    [RelayCommand]
    private async Task NavigateToSettings() => await NavigateToAsync(Settings);

    [RelayCommand]
    private async Task NavigateToCacheBrowser() => await NavigateToAsync(CacheBrowser);

    [RelayCommand]
    private async Task NavigateToDownloadQueue() => await NavigateToAsync(DownloadQueue);

    [RelayCommand]
    private async Task NavigateToCookies() => await NavigateToAsync(Cookies);

    [RelayCommand]
    private async Task NavigateToLogViewer() => await NavigateToAsync(LogViewer);

    [RelayCommand]
    private async Task NavigateToHistory() => await NavigateToAsync(History);

    [RelayCommand]
    public async Task NavigateToAbout() => await NavigateToAsync(About);

    public void ShowUpdate(UpdateInfo info)
    {
        _pendingRelease = info.Release;
        UpdateVersionText = $"Version {info.Version} is available!";
        IsUpdateAvailable = true;
    }

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        if (_pendingRelease == null) return;
        IsUpdatePending = true;
        UpdateVersionText = "Downloading update...";
        // ApplyUpdate exits the process on success, so the failure message only shows
        // when the swap or download legitimately failed.
        var ok = await Updater.ApplyUpdate(_pendingRelease);
        if (!ok)
        {
            IsUpdatePending = false;
            UpdateVersionText = "Update failed. Check logs and try again.";
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        var url = _pendingRelease?.html_url
                  ?? Program.LatestReleaseUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* ignore — best effort */ }
    }

    public void CheckDnsFailure()
    {
        if (VideoTools.HasDnsFailureFlag())
            IsDnsFlushPromptVisible = true;
    }

    [RelayCommand]
    private void FlushDns()
    {
        VideoTools.FlushSystemDnsCache();
        VideoTools.ClearDnsFailureFlag();
        IsDnsFlushPromptVisible = false;
    }

    [RelayCommand]
    private void DismissDnsPrompt()
    {
        VideoTools.ClearDnsFailureFlag();
        IsDnsFlushPromptVisible = false;
    }
}
