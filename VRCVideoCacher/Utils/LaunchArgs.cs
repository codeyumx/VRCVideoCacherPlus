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
    /// True when this process was spawned by the elevation helper purely to edit the hosts
    /// file and exit. Such a process has no UI and should not touch user config.
    /// </summary>
    public static bool IsHostsEdit => AddHost || RemoveHost;

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
        }
    }

    public static List<string> BuildArgs()
    {
        var args = new List<string>();
        if (!HasGui)
            args.Add(NoGuiArg);

        if (UseGlobalPath)
            args.Add(GlobalPathArg);

        return args;
    }
}
