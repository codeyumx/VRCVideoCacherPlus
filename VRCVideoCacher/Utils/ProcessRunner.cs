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
        return new ProcessResult(output.Trim(), error.Trim(), process.ExitCode);
    }
}
