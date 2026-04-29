using System.Collections.ObjectModel;
using Avalonia.Threading;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class CacheItemViewModel : ViewModelBase
{
    public string FileName { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _thumbnailSource = string.Empty;

    [ObservableProperty]
    private UrlType _urlType = UrlType.Other;

    [ObservableProperty]
    private string? _originalUrl;

    public bool IsYouTube => UrlType == UrlType.YouTube;

    public bool HasOpenSourceLink => !IsYouTube && !string.IsNullOrEmpty(OriginalUrl);

    partial void OnUrlTypeChanged(UrlType value)
    {
        OnPropertyChanged(nameof(IsYouTube));
        OnPropertyChanged(nameof(HasOpenSourceLink));
    }

    partial void OnOriginalUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(HasOpenSourceLink));
    }

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;

    public string SizeFormatted => FormatSize(Size);

    // Event to notify parent when item is deleted
    public event Action<CacheItemViewModel>? OnDeleted;

    public async Task LoadMetadataAsync(string? originalUrl = null, UrlType? historyType = null)
    {
        // Load from DB
        var videoInfo = await YouTubeMetadataService.GetVideoMetadataAsync(VideoId);

        if (videoInfo != null)
        {
            UrlType = videoInfo.Type;
            if (!string.IsNullOrEmpty(videoInfo.Title))
            {
                Title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
        else if (historyType.HasValue)
        {
            // No VideoInfoCache row (legacy item) — classify from the latest History entry instead.
            UrlType = historyType.Value;
        }

        // Original URL for non-YouTube items so we can open the source stream/page.
        // Prefer the caller-provided value (batch-fetched) to avoid an extra DB round-trip per item.
        OriginalUrl = originalUrl ?? DatabaseManager.GetLatestHistoryUrl(VideoId);

        // Load thumbnail
        var thumbnailPath = ThumbnailManager.GetThumbnail(VideoId);
        if (VideoId.Length == 11 && string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = await YouTubeMetadataService.GetThumbnail(VideoId);

        if (!string.IsNullOrEmpty(thumbnailPath))
            ThumbnailSource = thumbnailPath;
    }

    [RelayCommand]
    private void WatchVideo()
    {
        var filePath = Path.Join(CacheManager.CachePath, FileName);
        if (!File.Exists(filePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private void OpenOnYouTube()
    {
        var url = $"https://www.youtube.com/watch?v={VideoId}";
        OpenUrlExternal(url);
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (string.IsNullOrEmpty(OriginalUrl)) return;
        OpenUrlExternal(OriginalUrl);
    }

    private static void OpenUrlExternal(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private async Task CopyUrl()
    {
        var url = $"{ConfigManager.Config.YtdlpWebServerUrl}/{FileName}";
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
            }
        }
    }

    [RelayCommand]
    private void Delete()
    {
        CacheManager.DeleteCacheItem(FileName);
        OnDeleted?.Invoke(this);
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, suffixes.Length - 1);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }
}

public partial class CacheBrowserViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private CacheItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<CacheItemViewModel> CachedVideos { get; } = [];
    public ObservableCollection<CacheItemViewModel> FilteredVideos { get; } = [];

    public CacheBrowserViewModel()
    {
        CacheManager.OnCacheChanged += OnCacheChanged;
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCache);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredVideos.Clear();

        var filter = SearchFilter?.ToLowerInvariant() ?? string.Empty;
        foreach (var video in CachedVideos)
        {
            if (string.IsNullOrEmpty(filter) ||
                video.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                video.VideoId.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredVideos.Add(video);
            }
        }

        StatusText = string.Format(Loc.Tr("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
    }

    [RelayCommand]
    private void RefreshCache()
    {
        CachedVideos.Clear();
        FilteredVideos.Clear();

        var cachedAssets = CacheManager.GetCachedAssets();
        var itemsToLoad = new List<CacheItemViewModel>();

        foreach (var (fileName, cache) in cachedAssets.OrderByDescending(x => x.Value.LastModified))
        {
            // Filter out non-video files like index.html
            if (fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;

            var videoId = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);

            var item = new CacheItemViewModel
            {
                FileName = fileName,
                VideoId = videoId,
                Size = cache.Size,
                LastModified = cache.LastModified,
                Extension = extension
            };

            // Subscribe to delete event
            item.OnDeleted += OnItemDeleted;

            CachedVideos.Add(item);
            itemsToLoad.Add(item);
        }

        ApplyFilter();

        // Load metadata (titles + thumbnails) asynchronously in the background
        _ = Task.Run(async () =>
        {
            // Batch-fetch original URLs (and history-derived type) in one query instead of N.
            var history = DatabaseManager.GetLatestHistoryUrls(itemsToLoad.Select(i => i.VideoId));
            foreach (var item in itemsToLoad)
            {
                history.TryGetValue(item.VideoId, out var entry);
                await item.LoadMetadataAsync(entry.Url, entry.Url == null ? null : entry.Type);
            }
        });
    }

    private void OnItemDeleted(CacheItemViewModel item)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedVideos.Remove(item);
            FilteredVideos.Remove(item);
            StatusText = string.Format(Loc.Tr("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
        });
    }

    [RelayCommand]
    private async Task DeleteAll()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner == null)
        {
            // No UI available — fall back to direct clear so headless/console mode still works.
            CacheManager.ClearCache();
            return;
        }

        var message = string.Format(
            Loc.Tr("ConfirmClearCacheFormat"),
            CachedVideos.Count);
        var dialog = Views.PopupWindow.CreateConfirm(message, Loc.Tr("Yes"), Loc.Tr("No"));
        dialog.Title = Loc.Tr("ConfirmClearCacheTitle");
        await dialog.ShowDialog(owner);
        if (dialog.Confirmed)
            CacheManager.ClearCache();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            if (SelectedItem != null)
            {
                var filePath = Path.Join(cachePath, SelectedItem.FileName);
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", cachePath);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

}
