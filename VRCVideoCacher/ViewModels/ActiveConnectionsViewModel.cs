using System;
using System.Collections.ObjectModel;
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
    private bool _hasConnections;

    private readonly DispatcherTimer _timer;

    public ActiveConnectionsViewModel()
    {
        // Polling timer to refresh active TCP connections every 3 seconds
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timer.Tick += (s, e) => RefreshConnections();
        _timer.Start();

        RefreshConnections();
    }

    private void RefreshConnections()
    {
        var active = SocketKill.ListActiveConnections();
        
        // Simple full reload of the socket list
        Connections.Clear();
        foreach (var conn in active)
        {
            Connections.Add(conn);
        }

        HasConnections = Connections.Count > 0;
    }

    [RelayCommand]
    private void SeverConnection(ActiveConnectionInfo? conn)
    {
        if (conn == null) return;
        SocketKill.SeverConnectionByIp(conn.RemoteAddress);
        RefreshConnections();
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
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
