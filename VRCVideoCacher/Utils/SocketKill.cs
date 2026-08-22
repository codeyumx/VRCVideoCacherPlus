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
            YTDL.ActiveStreamTracker.ClearActiveVideoIps();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sever active connections.");
        }
    }

    public static void SeverConnectionByIp(string ip)
    {
        try
        {
            Log.Information("Attempting to sever active video connection to IP: {Ip}...", ip);
            if (OperatingSystem.IsWindows())
            {
                SeverConnectionsWindows(ip);
            }
            else if (OperatingSystem.IsLinux())
            {
                SeverConnectionsLinux(ip);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sever connection for IP: {Ip}", ip);
        }
    }

    private static void SeverConnectionsWindows(string? filterIp = null)
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

            var activeIps = filterIp != null ? null : YTDL.ActiveStreamTracker.GetActiveVideoIps();
            int killedCount = 0;
            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;

                if (!targetPids.Contains(row.owningPid))
                    continue;

                ushort localPort = (ushort)(((row.localPort & 0xFF00) >> 8) | ((row.localPort & 0x00FF) << 8));
                ushort remotePort = (ushort)(((row.remotePort & 0xFF00) >> 8) | ((row.remotePort & 0x00FF) << 8));

                bool shouldKill = false;
                if (localPort == 9696 || remotePort == 9696)
                {
                    if (filterIp == null)
                        shouldKill = true;
                }
                else if (remotePort == 80 || remotePort == 443)
                {
                    var remoteIpBytes = BitConverter.GetBytes(row.remoteAddr);
                    var remoteIp = new IPAddress(remoteIpBytes).ToString();
                    if (filterIp != null)
                    {
                        if (remoteIp == filterIp)
                            shouldKill = true;
                    }
                    else if (activeIps != null && activeIps.Contains(remoteIp))
                    {
                        shouldKill = true;
                    }
                }

                if (shouldKill)
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

    private static void SeverConnectionsLinux(string? filterIp = null)
    {
        Log.Information("SeverConnectionsLinux called with filterIp: {FilterIp}", filterIp ?? "(none)");

        var targetIps = new List<string>();
        if (!string.IsNullOrEmpty(filterIp))
        {
            targetIps.Add(filterIp);
        }
        else
        {
            var activeIps = YTDL.ActiveStreamTracker.GetActiveVideoIps();
            targetIps.AddRange(activeIps);
            Log.Information("SeverConnectionsLinux: Found {Count} active video IPs from tracker: {Ips}",
                activeIps.Count, string.Join(", ", activeIps));
        }

        if (targetIps.Count == 0)
        {
            Log.Warning("SeverConnectionsLinux: No target IPs available to sever.");
            return;
        }

        int totalKilled = 0;
        foreach (var ip in targetIps)
        {
            if (string.IsNullOrWhiteSpace(ip)) continue;

            Log.Information("Attempting direct socket kill for target IP: {IP}", ip);
            if (TryKillSocketsForIp(ip))
            {
                totalKilled++;
            }
        }

        var pids = GetTargetPids();
        Log.Information("Target process PIDs for socket scan: {Pids}", string.Join(", ", pids));

        if (totalKilled > 0)
        {
            Log.Information("Successfully severed {Count} active video connection targets on Linux.", totalKilled);
        }
        else
        {
            Log.Warning("No connections were severed by ss -t -K for target IPs: {Ips}", string.Join(", ", targetIps));
        }
    }

    private static bool TryKillSocketsForIp(string ip)
    {
        try
        {
            var filter = $"dst {ip}";
            var psi = new ProcessStartInfo
            {
                FileName = "ss",
                Arguments = $"-t -K {filter}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                Log.Information("ss -t -K dst {IP} exited with code {Code}. Output: {Out} | Stderr: {Err}",
                    ip, proc.ExitCode, stdout.Trim(), stderr.Trim());

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    return true;
                }
            }

            var sudoPsi = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"-n ss -t -K {filter}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var sudoProc = Process.Start(sudoPsi);
            if (sudoProc != null)
            {
                var stdout = sudoProc.StandardOutput.ReadToEnd();
                var stderr = sudoProc.StandardError.ReadToEnd();
                sudoProc.WaitForExit();
                Log.Information("sudo -n ss -t -K dst {IP} exited with code {Code}. Output: {Out} | Stderr: {Err}",
                    ip, sudoProc.ExitCode, stdout.Trim(), stderr.Trim());

                if (sudoProc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed executing ss command for IP {IP}", ip);
        }

        return false;
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

    public static List<ActiveConnectionInfo> ListActiveConnections()
    {
        var list = new List<ActiveConnectionInfo>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                list.AddRange(ListConnectionsWindows());
            }
            else if (OperatingSystem.IsLinux())
            {
                list.AddRange(ListConnectionsLinux());
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to list active connections.");
        }
        return list;
    }

    private static List<ActiveConnectionInfo> ListConnectionsWindows()
    {
        var list = new List<ActiveConnectionInfo>();
        var targetPids = GetTargetPids();
        if (targetPids.Count == 0) return list;

        var pidNames = new Dictionary<int, string>();
        foreach (var pid in targetPids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                pidNames[pid] = p.ProcessName;
            }
            catch { pidNames[pid] = "VRChat"; }
        }

        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
        if (bufferSize == 0) return list;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            if (result != 0) return list;

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = tcpTablePtr + sizeof(int);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;

                if (!targetPids.Contains(row.owningPid))
                    continue;

                ushort localPort = (ushort)(((row.localPort & 0xFF00) >> 8) | ((row.localPort & 0x00FF) << 8));
                ushort remotePort = (ushort)(((row.remotePort & 0xFF00) >> 8) | ((row.remotePort & 0x00FF) << 8));

                if (localPort == 9696 || remotePort == 9696 || remotePort == 80 || remotePort == 443)
                {
                    var localIp = new IPAddress(BitConverter.GetBytes(row.localAddr)).ToString();
                    var remoteIp = new IPAddress(BitConverter.GetBytes(row.remoteAddr)).ToString();

                    var info = new ActiveConnectionInfo
                    {
                        LocalAddress = localIp,
                        LocalPort = localPort,
                        RemoteAddress = remoteIp,
                        RemotePort = remotePort,
                        OwningPid = row.owningPid,
                        ProcessName = pidNames.TryGetValue(row.owningPid, out var name) ? name : "VRChat",
                        AssociatedUrl = string.Empty,
                        AssociatedTitle = string.Empty
                    };

                    if (YTDL.ActiveStreamTracker.TryGetUrlInfo(remoteIp, out var urlInfo))
                    {
                        info.AssociatedUrl = urlInfo.OriginalUrl;
                        info.AssociatedTitle = urlInfo.Title;
                    }
                    else
                    {
                        var sessions = YTDL.ActiveStreamTracker.GetActiveSessions();
                        var match = sessions.FirstOrDefault(s => s.RemoteIp == remoteIp);
                        if (match != null)
                        {
                            info.AssociatedUrl = match.OriginalUrl;
                            info.AssociatedTitle = match.Title;
                        }
                    }

                    list.Add(info);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }
        return list;
    }

    private static List<ActiveConnectionInfo> ListConnectionsLinux()
    {
        var list = new List<ActiveConnectionInfo>();
        var targetPids = GetTargetPids();
        if (targetPids.Count == 0) return list;

        var pidNames = new Dictionary<int, string>();
        foreach (var pid in targetPids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                pidNames[pid] = p.ProcessName;
            }
            catch { pidNames[pid] = "VRChat"; }
        }

        var inodes = new Dictionary<string, int>();
        foreach (var pid in targetPids)
        {
            var fdDir = $"/proc/{pid}/fd";
            if (!Directory.Exists(fdDir)) continue;

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
                            inodes[inode] = pid;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        if (inodes.Count == 0) return list;

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
                    if (!inodes.TryGetValue(inode, out var pid)) continue;

                    var localParts = parts[1].Split(':');
                    var remoteParts = parts[2].Split(':');
                    if (localParts.Length != 2 || remoteParts.Length != 2) continue;

                    var localIp = ParseHexIp(localParts[0]);
                    var localPort = int.Parse(localParts[1], System.Globalization.NumberStyles.HexNumber);
                    var remoteIp = ParseHexIp(remoteParts[0]);
                    var remotePort = int.Parse(remoteParts[1], System.Globalization.NumberStyles.HexNumber);

                    if (localPort == 9696 || remotePort == 9696 || remotePort == 80 || remotePort == 443)
                    {
                        var info = new ActiveConnectionInfo
                        {
                            LocalAddress = localIp,
                            LocalPort = localPort,
                            RemoteAddress = remoteIp,
                            RemotePort = remotePort,
                            OwningPid = pid,
                            ProcessName = pidNames.TryGetValue(pid, out var name) ? name : "VRChat",
                            AssociatedUrl = string.Empty,
                            AssociatedTitle = string.Empty
                        };

                        if (YTDL.ActiveStreamTracker.TryGetUrlInfo(remoteIp, out var urlInfo))
                        {
                            info.AssociatedUrl = urlInfo.OriginalUrl;
                            info.AssociatedTitle = urlInfo.Title;
                        }
                        else
                        {
                            var sessions = YTDL.ActiveStreamTracker.GetActiveSessions();
                            var match = sessions.FirstOrDefault(s => s.RemoteIp == remoteIp);
                            if (match != null)
                            {
                                info.AssociatedUrl = match.OriginalUrl;
                                info.AssociatedTitle = match.Title;
                            }
                        }

                        list.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read /proc/net/tcp.");
            }
        }
        return list;
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
}
