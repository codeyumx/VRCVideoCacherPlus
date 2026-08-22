using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class ActiveConnectionsViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<ActiveConnectionInfo> Connections { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#81C784";

    [ObservableProperty]
    private bool _hasConnections;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _cts = new();

    // The unfiltered result of the last poll. Typing in the search box filters this rather
    // than re-querying the operating system on every keystroke, which is what it used to do.
    private List<ActiveConnectionInfo> _lastSnapshot = [];

    public ActiveConnectionsViewModel()
    {
        _ = RefreshLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Polls on a background task.
    ///
    /// This was a DispatcherTimer, so every tick ran the whole enumeration — every process
    /// on the machine, a walk of /proc/[pid]/fd, and a parse of /proc/net/tcp — on the UI
    /// thread, every three seconds, for as long as the application was running. Only the
    /// finished list is marshalled back to the UI now.
    /// </summary>
    private async Task RefreshLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = await Task.Run(NetworkConnections.List, token);
                await Dispatcher.UIThread.InvokeAsync(() => Apply(snapshot));
                await timer.WaitForNextTickAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Active connection refresh failed.");

                try { await timer.WaitForNextTickAsync(token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private void Apply(List<ActiveConnectionInfo> snapshot)
    {
        _lastSnapshot = snapshot;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Reconciles the visible list in place rather than clearing and rebuilding it. A full
    /// rebuild every three seconds dropped the grid selection, which made the per-row Sever
    /// and Create Rule buttons a race against the next tick.
    /// </summary>
    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var desired = string.IsNullOrEmpty(query)
            ? _lastSnapshot
            : _lastSnapshot.Where(c => Matches(c, query)).ToList();

        var desiredKeys = desired.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        for (var i = Connections.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(Connections[i].Key))
                Connections.RemoveAt(i);
        }

        var presentKeys = Connections.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var connection in desired)
        {
            if (presentKeys.Contains(connection.Key))
                continue;

            Connections.Add(connection);
        }

        HasConnections = Connections.Count > 0;
    }

    private static bool Matches(ActiveConnectionInfo connection, string query) =>
        connection.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        connection.RemoteAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        connection.LocalAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        connection.AssociatedTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        connection.AssociatedUrl.Contains(query, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task RefreshConnections()
    {
        var snapshot = await Task.Run(NetworkConnections.List);
        Apply(snapshot);
    }

    [RelayCommand]
    private async Task SeverAllConnections()
    {
        var result = await ConnectionSevering.SeverAllAsync();
        ReportSeverResult(result);
        await RefreshConnections();
    }

    [RelayCommand]
    private async Task SeverConnection(ActiveConnectionInfo? connection)
    {
        if (connection == null)
            return;

        var result = await ConnectionSevering.SeverAddressAsync(connection.RemoteAddress);
        ReportSeverResult(result);
        await RefreshConnections();
    }

    /// <summary>
    /// Says what actually happened. "Not permitted" is reported as its own outcome rather
    /// than being dressed up as success, which is what the old code did on every Linux
    /// machine without root and every Windows machine without elevation.
    /// </summary>
    private void ReportSeverResult(SeverResult result)
    {
        switch (result.RemoteOutcome)
        {
            case SeverOutcome.NotPermitted:
                SetStatus(Localizer.Get("SeverNeedsPrivileges"), "#FFB74D");
                return;

            case SeverOutcome.Unsupported when result.LocalStreamsClosed == 0:
                SetStatus(Localizer.Get("SeverUnsupported"), "#FFB74D");
                return;

            case SeverOutcome.Failed:
                SetStatus(Localizer.Get("SeverFailed"), "#E57373");
                return;
        }

        if (result.AnythingClosed)
        {
            var total = result.LocalStreamsClosed + result.RemoteSocketsSevered;
            SetStatus(string.Format(Localizer.Get("SeverSucceeded"), total), "#81C784");
            return;
        }

        SetStatus(Localizer.Get("SeverNothingToDo"), "#888888");
    }

    [RelayCommand]
    private async Task CreateRule(ActiveConnectionInfo? connection)
    {
        if (connection == null)
            return;

        var pattern = !string.IsNullOrEmpty(connection.AssociatedUrl) ? connection.AssociatedUrl : connection.RemoteAddress;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
            return;

        if (lifetime.MainWindow?.DataContext is not MainWindowViewModel mainVm)
            return;

        await mainVm.NavigateToRulesCommand.ExecuteAsync(null);
        await mainVm.Rules.AddRuleWithPattern(pattern);
    }

    [RelayCommand]
    private async Task CopyAddress(ActiveConnectionInfo? connection)
    {
        if (connection == null)
            return;

        var text = !string.IsNullOrEmpty(connection.AssociatedUrl) ? connection.AssociatedUrl : connection.RemoteAddress;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard == null)
            return;

        await clipboard.SetTextAsync(text);
        SetStatus(Localizer.Get("AddressCopied"), "#81C784");
    }

    private void SetStatus(string message, string colorHex)
    {
        StatusMessage = message;
        StatusMessageColor = colorHex;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
