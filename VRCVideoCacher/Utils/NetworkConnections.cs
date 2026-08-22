using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Serilog;
using Vanara.PInvoke;
using static Vanara.PInvoke.IpHlpApi;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Read-only enumeration of the TCP connections owned by VRChat.
///
/// Deliberately separate from <see cref="ConnectionSevering"/>: listing is safe, needs no
/// privileges and drives a UI that refreshes on a timer, whereas severing is privileged and
/// destructive. Keeping them together made it easy to assume that seeing a connection meant
/// being able to close it, which is not true on either platform.
/// </summary>
public static class NetworkConnections
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(NetworkConnections));

    // Ports worth showing: our own server, and plain HTTP/HTTPS to a CDN.
    private static readonly int[] InterestingPorts = [9696, 80, 443];

    // Enumerating every process is expensive and the answer barely changes, so it is cached
    // briefly. This runs on a repeating refresh, and Process.GetProcesses() is by far the
    // most costly part of a tick.
    private static readonly TimeSpan PidCacheLifetime = TimeSpan.FromSeconds(15);
    private static readonly object PidCacheLock = new();
    private static Dictionary<int, string> _cachedPids = [];
    private static DateTime _pidsCachedAt = DateTime.MinValue;

    /// <summary>
    /// VRChat's PIDs mapped to process names. Cached for a few seconds.
    /// </summary>
    public static Dictionary<int, string> GetVrChatProcesses()
    {
        lock (PidCacheLock)
        {
            if (DateTime.UtcNow - _pidsCachedAt < PidCacheLifetime)
                return _cachedPids;
        }

        var found = new Dictionary<int, string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                // Match VRChat itself, not this application — "VRCVideoCacher" must never
                // appear here or we would list and sever our own connections.
                if (name.Equals("VRChat", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("VRChat.", StringComparison.OrdinalIgnoreCase))
                {
                    found[process.Id] = name;
                }
            }
            catch
            {
                // Process exited between enumeration and inspection, or is inaccessible.
            }
            finally
            {
                process.Dispose();
            }
        }

        lock (PidCacheLock)
        {
            _cachedPids = found;
            _pidsCachedAt = DateTime.UtcNow;
        }

        return found;
    }

    /// <summary>
    /// Lists VRChat's current TCP connections on ports we care about. Safe to call from a
    /// background thread; never touches the UI.
    /// </summary>
    public static List<ActiveConnectionInfo> List()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return ListWindows();

            if (OperatingSystem.IsLinux())
                return ListLinux();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to list active connections.");
        }

        return [];
    }

    private static void Annotate(ActiveConnectionInfo info)
    {
        if (YTDL.ActiveStreamTracker.TryGetUrlInfo(info.RemoteAddress, out var urlInfo))
        {
            info.AssociatedUrl = urlInfo.OriginalUrl;
            info.AssociatedTitle = urlInfo.Title;
            return;
        }

        var match = YTDL.ActiveStreamTracker.GetActiveSessions()
            .FirstOrDefault(s => s.RemoteIp == info.RemoteAddress);
        if (match == null)
            return;

        info.AssociatedUrl = match.OriginalUrl;
        info.AssociatedTitle = match.Title;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<ActiveConnectionInfo> ListWindows()
    {
        var list = new List<ActiveConnectionInfo>();
        var processes = GetVrChatProcesses();
        if (processes.Count == 0)
            return list;

        foreach (var row in ReadWindowsTcpTable())
        {
            if (!processes.TryGetValue((int)row.dwOwningPid, out var processName))
                continue;

            var localPort = NetworkPort(row.dwLocalPort);
            var remotePort = NetworkPort(row.dwRemotePort);
            if (!InterestingPorts.Contains(localPort) && !InterestingPorts.Contains(remotePort))
                continue;

            var info = new ActiveConnectionInfo
            {
                LocalAddress = new IPAddress((long)row.dwLocalAddr).ToString(),
                LocalPort = localPort,
                RemoteAddress = new IPAddress((long)row.dwRemoteAddr).ToString(),
                RemotePort = remotePort,
                OwningPid = (int)row.dwOwningPid,
                ProcessName = processName
            };

            Annotate(info);
            list.Add(info);
        }

        return list;
    }

    /// <summary>
    /// Reads the IPv4 TCP table with owning PIDs.
    ///
    /// Note this is IPv4 only: there is no IPv6 equivalent wired up, so an IPv6 stream is
    /// invisible here and cannot be severed. CDNs increasingly serve over IPv6, which is a
    /// real gap rather than a theoretical one.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<MIB_TCPROW_OWNER_PID> ReadWindowsTcpTable()
    {
        const int afInet = 2;

        uint bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, afInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
        if (bufferSize == 0)
            yield break;

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferSize, false, afInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
            if (result != 0)
            {
                Log.Debug("GetExtendedTcpTable returned {Result}", result);
                yield break;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + sizeof(int);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < count; i++)
            {
                yield return Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Windows reports ports in network byte order packed into a DWORD.</summary>
    private static int NetworkPort(uint value) => (ushort)IPAddress.NetworkToHostOrder((short)(ushort)value);

    private static List<ActiveConnectionInfo> ListLinux()
    {
        var list = new List<ActiveConnectionInfo>();
        var processes = GetVrChatProcesses();
        if (processes.Count == 0)
            return list;

        var socketInodes = ReadSocketInodes(processes.Keys);
        if (socketInodes.Count == 0)
            return list;

        // Both families, so an IPv6 stream is at least visible even though severing it is
        // still not supported.
        foreach (var path in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            foreach (var entry in ReadProcNetTcp(path))
            {
                if (!socketInodes.TryGetValue(entry.Inode, out var pid))
                    continue;

                if (!InterestingPorts.Contains(entry.LocalPort) && !InterestingPorts.Contains(entry.RemotePort))
                    continue;

                var info = new ActiveConnectionInfo
                {
                    LocalAddress = entry.LocalAddress,
                    LocalPort = entry.LocalPort,
                    RemoteAddress = entry.RemoteAddress,
                    RemotePort = entry.RemotePort,
                    OwningPid = pid,
                    ProcessName = processes.TryGetValue(pid, out var name) ? name : "VRChat"
                };

                Annotate(info);
                list.Add(info);
            }
        }

        return list;
    }

    private static Dictionary<string, int> ReadSocketInodes(IEnumerable<int> pids)
    {
        var inodes = new Dictionary<string, int>();

        foreach (var pid in pids)
        {
            var fdDir = $"/proc/{pid}/fd";
            if (!Directory.Exists(fdDir))
                continue;

            try
            {
                foreach (var fd in Directory.EnumerateFiles(fdDir))
                {
                    try
                    {
                        var target = File.ResolveLinkTarget(fd, true)?.FullName;
                        if (target == null || !target.StartsWith("socket:[", StringComparison.Ordinal) || !target.EndsWith(']'))
                            continue;

                        inodes[target[8..^1]] = pid;
                    }
                    catch
                    {
                        // Descriptor closed mid-enumeration; skip it.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not enumerate {FdDir}: {Error}", fdDir, ex.Message);
            }
        }

        return inodes;
    }

    private readonly record struct ProcNetEntry(
        string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, string Inode);

    private static IEnumerable<ProcNetEntry> ReadProcNetTcp(string path)
    {
        if (!File.Exists(path))
            yield break;

        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path);
        }
        catch (Exception ex)
        {
            Log.Debug("Could not read {Path}: {Error}", path, ex.Message);
            yield break;
        }

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 10)
                continue;

            if (!TryParseEndpoint(parts[1], out var localAddress, out var localPort) ||
                !TryParseEndpoint(parts[2], out var remoteAddress, out var remotePort))
                continue;

            yield return new ProcNetEntry(localAddress, localPort, remoteAddress, remotePort, parts[9]);
        }
    }

    /// <summary>
    /// Parses a procfs "ADDRESS:PORT" field, where the address is little-endian hex — 8 hex
    /// digits for IPv4, 32 for IPv6.
    ///
    /// Returns false rather than guessing. The previous implementation returned "127.0.0.1"
    /// for anything it could not parse, which is not an error value: it is a real address
    /// that would then be matched against and severed.
    /// </summary>
    private static bool TryParseEndpoint(string field, out string address, out int port)
    {
        address = string.Empty;
        port = 0;

        var separator = field.LastIndexOf(':');
        if (separator <= 0)
            return false;

        var hexAddress = field[..separator];
        if (!int.TryParse(field[(separator + 1)..], System.Globalization.NumberStyles.HexNumber, null, out port))
            return false;

        return hexAddress.Length switch
        {
            8 => TryParseIpV4(hexAddress, out address),
            32 => TryParseIpV6(hexAddress, out address),
            _ => false
        };
    }

    private static bool TryParseIpV4(string hex, out string address)
    {
        address = string.Empty;
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return false;

        // Stored host-endian on a little-endian machine, i.e. already network order bytes.
        address = new IPAddress(BitConverter.GetBytes(value)).ToString();
        return true;
    }

    private static bool TryParseIpV6(string hex, out string address)
    {
        address = string.Empty;
        var bytes = new byte[16];

        // Four little-endian 32-bit words.
        for (var word = 0; word < 4; word++)
        {
            if (!uint.TryParse(hex.Substring(word * 8, 8), System.Globalization.NumberStyles.HexNumber, null, out var value))
                return false;

            BitConverter.GetBytes(value).CopyTo(bytes, word * 4);
        }

        address = new IPAddress(bytes).ToString();
        return true;
    }
}

public class ActiveConnectionInfo
{
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public int OwningPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string AssociatedUrl { get; set; } = string.Empty;
    public string AssociatedTitle { get; set; } = string.Empty;

    /// <summary>Identity for reconciling a refreshed list against what the UI already shows.</summary>
    public string Key => $"{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}#{OwningPid}";
}
