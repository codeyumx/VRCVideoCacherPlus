using System.Diagnostics;
using Serilog;

namespace VRCVideoCacher.Utils;

public static class LoggerUtils
{
    private static readonly string LogsPath = Path.Join(Program.DataPath, "Logs");
    private static DateTime? LoggerStartDateTime = DateTime.Now;

    public static void LogUnhandledException(Exception ex, string message)
    {
        try
        {
            Console.WriteLine($"{message}: " + ex);
        }
        catch
        {
        }

        try
        {
            Program.Logger.Error(ex, "{Message}", message);

            var logFile = Path.Combine(LogsPath, $"VRCVideoCacher{(LoggerStartDateTime ?? DateTime.Now):yyyyMMdd}.log");
            if (OperatingSystem.IsWindows() && File.Exists(logFile))
            {
                // /select, needs the argument as a single token including the comma, which
                // ArgumentList would quote wrongly — so this one case stays hand-built.
                var psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = false };
                psi.ArgumentList.Add($"/select,{logFile}");
                Process.Start(psi);
            }
            else
            {
                OpenUrl.OpenFolder(LogsPath);
            }
        }
        catch
        {
        }
    }
}
