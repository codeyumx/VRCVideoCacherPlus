namespace VRCVideoCacher.Utils;

public class LaunchArgs
{
    private const string NoGuiArg = "--no-gui";
    private const string GlobalPathArg = "--global-path";
    private const string KillExistingInstanceArg = "--kill-existing-instance";
    private const string WaitForPidArg = "--wait-for-pid";
    private const string NoSteamArg = "--no-steam";
    private const string NoOvrArg = "--no-ovr";
    private const string CloseWithSteamVrArg = "--close-with-steamvr";
    private const string AddHostArg = "--addhost";
    private const string RemoveHostArg = "--removehost";
    private const string SeverArg = "--sever-connections";

    public static bool HasGui = true;
    public static bool UseGlobalPath;
    public static bool KillExistingInstance = false;
    public static int? WaitForPid;
    public static bool SteamSdk = true;
    public static bool OVR = true;
    public static bool CloseWithSteamVr = false;
    public static bool AddHost = false;
    public static bool RemoveHost = false;

    /// <summary>
    /// Addresses passed to an elevated instance spawned purely to close sockets and exit.
    /// Populated from --sever-connections=1.2.3.4,2001:db8::1
    /// </summary>
    public static IReadOnlyList<string> SeverAddresses = [];

    /// <summary>
    /// True when this process was spawned by the elevation helper purely to edit the hosts
    /// file and exit. Such a process has no UI and should not touch user config.
    /// </summary>
    public static bool IsHostsEdit => AddHost || RemoveHost;

    /// <summary>True when this process exists only to sever connections and exit.</summary>
    public static bool IsSeverCommand => SeverAddresses.Count > 0;

    /// <summary>
    /// A short-lived privileged helper: no window, no config writes, no background work.
    /// </summary>
    public static bool IsPrivilegedHelper => IsHostsEdit || IsSeverCommand;

    public static void SetupArguments(params string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals(NoGuiArg, StringComparison.OrdinalIgnoreCase))
                HasGui = false;

            if (arg.Equals(GlobalPathArg, StringComparison.OrdinalIgnoreCase))
                UseGlobalPath = true;

            if (arg.Equals(KillExistingInstanceArg, StringComparison.OrdinalIgnoreCase))
                KillExistingInstance = true;

            if (arg.StartsWith(WaitForPidArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                var pidStr = arg.Substring(WaitForPidArg.Length + 1);
                if (int.TryParse(pidStr, out var pid))
                    WaitForPid = pid;
            }

            if (arg.Equals(NoSteamArg, StringComparison.OrdinalIgnoreCase))
                SteamSdk = false;

            if (arg.Equals(NoOvrArg, StringComparison.OrdinalIgnoreCase))
                OVR = false;

            if (arg.Equals(CloseWithSteamVrArg, StringComparison.OrdinalIgnoreCase))
                CloseWithSteamVr = true;

            if (arg.Equals(AddHostArg, StringComparison.OrdinalIgnoreCase))
                AddHost = true;

            if (arg.Equals(RemoveHostArg, StringComparison.OrdinalIgnoreCase))
                RemoveHost = true;

            if (arg.StartsWith(SeverArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                SeverAddresses = arg[(SeverArg.Length + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Reconstructs the flags this process was started with, so that the updater's relaunch
    /// preserves how the user (or SteamVR, or VRCX) launched it. Only --no-gui and
    /// --global-path used to be re-emitted, so an update silently dropped --no-steam,
    /// --no-ovr, --close-with-steamvr and --kill-existing-instance.
    ///
    /// Deliberately excludes --wait-for-pid, which the updater supplies itself, and the
    /// one-shot hosts commands, which belong to a subprocess that exits immediately.
    /// </summary>
    public static List<string> BuildArgs()
    {
        var args = new List<string>();
        if (!HasGui)
            args.Add(NoGuiArg);

        if (UseGlobalPath)
            args.Add(GlobalPathArg);

        if (!SteamSdk)
            args.Add(NoSteamArg);

        if (!OVR)
            args.Add(NoOvrArg);

        if (CloseWithSteamVr)
            args.Add(CloseWithSteamVrArg);

        if (KillExistingInstance)
            args.Add(KillExistingInstanceArg);

        return args;
    }
}
