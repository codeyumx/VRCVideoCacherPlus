using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
            ScrollToTop();
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogViewerViewModel { AutoScroll: true } && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToTop();
        }
    }

    private void ScrollToTop()
    {
        if (LogListBox.ItemCount > 0)
        {
            LogListBox.ScrollIntoView(0);
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

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }
}
