using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using VRCVideoCacher.Models;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class LogViewerView : UserControl
{
    public LogViewerView()
    {
        InitializeComponent();

        // Subscribe to collection changes for auto-scroll
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogViewerViewModel vm)
            {
                vm.FilteredLogEntries.CollectionChanged += OnLogEntriesChanged;
            }
        };

        // Scroll to bottom when view becomes visible
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is true)
        {
            ScrollToBottom();
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogViewerViewModel { AutoScroll: true } && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        if (LogListBox.ItemCount > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
        }
    }

    private async void OnCopyLogEntries(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var selected = LogListBox.SelectedItems;
        if (selected == null || selected.Count == 0) return;

        var messageOnly = menuItem.Tag?.ToString() == "message";
        var lines = selected.OfType<LogEntry>()
            .Select(entry => messageOnly
                ? entry.Message
                : $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] [{entry.Source}] {entry.Message}");

        var text = string.Join(Environment.NewLine, lines);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
    }
}
