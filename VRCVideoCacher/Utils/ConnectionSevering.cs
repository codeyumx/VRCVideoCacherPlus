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

    // Exit codes of the elevated helper, read back by the unprivileged parent. Deliberately
    // outside the 0-1 range so an unrelated crash is never mistaken for a real outcome.
    private const int HelperSevered = 0;
    private const int HelperNothingToDo = 10;
    private const int HelperNotPermitted = 11;
    private const int HelperUnsupported = 12;
    private const int HelperFailed = 13;

    /// <summary>
    /// Entry point for the short-lived privileged instance spawned by
    /// <see cref="ElevatorManager.RunElevatedSelfAsync"/>. Closes the requested sockets and
    /// exits — it never starts the UI, the web server, or touches the user's config.
    ///
    /// Called from Program.Main before anything else is initialised.
    /// </summary>
    public static void TryRunElevatedCommand()
    {
        if (!LaunchArgs.IsSeverCommand)
            return;

        // This runs as root. Everything on the command line is re-parsed as an IP address and
        // anything that is not one is dropped, so nothing reaches `ss` that could be read as a
        // filter expression or a shell fragment.
        var addresses = LaunchArgs.SeverAddresses
            .Where(a => IPAddress.TryParse(a, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rejected = LaunchArgs.SeverAddresses.Count - addresses.Count;
        if (rejected > 0)
            Log.Warning("Ignored {Count} --sever-connections value(s) that are not IP addresses.", rejected);

        if (addresses.Count == 0)
        {
            Environment.Exit(HelperNothingToDo);
            return;
        }

        var (severed, outcome) = SeverRemoteAsync(addresses).GetAwaiter().GetResult();
        Log.Information("Elevated sever helper closed {Count} socket(s): {Outcome}", severed, outcome);

        Environment.Exit(outcome switch
        {
            SeverOutcome.Severed => HelperSevered,
            SeverOutcome.NothingToDo => HelperNothingToDo,
            SeverOutcome.NotPermitted => HelperNotPermitted,
            SeverOutcome.Unsupported => HelperUnsupported,
            _ => HelperFailed
        });
    }

    /// <summary>
    /// Closes everything: our own streams, then best-effort on VRChat's direct connections.
    /// </summary>
    /// <param name="allowElevation">
    /// Whether to fall back to an elevation prompt when the kernel refuses. Off by default:
    /// this method is also called from the automatic "block all videos" toggle, and throwing a
    /// polkit or UAC dialog at somebody mid-session because a setting flipped is hostile. Pass
    /// true only when the user explicitly asked for this connection to be cut.
    /// </param>
    public static async Task<SeverResult> SeverAllAsync(bool allowElevation = false)
    {
        var localClosed = API.LocalStreamRegistry.CloseAll();
        if (localClosed > 0)
            Log.Information("Closed {Count} cached-video stream(s) served by this application.", localClosed);

        var targets = YTDL.ActiveStreamTracker.GetActiveVideoIps();
        var (severed, outcome) = await SeverRemoteAsync(targets);

        if (outcome == SeverOutcome.NotPermitted && allowElevation)
            (severed, outcome) = await SeverElevatedAsync(targets);

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
    public static async Task<SeverResult> SeverAddressAsync(string address, bool allowElevation = false)
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

        if (outcome == SeverOutcome.NotPermitted && allowElevation)
            (severed, outcome) = await SeverElevatedAsync([address]);

        LogOutcome(outcome, severed, 1);
        return new SeverResult(0, severed, outcome);
    }

    /// <summary>
    /// Re-runs the sever as root by asking the desktop's authorisation agent — polkit on
    /// Linux (KDE, GNOME and the rest all ship an agent for it), UAC on Windows. The prompt
    /// is the system's, so the password never passes through this process.
    ///
    /// Users who would rather never see the prompt can grant the capability once instead;
    /// <see cref="CapabilityHint"/> spells out how.
    /// </summary>
    private static async Task<(int, SeverOutcome)> SeverElevatedAsync(IReadOnlyCollection<string> addresses)
    {
        var targets = addresses
            .Where(a => IPAddress.TryParse(a, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
            return (0, SeverOutcome.NothingToDo);

        Log.Information("Requesting elevated privileges to close {Count} connection(s).", targets.Count);

        var exitCode = await ElevatorManager.RunElevatedSelfAsync(
            $"--sever-connections={string.Join(',', targets)}");

        if (exitCode is null)
        {
            // No elevation mechanism, or the user dismissed the prompt. Either way the
            // original refusal still stands — say so rather than inventing a new failure.
            Log.Information("Elevation was declined or unavailable. {Hint}", CapabilityHint);
            return (0, SeverOutcome.NotPermitted);
        }

        switch (exitCode)
        {
            case HelperSevered:
                // The helper does not report a count across the process boundary; one
                // confirmed close is enough for the UI to say the connection was cut.
                return (targets.Count, SeverOutcome.Severed);
            case HelperNothingToDo:
                return (0, SeverOutcome.NothingToDo);
            case HelperUnsupported:
                return (0, SeverOutcome.Unsupported);
            case HelperNotPermitted:
                Log.Warning("Even with elevation the kernel refused to close the connection.");
                return (0, SeverOutcome.NotPermitted);
            default:
                Log.Warning("Elevated sever helper exited with {Code}.", exitCode);
                return (0, SeverOutcome.Failed);
        }
    }

    /// <summary>
    /// The one-off alternative to being prompted every time. CAP_NET_ADMIN is exactly the
    /// privilege SOCK_DESTROY requires, so granting it to the binary removes the need for
    /// root entirely — this is the "do we still need sudo" answer, and the answer is no.
    ///
    /// It has to be re-applied after an update, because the updater replaces the binary and
    /// file capabilities live on the inode.
    /// </summary>
    public static string CapabilityHint =>
        OperatingSystem.IsLinux()
            ? $"To close connections without a password prompt, grant the capability once: " +
              $"sudo setcap cap_net_admin+ep \"{Environment.ProcessPath}\" (re-apply after each update)."
            : "Run VRCVideoCacher as administrator to close connections without a prompt.";

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
                    "already streaming from a CDN will keep playing until it ends. {Hint}",
                    OperatingSystem.IsWindows() ? "administrator rights" : "root (CAP_NET_ADMIN)",
                    CapabilityHint);
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
        {
            // Windows exposes MIB_TCP6ROW for *listing* IPv6 connections but has never shipped
            // a SetTcp6Entry to close one — there is no public API for it at any privilege
            // level, elevation included. Severing IPv6 here is not a permissions problem and
            // must not be reported as one, or the UI will offer an elevation prompt that
            // cannot possibly help.
            var v6 = addresses.Where(IsIpV6).ToList();
            var v4 = addresses.Where(a => !IsIpV6(a)).ToList();

            var result = v4.Count > 0 ? SeverRemoteWindows(v4) : (0, SeverOutcome.NothingToDo);

            if (v6.Count > 0)
            {
                Log.Warning(
                    "{Count} connection(s) are over IPv6, which Windows provides no way to close. " +
                    "Blocking still applies to new requests.", v6.Count);

                // Only downgrade when nothing else happened — an IPv4 sever that worked is
                // still a success, it just did not cover everything.
                if (result.Item2 == SeverOutcome.NothingToDo)
                    result = (result.Item1, SeverOutcome.Unsupported);
            }

            return result;
        }

        if (OperatingSystem.IsLinux())
            return await SeverRemoteLinuxAsync(addresses);

        return (0, SeverOutcome.Unsupported);
    }

    private static bool IsIpV6(string address) =>
        IPAddress.TryParse(address, out var parsed) &&
        parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

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

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var outcome = await RunSsKillAsync(address);

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

        return (0, SeverOutcome.NothingToDo);
    }

    /// <summary>
    /// ss parses its address filter itself, and a bare IPv6 literal loses: the colons read as
    /// a host:port separator, so `dst 2001:db8::1` is rejected with
    /// "an inet prefix is expected rather than 2001:db8:" and nothing is closed. Brackets
    /// disambiguate it, and IPv4 is unaffected either way — so this is the whole of IPv6
    /// support on Linux.
    /// </summary>
    internal static string FormatSsDestination(string address) =>
        IsIpV6(address) ? $"[{address}]" : address;

    /// <summary>
    /// Runs `ss -t -K dst ADDRESS` and works out what really happened.
    ///
    /// Unlike Windows, Linux closes IPv6 sockets through the very same SOCK_DESTROY call, so
    /// there is nothing extra to implement — only the filter has to be spelled correctly.
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
            var result = await ProcessRunner.RunAsync("ss", ["-t", "-K", "dst", FormatSsDestination(address)]);

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
