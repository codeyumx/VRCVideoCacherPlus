using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;
using Vanara.PInvoke;
using static Vanara.PInvoke.IpHlpApi;

namespace VRCVideoCacher.Utils;

/// <summary>How a sever attempt against the operating system actually went.</summary>
public enum SeverOutcome
{
    /// <summary>Nothing matched — there was nothing to close.</summary>
    NothingToDo,

    /// <summary>At least one socket was genuinely closed.</summary>
    Severed,

    /// <summary>The kernel refused: this needs administrator/root and we do not have it.</summary>
    NotPermitted,

    /// <summary>No mechanism exists on this platform, or the tooling is missing.</summary>
    Unsupported,

    /// <summary>Attempted and failed for some other reason.</summary>
    Failed
}

/// <summary>
/// The result of severing. Local and remote are reported separately because they have very
/// different reliability, and conflating them is what let the previous implementation claim
/// success while doing nothing.
/// </summary>
public readonly record struct SeverResult(int LocalStreamsClosed, int RemoteSocketsSevered, SeverOutcome RemoteOutcome)
{
    public bool AnythingClosed => LocalStreamsClosed > 0 || RemoteSocketsSevered > 0;
}

/// <summary>
/// Stops in-flight video playback.
///
/// There are two very different cases, and the distinction matters:
///
///   Local  — the video is cached and VRChat is streaming it from our own web server. We
///            own that socket, so closing it always works, on every platform, with no
///            special privileges.
///
///   Remote — the video is not cached and VRChat is talking to a CDN directly. Closing
///            somebody else's socket is a privileged operation: SetTcpEntry needs
///            administrator on Windows, and `ss -K` needs CAP_NET_ADMIN on Linux.
///
/// The remote path therefore fails for most users, and it fails *quietly*: `ss` writes
/// "SOCK_DESTROY answers: Operation not permitted" to stderr, prints its column header to
/// stdout, and still exits 0. The previous implementation treated exit-code-0-plus-nonempty-
/// stdout as success, so it reported "Successfully severed N connections" every time while
/// changing nothing. Everything here is built around not doing that again: outcomes are
/// classified from what actually happened, and "not permitted" is reported as such.
/// </summary>
public static class ConnectionSevering
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(ConnectionSevering));

    // ss reports a refused SOCK_DESTROY on stderr but still exits 0.
    private const string PermissionDeniedMarker = "Operation not permitted";

    /// <summary>
    /// Closes everything: our own streams, then best-effort on VRChat's direct connections.
    /// </summary>
    public static async Task<SeverResult> SeverAllAsync()
    {
        var localClosed = API.LocalStreamRegistry.CloseAll();
        if (localClosed > 0)
            Log.Information("Closed {Count} cached-video stream(s) served by this application.", localClosed);

        var targets = YTDL.ActiveStreamTracker.GetActiveVideoIps();
        var (severed, outcome) = await SeverRemoteAsync(targets);

        YTDL.ActiveStreamTracker.ClearActiveVideoIps();
        LogOutcome(outcome, severed, targets.Count);

        return new SeverResult(localClosed, severed, outcome);
    }

    /// <summary>
    /// Closes the connections to one remote address.
    ///
    /// Does not touch other cached streams — severing one CDN connection used to call
    /// CloseAllLocalStreams and take every other playing video down with it.
    /// </summary>
    public static async Task<SeverResult> SeverAddressAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new SeverResult(0, 0, SeverOutcome.NothingToDo);

        // Streaming from us: we own the socket, so this always works.
        if (IsLoopback(address))
        {
            var closed = API.LocalStreamRegistry.CloseAll();
            Log.Information("Closed {Count} local stream(s) for {Address}.", closed, address);
            return new SeverResult(closed, 0, SeverOutcome.NothingToDo);
        }

        var (severed, outcome) = await SeverRemoteAsync([address]);
        LogOutcome(outcome, severed, 1);
        return new SeverResult(0, severed, outcome);
    }

    private static bool IsLoopback(string address) =>
        IPAddress.TryParse(address, out var parsed) && IPAddress.IsLoopback(parsed);

    private static void LogOutcome(SeverOutcome outcome, int severed, int targetCount)
    {
        switch (outcome)
        {
            case SeverOutcome.Severed:
                Log.Information("Severed {Count} direct connection(s) to video hosts.", severed);
                break;

            case SeverOutcome.NotPermitted:
                Log.Warning(
                    "Could not close VRChat's direct connections: this needs {Requirement}. " +
                    "Cached videos were still stopped, and further requests are blocked, but a video " +
                    "already streaming from a CDN will keep playing until it ends.",
                    OperatingSystem.IsWindows() ? "administrator rights" : "root (CAP_NET_ADMIN)");
                break;

            case SeverOutcome.Unsupported:
                Log.Information("No mechanism available on this platform to close VRChat's direct connections.");
                break;

            case SeverOutcome.Failed:
                Log.Warning("Failed to close VRChat's direct connections.");
                break;

            case SeverOutcome.NothingToDo when targetCount == 0:
                Log.Debug("No direct video connections were being tracked.");
                break;
        }
    }

    private static async Task<(int Severed, SeverOutcome Outcome)> SeverRemoteAsync(IReadOnlyCollection<string> addresses)
    {
        if (addresses.Count == 0)
            return (0, SeverOutcome.NothingToDo);

        if (OperatingSystem.IsWindows())
            return SeverRemoteWindows(addresses);

        if (OperatingSystem.IsLinux())
            return await SeverRemoteLinuxAsync(addresses);

        return (0, SeverOutcome.Unsupported);
    }

    [SupportedOSPlatform("windows")]
    private static (int, SeverOutcome) SeverRemoteWindows(IReadOnlyCollection<string> addresses)
    {
        var processes = NetworkConnections.GetVrChatProcesses();
        if (processes.Count == 0)
            return (0, SeverOutcome.NothingToDo);

        var wanted = new HashSet<string>(addresses, StringComparer.OrdinalIgnoreCase);
        var severed = 0;
        var denied = false;
        var attempted = 0;

        const int afInet = 2;
        uint bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, afInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
        if (bufferSize == 0)
            return (0, SeverOutcome.NothingToDo);

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, false, afInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL) != 0)
                return (0, SeverOutcome.Failed);

            var count = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + sizeof(int);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;

                if (!processes.ContainsKey((int)row.dwOwningPid))
                    continue;

                var remote = new IPAddress((long)row.dwRemoteAddr).ToString();
                if (!wanted.Contains(remote))
                    continue;

                attempted++;
                var closing = new MIB_TCPROW
                {
                    dwState = MIB_TCP_STATE.MIB_TCP_STATE_DELETE_TCB,
                    dwLocalAddr = row.dwLocalAddr,
                    dwLocalPort = row.dwLocalPort,
                    dwRemoteAddr = row.dwRemoteAddr,
                    dwRemotePort = row.dwRemotePort
                };

                var result = SetTcpEntry(closing);
                if (result.Succeeded)
                {
                    severed++;
                    continue;
                }

                // ERROR_ACCESS_DENIED is the ordinary outcome without elevation, and it is
                // the whole reason this feature appears to do nothing for most users.
                if (result == Win32Error.ERROR_ACCESS_DENIED)
                    denied = true;
                else
                    Log.Debug("SetTcpEntry failed for PID {Pid}: {Error}", row.dwOwningPid, result);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        if (severed > 0)
            return (severed, SeverOutcome.Severed);
        if (denied)
            return (0, SeverOutcome.NotPermitted);

        return (0, attempted == 0 ? SeverOutcome.NothingToDo : SeverOutcome.Failed);
    }

    private static async Task<(int, SeverOutcome)> SeverRemoteLinuxAsync(IReadOnlyCollection<string> addresses)
    {
        var severed = 0;
        var denied = false;
        var ran = false;

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var outcome = await RunSsKillAsync(address);
            ran = true;

            switch (outcome)
            {
                case SeverOutcome.Severed:
                    severed++;
                    break;
                case SeverOutcome.NotPermitted:
                    denied = true;
                    break;
                case SeverOutcome.Unsupported:
                    // ss is not installed; no point trying the remaining addresses.
                    return (severed, severed > 0 ? SeverOutcome.Severed : SeverOutcome.Unsupported);
            }
        }

        if (severed > 0)
            return (severed, SeverOutcome.Severed);
        if (denied)
            return (0, SeverOutcome.NotPermitted);

        return (0, ran ? SeverOutcome.NothingToDo : SeverOutcome.NothingToDo);
    }

    /// <summary>
    /// Runs `ss -t -K dst ADDRESS` and works out what really happened.
    ///
    /// ss exits 0 whether or not it managed to destroy anything, and always prints a column
    /// header to stdout, so neither the exit code nor "stdout is non-empty" tells us
    /// anything. What does: the permission error on stderr, and whether any socket rows were
    /// printed beneath the header.
    /// </summary>
    private static async Task<SeverOutcome> RunSsKillAsync(string address)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("ss", ["-t", "-K", "dst", address]);

            if (result.Error.Contains(PermissionDeniedMarker, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("ss -K refused for {Address}: {Error}", address, result.Error);
                return SeverOutcome.NotPermitted;
            }

            if (result.ExitCode != 0)
            {
                Log.Debug("ss -K exited {Code} for {Address}: {Error}", result.ExitCode, address, result.Error);
                return SeverOutcome.Failed;
            }

            // First line is the header; anything after it is a socket that was destroyed.
            var closedRows = result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Count(line => !string.IsNullOrWhiteSpace(line));

            return closedRows > 0 ? SeverOutcome.Severed : SeverOutcome.NothingToDo;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ss is not installed (iproute2 missing).
            Log.Debug("ss is not available; cannot close VRChat's direct connections.");
            return SeverOutcome.Unsupported;
        }
        catch (Exception ex)
        {
            Log.Debug("ss -K failed for {Address}: {Error}", address, ex.Message);
            return SeverOutcome.Failed;
        }
    }
}
