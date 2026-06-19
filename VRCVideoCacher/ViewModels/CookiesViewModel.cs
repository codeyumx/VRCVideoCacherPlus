using System.IO;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.API;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

// Dedicated panel for managing YouTube cookies: toggle, request a refresh from the
// browser extension, paste manually, and verify the cookies actually work against YouTube.
public partial class CookiesViewModel : ViewModelBase
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#81C784"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFB74D"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E57373"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#888888"));

    [ObservableProperty]
    private bool _useCookies;

    // When false (default), only legacy-compatible features show: verify + manual paste.
    // The "Request fresh cookies" instant-refresh button is a Beta-extension-only feature.
    [ObservableProperty]
    private bool _useBetaExtension;

    [ObservableProperty]
    private string _manualCookies = string.Empty;

    [ObservableProperty]
    private string _manualCookiesError = string.Empty;

    [ObservableProperty]
    private bool _isVerifying;

    // Live-check result: null = unknown/not yet verified, true = working, false = rejected.
    [ObservableProperty]
    private bool? _liveValid;

    public CookiesViewModel()
    {
        _useCookies = ConfigManager.Config.YtdlpUseCookies;
        _useBetaExtension = ConfigManager.Config.UseBetaExtension;
        Program.OnCookiesUpdated += OnCookiesUpdated;
        _ = VerifyAsync();
    }

    private void OnCookiesUpdated()
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            LiveValid = null;
            RefreshStatus();
            await VerifyAsync();
        });
    }

    partial void OnUseCookiesChanged(bool value)
    {
        ConfigManager.Config.YtdlpUseCookies = value;
        ConfigManager.TrySaveConfig();
        LiveValid = null;
        RefreshStatus();
        if (value)
            _ = VerifyAsync();
    }

    partial void OnUseBetaExtensionChanged(bool value)
    {
        ConfigManager.Config.UseBetaExtension = value;
        ConfigManager.TrySaveConfig();
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColor));
    }

    private bool HasValidFile =>
        Program.DoesCookieFileExist() &&
        Program.IsCookiesValid(File.Exists(YtdlManager.CookiesPath) ? File.ReadAllText(YtdlManager.CookiesPath) : string.Empty);

    public string StatusText
    {
        get
        {
            if (!UseCookies) return Localizer.Get("CookiesStatusDisabled");
            if (IsVerifying) return Localizer.Get("CookiesStatusVerifying");
            if (!HasValidFile) return Localizer.Get("CookiesStatusMissing");
            return LiveValid switch
            {
                true => Localizer.Get("CookiesStatusWorking"),
                false => Localizer.Get("CookiesStatusExpired"),
                null => Localizer.Get("CookiesStatusPresent"),
            };
        }
    }

    public string StatusIcon
    {
        get
        {
            if (!UseCookies) return "CloseCircle";
            if (IsVerifying) return "TimerSand";
            if (!HasValidFile) return "AlertCircle";
            return LiveValid switch
            {
                true => "CheckCircle",
                false => "AlertCircle",
                null => "HelpCircle",
            };
        }
    }

    public IBrush StatusColor
    {
        get
        {
            if (!UseCookies) return Grey;
            if (IsVerifying) return Amber;
            if (!HasValidFile) return Red;
            return LiveValid switch
            {
                true => Green,
                false => Red,
                null => Amber,
            };
        }
    }

    partial void OnIsVerifyingChanged(bool value) => RefreshStatus();
    partial void OnLiveValidChanged(bool? value) => RefreshStatus();

    private async Task VerifyAsync()
    {
        if (!UseCookies || !HasValidFile)
            return;

        IsVerifying = true;
        try
        {
            LiveValid = await Program.ValidateCookiesAsync();
        }
        finally
        {
            IsVerifying = false;
        }
    }

    [RelayCommand]
    private async Task Verify() => await VerifyAsync();

    // Beta-extension only: wakes the extension's long-poll so it pushes fresh cookies
    // immediately. The browser just needs the Beta extension installed and a YouTube login;
    // automatic sharing does not need to be enabled.
    [RelayCommand]
    private void RequestRefresh()
    {
        ApiController.RequestCookieRefresh();
        ManualCookiesError = string.Empty;
    }

    [RelayCommand]
    private async Task SaveManualCookies()
    {
        var cookies = ManualCookies.Trim();
        if (!Program.IsCookiesValid(cookies))
        {
            ManualCookiesError = Localizer.Get("ManualCookiesWrongFormat");
            return;
        }

        await File.WriteAllTextAsync(YtdlManager.CookiesPath, cookies);
        Program.NotifyCookiesUpdated();

        var live = await Program.ValidateCookiesAsync();
        if (live == false)
        {
            ManualCookiesError = Localizer.Get("ManualCookiesExpired");
            return;
        }

        ManualCookiesError = string.Empty;
        ManualCookies = string.Empty;
    }

    [RelayCommand]
    private void ClearCookies()
    {
        Program.DeleteCookieFile();
        LiveValid = null;
        Program.NotifyCookiesUpdated();
    }
}
