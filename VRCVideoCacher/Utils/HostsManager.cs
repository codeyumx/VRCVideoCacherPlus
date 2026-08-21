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
        if (Environment.CommandLine.Contains("--addhost"))
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
        if (Environment.CommandLine.Contains("--removehost"))
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
        if (hostsFile.Contains(HeaderMarker, StringComparison.Ordinal))
            return;

        File.AppendAllText(HostsPath, $"{Header}{ManagedHostLine}{Footer}");
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

        var hostsFile = File.ReadAllText(HostsPath);
        return hostsFile.Contains(HeaderMarker, StringComparison.Ordinal);
    }

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