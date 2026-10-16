using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Integrations.YouTube;

public class YouTubeIntegration : Integration
{
    private static readonly ILogger Log = Program.Logger.ForContext<YouTubeIntegration>();

    /// <summary>
    /// VideoId given to a channel's /live URL. It names no particular video — the channel
    /// decides what is live — so it must never be cached, nor used as a metadata key.
    /// </summary>
    public const string LiveVideoId = "live";

    private static readonly string[] Hosts =
    [
        "youtube.com", "youtu.be", "www.youtube.com", "m.youtube.com", "music.youtube.com",
        "youtube-nocookie.com", "www.youtube-nocookie.com"
    ];
    private static readonly Regex IdRegex = new(@"(?:youtube(?:-nocookie)?\.com\/(?:[^\/\n\s]+\/\S+\/|(?:v|e(?:mbed)?)\/|live\/|\S*?[?&]v=)|youtu\.be\/)([a-zA-Z0-9_-]{11})");
    private static readonly Regex BareIdRegex = new(@"^\/(?=[a-zA-Z0-9_-]*[\d_A-Z-])[a-zA-Z0-9_-]{11}$");
    private static readonly Regex VideoIdRegex = new(@"^[a-zA-Z0-9_-]{11}$");

    private const string AVProFormat = "(mp4/best)[height<=?1080][height>=?64][width>=?64]";
    private const string UnityPlayerFormat = "(mp4/best)[vcodec!=av01][vcodec!=vp9.2][height<=?1080][height>=?64][width>=?64][protocol^=http]";

    public override string Name => "YouTube";

    public override bool CanHandle(Uri uri) => Hosts.Contains(uri.Host);

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        string? videoId = null;
        // Persist a canonical single-video URL so history/buttons never link to a playlist.
        string? canonicalUrl = null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var match = IdRegex.Match(url);
        if (match.Success)
            videoId = match.Groups[1].Value;
        else if (segments is ["shorts", var shortId, ..])
            videoId = VideoIdRegex.IsMatch(shortId) ? shortId : null;
        else if (segments is [_, .., "live"])
        {
            // A channel's live page (/@handle/live, /channel/UC…/live, …). There is no
            // video id to canonicalise to; yt-dlp resolves the channel URL to whatever is
            // currently live, so keep that URL, minus query and fragment.
            videoId = LiveVideoId;
            canonicalUrl = $"https://www.youtube.com/{string.Join('/', segments)}";
        }
        else if (BareIdRegex.IsMatch(uri.AbsolutePath))
            videoId = uri.AbsolutePath.TrimStart('/');

        if (string.IsNullOrEmpty(videoId))
        {
            Log.Warning("Failed to parse video ID from YouTube URL: {URL}", url);
            return Task.FromResult<VideoInfo?>(null);
        }

        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = canonicalUrl ?? $"https://www.youtube.com/watch?v={videoId}",
            VideoId = videoId,
            UrlType = UrlType.YouTube,
            DownloadFormat = avPro ? DownloadFormat.Webm : DownloadFormat.MP4
        });
    }

    public override List<string> GetYtdlpArguments(Uri uri, bool avPro)
    {
        var args = new List<string>();

        if (avPro)
        {
            // Safari impersonation with the web client is what surfaces the muxed m3u8
            // streams, which are currently the only ones AVPro can play — WMF is unmaintained.
            args.Add("--impersonate");
            args.Add("safari");
            args.Add("--extractor-args");
            args.Add("youtube:player_client=web");

            var lang = ConfigManager.Config.YtdlpDubLanguage;
            args.Add("-f");
            args.Add(!string.IsNullOrEmpty(lang) ? $"[language={lang}]/{AVProFormat}" : AVProFormat);
        }
        else
        {
            // Unity Player
            args.Add("-f");
            args.Add(UnityPlayerFormat);
        }

        return args;
    }
}
