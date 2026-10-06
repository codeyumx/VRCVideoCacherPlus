using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<ActiveVideoSessionViewModel> Sessions { get; } = [];

    [ObservableProperty]
    private bool _hasActiveSessions;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#81C784";

    private readonly DispatcherTimer _timer;

    public NowPlayingViewModel()
    {
        // Setup timer to tick progress bars every 1 second
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => TickProgress();
        _timer.Start();

        ActiveStreamTracker.OnSessionsChanged += OnSessionsChangedHandler;
        RefreshSessions();
    }

    private void OnSessionsChangedHandler()
    {
        Dispatcher.UIThread.Post(RefreshSessions);
    }

    private void RefreshSessions()
    {
        var rawSessions = ActiveStreamTracker.GetActiveSessions();

        // 1. Remove old sessions
        var toRemove = Sessions.Where(s => rawSessions.All(r => r.ResolvedUrl != s.ResolvedUrl && r.OriginalUrl != s.OriginalUrl)).ToList();
        foreach (var item in toRemove)
        {
            Sessions.Remove(item);
        }

        // 2. Add or update sessions
        foreach (var raw in rawSessions)
        {
            var existing = Sessions.FirstOrDefault(s => s.ResolvedUrl == raw.ResolvedUrl || s.OriginalUrl == raw.OriginalUrl);
            if (existing == null)
            {
                Sessions.Add(new ActiveVideoSessionViewModel(raw));
            }
            else
            {
                existing.TriggerRefresh();
            }
        }

        HasActiveSessions = Sessions.Count > 0;
    }

    private void TickProgress()
    {
        foreach (var session in Sessions)
        {
            session.TriggerRefresh();
        }
    }

    [RelayCommand]
    private async Task SeverStream(ActiveVideoSessionViewModel? item)
    {
        if (item == null) return;

        // A cached video streams from our own server (no remote address); anything else is a
        // direct CDN connection.
        var result = string.IsNullOrEmpty(item.RemoteIp)
            ? new SeverResult(API.LocalStreamRegistry.CloseAll(), 0, SeverOutcome.NothingToDo)
            : await ConnectionSevering.SeverAddressAsync(item.RemoteIp, allowElevation: true);

        var (message, color) = ActiveConnectionsViewModel.DescribeSeverResult(result);
        StatusMessage = message;
        StatusMessageColor = color;

        if (result.AnythingClosed)
            ActiveStreamTracker.RemoveSessionByUrl(item.ResolvedUrl);
    }

    [RelayCommand]
    private async Task CreateRule(ActiveVideoSessionViewModel? item)
    {
        if (item == null) return;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            await mainVm.NavigateToRulesCommand.ExecuteAsync(null);
            // Rule patterns are regular expressions, so the URL must be escaped to match literally.
            await mainVm.Rules.AddRuleWithPattern("^" + Regex.Escape(item.OriginalUrl));
        }
    }

    [RelayCommand]
    private async Task CopyUrl(ActiveVideoSessionViewModel? item)
    {
        if (item == null) return;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(item.OriginalUrl);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        ActiveStreamTracker.OnSessionsChanged -= OnSessionsChangedHandler;
    }
}

public class ActiveVideoSessionViewModel : ViewModelBase
{
    private readonly ActiveVideoSession _session;

    public ActiveVideoSessionViewModel(ActiveVideoSession session)
    {
        _session = session;
    }

    public string? VideoId => _session.VideoId;
    public string Title => _session.Title;
    public string OriginalUrl => _session.OriginalUrl;
    public string ResolvedUrl => _session.ResolvedUrl;
    public string? RemoteIp => _session.RemoteIp;
    public string Status => _session.Status;
    public double? Duration => _session.Duration;
    public string? ThumbnailUrl => _session.ThumbnailUrl;

    public double CurrentPosition => _session.CurrentPosition;

    public double ProgressPercentage
    {
        get
        {
            if (Duration == null || Duration == 0) return 0;
            return (CurrentPosition / Duration.Value) * 100;
        }
    }

    public string ProgressText
    {
        get
        {
            if (Duration == null || Duration == 0)
            {
                if (Status == "Playing")
                {
                    var elapsed = _session.PlaybackStartedTime.HasValue
                        ? (DateTime.UtcNow - _session.PlaybackStartedTime.Value)
                        : TimeSpan.Zero;
                    return $"{elapsed:mm\\:ss} / Live";
                }
                return "--:--";
            }
            var current = TimeSpan.FromSeconds(CurrentPosition);
            var total = TimeSpan.FromSeconds(Duration.Value);
            return $"{current:mm\\:ss} / {total:mm\\:ss}";
        }
    }

    public bool IsPlaying => Status == "Playing";
    public bool IsLoading => Status == "Loading";
    public bool IsFailed => Status == "Failed";

    public string StatusColor => Status switch
    {
        "Playing" => "#81C784", // Green
        "Loading" => "#FFD54F", // Yellow
        "Failed" => "#E57373",  // Red
        _ => "#E0E0E0"          // Gray
    };

    public void TriggerRefresh()
    {
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(StatusColor));
    }
}
