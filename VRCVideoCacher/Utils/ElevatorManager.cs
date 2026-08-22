using System.Diagnostics;
using Serilog;
using VRCVideoCacher.API;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();
    // Reading /etc/hosts can throw (permissions, or the file simply being absent on a
    // stripped-down system). A static field initialiser that throws surfaces as a
    // TypeInitializationException from whatever first touched this class.
    public static bool HasHostsLine = SafeIsHostAdded();

    private static bool SafeIsHostAdded()
    {
        try
        {
            return HostsManager.IsHostAdded();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the hosts file; assuming the entry is absent.");
            return false;
        }
    }

    private static readonly bool InPressureVessel = Directory.Exists("/run/pressure-vessel");

    private static string? FindLaunchClient()
    {
        string[] candidates =
        [
            "/usr/lib/pressure-vessel/from-host/bin/steam-runtime-launch-client",
            "/usr/bin/steam-runtime-launch-client",
            "/usr/lib/pressure-vessel/bin/steam-runtime-launch-client",
        ];
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return null;
    }

    private static string? FindHostBin(string name)
    {
        var paths = new[] { $"/usr/bin/{name}", $"/bin/{name}", $"/usr/local/bin/{name}" };
        foreach (var p in paths)
        {
            var check = InPressureVessel ? $"/run/host{p}" : p;
            if (File.Exists(check)) return p;
        }
        return null;
    }

    private static Process? MakeLinuxElevatedProcess(params string[] flags)
    {
        var launchClient = InPressureVessel ? FindLaunchClient() : null;
        Log.Debug("InPressureVessel={InPressureVessel} launch-client={LC}", InPressureVessel, launchClient ?? "n/a");

        string appPath = Environment.ProcessPath!;

        // ArgumentList rather than a formatted command line: appPath is an install location
        // that routinely contains spaces (a Steam library on an external drive, say), and
        // interpolating it unquoted split it into two arguments — so the elevated command
        // either failed or, worse, ran against a truncated path.
        ProcessStartInfo MakeStartInfo(string exe, string[] args)
        {
            var psi = new ProcessStartInfo { UseShellExecute = false };
            if (launchClient != null)
            {
                psi.FileName = launchClient;
                psi.ArgumentList.Add("--alongside-steam");
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(exe);
            }
            else
            {
                psi.FileName = exe;
            }

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            return psi;
        }

        // 1. Try pkexec
        var pkexec = FindHostBin("pkexec");
        if (pkexec != null)
        {
            Log.Debug("Using pkexec");
            return new Process { StartInfo = MakeStartInfo(pkexec, [appPath, .. flags]) };
        }

        // 2. Try sudo -A with a graphical askpass helper
        string[] askpassCandidates =
        [
            "/usr/lib/openssh/gnome-ssh-askpass",
            "/usr/lib/ssh/x11-ssh-askpass",
            "/usr/bin/ksshaskpass",
            "/usr/lib/seahorse/seahorse-ssh-askpass",
        ];
        foreach (var askpass in askpassCandidates)
        {
            var check = InPressureVessel ? $"/run/host{askpass}" : askpass;
            if (!File.Exists(check)) continue;
            Log.Debug("Using sudo -A with askpass: {Askpass}", askpass);
            var psi = MakeStartInfo("/usr/bin/sudo", ["-A", appPath, .. flags]);
            psi.Environment["SUDO_ASKPASS"] = askpass;
            return new Process { StartInfo = psi };
        }

        // 3. Fall back to a terminal emulator with sudo
        string[] terminals = ["x-terminal-emulator", "xterm", "konsole", "gnome-terminal", "xfce4-terminal", "mate-terminal"];
        foreach (var term in terminals)
        {
            var termPath = FindHostBin(term);
            if (termPath == null) continue;
            Log.Debug("Using terminal {Term} with sudo", termPath);
            // gnome-terminal wants "-- cmd args", the others "-e cmd args".
            var termFlag = termPath.Contains("gnome-terminal") ? "--" : "-e";
            return new Process { StartInfo = MakeStartInfo(termPath, [termFlag, "/usr/bin/sudo", appPath, .. flags]) };
        }

        Log.Error("No elevation method found (tried pkexec, sudo with a graphical askpass, and a terminal).");
        return null;
    }

    /// <summary>
    /// Result of asking the system for elevation and running ourselves with <paramref name="argument"/>.
    /// </summary>
    /// <returns>
    /// The helper's exit code, or null when elevation was unavailable or the user dismissed
    /// the prompt — those are not failures of the operation, they are the user declining it.
    /// </returns>
    public static async Task<int?> RunElevatedSelfAsync(string argument)
    {
        Process? proc;
        if (OperatingSystem.IsWindows())
        {
            proc = new Process
            {
                StartInfo = { FileName = Environment.ProcessPath, Arguments = argument, UseShellExecute = true, Verb = "runas" }
            };
            try
            {
                proc.Start();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Information("User cancelled the UAC prompt.");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to launch elevated helper.");
                return null;
            }
        }
        else
        {
            proc = MakeLinuxElevatedProcess(argument);
            if (proc == null)
                return null;

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to launch privilege elevator.");
                return null;
            }
        }

        using (proc)
        {
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }
    }

    /// <summary>
    /// Adds or removes the hosts entry, elevating as required.
    ///
    /// Async because the elevation prompt is modal to the *system*, not to us: the previous
    /// synchronous WaitForExit ran on the UI thread, so the whole window froze — no repaint,
    /// no tray, no response — for as long as the UAC or polkit dialog stayed open.
    /// </summary>
    public static Task ToggleHostLineAsync() =>
        HasHostsLine ? RemoveHostFileAsync() : AddHostFileAsync();

    private static async Task AddHostFileAsync()
    {
        Process? proc;
        if (OperatingSystem.IsWindows())
        {
            proc = new Process { StartInfo = { FileName = Environment.ProcessPath, Arguments = "--addhost", UseShellExecute = true, Verb = "runas" } };
            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Warning("User cancelled UAC prompt.");
                return;
            }
        }
        else
        {
            proc = MakeLinuxElevatedProcess("--addhost");
            if (proc == null) return;
            try { proc.Start(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch privilege elevator for adding host entry.");
                return;
            }
        }

        await proc.WaitForExitAsync();

        if (HostsManager.IsHostAdded())
        {
            Log.Information("Host entry added successfully.");
            HasHostsLine = true;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost.youtube.com:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
        }
        else
        {
            Log.Warning("Host entry not found after elevation — user may have cancelled or elevation failed (exit code: {ExitCode}).", proc.ExitCode);
        }
    }

    private static async Task RemoveHostFileAsync()
    {
        Process? proc;
        if (OperatingSystem.IsWindows())
        {
            proc = new Process { StartInfo = { FileName = Environment.ProcessPath, Arguments = "--removehost", UseShellExecute = true, Verb = "runas" } };
            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Warning("User cancelled UAC prompt.");
                return;
            }
        }
        else
        {
            proc = MakeLinuxElevatedProcess("--removehost");
            if (proc == null) return;
            try { proc.Start(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch privilege elevator for removing host entry.");
                return;
            }
        }

        await proc.WaitForExitAsync();

        if (!HostsManager.IsHostAdded())
        {
            Log.Information("Host entry removed successfully.");
            HasHostsLine = false;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
        }
        else
        {
            Log.Warning("Host entry still present after elevation — user may have cancelled or elevation failed (exit code: {ExitCode}).", proc.ExitCode);
        }
    }
}
