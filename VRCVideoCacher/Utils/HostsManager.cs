namespace VRCVideoCacher.Utils;

public class HostsManager
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<HostsManager>();

    // Detection keys off the bare markers, not the newline-wrapped forms below: the hosts
    // file may use LF while Environment.NewLine is CRLF, and a mismatch there made
    // IsHostAdded report "absent" for a block that was plainly present — which in turn made
    // Add() append a second copy every time.
    private const string HeaderMarker = "# ----- BEGIN VRCVIDEOCACHER -----";
    private const string FooterMarker = "# ----- END VRCVIDEOCACHER -----";
    private const string ManagedHostLine = "127.0.0.1 localhost.youtube.com";

    private static readonly string Header = $"{Environment.NewLine}{HeaderMarker}{Environment.NewLine}";
    private static readonly string Footer = $"{Environment.NewLine}{FooterMarker}{Environment.NewLine}";
    private static readonly string HostsPath = OperatingSystem.IsWindows()
        ? $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}/drivers/etc/hosts"
        : "/etc/hosts";

    public static void TryRun()
    {
        if (LaunchArgs.AddHost)
        {
            try
            {
                Add();
                Log.Information("Host entry added successfully.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to add host entry");
                Environment.Exit(1);
            }
        }
        if (LaunchArgs.RemoveHost)
        {
            try
            {
                Remove();
                Log.Information("Host entry removed successfully.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove host entry");
                Environment.Exit(1);
            }
        }
    }

    private static void Add()
    {
        CreateHostsIfNotExists();
        var hostsFile = File.ReadAllText(HostsPath);
        var updated = WithManagedBlock(hostsFile);
        if (updated == hostsFile)
            return;

        // Appending leaves the user's existing bytes untouched; only a repair that inserts
        // inside an existing block needs the whole file rewritten.
        if (updated.StartsWith(hostsFile, StringComparison.Ordinal))
            File.AppendAllText(HostsPath, updated[hostsFile.Length..]);
        else
            File.WriteAllText(HostsPath, updated);
    }

    private static void Remove()
    {
        CreateHostsIfNotExists();
        var hostsFile = File.ReadAllText(HostsPath);

        var headerStart = hostsFile.IndexOf(HeaderMarker, StringComparison.Ordinal);
        if (headerStart < 0)
            return;

        // Search for the footer *after* the header. Searching from index 0 meant that a
        // hand-edited file with the footer deleted (IndexOf returns -1) or sitting before
        // the header yielded a negative length, and String.Remove threw
        // ArgumentOutOfRangeException — inside an elevated subprocess, where it is close to
        // invisible and leaves the entry in place.
        var footerStart = hostsFile.IndexOf(FooterMarker, headerStart, StringComparison.Ordinal);
        if (footerStart >= 0)
        {
            var blockEnd = footerStart + FooterMarker.Length;
            File.WriteAllText(HostsPath, hostsFile.Remove(headerStart, blockEnd - headerStart));
            return;
        }

        // No footer, so there is no way to know how far the block was meant to extend, and
        // everything after it may be the user's own entries. Drop only the marker and the
        // single line we manage rather than guessing.
        Log.Warning("Hosts block end marker missing; removing only the start marker and the managed entry.");
        var kept = hostsFile
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.Trim();
                return trimmed != HeaderMarker && trimmed != FooterMarker && trimmed != ManagedHostLine;
            });
        File.WriteAllText(HostsPath, string.Join('\n', kept));
    }

    public static bool IsHostAdded()
    {
        if (!File.Exists(HostsPath))
            return false;

        return HasManagedBlock(File.ReadAllText(HostsPath));
    }

    // The begin marker alone used to count as "added", so a block that had lost its mapping
    // line or end marker was reported as working and Add() refused to repair it. A block only
    // counts when the begin marker, the mapping line and the end marker are all present.
    internal static bool HasManagedBlock(string hostsFile)
    {
        var (_, footer, hasMapping) = LocateBlock(hostsFile.Split('\n'));
        return footer >= 0 && hasMapping;
    }

    /// <summary>
    /// Returns the hosts content with a complete managed block. A missing block is appended;
    /// a malformed one is repaired by inserting only what is missing directly after our own
    /// begin marker, so no line outside our markers is changed or pulled inside them.
    /// </summary>
    internal static string WithManagedBlock(string hostsFile)
    {
        var (header, footer, hasMapping) = LocateBlock(hostsFile.Split('\n'));
        if (header < 0)
            return hostsFile + $"{Header}{ManagedHostLine}{Footer}";
        if (footer >= 0 && hasMapping)
            return hostsFile;

        // Keep the file's own line endings, and make sure the header line is terminated so
        // insertions after it never land on the same line.
        var crlf = hostsFile.Contains("\r\n", StringComparison.Ordinal);
        if (!hostsFile.EndsWith('\n'))
            hostsFile += crlf ? "\r\n" : "\n";

        var lineSuffix = crlf ? "\r" : "";
        var lines = hostsFile.Split('\n').ToList();
        var insertAt = header + 1;
        if (footer >= 0)
        {
            // Markers intact, mapping gone: put it back just inside the begin marker.
            lines.Insert(insertAt, ManagedHostLine + lineSuffix);
        }
        else
        {
            // No end marker, so the block's extent is unknown and everything after the begin
            // marker may be the user's. Close the block straight after the begin marker
            // (keeping a mapping line already sitting directly beneath it).
            if (!IsManagedHostLine(lines[insertAt]))
                lines.Insert(insertAt, ManagedHostLine + lineSuffix);
            lines.Insert(insertAt + 1, FooterMarker + lineSuffix);
        }

        return string.Join('\n', lines);
    }

    // Finds the first begin marker line, the first end marker line after it (-1 if none),
    // and whether the mapping line sits between them. Lines are compared trimmed so CRLF/LF
    // and stray indentation don't matter.
    private static (int Header, int Footer, bool HasMapping) LocateBlock(string[] lines)
    {
        var header = Array.FindIndex(lines, line => line.Trim() == HeaderMarker);
        if (header < 0)
            return (-1, -1, false);

        var hasMapping = false;
        for (var i = header + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == FooterMarker)
                return (header, i, hasMapping);
            if (IsManagedHostLine(lines[i]))
                hasMapping = true;
        }

        return (header, -1, hasMapping);
    }

    // Tolerates tabs or repeated spaces between address and name.
    private static bool IsManagedHostLine(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) == ManagedHostLine;

    private static void CreateHostsIfNotExists()
    {
        if (!File.Exists(HostsPath))
        {
            Log.Information("Hosts file not found at {HostsPath}. Creating a new one with default content.", HostsPath);
            File.WriteAllText(HostsPath, DefaultHostsFile);
        }
    }

    // Default content for the hosts file, based on the standard Windows hosts file.
    // Source: https://support.microsoft.com/en-us/topic/how-to-reset-the-hosts-file-back-to-the-default-c2a43f9d-e176-c6f3-e4ef-3500277a6dae
    private const string DefaultHostsFile = @"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# This file contains the mappings of IP addresses to host names. Each
# entry should be kept on an individual line. The IP address should
# be placed in the first column followed by the corresponding host name.
# The IP address and the host name should be separated by at least one
# space.
#
# Additionally, comments (such as these) may be inserted on individual
# lines or following the machine name denoted by a '#' symbol.
#
# For example:
#
#      102.54.94.97     rhino.acme.com          # source server
#       38.25.63.10     x.acme.com              # x client host
# localhost name resolution is handled within DNS itself.
#    127.0.0.1       localhost
#    ::1             localhost
";
}