using System.Security.Cryptography;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// SHA-256 helpers for checking downloaded binaries against the digest GitHub publishes
/// alongside each release asset.
///
/// Everything this application downloads into the utils directory is subsequently marked
/// executable and run — yt-dlp, Deno and FFmpeg all are — so TLS alone is a thin guarantee.
/// </summary>
public static class FileHash
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(FileHash));

    public static async Task<string> ComputeSha256HexAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Verifies <paramref name="path"/> against a GitHub asset digest, which has the form
    /// "sha256:&lt;hex&gt;".
    ///
    /// Returns true on a match, and also when GitHub published no digest at all: older
    /// assets and third-party mirrors have none, and refusing those outright would break
    /// tool updates entirely. That case is logged as a warning so it stays visible rather
    /// than silently degrading to no verification.
    /// </summary>
    public static async Task<bool> VerifyGitHubDigestAsync(string path, string? digest, string label)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            Log.Warning("{Label}: no digest published for this asset, download cannot be verified.", label);
            return true;
        }

        var separator = digest.IndexOf(':');
        var expected = separator >= 0 ? digest[(separator + 1)..] : digest;
        var actual = await ComputeSha256HexAsync(path);

        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("{Label}: digest verified.", label);
            return true;
        }

        Log.Error("{Label}: digest mismatch — expected {Expected}, got {Actual}. Discarding the download.",
            label, expected, actual);
        return false;
    }
}
