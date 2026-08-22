using Jeek.Avalonia.Localization;
using Avalonia.Controls;
using Avalonia.Threading;
using VRCVideoCacher.Utils;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LoggerUtils.LogUnhandledException(e.Exception, "Unhandled UI thread exception");
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Only run once
        Opened -= OnWindowOpened;

        // Delay slightly to let the main window fully render
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(500);
            Program.InitializeUIBackend();
        });

        // Settings now share Config.json with the original VRCVideoCacher, which rewrites
        // that file and drops what it doesn't recognise. Say so once — on a fresh install
        // and on the first launch after migrating — before anything else competes for
        // attention.
        await ShowSharedConfigNoticeIfNeeded();

        // Check if we should show the cookie setup wizard
        // Show if: cookies are enabled, setup not completed, and cookies not already valid
        if (ConfigManager.Config.YtdlpUseCookies &&
            !ConfigManager.Config.CookieSetupCompleted &&
            !Program.IsCookiesEnabledAndValid())
        {
            // Delay slightly to let the main window fully render
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(500);
                await ShowCookieSetupDialog();
            });
        }
    }

    private async Task ShowSharedConfigNoticeIfNeeded()
    {
        if (ConfigManager.Config.HasShownSharedConfigNotice)
            return;

        // Recorded before showing, so a crash in the dialog can't turn this into a prompt
        // that reappears every launch.
        ConfigManager.Config.HasShownSharedConfigNotice = true;
        ConfigManager.TrySaveConfig();

        var notice = new PopupWindow(Localizer.Get("SharedConfigNotice"))
        {
            Title = Localizer.Get("SharedConfigNoticeTitle")
        };
        await notice.ShowDialog(this);
    }

    private async Task ShowCookieSetupDialog()
    {
        var viewModel = new CookieSetupViewModel();
        var window = new CookieSetupWindow
        {
            DataContext = viewModel
        };

        viewModel.RequestClose += () => window.Close();

        await window.ShowDialog(this);
    }
}
