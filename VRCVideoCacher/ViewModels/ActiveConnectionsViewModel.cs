using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private readonly DispatcherTimer _timer;

    public ActiveConnectionsViewModel()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timer.Tick += (s, e) => RefreshConnections();
        _timer.Start();

        RefreshConnections();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterConnections();
    }

    [RelayCommand]
    public void RefreshConnections()
    {
        var active = SocketKill.ListActiveConnections();
        Connections.Clear();

        var query = SearchText.Trim().ToLowerInvariant();
        foreach (var conn in active)
        {
            if (string.IsNullOrEmpty(query) ||
                conn.ProcessName.ToLowerInvariant().Contains(query) ||
                conn.RemoteAddress.Contains(query) ||
                conn.LocalAddress.Contains(query) ||
                conn.AssociatedTitle.ToLowerInvariant().Contains(query) ||
                conn.AssociatedUrl.ToLowerInvariant().Contains(query))
            {
                Connections.Add(conn);
            }
        }

        HasConnections = Connections.Count > 0;
    }

    private void FilterConnections()
    {
        RefreshConnections();
    }

    [RelayCommand]
    public void SeverAllConnections()
    {
        try
        {
            SocketKill.SeverActiveVideoConnections();
            SetStatus("Severed all active video connections.", "#81C784");
            RefreshConnections();
        }
        catch (Exception ex)
        {
            SetStatus($"Error severing connections: {ex.Message}", "#E57373");
        }
    }

    [RelayCommand]
    private void SeverConnection(ActiveConnectionInfo? conn)
    {
        if (conn == null) return;
        try
        {
            SocketKill.SeverConnectionByIp(conn.RemoteAddress);
            SetStatus($"Severed connection to {conn.RemoteAddress}", "#81C784");
            RefreshConnections();
        }
        catch (Exception ex)
        {
            SetStatus($"Error severing connection: {ex.Message}", "#E57373");
        }
    }

    [RelayCommand]
    private async Task CreateRule(ActiveConnectionInfo? conn)
    {
        if (conn == null) return;
        var pattern = !string.IsNullOrEmpty(conn.AssociatedUrl) ? conn.AssociatedUrl : conn.RemoteAddress;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            await mainVm.NavigateToRulesCommand.ExecuteAsync(null);
            await mainVm.Rules.AddRuleWithPattern(pattern);
        }
    }

    [RelayCommand]
    private async Task CopyAddress(ActiveConnectionInfo? conn)
    {
        if (conn == null) return;
        var text = !string.IsNullOrEmpty(conn.AssociatedUrl) ? conn.AssociatedUrl : conn.RemoteAddress;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
            SetStatus("Address copied to clipboard.", "#81C784");
        }
    }

    private void SetStatus(string message, string colorHex)
    {
        StatusMessage = message;
        StatusMessageColor = colorHex;
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
