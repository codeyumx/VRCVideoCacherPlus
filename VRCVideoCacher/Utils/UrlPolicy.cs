using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Guards on the outbound requests this application makes on behalf of whoever asked for a
/// video. URLs arrive from a VRChat world — i.e. from anyone in the instance — and are then
/// probed, redirect-followed and downloaded, so they need some floor of validation.
///
/// Deliberately does NOT block private LAN ranges: hosting videos on a NAS or a local media
/// server is an ordinary thing to do and blocking it would break real setups. What is
/// blocked is link-local, which is where cloud instance-metadata services live
/// (169.254.169.254 and friends) and which no video is ever served from.
/// </summary>
public static class UrlPolicy
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(UrlPolicy));

    /// <summary>
    /// Only http(s) is meaningful for a video URL. Anything else — file://, ftp://, and the
    /// rest — is a way to make the application read something it was never meant to.
    /// </summary>
    public static bool IsFetchableWebUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    public static bool IsFetchableWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsFetchableWebUrl(uri);

    /// <summary>
    /// Link-local addresses, which is where cloud metadata endpoints live. Reaching one of
    /// those from a request an untrusted party chose is the classic SSRF payoff, and
    /// nothing legitimate serves video from 169.254/16 or fe80::/10.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address) =>
        address.IsIPv4LinkLocal() || address.IsIPv6LinkLocal;

    private static bool IsIPv4LinkLocal(this IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var octets = address.GetAddressBytes();
        return octets[0] == 169 && octets[1] == 254;
    }

    /// <summary>
    /// A SocketsHttpHandler ConnectCallback that refuses to open a connection to a blocked
    /// address. Enforcing at connect time rather than by inspecting the request URL means
    /// redirects and DNS results are covered too — a check on the original URL alone sees
    /// neither.
    /// </summary>
    public static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        var allowed = addresses.Where(address => !IsBlockedAddress(address)).ToArray();

        if (allowed.Length == 0)
        {
            Log.Warning("Refusing to connect to {Host}: resolves only to blocked addresses.", endPoint.Host);
            throw new HttpRequestException($"Refusing to connect to {endPoint.Host}: blocked address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
