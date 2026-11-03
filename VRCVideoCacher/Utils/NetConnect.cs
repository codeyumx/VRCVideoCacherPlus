using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Outbound connections with one socket per address family. .NET's default connect opens a
/// single dual-mode IPv6 socket and reaches IPv4 hosts through <c>::ffff:</c>-mapped
/// addresses. VPN split-tunnel drivers (Mullvad's, for an app excluded from the tunnel)
/// re-bind IPv6 sockets to the physical IPv6 address, after which every mapped IPv4 connect
/// fails with WSAEACCES (10013) while plain IPv4 sockets work. Matching the socket to each
/// address avoids that and still reaches IPv6 hosts.
/// </summary>
public static class NetConnect
{
    // For clients that need no other handler settings.
    public static SocketsHttpHandler CreateHandler() => new() { ConnectCallback = ConnectAsync };

    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        return await ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
    }

    // Tries the addresses in resolver order, as the default connect does; a refused or
    // unreachable address falls through to the next.
    public static async ValueTask<Stream> ConnectAsync(
        IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken)
    {
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                lastError = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}
