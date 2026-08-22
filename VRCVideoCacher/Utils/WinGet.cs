using System.Diagnostics;
using System.Runtime.Versioning;
using Serilog;

namespace VRCVideoCacher.Utils;

public class WinGet
{
    private static readonly ILogger Log = Program.Logger.ForContext<WinGet>();
    private static readonly string WingetPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
    private static readonly Dictionary<string, string> WingetPackages = new()
    {
        { "VP9 Video Extensions", "9n4d0msmp0pt" },
        { "AV1 Video Extension", "9mvzqvxjbq9v" },
        { "Dolby Digital Plus decoder for PC OEMs", "9nvjqjbdkn97" }
    };

    [SupportedOSPlatform("windows")]
    public static async Task TryInstallPackages()
    {
        Log.Information("Checking for missing codec packages...");
        if (!await IsOurPackagesInstalled())
        {
            Log.Information("Installing missing codec packages...");
            await InstallAllPackages();
        }
    }

    private static async Task<bool> IsOurPackagesInstalled()
    {
        foreach (var package in WingetPackages.Values)
        {
            if (!await IsPackageInstalled(package))
            {
                return false;
            }
        }

        Log.Information("Codec packages are already installed.");
        return true;
    }

    private static async Task<bool> IsPackageInstalled(string packageId)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = WingetPath,
                Arguments = $"list \"{packageId}\" -s msstore --accept-source-agreements"
            });
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed on IsPackageInstalled");
            return false;
        }
    }

    private static async Task InstallAllPackages()
    {
        foreach (var package in WingetPackages.Values)
        {
            await InstallPackage(package);
        }
    }

    private static async Task InstallPackage(string packageId)
    {
        try
        {
            var (output, error, exitCode) = await ProcessRunner.RunAsync(new ProcessStartInfo
            {
                FileName = WingetPath,
                Arguments = $"install --id {packageId} -s msstore --accept-package-agreements --accept-source-agreements"
            });

            foreach (var line in output.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Log.Debug("{Winget}: {Line}", "winget", line.TrimEnd());
            }

            if (exitCode != 0 && !string.IsNullOrEmpty(error))
                throw new Exception($"Installation failed with exit code {exitCode}. Error: {error}");

            var packageName = WingetPackages.FirstOrDefault(x => x.Value == packageId).Key;
            if (exitCode == 0)
                Log.Information("Successfully installed package: {PackageName}", packageName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed on InstallPackage");
        }
    }
}