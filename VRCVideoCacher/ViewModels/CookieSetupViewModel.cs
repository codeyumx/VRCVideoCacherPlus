using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class CookieSetupViewModel : ViewModelBase
{
    private const string ChromeExtensionUrl = "https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge";
    private const string FirefoxExtensionUrl = "https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter";

    // Step layout:
    //  1 - Browser selection
    //  2 - Install extension
    //  3 - Visit YouTube / wait for cookies
    //  4 - Hosts file (Windows only; skipped on other platforms)
    //  5 - Complete

    public event Action? RequestClose;
    // Set by the View to show a file picker and return the selected executable path (or null).
    public Func<Task<string?>>? PickBrowserExecutable { get; set; }

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isChrome;

    [ObservableProperty]
    private bool _cookiesReceived;

    [ObservableProperty]
    private string _manualCookies = string.Empty;

    [ObservableProperty]
    private string _manualCookiesError = string.Empty;

    [ObservableProperty]
    private bool _hostState;

    [ObservableProperty]
    private bool _dontShowAgainChecked;

    public bool IsDontShowAgainCheckboxVisible => !IsStep5 && !(ConfigManager.Config?.CookieSetupCompleted ?? false);

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;

    public bool CanGoBack => CurrentStep > 1 && CurrentStep < 5;
    public bool CanGoNext => CurrentStep switch
    {
        1 => false, // Must select browser
        2 => true,
        3 => CookiesReceived,
        4 => true,  // Hosts step is optional
        5 => true,
        _ => false
    };

    public string NextButtonText => CurrentStep == 5 ? Localizer.Get("Done") : Localizer.Get("Next");

    public string ExtensionStoreButtonText => IsChrome
        ? Localizer.Get("OpenChromeWebStore")
        : Localizer.Get("OpenFirefoxAddons");

    public string CookieStatusText => CookiesReceived
        ? Localizer.Get("CookiesReceived")
        : Localizer.Get("WaitingForCookies");

    public string CookieStatusIcon => CookiesReceived
        ? "CheckCircle"
        : "TimerSand";

    public IBrush CookieStatusColor => CookiesReceived
        ? new SolidColorBrush(Color.Parse("#81C784"))
        : new SolidColorBrush(Color.Parse("#FFB74D"));

    public string HostStatusText => HostState
        ? Localizer.Get("HostsActive")
        : Localizer.Get("NotConfigured");

    public string HostStatusIcon => HostState ? "CheckCircle" : "AlertCircleOutline";

    public IBrush HostStatusColor => HostState
        ? new SolidColorBrush(Color.Parse("#81C784"))
        : new SolidColorBrush(Color.Parse("#FFB74D"));

    public string HostButtonText => HostState ? Localizer.Get("RemoveEntry") : Localizer.Get("AddHostsEntry");

    public CookieSetupViewModel()
    {
        Program.OnCookiesUpdated += OnCookiesUpdated;
        CookiesReceived = Program.IsCookiesEnabledAndValid();
        _hostState = ElevatorManager.HasHostsLine;

        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.InvokeAsync(RefreshLocalizedComputedProperties);
        DontShowAgainChecked = false;
    }

    private void RefreshLocalizedComputedProperties()
    {
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
        OnPropertyChanged(nameof(CookieStatusText));
        OnPropertyChanged(nameof(HostStatusText));
        OnPropertyChanged(nameof(HostButtonText));
    }

    private void OnCookiesUpdated()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CookiesReceived = Program.IsCookiesEnabledAndValid();
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CookieStatusText));
            OnPropertyChanged(nameof(CookieStatusIcon));
            OnPropertyChanged(nameof(CookieStatusColor));

            if (CookiesReceived && CurrentStep == 3)
            {
                CurrentStep = NextStepFrom(3);
                UpdateStepProperties();
            }
        });
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepProperties();
    }

    private void UpdateStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
    }

    // On non-Windows platforms the hosts step doesn't apply, so skip over it.
    private static int NextStepFrom(int step) =>
        step == 3 && !OperatingSystem.IsWindows() ? 5 : step + 1;

    private static int PrevStepFrom(int step) =>
        step == 5 && !OperatingSystem.IsWindows() ? 3 : step - 1;

    [RelayCommand]
    private void SelectChrome()
    {
        IsChrome = true;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void SelectFirefox()
    {
        IsChrome = false;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void OpenExtensionStore()
    {
        OpenUrl(IsChrome ? ChromeExtensionUrl : FirefoxExtensionUrl);
    }

    [RelayCommand]
    private void OpenYouTube()
    {
        OpenUrl("https://www.youtube.com");
    }

    [RelayCommand]
    private async Task OpenYouTubeWith()
    {
        if (PickBrowserExecutable == null) return;
        var exePath = await PickBrowserExecutable();
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = exePath, Arguments = "https://www.youtube.com", UseShellExecute = false });
        }
        catch (Exception ex)
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var owner = lifetime?.MainWindow;
            if (owner != null)
                await new Views.PopupWindow($"Could not launch browser:\n{ex.Message}").ShowDialog(owner);
        }
    }

    // Lets a user who copied cookies from the extension paste them in directly.
    // Two distinct failures: wrong format (bad copy-paste) vs. well-formed but dead cookies.
    [RelayCommand]
    private async Task SaveManualCookies()
    {
        var cookies = ManualCookies.Trim();
        if (!Program.IsCookiesValid(cookies))
        {
            // Missing youtube.com / LOGIN_INFO — not the Netscape export the extension produces.
            ManualCookiesError = Localizer.Get("ManualCookiesWrongFormat");
            return;
        }

        File.WriteAllText(YtdlManager.CookiesPath, cookies);
        Program.NotifyCookiesUpdated();

        // Well-formed: now check they actually work against YouTube.
        var live = await Program.ValidateCookiesAsync();
        if (live == false)
        {
            ManualCookiesError = Localizer.Get("ManualCookiesExpired");
            return;
        }

        // true = valid, null = couldn't verify (offline) — either way they're saved.
        ManualCookiesError = string.Empty;
        ManualCookies = string.Empty;
    }

    [RelayCommand]
    private void ToggleHost()
    {
        ElevatorManager.ToggleHostLine();
        Dispatcher.UIThread.Post(() =>
        {
            HostState = ElevatorManager.HasHostsLine;
            OnPropertyChanged(nameof(HostButtonText));
            OnPropertyChanged(nameof(HostStatusText));
            OnPropertyChanged(nameof(HostStatusIcon));
            OnPropertyChanged(nameof(HostStatusColor));
        });
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == 5)
        {
            ConfigManager.Config.CookieSetupCompleted = true;
            ConfigManager.TrySaveConfig();
            Program.OnCookiesUpdated -= OnCookiesUpdated;
            RequestClose?.Invoke();
            return;
        }

        CurrentStep = NextStepFrom(CurrentStep);
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
            CurrentStep = PrevStepFrom(CurrentStep);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (DontShowAgainChecked)
        {
            ConfigManager.Config.CookieSetupCompleted = true;
            ConfigManager.TrySaveConfig();
        }

        Program.OnCookiesUpdated -= OnCookiesUpdated;
        RequestClose?.Invoke();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { /* Ignore errors */ }
    }
}
