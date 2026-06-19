using System.Runtime.InteropServices;

namespace VRCVideoCacher;

public class VideoTools
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<VideoTools>();

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern uint FlushDnsCache();
    private static readonly object HttpClientLock = new();
    private static HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        // Recycle connections periodically so stale DNS entries don't stick forever.
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>
    /// Disposes the current HttpClient and creates a fresh one, flushing
    /// all pooled connections and any associated cached DNS entries.
    /// </summary>
    private static void ResetHttpClient()
    {
        lock (HttpClientLock)
        {
            var old = _httpClient;
            _httpClient = CreateHttpClient();
            old.Dispose();
        }
    }

    public static async Task<bool> Prefetch(string videoUrl, int maxRetryCount = 7)
    {
        // If the URL is invalid, skip prefetching
        if (string.IsNullOrWhiteSpace(videoUrl) || !Uri.IsWellFormedUriString(videoUrl, UriKind.RelativeOrAbsolute))
        {
            Log.Warning("Invalid video URL provided for prefetch: {URL}", videoUrl);
            return false;
        }

        try
        {
            // Determine if the URL is an M3U8 playlist
            var uri = new Uri(videoUrl);
            var isM3U8 = uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || videoUrl.Contains("mime=application/vnd.apple.mpegurl");

            // Prefetch the video URL
            // - Use GET for M3U8 to extract the direct stream URL
            string? firstM3U8Url = null;
            using var prefetchRequest = new HttpRequestMessage(HttpMethod.Get, videoUrl);
            // ponytail: googlevideo CDN resets HEAD requests; ranged GET warms the cache without downloading the file
            if (!isM3U8)
                prefetchRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var prefetchResponse = await _httpClient.SendAsync(prefetchRequest);
            Log.Information("Video prefetch request returned status code {status}.", (int)prefetchResponse.StatusCode);

            if (prefetchRequest.Method == HttpMethod.Get && prefetchResponse.Content.Headers.ContentType?.MediaType == "application/vnd.apple.mpegurl")
            {
                var body = await prefetchResponse.Content.ReadAsStringAsync();
                firstM3U8Url = body.Split('\n').FirstOrDefault(line => Uri.IsWellFormedUriString(line, UriKind.RelativeOrAbsolute));
            }

            if (firstM3U8Url == null)
                return true;

            // If we have an M3U8 URL, perform HEAD requests to validate accessibility
            var statusCode = 0;
            const int wait = 1500;
            for (var i = 0; i < maxRetryCount; i++)
            {
                try
                {
                    using var m3u8Request = new HttpRequestMessage(HttpMethod.Get, firstM3U8Url);
                    m3u8Request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    using var m3u8Response = await _httpClient.SendAsync(m3u8Request);
                    statusCode = (int)m3u8Response.StatusCode;

                    if (statusCode >= 400)
                    {
                        Log.Warning(
                            "Prefetching M3U8 stream returned status code {status}, retrying... ({attempt}/{limit})",
                            statusCode, i + 1, maxRetryCount);
                        await Task.Delay(wait);
                    }
                    else
                    {
                        Log.Information("Prefetching M3U8 stream returned status code {status}, proceeding.", statusCode);
                        break;
                    }
                }
                catch (HttpRequestException ex)
                {
                    Log.Warning(ex, "Prefetching M3U8 stream failed with exception, retrying... ({attempt}/{limit})",
                        i + 1, maxRetryCount);
                    await Task.Delay(wait);
                }
            }

            if (statusCode != 200 && statusCode != 206)
            {
                Log.Error(
                    "Prefetching M3U8 stream failed after {limit} attempts, status code {status}. Video may not play.",
                    maxRetryCount, statusCode);
                return false;
            }
            else
            {
                return true;
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Prefetch failed due to a connection error for URL: {URL}. Resetting HTTP client.", videoUrl);
            ResetHttpClient();
            FlagDnsFailure();
            return false;
        }
        catch (TaskCanceledException ex)
        {
            Log.Error(ex, "Prefetch timed out for URL: {URL}", videoUrl);
            return false;
        }
    }

    private static readonly string DnsFailureFlagPath = Path.Join(Program.DataPath, "dns_failure");

    private static void FlagDnsFailure()
    {
        try
        {
            if (!File.Exists(DnsFailureFlagPath))
                File.WriteAllText(DnsFailureFlagPath, DateTime.UtcNow.ToString("O"));
        }
        catch { /* best effort */ }
    }

    /// <summary>Returns true if a previous run recorded a DNS connection failure.</summary>
    public static bool HasDnsFailureFlag() => File.Exists(DnsFailureFlagPath);

    /// <summary>Clears the DNS failure flag.</summary>
    public static void ClearDnsFailureFlag()
    {
        try { File.Delete(DnsFailureFlagPath); } catch { /* best effort */ }
    }

    /// <summary>Flushes the Windows OS DNS resolver cache.</summary>
    public static void FlushSystemDnsCache()
    {
        Log.Information("Flushing OS DNS cache to clear stale entries.");
        try { FlushDnsCache(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to flush DNS cache."); }
    }
}