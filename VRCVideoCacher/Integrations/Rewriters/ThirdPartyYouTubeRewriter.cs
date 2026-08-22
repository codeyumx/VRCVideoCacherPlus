using Serilog;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Integrations.Rewriters;

/// <summary>
/// Follows redirect chains from third-party YouTube front-ends (dmn.moe, u2b.cx and
/// friends) until the destination is something an integration recognises.
///
/// This is the case a Rewrite rule cannot express: where the real URL is only discoverable
/// by asking the server, not by pattern-matching the one we were given.
/// </summary>
public class ThirdPartyYouTubeRewriter : UrlRewriter
{
    private static readonly ILogger Log = Program.Logger.ForContext<ThirdPartyYouTubeRewriter>();

    private const int MaxHops = 5;

    // AllowAutoRedirect=false so each hop can be inspected and the walk stopped as soon as
    // the destination is a URL an integration recognises.
    private static readonly HttpClient NoAutoRedirectClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Walks redirect chains for URLs chosen by whoever is in the instance.
        ConnectCallback = UrlPolicy.GuardedConnectAsync,
    })
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(20)
    };

    public override async Task<string> RewriteAsync(string url, Uri uri)
    {
        if (IntegrationRegistry.HasSpecificIntegration(uri))
            return url;

        var current = url;

        try
        {
            for (var hop = 0; hop < MaxHops; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, current);
                using var response = await NoAutoRedirectClient.SendAsync(request);

                var location = response.Headers.Location;
                if (location == null)
                    break;

                // Resolve relative redirects against the current URL
                var next = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(current), location).ToString();

                if (!Uri.TryCreate(next, UriKind.Absolute, out var nextUri))
                    break;

                // A Location header can name any scheme it likes; only http(s) is ever a
                // video we could go on to fetch.
                if (!UrlPolicy.IsFetchableWebUrl(nextUri))
                {
                    Log.Warning("Stopping redirect chain at non-web URL {Url}", next);
                    break;
                }

                // Stop as soon as the redirect target is something we handle specifically.
                if (IntegrationRegistry.HasSpecificIntegration(nextUri))
                {
                    Log.Information("Resolved redirect: {URL} -> {Resolved}", url, next);
                    return next;
                }

                current = next;

                var status = (int)response.StatusCode;
                if (status is < 300 or >= 400)
                    break;
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Failed to follow redirects for {URL}, returning original", url);
            return url;
        }

        if (current != url)
        {
            Log.Information("Resolved redirect: {URL} -> {Resolved}", url, current);
            if (Uri.TryCreate(current, UriKind.Absolute, out var finalUri) && !IntegrationRegistry.HasSpecificIntegration(finalUri))
                Log.Warning("Resolved URL has no specific integration, will use generic: {URL}", current);
        }

        return current;
    }
}
