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
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(logFile))
                    Process.Start("explorer.exe", $"/select,\"{logFile}\"");
                else
                    Process.Start("explorer.exe", LogsPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", LogsPath);
            }
        }
        catch
        {
        }
    }
}
