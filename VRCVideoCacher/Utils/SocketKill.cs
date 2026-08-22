using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Serilog;

namespace VRCVideoCacher.Utils;

public static class SocketKill
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(SocketKill));

    // Win32 structures & P/Invokes
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort; // in network byte order
        public uint remoteAddr;
        public uint remotePort;
        public int owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int SetTcpEntry(ref MIB_TCPROW pTcpRow);

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int MIB_TCP_STATE_DELETE_TCB = 12;

    public static void SeverActiveVideoConnections()
    {
        try
        {
            Log.Information("Attempting to sever active video player connections...");
            if (OperatingSystem.IsWindows())
            {
                SeverConnectionsWindows();
            }
            else if (OperatingSystem.IsLinux())
            {
                SeverConnectionsLinux();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sever active connections.");
        }
    }

    private static void SeverConnectionsWindows()
    {
        var targetPids = GetTargetPids();
        if (targetPids.Count == 0)
        {
            Log.Debug("No active VRChat/Unity processes found on Windows.");
            return;
        }

        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
        if (bufferSize == 0) return;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            if (result != 0)
            {
                Log.Warning("GetExtendedTcpTable returned error: {Result}", result);
                return;
            }

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = tcpTablePtr + sizeof(int);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            int killedCount = 0;
            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;

                if (!targetPids.Contains(row.owningPid))
                    continue;

                ushort localPort = (ushort)(((row.localPort & 0xFF00) >> 8) | ((row.localPort & 0x00FF) << 8));
                ushort remotePort = (ushort)(((row.remotePort & 0xFF00) >> 8) | ((row.remotePort & 0x00FF) << 8));

                // Sever connections where remote port is 80 (HTTP), 443 (HTTPS), or either port is 9696 (local VRCVideoCacher)
                if (localPort == 9696 || remotePort == 9696 || remotePort == 80 || remotePort == 443)
                {
                    var rowToKill = new MIB_TCPROW
                    {
                        dwState = MIB_TCP_STATE_DELETE_TCB,
                        dwLocalAddr = row.localAddr,
                        dwLocalPort = row.localPort,
                        dwRemoteAddr = row.remoteAddr,
                        dwRemotePort = row.remotePort
                    };

                    int setRes = SetTcpEntry(ref rowToKill);
                    if (setRes == 0)
                        killedCount++;
                    else
                        Log.Debug("SetTcpEntry failed to close connection owned by PID {Pid}: {Error}", row.owningPid, setRes);
                }
            }

            if (killedCount > 0)
                Log.Information("Severed {Count} active video connections on Windows.", killedCount);
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }
    }

    private static void SeverConnectionsLinux()
    {
        var targetPids = GetTargetPids();
        if (targetPids.Count == 0)
        {
            Log.Debug("No active VRChat/Unity processes found on Linux.");
            return;
        }

        // Find inodes from /proc/<pid>/fd/
        var inodes = new HashSet<string>();
        foreach (var pid in targetPids)
        {
            var fdDir = $"/proc/{pid}/fd";
            if (!Directory.Exists(fdDir))
                continue;

            try
            {
                foreach (var fdFile in Directory.GetFiles(fdDir))
                {
                    try
                    {
                        var target = File.ResolveLinkTarget(fdFile, true);
                        if (target == null) continue;

                        var name = target.FullName;
                        if (name.StartsWith("socket:[") && name.EndsWith("]"))
                        {
                            var inode = name.Substring(8, name.Length - 9);
                            inodes.Add(inode);
                        }
                    }
                    catch { /* ignore individual file access errors */ }
                }
            }
            catch { /* ignore folder access errors */ }
        }

        if (inodes.Count == 0)
        {
            Log.Debug("No socket inodes found for target processes.");
            return;
        }

        // Parse /proc/net/tcp to match inodes to connections
        var connectionsToKill = new List<(string LocalIp, int LocalPort, string RemoteIp, int RemotePort)>();
        const string tcpPath = "/proc/net/tcp";
        if (File.Exists(tcpPath))
        {
            try
            {
                var lines = File.ReadLines(tcpPath);
                foreach (var line in lines)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 10) continue;

                    var inode = parts[9];
                    if (!inodes.Contains(inode)) continue;

                    // Parse local/remote addresses
                    var localParts = parts[1].Split(':');
                    var remoteParts = parts[2].Split(':');
                    if (localParts.Length != 2 || remoteParts.Length != 2) continue;

                    var localIp = ParseHexIp(localParts[0]);
                    var localPort = int.Parse(localParts[1], System.Globalization.NumberStyles.HexNumber);
                    var remoteIp = ParseHexIp(remoteParts[0]);
                    var remotePort = int.Parse(remoteParts[1], System.Globalization.NumberStyles.HexNumber);

                    if (localPort == 9696 || remotePort == 9696 || remotePort == 80 || remotePort == 443)
                    {
                        connectionsToKill.Add((localIp, localPort, remoteIp, remotePort));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read /proc/net/tcp.");
            }
        }

        if (connectionsToKill.Count == 0)
        {
            Log.Debug("No active target video connections found in /proc/net/tcp.");
            return;
        }

        int killedCount = 0;
        foreach (var conn in connectionsToKill)
        {
            try
            {
                // Run sudo -n ss -t -K dst <remote> dport = :<port> src <local> sport = :<port>
                var filter = $"dst {conn.RemoteIp} dport = :{conn.RemotePort} src {conn.LocalIp} sport = :{conn.LocalPort}";
                var psi = new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"-n ss -t -K {filter}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                        killedCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to execute ss -K for connection {Local} -> {Remote}", conn.LocalPort, conn.RemotePort);
            }
        }

        if (killedCount > 0)
            Log.Information("Severed {Count} active video connections on Linux.", killedCount);
    }

    private static string ParseHexIp(string hex)
    {
        if (hex.Length != 8) return "127.0.0.1";
        var b3 = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        var b2 = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        var b1 = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        var b0 = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
        return $"{b0}.{b1}.{b2}.{b3}";
    }

    private static HashSet<int> GetTargetPids()
    {
        var pids = new HashSet<int>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName;
                if (name.Contains("VRChat", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("UnitySubsystems", StringComparison.OrdinalIgnoreCase))
                {
                    pids.Add(proc.Id);
                }
            }
            catch { /* ignore processes we can't access */ }
        }
        return pids;
    }
}
