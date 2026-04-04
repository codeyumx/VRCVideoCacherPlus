using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    public int DbKey { get; init; }
    public string VideoUrl { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public string UrlType { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string QueuedAt { get; init; } = string.Empty;
    public string? Title { get; init; }

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(Title))
                return Title.Length > 50 ? Title[..47] + "..." : Title;
            return VideoId;
        }
    }
}

public partial class DownloadQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private DownloadItemViewModel? _currentDownload;

    [ObservableProperty]
    private string _currentStatus = "Idle";

    [ObservableProperty]
    private string _manualUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    public ObservableCollection<DownloadItemViewModel> QueuedDownloads { get; } = [];

    public DownloadQueueViewModel()
    {
        RefreshQueue();

        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnDownloadPaused += OnDownloadPaused;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        VideoDownloader.OnDownloadProgress += OnDownloadProgressUpdate;
        DatabaseManager.OnPendingDownloadsChanged += OnQueueChanged;
    }

    private static string? LookupTitle(string videoId)
    {
        return DatabaseManager.GetVideoInfoCache(videoId)?.Title;
    }

    private void OnDownloadStarted(VideoInfo video)
    {
        var title = LookupTitle(video.VideoId);
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownload = new DownloadItemViewModel
            {
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString(),
                Title = title
            };
            CurrentStatus = $"Downloading {CurrentDownload.DisplayTitle}...";
            DownloadProgress = 0;
            IsDownloading = true;
            RefreshQueue();
        });
    }

    private void OnDownloadCompleted(VideoInfo video, bool success)
    {
        var title = LookupTitle(video.VideoId);
        var displayName = !string.IsNullOrEmpty(title) ? title : video.VideoId;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownload = null;
            CurrentStatus = success ? "Completed" : "Failed";
            StatusMessage = success
                ? $"Downloaded: {displayName}"
                : $"Failed to download: {displayName}";
            DownloadProgress = 0;
            IsDownloading = false;
            RefreshQueue();
        });
    }

    private DateTime _lastProgressUpdate;

    private void OnDownloadProgressUpdate(double percent)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUpdate).TotalMilliseconds < 250 && percent < 99.9)
            return;
        _lastProgressUpdate = now;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadProgress = percent;
        });
    }

    private void OnDownloadPaused(VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = $"Paused — waiting for stream to finish";
            RefreshQueue();
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(RefreshQueue);
    }

    [RelayCommand]
    private void RefreshQueue()
    {
        QueuedDownloads.Clear();

        var pending = VideoDownloader.GetQueueSnapshot();
        foreach (var item in pending)
        {
            QueuedDownloads.Add(new DownloadItemViewModel
            {
                DbKey = item.Key,
                VideoUrl = item.VideoUrl,
                VideoId = item.VideoId,
                UrlType = item.UrlType.ToString(),
                Format = item.DownloadFormat.ToString(),
                QueuedAt = item.QueuedAt.ToLocalTime().ToString("g"),
                Title = LookupTitle(item.VideoId)
            });
        }

        var state = VideoDownloader.GetDownloadState();
        var current = VideoDownloader.GetCurrentDownload() ?? VideoDownloader.GetPausedDownload();
        if (current != null)
        {
            CurrentDownload = new DownloadItemViewModel
            {
                VideoUrl = current.VideoUrl,
                VideoId = current.VideoId,
                UrlType = current.UrlType.ToString(),
                Format = current.DownloadFormat.ToString(),
                Title = LookupTitle(current.VideoId)
            };
            CurrentStatus = state switch
            {
                DownloadState.Paused => "Paused — waiting for stream to finish",
                DownloadState.WaitingForIdle => "Waiting for streaming to stop...",
                _ => $"Downloading {CurrentDownload.DisplayTitle}..."
            };
        }
        else
        {
            CurrentDownload = null;
            if (QueuedDownloads.Count == 0)
                CurrentStatus = "Idle";
            else if (state == DownloadState.WaitingForIdle)
                CurrentStatus = "Waiting for streaming to stop...";
            else
                CurrentStatus = $"{QueuedDownloads.Count} pending";
        }
    }

    [RelayCommand]
    private void RemoveFromQueue(DownloadItemViewModel? item)
    {
        if (item == null) return;

        if (item.DbKey > 0)
            VideoDownloader.RemoveFromQueueByKey(item.DbKey);
        else
            VideoDownloader.RemoveFromQueue(item.VideoId, Enum.Parse<DownloadFormat>(item.Format));

        StatusMessage = $"Removed: {item.DisplayTitle}";
        RefreshQueue();
    }

    [RelayCommand]
    private async Task AddManualDownload()
    {
        if (string.IsNullOrWhiteSpace(ManualUrl))
        {
            StatusMessage = "Please enter a URL";
            return;
        }

        var urls = ManualUrl.Split(['\n', '\r', ',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();

        var added = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var url in urls)
        {
            try
            {
                if (VideoId.IsYouTubePlaylist(url))
                {
                    StatusMessage = "Extracting playlist...";
                    var playlistVideos = await VideoId.GetPlaylistVideoInfos(url, true);
                    if (playlistVideos.Count == 0)
                    {
                        errors.Add($"No videos found in playlist: {url}");
                        failed++;
                        continue;
                    }
                    foreach (var video in playlistVideos)
                    {
                        VideoDownloader.QueueDownload(video);
                        added++;
                    }
                }
                else
                {
                    var videoInfo = await VideoId.GetVideoId(url, true);
                    if (videoInfo != null)
                    {
                        VideoDownloader.QueueDownload(videoInfo);
                        added++;
                    }
                    else
                    {
                        errors.Add($"Could not parse: {url}");
                        failed++;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error with {url}: {ex.Message}");
                failed++;
            }
        }

        if (added > 0)
            ManualUrl = string.Empty;

        if (failed == 0)
            StatusMessage = $"Added {added} video(s) to queue";
        else if (added == 0)
            StatusMessage = string.Join("; ", errors);
        else
            StatusMessage = $"Added {added} video(s), {failed} failed: {string.Join("; ", errors)}";
    }

    [RelayCommand]
    private void ClearQueue()
    {
        VideoDownloader.ClearQueue();
        StatusMessage = "Download queue cleared";
    }
}
