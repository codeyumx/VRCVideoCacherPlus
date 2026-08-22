using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.API;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public partial class UrlEntry : ObservableObject
{
    [ObservableProperty]
    private string _url;

    public UrlEntry(string url)
    {
        _url = url;
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;

    // Server Settings
    [ObservableProperty]
    private string _webServerUrl = string.Empty;

    // Download Settings
    [ObservableProperty]
    private bool _ytdlAutoUpdate;

    [ObservableProperty]
    private string _ytdlAdditionalArgs = string.Empty;

    [ObservableProperty]
    private string _ytdlDubLanguage = string.Empty;

    // Cache Settings
    [ObservableProperty]
    private string _cachedAssetPath = string.Empty;

    [ObservableProperty]
    private bool _cacheYouTubePreferVp9;

    // Resolution options for the dropdown
    public int[] ResolutionOptions { get; } = [720, 1080, 1440, 2160];

    [ObservableProperty]
    private float _cacheMaxSizeInGb;

    [ObservableProperty]
    private bool _cacheHlsPlaylists;

    [ObservableProperty]
    private int _cacheHlsMaxLength;

    [ObservableProperty]
    private bool _cacheOnly;

    [ObservableProperty]
    private bool _isDelayEnabled;

    [ObservableProperty]
    private int _cacheDownloadIdleSeconds;

    [ObservableProperty]
    private bool _isRateLimitEnabled;

    [ObservableProperty]
    private int _cacheDownloadRateLimitMBs;

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;


    // Updates
    [ObservableProperty]
    private bool _autoUpdate;


    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _startMinimized;

    // Video URLs pre-cached at startup (distinct from config.PreCacheUrls, which mirrors
    // JSON manifests of direct file downloads and has no UI).
    public ObservableCollection<UrlEntry> PreCacheVideos { get; } = [];

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = string.Empty;

    [ObservableProperty]
    private bool _startWithSteamVr;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private bool _errorPopups;

    // Language selection
    public IReadOnlyList<LanguageOption> AvailableLanguageOptions =>
        Localizer.Languages
            .Select(code => new LanguageOption(code, GetLanguageDisplayName(code)))
            .ToList();

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        Localizer.Language = value.Code;
        ConfigManager.Config.Language = value.Code;
        ConfigManager.TrySaveConfig();
    }

    private static string GetLanguageDisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).NativeName; }
        catch { return code; }
    }

    public SettingsViewModel()
    {
        PreCacheVideos.CollectionChanged += OnUrlCollectionChanged;
        // One file, one event. This used to subscribe to both managers and so ran
        // LoadFromConfig twice for every save.
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        _isLoadingConfig = true;
        var config = ConfigManager.Config;

        WebServerUrl = config.YtdlpWebServerUrl;
        YtdlAutoUpdate = config.YtdlpAutoUpdate;
        YtdlAdditionalArgs = config.YtdlpAdditionalArgs;
        YtdlDubLanguage = config.YtdlpDubLanguage;
        CachedAssetPath = config.CachedAssetPath;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CacheHlsPlaylists = config.CacheHlsPlaylists;
        CacheHlsMaxLength = config.CacheHlsMaxLength;
        CacheOnly = config.CacheOnly;
        var plusConfig = PlusConfigManager.Config;
        CacheYouTubePreferVp9 = plusConfig.CacheYouTubePreferVp9;
        IsDelayEnabled = plusConfig.CacheDownloadIdleSeconds > 0;
        CacheDownloadIdleSeconds = plusConfig.CacheDownloadIdleSeconds > 0 ? plusConfig.CacheDownloadIdleSeconds : 30;
        IsRateLimitEnabled = plusConfig.CacheDownloadRateLimitMBs > 0;
        CacheDownloadRateLimitMBs = plusConfig.CacheDownloadRateLimitMBs > 0 ? plusConfig.CacheDownloadRateLimitMBs : 5;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVrChat;
        CloseToTray = config.CloseToTray;
        StartMinimized = config.StartMinimized;
        StartWithSteamVr = config.StartWithSteamVr;
        ErrorPopups = config.ErrorPopups;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;

        PreCacheVideos.Clear();
        foreach (var url in config.PreCacheVideos)
        {
            PreCacheVideos.Add(new UrlEntry(url));
        }

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(o => o.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        StatusMessage = string.Empty;
        StatusMessageColor = "#81C784";
        _isLoadingConfig = false;
    }

    private void SetHasChanges()
    {
        if (_isLoadingConfig)
        {
            return;
        }

        HasChanges = true;
        StatusMessage = Localizer.Get("SettingsUnsavedChanges");
        StatusMessageColor = "#FFB74D";
    }

    private void OnUrlCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var oldItem in e.OldItems.OfType<UrlEntry>())
            {
                oldItem.PropertyChanged -= OnUrlEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newItem in e.NewItems.OfType<UrlEntry>())
            {
                newItem.PropertyChanged += OnUrlEntryPropertyChanged;
            }
        }

        SetHasChanges();
    }

    private void OnUrlEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UrlEntry.Url))
        {
            SetHasChanges();
        }
    }

    partial void OnWebServerUrlChanged(string value) => SetHasChanges();
    partial void OnYtdlAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnYtdlAdditionalArgsChanged(string value) => SetHasChanges();
    partial void OnYtdlDubLanguageChanged(string value) => SetHasChanges();
    partial void OnCachedAssetPathChanged(string value) => SetHasChanges();
    partial void OnCacheYouTubePreferVp9Changed(bool value) => SetHasChanges();
    partial void OnCacheMaxSizeInGbChanged(float value) => SetHasChanges();
    partial void OnCacheHlsPlaylistsChanged(bool value) => SetHasChanges();
    partial void OnCacheHlsMaxLengthChanged(int value) => SetHasChanges();
    partial void OnCacheOnlyChanged(bool value) => SetHasChanges();
    partial void OnIsDelayEnabledChanged(bool value) => SetHasChanges();
    partial void OnCacheDownloadIdleSecondsChanged(int value) => SetHasChanges();
    partial void OnIsRateLimitEnabledChanged(bool value) => SetHasChanges();
    partial void OnCacheDownloadRateLimitMBsChanged(int value) => SetHasChanges();
    partial void OnPatchResoniteChanged(bool value) => SetHasChanges();
    partial void OnPatchVRCChanged(bool value) => SetHasChanges();
    partial void OnCloseToTrayChanged(bool value) => SetHasChanges();
    partial void OnStartMinimizedChanged(bool value) => SetHasChanges();
    partial void OnAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnStartWithSteamVrChanged(bool value) => SetHasChanges();
    partial void OnErrorPopupsChanged(bool value) => SetHasChanges();

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        // CacheManager resolves its path once at type initialisation and the web server
        // serves that directory, so this one genuinely cannot be applied live — say so
        // rather than appearing to have taken effect.
        var cachePathChanged = config.CachedAssetPath != CachedAssetPath;
        var patchSettingsChanged = config.PatchVrChat != PatchVRC || config.PatchResonite != PatchResonite;

        if (config.YtdlpWebServerUrl != WebServerUrl)
        {
            config.YtdlpWebServerUrl = WebServerUrl;
            WebServer.Init();
        }

        config.YtdlpAutoUpdate = YtdlAutoUpdate;
        config.YtdlpAdditionalArgs = YtdlAdditionalArgs;
        config.YtdlpDubLanguage = YtdlDubLanguage;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CacheHlsPlaylists = CacheHlsPlaylists;
        config.CacheHlsMaxLength = CacheHlsMaxLength;
        config.CacheOnly = CacheOnly;
        var plusConfig = PlusConfigManager.Config;
        plusConfig.CacheDownloadIdleSeconds = IsDelayEnabled ? CacheDownloadIdleSeconds : 0;
        plusConfig.CacheDownloadRateLimitMBs = IsRateLimitEnabled ? CacheDownloadRateLimitMBs : 0;
        plusConfig.CacheYouTubePreferVp9 = CacheYouTubePreferVp9;
        config.PatchResonite = PatchResonite;
        config.PatchVrChat = PatchVRC;
        config.CloseToTray = CloseToTray;
        config.StartMinimized = StartMinimized;
        config.StartWithSteamVr = StartWithSteamVr;
        config.ErrorPopups = ErrorPopups;
        // One row may hold a whole pasted list; split it so each URL gets its own row.
        config.PreCacheVideos = VideoPreCache.SplitUrls(PreCacheVideos.Select(item => item.Url));
        if (!config.PreCacheVideos.SequenceEqual(PreCacheVideos.Select(item => item.Url)))
        {
            PreCacheVideos.Clear();
            foreach (var url in config.PreCacheVideos)
                PreCacheVideos.Add(new UrlEntry(url));
        }
        config.AutoUpdateVrcVideoCacher = AutoUpdate;

        // Temporarily unhook config-changed events to avoid redundant LoadFromConfig calls during save
        ConfigManager.OnConfigChanged -= LoadFromConfig;
        try
        {
            ConfigManager.TrySaveConfig();
        }
        finally
        {
            ConfigManager.OnConfigChanged += LoadFromConfig;
        }

        // Patch toggles are applied straight away; they used to sit inert until the next
        // launch, with nothing saying so.
        if (patchSettingsChanged)
            FileTools.ApplyPatchSettings();

        HasChanges = false;
        StatusMessage = cachePathChanged
            ? Localizer.Get("SettingsSavedRestartRequired")
            : Localizer.Get("SettingsSaved");
        StatusMessageColor = cachePathChanged ? "#FFB74D" : "#81C784";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = Localizer.Get("SettingsReset");
        StatusMessageColor = "#81C784";
    }

    [RelayCommand]
    private void AddPreCacheVideo()
    {
        PreCacheVideos.Add(new UrlEntry("https://"));
    }

    [RelayCommand]
    private void RemovePreCacheVideo(UrlEntry url)
    {
        PreCacheVideos.Remove(url);
    }

    [RelayCommand]
    private void OpenUtilsFolder() => OpenUrl.OpenFolder(Program.UtilsPath);

    [RelayCommand]
    private void OpenSettingsFolder() => OpenUrl.OpenFolder(Program.DataPath);

    [ObservableProperty]
    private bool _isRedownloading;

    [RelayCommand]
    private async Task RedownloadUtils()
    {
        IsRedownloading = true;
        StatusMessage = Localizer.Get("RedownloadingUtils");
        StatusMessageColor = "#FFB74D";
        try
        {
            Versions.CurrentVersion.Ytdlp = string.Empty;
            Versions.CurrentVersion.Deno = string.Empty;
            Versions.CurrentVersion.Ffmpeg = string.Empty;
            Versions.Save();

            await Task.WhenAll(
                YtdlManager.TryDownloadYtdlp(),
                YtdlManager.TryDownloadDeno(),
                YtdlManager.TryDownloadFfmpeg()
            );

            StatusMessage = Localizer.Get("RedownloadComplete");
            StatusMessageColor = "#81C784";
        }
        catch (Exception)
        {
            StatusMessage = Localizer.Get("RedownloadFailed");
            StatusMessageColor = "#EF5350";
        }
        finally
        {
            IsRedownloading = false;
        }
    }
}
