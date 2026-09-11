using System.Collections.ObjectModel;
using Avalonia.Threading;
using Jeek.Avalonia.Localization;
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

    /// <summary>
    /// "1.2 MB/s · 3m 20s left", or just the rate when no estimate is available, or empty
    /// when neither is known yet. Deliberately blank rather than showing a placeholder:
    /// early in a transfer yt-dlp genuinely reports "Unknown".
    /// </summary>
    [ObservableProperty]
    private string _downloadRateText = string.Empty;

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
            DownloadRateText = string.Empty;
            IsDownloading = true;
            RefreshQueue();
        });
    }

    private static string TranslateFailReason(string failReason)
    {
        // Format: "SkipReasonTooLong|34|10" for parameterized keys
        var parts = failReason.Split('|');
        var key = parts[0];
        var translated = Localizer.Get(key);
        if (translated == key)
            return failReason; // not a translation key, use as-is (e.g. exception messages)
        if (parts.Length > 1)
        {
            var args = parts.Skip(1).Cast<object>().ToArray();
            return string.Format(translated, args);
        }
        return translated;
    }

    private void OnDownloadCompleted(VideoInfo video, bool success, string? failReason)
    {
        var title = LookupTitle(video.VideoId);
        var displayName = !string.IsNullOrEmpty(title) ? title : video.VideoId;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownload = null;
            CurrentStatus = success ? "Completed" : "Failed";
            if (success)
                StatusMessage = string.Format(Localizer.Get("DownloadCompleted"), displayName);
            else if (!string.IsNullOrEmpty(failReason))
                StatusMessage = string.Format(Localizer.Get("DownloadSkipped"), displayName, TranslateFailReason(failReason));
            else
                StatusMessage = string.Format(Localizer.Get("DownloadFailed"), displayName);
            DownloadProgress = 0;
            DownloadRateText = string.Empty;
            IsDownloading = false;
            RefreshQueue();
        });
    }

    private DateTime _lastProgressUpdate;

    private void OnDownloadProgressUpdate(DownloadProgress progress)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUpdate).TotalMilliseconds < 250 && progress.Percent < 99.9)
            return;
        _lastProgressUpdate = now;

        var detail = FormatProgressDetail(progress);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadProgress = progress.Percent;
            DownloadRateText = detail;
        });
    }

    /// <summary>
    /// Joins whichever of rate and time-remaining are actually known.
    /// </summary>
    private static string FormatProgressDetail(DownloadProgress progress)
    {
        var rate = progress.FormatRate();
        var eta = progress.FormatEta();

        if (eta != null)
        {
            var remaining = string.Format(Localizer.Get("DownloadTimeRemaining"), eta);
            return rate != null ? $"{rate} · {remaining}" : remaining;
        }

        return rate ?? string.Empty;
    }

    private void OnDownloadPaused(VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = $"Paused — waiting for stream to finish";
            RefreshQueue();
        });
    }

    private bool _refreshPending;

    private void OnQueueChanged()
    {
        // Queue events arrive once per queued item; when a playlist enqueues hundreds
        // at once, rebuilding the whole list per event saturates the UI thread.
        // Coalesce bursts into a single refresh on the next dispatcher pass.
        if (_refreshPending)
            return;
        _refreshPending = true;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _refreshPending = false;
            RefreshQueue();
        });
    }

    [RelayCommand]
    private void RefreshQueue()
    {
        var pending = VideoDownloader.GetQueueSnapshot();
        var state = VideoDownloader.GetDownloadState();
        var current = VideoDownloader.GetCurrentDownload() ?? VideoDownloader.GetPausedDownload();

        // One batched title lookup for the whole list — per-row queries made every
        // refresh O(N) DB calls, and the queue fires a refresh per queued item.
        var titleIds = pending.Select(p => p.VideoId).ToList();
        if (current != null)
            titleIds.Add(current.VideoId);
        var titles = DatabaseManager.GetVideoTitles(titleIds);

        QueuedDownloads.Clear();
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
                Title = titles.GetValueOrDefault(item.VideoId)
            });
        }

        if (current != null)
        {
            CurrentDownload = new DownloadItemViewModel
            {
                VideoUrl = current.VideoUrl,
                VideoId = current.VideoId,
                UrlType = current.UrlType.ToString(),
                Format = current.DownloadFormat.ToString(),
                Title = titles.GetValueOrDefault(current.VideoId)
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
    private void DownloadNow(DownloadItemViewModel? item)
    {
        if (item == null) return;

        // Bump to top of queue first (unless it's already first in DB)
        if (item.DbKey > 0)
            VideoDownloader.BumpToTopOfQueue(item.DbKey);

        VideoDownloader.ForceDownloadNext();
        StatusMessage = $"Force downloading: {item.DisplayTitle}";
        RefreshQueue();
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
                    var queued = 0;
                    foreach (var video in playlistVideos)
                    {
                        VideoDownloader.QueueDownload(video);
                        added++;

                        // Enqueueing is synchronous DB work; yield occasionally so a
                        // multi-thousand-entry playlist can't pin the UI thread.
                        if (++queued % 25 == 0)
                            await Task.Yield();
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
