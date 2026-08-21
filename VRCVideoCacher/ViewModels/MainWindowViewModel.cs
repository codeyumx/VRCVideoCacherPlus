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

    // Built on first navigation rather than up front. All nine used to be constructed in
    // this constructor, on the UI thread, before the first frame was ever drawn — each one
    // querying the database, scanning the cache directory or reading config. Only the
    // dashboard is needed to show the window.
    //
    // The window binds CurrentView and picks the view by DataTemplate on type, so nothing
    // in XAML touches these properties; only the navigation commands do.
    private readonly Lazy<DashboardViewModel> _dashboard = new(() => new DashboardViewModel());
    private readonly Lazy<RulesViewModel> _rules = new(() => new RulesViewModel());
    private readonly Lazy<SettingsViewModel> _settings = new(() => new SettingsViewModel());
    private readonly Lazy<CookiesViewModel> _cookies = new(() => new CookiesViewModel());
    private readonly Lazy<CacheBrowserViewModel> _cacheBrowser = new(() => new CacheBrowserViewModel());
    private readonly Lazy<DownloadQueueViewModel> _downloadQueue = new(() => new DownloadQueueViewModel());
    private readonly Lazy<LogViewerViewModel> _logViewer = new(() => new LogViewerViewModel());
    private readonly Lazy<HistoryViewModel> _history = new(() => new HistoryViewModel());
    private readonly Lazy<AboutViewModel> _about = new(() => new AboutViewModel());

    public DashboardViewModel Dashboard => _dashboard.Value;
    public RulesViewModel Rules => _rules.Value;
    public SettingsViewModel Settings => _settings.Value;
    public CookiesViewModel Cookies => _cookies.Value;
    public CacheBrowserViewModel CacheBrowser => _cacheBrowser.Value;
    public DownloadQueueViewModel DownloadQueue => _downloadQueue.Value;
    public LogViewerViewModel LogViewer => _logViewer.Value;
    public HistoryViewModel History => _history.Value;
    public AboutViewModel About => _about.Value;

    public MainWindowViewModel()
    {
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

        // Guard on IsValueCreated first: reading the Rules property would construct the
        // view model purely to ask whether it has unsaved changes, which defeats the point
        // of deferring it.
        if (_rules.IsValueCreated && ReferenceEquals(CurrentView, _rules.Value) && _rules.Value.HasChanges)
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parentWindow = lifetime?.MainWindow;
            await _rules.Value.CheckUnsavedChangesAsync(parentWindow);
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
        UpdateVersionText = string.Format(Localizer.Get("UpdateAvailable"), info.Version);
        IsUpdateAvailable = true;
    }

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        if (_pendingRelease == null) return;
        IsUpdatePending = true;
        UpdateVersionText = Localizer.Get("UpdateDownloading");
        // ApplyUpdate exits the process on success, so the failure message only shows
        // when the swap or download legitimately failed.
        var ok = await Updater.ApplyUpdate(_pendingRelease);
        if (!ok)
        {
            IsUpdatePending = false;
            UpdateVersionText = Localizer.Get("UpdateFailed");
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
