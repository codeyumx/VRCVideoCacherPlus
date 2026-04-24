using Serilog;
using Valve.VR;

namespace VRCVideoCacher.Services;

public class OpenVRService
{
    private static readonly ILogger Logger = Program.Logger.ForContext<OpenVRService>();

    public static void Start(string dataPath)
    {
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
                        // Upstream EllyVR build (including its Steam release) registers itself under
                        // "com.github.ellyvr.vrcvideocacher". If a user previously ran the Steam build
                        // and SteamVR still has that key set to auto-launch, SteamVR tries to launch
                        // it via Steam and Steam pops the store page when the app isn't owned.
                        // Proactively clear that auto-launch on startup.
                        const string LegacyAppKey = "com.github.ellyvr.vrcvideocacher";
                        const string ForkAppKey = "com.github.codeyumx.vrcvideocacherplus";
                        try
                        {
                            if (OpenVR.Applications.IsApplicationInstalled(LegacyAppKey) &&
                                OpenVR.Applications.GetApplicationAutoLaunch(LegacyAppKey))
                            {
                                Logger.Information("Disabling stale SteamVR auto-launch for legacy app key {Key}", LegacyAppKey);
                                OpenVR.Applications.SetApplicationAutoLaunch(LegacyAppKey, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "Failed to clear legacy auto-launch entry");
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
                        await File.WriteAllTextAsync(manifestPath, manifestJson);
                        var manifestError = OpenVR.Applications.AddApplicationManifest(manifestPath, false);
                        if (manifestError != EVRApplicationError.None)
                        {
                            Logger.Warning("Failed to register startup manifest: {Error}", manifestError);
                        }
                        else
                        {
                            if (OpenVR.Applications.IsApplicationInstalled(ForkAppKey))
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
}
