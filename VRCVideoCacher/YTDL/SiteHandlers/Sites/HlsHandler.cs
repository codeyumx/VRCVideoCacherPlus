using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

/// <summary>
/// Handles HLS manifests regardless of URL shape. Detection is content-based:
/// a GET on the URL is inspected for an HLS content-type or a #EXTM3U body
/// prefix. Probe results are memoized briefly so GetVideoInfo doesn't refetch.
/// </summary>
public class HlsHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<HlsHandler>();

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(3),
        AllowAutoRedirect = true,
    })
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(5),
    };

    // HLS content types per RFC 8216 + common variants
    private static readonly string[] HlsContentTypes =
    [
        "application/vnd.apple.mpegurl",
        "application/x-mpegurl",
        "audio/mpegurl",
        "audio/x-mpegurl",
        "vnd.apple.mpegurl"
    ];

    // IsTransportStream covers raw progressive MPEG-TS endpoints (no playlist, just a
    // single binary stream with Content-Length). Treated as a sibling of HLS for caching
    // — they share the AVPro pass-through and the yt-dlp remux-to-MP4 download path.
    private sealed record ProbeResult(
        bool IsHls,
        double? Duration,
        bool IsComplete,
        string? Title,
        bool IsTransportStream = false,
        long? ContentLength = null);

    private static readonly ConcurrentDictionary<string, (ProbeResult Result, DateTime At)> ProbeCache = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<ProbeResult>>> InFlightProbes = new();
    private static readonly TimeSpan ProbeTtl = TimeSpan.FromMinutes(5);
    // Negative results (probe failed or returned non-HLS) are cached briefly so that a
    // transient CDN error doesn't permanently mask a real HLS URL within ProbeTtl.
    private static readonly TimeSpan NegativeProbeTtl = TimeSpan.FromSeconds(30);

    // Signed CDN URLs often rotate query tokens per request — key on scheme+host+path
    // so the 5-min TTL actually holds across plays of the same manifest.
    private static string ProbeKey(string url)
    {
        try { var u = new Uri(url); return u.GetLeftPart(UriPartial.Path); }
        catch { return url; }
    }

    public bool CanHandle(Uri uri)
    {
        // Fast path: explicit .m3u8 extension — skip the probe entirely.
        return uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Content-based detection for URLs that don't look like HLS by shape.
    /// True for HLS manifests AND raw progressive MPEG-TS streams — both are routed
    /// through this handler so the streaming pass-through and cache-on-play machinery
    /// can reuse a single code path. Caches the probe result so GetVideoInfo can reuse
    /// it without a second fetch.
    /// </summary>
    public static async Task<bool> LooksLikeStreamable(string url)
    {
        var probe = await ProbeCached(url);
        return probe.IsHls || probe.IsTransportStream;
    }

    // Skip the content probe for URLs that are obviously not HLS. Avoids a blocking
    // GET on every generic video URL just to confirm what the extension already tells us.
    private static readonly string[] NonHlsExtensions =
    [
        ".mp4", ".webm", ".mov", ".mkv", ".avi", ".flv", ".wmv",
        ".mp3", ".ogg", ".wav", ".m4a", ".aac",
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    ];

    public static bool LooksObviouslyNotHls(Uri uri)
    {
        var path = uri.AbsolutePath;
        return NonHlsExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the in-memory probe result if still cached. Used by cache-gating to check
    /// whether a manifest has #EXT-X-ENDLIST (or, for raw TS, has a finite Content-Length)
    /// without re-probing.
    /// </summary>
    public static (double? Duration, bool IsComplete, bool IsTransportStream, long? ContentLength)? TryGetCachedProbe(string url)
    {
        if (ProbeCache.TryGetValue(ProbeKey(url), out var entry) && DateTime.UtcNow - entry.At < EffectiveTtl(entry.Result))
            return (entry.Result.Duration, entry.Result.IsComplete, entry.Result.IsTransportStream, entry.Result.ContentLength);
        return null;
    }

    private static TimeSpan EffectiveTtl(ProbeResult result) =>
        (result.IsHls || result.IsTransportStream) ? ProbeTtl : NegativeProbeTtl;

    public async Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        // Hash the path-only form so signed CDN URLs (rotating query tokens per play)
        // resolve to a stable videoId — otherwise the cached MP4 is never re-served.
        var videoId = VideoId.HashUrl(ProbeKey(url));
        var videoInfo = new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Hls,
            DownloadFormat = DownloadFormat.MP4
        };

        try
        {
            var probe = await ProbeCached(url);
            // Prefer a manifest-declared title; otherwise derive a readable name from the URL path.
            var title = probe.Title ?? DeriveTitleFromUrl(uri);
            if (probe.Duration is > 0 || !string.IsNullOrEmpty(title))
            {
                DatabaseManager.AddVideoInfoCache(new VideoInfoCache
                {
                    Id = videoId,
                    Title = title,
                    Duration = probe.Duration,
                    Type = UrlType.Hls
                });
            }
            if (probe.IsTransportStream)
                Log.Information("MPEG-TS {URL}: content-length={Bytes}", url, probe.ContentLength);
            else if (probe.Duration is > 0)
                Log.Information("HLS {URL}: duration={Duration:F1}s, complete={Complete}",
                    url, probe.Duration, probe.IsComplete);
            else
                Log.Warning("HLS {URL}: could not parse duration (live or malformed manifest)", url);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to probe HLS manifest for {URL}: {Error}", url, ex.Message);
        }

        return videoInfo;
    }

    /// <summary>
    /// Last non-empty path segment with the .m3u8 extension stripped and percent-encoding
    /// decoded. CDN URLs typically embed a slug here ("neko-dance-sample.m3u8") which is
    /// the only readable label available without manifest cooperation.
    /// </summary>
    private static string? DeriveTitleFromUrl(Uri uri)
    {
        try
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                var seg = Uri.UnescapeDataString(segments[i]).Trim();
                if (string.IsNullOrEmpty(seg)) continue;
                if (seg.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                    seg = seg[..^5];
                if (!string.IsNullOrEmpty(seg))
                    return seg;
            }
        }
        catch { /* malformed URI — fall through */ }
        return null;
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

    // Share-URL rewriting is opt-in: this rewriter sees *every* URL (content detection
    // happens later), so the Dropbox/GDrive rewrites would change behavior for plain MP4
    // shares too. Gating on CacheHlsPlaylists keeps the default install unaffected.
    public Task<string> RewriteUrl(string url, Uri uri) =>
        Task.FromResult(ConfigManager.Config.CacheHlsPlaylists ? RewriteShareUrl(url, uri) : url);

    /// <summary>
    /// Rewrites common cloud-host share URLs to their direct-download form so the probe
    /// and yt-dlp see the actual file body instead of an HTML preview page. Applies to
    /// the request URL only — segment URLs inside an HLS manifest aren't touched.
    /// </summary>
    public static string RewriteShareUrl(string url, Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();

        // Dropbox: dl=0 (preview page) → dl=1 (direct download). Also covers
        // www.dropbox.com/s/... and dropbox.com/scl/fi/... share URLs.
        if (host == "dropbox.com" || host.EndsWith(".dropbox.com"))
        {
            var q = uri.Query;
            if (q.Contains("dl=0"))
                return url.Replace("dl=0", "dl=1");
            if (string.IsNullOrEmpty(q))
                return url + "?dl=1";
            if (!q.Contains("dl=1") && !q.Contains("raw=1"))
                return url + "&dl=1";
        }

        // Google Drive: /file/d/<id>/view → /uc?export=download&id=<id>
        if (host == "drive.google.com")
        {
            var m = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/file/d/([^/]+)");
            if (m.Success)
                return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}";
        }

        return url;
    }

    private static async Task<ProbeResult> ProbeCached(string url)
    {
        var key = ProbeKey(url);
        if (ProbeCache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.At < EffectiveTtl(entry.Result))
            return entry.Result;

        // Dedupe in-flight probes so concurrent plays of the same URL share a single GET.
        var lazy = InFlightProbes.GetOrAdd(key, _ => new Lazy<Task<ProbeResult>>(() => Probe(url, depth: 0)));
        try
        {
            var result = await lazy.Value;
            ProbeCache[key] = (result, DateTime.UtcNow);

            // Opportunistic prune — keeps the cache bounded without a background task.
            // Force a sweep when over the cap even if nothing has expired, so unique-URL
            // floods can't grow the dictionary unboundedly.
            if (ProbeCache.Count > 256)
            {
                var now = DateTime.UtcNow;
                foreach (var kv in ProbeCache)
                    if (now - kv.Value.At >= EffectiveTtl(kv.Value.Result))
                        ProbeCache.TryRemove(kv.Key, out _);
                if (ProbeCache.Count > 256)
                {
                    foreach (var kv in ProbeCache.OrderBy(kv => kv.Value.At).Take(ProbeCache.Count - 256))
                        ProbeCache.TryRemove(kv.Key, out _);
                }
            }

            return result;
        }
        finally
        {
            InFlightProbes.TryRemove(key, out _);
        }
    }

    private const int ProbeMaxDepth = 3;
    private const int ProbeBodyCap = 64 * 1024;

    private static async Task<ProbeResult> Probe(string url, int depth)
    {
        if (depth >= ProbeMaxDepth)
        {
            Log.Debug("HLS probe depth limit reached for {URL}", url);
            return new ProbeResult(false, null, false, null);
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode)
                return new ProbeResult(false, null, false, null);

            var ct = res.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
            var contentLength = res.Content.Headers.ContentLength;
            var contentTypeMatch = HlsContentTypes.Any(t => ct.Contains(t, StringComparison.Ordinal));

            // MPEG-TS progressive stream — a finished video served as raw transport-stream
            // (video/mp2t, video/MP2T, video/mpegts). Cacheable as a single binary download;
            // skip the body read since there's no manifest to parse.
            if (!contentTypeMatch && (ct.Contains("mp2t", StringComparison.Ordinal) || ct.Contains("mpegts", StringComparison.Ordinal)))
                return new ProbeResult(false, null, false, null, IsTransportStream: true, ContentLength: contentLength);

            // Short-circuit on media/image content types that can't be an HLS manifest —
            // avoids pulling 64 KiB of binary just to fail the #EXTM3U check. Excludes
            // application/octet-stream and missing types so we still magic-byte sniff
            // those (some CDNs serve TS without a proper Content-Type).
            if (!contentTypeMatch && ct.Length > 0 &&
                (ct.StartsWith("video/") || ct.StartsWith("image/") || ct.StartsWith("audio/")) &&
                !ct.Contains("mpegurl"))
                return new ProbeResult(false, null, false, null);

            // Read at most 64 KiB — cheap for a non-HLS false positive and enough for
            // most manifests. Longer media playlists are handled via the truncation flag.
            await using var stream = await res.Content.ReadAsStreamAsync();
            var (bytes, truncated) = await ReadBoundedBytesAsync(stream, ProbeBodyCap);

            // MPEG-TS magic: 0x47 sync byte every 188 bytes. Check the first three packets
            // so we don't false-positive on a random file that happens to start with 'G'.
            if (!contentTypeMatch && bytes.Length >= 377 &&
                bytes[0] == 0x47 && bytes[188] == 0x47 && bytes[376] == 0x47)
                return new ProbeResult(false, null, false, null, IsTransportStream: true, ContentLength: contentLength);

            var body = System.Text.Encoding.UTF8.GetString(bytes);
            // Strip UTF-8 BOM if present so the #EXTM3U prefix check still matches.
            if (body.Length > 0 && body[0] == '﻿') body = body[1..];

            var bodyMatch = body.StartsWith("#EXTM3U", StringComparison.Ordinal);
            if (!contentTypeMatch && !bodyMatch)
            {
                // Common cloud-host failure: share URL returns an HTML preview wrapper
                // instead of the playlist body. Surface that clearly so users know to
                // use the direct-download form (Dropbox dl=1, GDrive uc?export=download).
                if (ct.Contains("html") || body.AsSpan().TrimStart().StartsWith("<"))
                    Log.Information("HLS probe for {URL} returned an HTML page — likely a share-page wrapper. Use the direct-download URL.", url);
                return new ProbeResult(false, null, false, null);
            }

            var sessionTitle = ParseSessionTitle(body);

            // Master playlist: fetch the first variant and recurse for duration.
            if (body.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
            {
                var variant = ResolveFirstVariant(body, url);
                if (variant != null)
                {
                    var inner = await Probe(variant, depth + 1);
                    return new ProbeResult(true, inner.Duration, inner.IsComplete, sessionTitle ?? inner.Title);
                }
                return new ProbeResult(true, null, false, sessionTitle);
            }

            var (duration, complete) = ParseMediaPlaylist(body);
            if (truncated && !complete)
            {
                // Manifest body was clipped at the 64 KiB cap before we saw #EXT-X-ENDLIST,
                // so the parsed duration is a lower bound. Don't trust it for cache-gating.
                Log.Debug("HLS manifest {URL} exceeded probe cap — treating duration as unknown.", url);
                return new ProbeResult(true, null, false, sessionTitle);
            }
            return new ProbeResult(true, duration, complete, sessionTitle);
        }
        catch (Exception ex)
        {
            Log.Debug("HLS probe failed for {URL}: {Error}", url, ex.Message);
            return new ProbeResult(false, null, false, null);
        }
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadBoundedBytesAsync(Stream stream, int maxBytes)
    {
        var buffer = new byte[Math.Min(8192, maxBytes)];
        using var ms = new MemoryStream();
        int read;
        while (ms.Length < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - ms.Length)))) > 0)
            ms.Write(buffer, 0, read);

        // Peek one more byte to distinguish "exactly maxBytes" from "truncated".
        var truncated = false;
        if (ms.Length >= maxBytes)
        {
            var probe = new byte[1];
            try { truncated = await stream.ReadAsync(probe.AsMemory(0, 1)) > 0; }
            catch { /* stream may be closed — assume not truncated */ }
        }
        return (ms.ToArray(), truncated);
    }

    private static (double? Duration, bool IsComplete) ParseMediaPlaylist(string body)
    {
        double total = 0;
        var any = false;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                continue;
            const int colon = 8; // length of "#EXTINF:"
            var commaIdx = line.IndexOf(',', colon);
            var durationStr = commaIdx > colon ? line.Substring(colon, commaIdx - colon) : line[colon..];
            if (double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0)
            {
                total += d;
                any = true;
            }
        }
        return (any ? total : null, body.Contains("#EXT-X-ENDLIST", StringComparison.Ordinal));
    }

    /// <summary>
    /// Parses an optional title out of #EXT-X-SESSION-DATA tags carrying
    /// DATA-ID="com.apple.hls.title" (rare but standard per RFC 8216 §4.3.4.4).
    /// </summary>
    private static string? ParseSessionTitle(string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("#EXT-X-SESSION-DATA:", StringComparison.Ordinal)) continue;
            if (!line.Contains("com.apple.hls.title", StringComparison.OrdinalIgnoreCase)) continue;
            var valueIdx = line.IndexOf("VALUE=\"", StringComparison.Ordinal);
            if (valueIdx < 0) continue;
            valueIdx += "VALUE=\"".Length;
            var endIdx = line.IndexOf('"', valueIdx);
            if (endIdx <= valueIdx) continue;
            var title = line[valueIdx..endIdx].Trim();
            if (!string.IsNullOrEmpty(title)) return title;
        }
        return null;
    }

    private static string? ResolveFirstVariant(string body, string manifestUrl)
    {
        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal))
                continue;
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j].Trim();
                if (string.IsNullOrEmpty(next) || next.StartsWith('#'))
                    continue;
                return new Uri(new Uri(manifestUrl), next).ToString();
            }
        }
        return null;
    }
}
