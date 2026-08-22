using System.Diagnostics;
using Serilog;

namespace VRCVideoCacher.Utils;

public static class OpenUrl
{
    private static readonly ILogger Log = Program.Logger.ForContext("SourceContext", nameof(OpenUrl));

    public static bool Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Warning("Refused to open empty URL");
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Warning("Refused to open invalid URL {Url}", url);
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("Refused to open non-web URL {Url}", url);
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
            return Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open link: {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Reveals a file in the system file manager, falling back to opening its containing
    /// folder where selecting is not supported.
    /// </summary>
    public static bool RevealFile(string path)
    {
        var folder = Path.GetDirectoryName(path);

        if (!OperatingSystem.IsWindows() || !File.Exists(path))
            return OpenFolder(folder ?? path);

        try
        {
            // "/select,<path>" has to arrive as one token including the comma, which is why
            // this cannot use a plain ArgumentList entry per word.
            var psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = false };
            psi.ArgumentList.Add($"/select,{path}");
            return Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reveal {Path}; opening the folder instead.", path);
            return OpenFolder(folder ?? path);
        }
    }

    /// <summary>
    /// Opens a local folder in the system file manager. Separate from <see cref="Open"/>,
    /// which deliberately refuses anything that is not http(s) — the callers that wanted a
    /// folder were each hand-rolling their own Process.Start instead.
    /// </summary>
    public static bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Warning("Refused to open an empty folder path");
            return false;
        }

        try
        {
            Directory.CreateDirectory(path);

            var fileManager = OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open";
            var psi = new ProcessStartInfo { FileName = fileManager, UseShellExecute = false };
            psi.ArgumentList.Add(path);
            return Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder: {Path}", path);
            return false;
        }
    }
}