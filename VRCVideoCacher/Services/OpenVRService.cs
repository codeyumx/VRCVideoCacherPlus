using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Serilog;
using Valve.VR;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public class OpenVRService
{
    private static readonly ILogger Logger = Program.Logger.ForContext<OpenVRService>();

    public static void Start(string dataPath)
    {
        if (!LaunchArgs.OVR)
            return;
        // Register as background app on a background thread with retry so SteamVR
        // doesn't activate theater mode, even if vrserver starts after us.
        Task.Run(async () =>
        {
            bool retry = true;

            while (retry)
            {
                retry = false;
                var initError = EVRInitError.None;
                try
                {
                    OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
                }
                catch (Exception ex)
                {
                    Logger.Warning("Exception during init: {Msg}", ex.Message);
                    return;
                }

                switch (initError)
                {
                    case EVRInitError.None:
                        // Builds we may have previously registered as. The upstream EllyVR build
                        // (including its Steam release) uses "com.github.ellyvr.vrcvideocacher"; the
                        // codeyumx Plus fork — which this fork used to identify as — uses its own key.
                        // If SteamVR still has either set to auto-launch it tries to start them via
                        // Steam, which pops the store page when the app isn't owned. Clear both.
                        string[] legacyAppKeys =
                        [
                            "com.github.ellyvr.vrcvideocacher",
                            "com.github.codeyumx.vrcvideocacherplus"
                        ];
                        const string ForkAppKey = "com.github.bluscream.vrcvideocacherplusplus";
                        foreach (var legacyAppKey in legacyAppKeys)
                        {
                            try
                            {
                                if (OpenVR.Applications.IsApplicationInstalled(legacyAppKey) &&
                                    OpenVR.Applications.GetApplicationAutoLaunch(legacyAppKey))
                                {
                                    Logger.Information("Disabling stale SteamVR auto-launch for legacy app key {Key}", legacyAppKey);
                                    OpenVR.Applications.SetApplicationAutoLaunch(legacyAppKey, false);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(ex, "Failed to clear legacy auto-launch entry {Key}", legacyAppKey);
                            }
                        }

                        // Write the manifest with the real on-disk exe name so SteamVR auto-launch
                        // still finds the binary if the user has renamed it.
                        var manifestPath = Path.Combine(dataPath, "manifest.vrmanifest");
                        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "VRCVideoCacher.exe";
                        var manifestJson = $$"""
                            {
                                "source" : "builtin",
                                "applications" : [{
                                    "app_key" : "{{ForkAppKey}}",
                                    "launch_type" : "binary",
                                    "binary_path_windows": "{{exeName}}",
                                    "binary_path_linux": "{{exeName}}",
                                    "is_dashboard_overlay" : true,
                                    "strings": {
                                        "en_us": {
                                            "name": "VRCVideoCacherPlus",
                                            "description": "Video Player utility for VRC (Plus fork)"
                                        }
                                    }
                                }]
                            }
                            """;
                        // The manifest lands next to the executable, which may well be a
                        // read-only install directory. This runs inside a fire-and-forget
                        // Task, so an escaping exception surfaces only as an unobserved
                        // task fault — SteamVR registration failing is not worth that.
                        try
                        {
                            await File.WriteAllTextAsync(manifestPath, manifestJson);
                            var manifestError = OpenVR.Applications.AddApplicationManifest(manifestPath, false);
                            if (manifestError != EVRApplicationError.None)
                            {
                                Logger.Warning("Failed to register startup manifest: {Error}", manifestError);
                            }
                            else if (OpenVR.Applications.IsApplicationInstalled(ForkAppKey))
                            {
                                Logger.Information("Startup manifest registered successfully");

                                Logger.Information("{AutoLaunchState} steamvr auto-launch", ConfigManager.Config.StartWithSteamVr ? "Enabling" : "Disabling");
                                OpenVR.Applications.SetApplicationAutoLaunch(ForkAppKey, ConfigManager.Config.StartWithSteamVr);
                            }
                            else
                            {
                                Logger.Warning("Failed to register startup manifest");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "Could not write or register the SteamVR manifest at {Path}", manifestPath);
                        }

                        if (LaunchArgs.CloseWithSteamVr)
                        {
                            await PollEventsUntilQuit();
                        }
                        break;
                    // Only retry if vrserver just isn't running yet
                    case EVRInitError.Init_HmdNotFound or EVRInitError.Init_HmdNotFoundPresenceFailed or EVRInitError.Init_NoServerForBackgroundApp:
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        retry = true;
                        break;
                    default:
                        Logger.Information("Not available: {Error}", initError);
                        break;
                }

                try
                {
                    OpenVR.Shutdown();
                }
                catch (Exception ex)
                {
                    Logger.Warning("Exception during shutdown: {Msg}", ex.Message);
                    return;
                }
            }
        });
    }

    private static async Task PollEventsUntilQuit()
    {
        var vrEvent = new VREvent_t();
        var eventSize = (uint)Marshal.SizeOf<VREvent_t>();

        bool quitApp = false;
        while (!quitApp)
        {
            await Task.Delay(500);

            if (OpenVR.System == null)
            {
                Logger.Warning("OpenVR system became unavailable, assuming SteamVR closed");
                quitApp = true;
            }
            else
            {
                while (OpenVR.System.PollNextEvent(ref vrEvent, eventSize))
                {
                    if ((EVREventType)vrEvent.eventType == EVREventType.VREvent_Quit)
                    {
                        Logger.Information("Received VREvent_Quit, shutting down");
                        quitApp = true;
                    }
                }
            }
        }

        if (LaunchArgs.HasGui && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lifetime.Shutdown();
            });
        }
        else
        {
            Environment.Exit(0);
        }
    }
}
