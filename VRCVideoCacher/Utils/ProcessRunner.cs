using System.Diagnostics;
using System.Text;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Runs a child process to completion and captures both of its output streams.
///
/// Both pipes have to be drained concurrently. Reading stdout to the end and only then
/// reading stderr deadlocks as soon as the child fills the pipe buffer (~64 KB on both
/// Windows and Linux) on the stream nobody is reading yet: the child blocks writing, so it
/// never closes stdout, so the parent never stops waiting. yt-dlp is more than capable of
/// producing that much stderr on a bad extraction, and when it happens the calling HTTP
/// request never returns at all.
/// </summary>
public static class ProcessRunner
{
    public readonly record struct ProcessResult(string Output, string Error, int ExitCode);

    /// <summary>
    /// Runs <paramref name="fileName"/> with each element of <paramref name="arguments"/>
    /// passed as one argv entry. Prefer this over building a command-line string: quoting
    /// is then the runtime's problem, and untrusted values cannot inject extra arguments.
    /// </summary>
    public static Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return RunAsync(startInfo, ct);
    }

    /// <summary>
    /// Starts <paramref name="startInfo"/> with both streams redirected, drains them in
    /// parallel with the wait, and returns the trimmed output once the process exits.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct = default)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.StandardOutputEncoding ??= Encoding.UTF8;
        startInfo.StandardErrorEncoding ??= Encoding.UTF8;

        if (OperatingSystem.IsLinux() && startInfo.EnvironmentVariables.ContainsKey("LD_PRELOAD"))
        {
            var ldPreload = startInfo.EnvironmentVariables["LD_PRELOAD"];
            if (!string.IsNullOrEmpty(ldPreload) && ldPreload.Contains("libextest.so"))
            {
                startInfo.EnvironmentVariables.Remove("LD_PRELOAD");
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Start both reads before awaiting the exit — that ordering is the whole point.
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        if (!string.IsNullOrEmpty(error))
        {
            var lines = error.Split('\n')
                .Where(l => !l.Contains("wrong ELF class") && !l.Contains("libextest.so"))
                .Select(l => l.TrimEnd('\r'));
            error = string.Join("\n", lines);
        }

        return new ProcessResult(output.Trim(), error.Trim(), process.ExitCode);
    }
}
