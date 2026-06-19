using Serilog;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Cheap sanity checks so HTML error pages, redirect stubs, or otherwise
/// truncated downloads don't get promoted into the cache and served forever.
/// </summary>
public static class VideoFileValidator
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(VideoFileValidator));

    // A real video almost never weighs less than this. 166-byte error bodies
    // (the symptom that triggered this guard) are nowhere near it.
    public const long MinValidBytes = 64 * 1024;

    public static bool IsLikelyValidVideo(string filePath, string? contentTypeHint = null)
    {
        if (!File.Exists(filePath)) return false;
        long len;
        try { len = new FileInfo(filePath).Length; }
        catch { return false; }

        // Distinct from a generic "too small" reject: a small body that's actually an
        // HLS playlist is a valid signal to the caller to retry via the HLS path.
        // Don't gate on size first — a 674-byte .m3u8 would otherwise be misclassified.
        if (LooksLikeHlsManifest(filePath, contentTypeHint))
        {
            Log.Warning("Rejecting {Path}: body is an HLS manifest, not a raw video.", filePath);
            return false;
        }

        if (len < MinValidBytes)
        {
            Log.Warning("Rejecting {Path}: {Bytes} bytes is below minimum {Min}.", filePath, len, MinValidBytes);
            return false;
        }

        // If the server declared a binary video content type, trust it. Real video bodies
        // can start with arbitrary bytes (fragmented-MP4 styp/mdat/wide, MPEG-TS 0x47,
        // raw H.264 NALU, etc.) and the markup-sniff below could false-positive on a
        // pathological binary that happens to lead with 0x3C. Size check above already
        // weeds out the 166-byte error bodies that motivated the validator.
        var ct = contentTypeHint?.ToLowerInvariant() ?? string.Empty;
        if (ct.StartsWith("video/") || ct.StartsWith("application/mp4"))
            return true;

        // Inverted check: enumerating every possible video container header is brittle.
        // Reject only on a small set of clearly-bad bodies — HTML / JSON / XML error
        // pages. Anything ≥ 64 KB that isn't one of those passes; the player is the
        // final arbiter on actual playability.
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> buf = stackalloc byte[256];
            var read = fs.Read(buf);
            if (read <= 0)
            {
                Log.Warning("Rejecting {Path}: empty read.", filePath);
                return false;
            }
            var sample = buf[..read];

            // Strip UTF-8 BOM (only at offset 0, and only the full 3-byte sequence) plus
            // leading ASCII whitespace, so a "\n<html>" can't bypass the check. Don't
            // skip lone 0xEF/0xBB/0xBF bytes — those occur naturally in binary.
            var i = 0;
            if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF) i = 3;
            while (i < sample.Length && sample[i] is 0x20 or 0x09 or 0x0A or 0x0D) i++;
            if (i >= sample.Length) return true;

            if (StartsWithCi(sample, i, "<!doctype") ||
                StartsWithCi(sample, i, "<html") ||
                StartsWithCi(sample, i, "<head") ||
                StartsWithCi(sample, i, "<?xml") ||
                StartsWithCi(sample, i, "<error") ||
                StartsWithCi(sample, i, "<response"))
            {
                LogRejectionWithPreview(filePath, len, ct, sample, "HTML/XML markup");
                return false;
            }

            if (sample[i] == (byte)'{' && LooksLikeJsonError(sample, i))
            {
                LogRejectionWithPreview(filePath, len, ct, sample, "JSON error envelope");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("Validation read failed for {Path}: {Err}", filePath, ex.Message);
            return false;
        }
    }

    private static void LogRejectionWithPreview(string filePath, long len, string ct, ReadOnlySpan<byte> sample, string reason)
    {
        var hexLen = Math.Min(32, sample.Length);
        var hex = Convert.ToHexString(sample[..hexLen]);
        var ascii = new System.Text.StringBuilder(hexLen);
        for (var k = 0; k < hexLen; k++)
        {
            var b = sample[k];
            ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
        }
        Log.Warning("Rejecting {Path} ({Bytes} bytes, Content-Type='{Ct}'): {Reason}. Head[hex]={Hex} ascii=\"{Ascii}\"",
            filePath, len, ct, reason, hex, ascii.ToString());
    }

    private static bool StartsWithCi(ReadOnlySpan<byte> sample, int offset, string prefix)
    {
        if (sample.Length - offset < prefix.Length) return false;
        for (var k = 0; k < prefix.Length; k++)
        {
            var b = sample[offset + k];
            // ASCII case-insensitive compare
            if (b >= 'A' && b <= 'Z') b = (byte)(b + 32);
            if (b != (byte)char.ToLowerInvariant(prefix[k])) return false;
        }
        return true;
    }

    private static bool LooksLikeJsonError(ReadOnlySpan<byte> sample, int offset)
    {
        // Very rough: a JSON body that starts with { and contains "error" or "message"
        // in the first 256 bytes is almost certainly an error envelope, not video.
        var asString = System.Text.Encoding.ASCII.GetString(sample[offset..]).ToLowerInvariant();
        return asString.Contains("\"error\"") || asString.Contains("\"message\"") || asString.Contains("\"status\"");
    }

    /// <summary>
    /// Sniffs the first bytes for an HLS playlist signature (#EXTM3U) or the
    /// vendor-specific Content-Type. Used so a small manifest body can be
    /// distinguished from an actually-corrupt download.
    /// </summary>
    public static bool LooksLikeHlsManifest(string filePath, string? contentTypeHint = null)
    {
        var ct = contentTypeHint?.ToLowerInvariant() ?? string.Empty;
        if (ct.StartsWith("application/vnd.apple.mpegurl") || ct.StartsWith("application/x-mpegurl"))
            return true;

        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> buf = stackalloc byte[64];
            var read = fs.Read(buf);
            if (read <= 0) return false;
            var sample = buf[..read];
            var i = 0;
            if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF) i = 3;
            while (i < sample.Length && sample[i] is 0x20 or 0x09 or 0x0A or 0x0D) i++;
            return StartsWithCi(sample, i, "#EXTM3U");
        }
        catch { return false; }
    }

    /// <summary>
    /// Some servers omit Content-Type entirely; treat that as "unknown, let the magic-byte
    /// check decide" rather than rejecting up-front.
    /// </summary>
    public static bool IsAcceptableContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return true;
        var ct = contentType.ToLowerInvariant();
        return ct.StartsWith("video/")
               || ct.StartsWith("application/octet-stream")
               || ct.StartsWith("application/mp4")
               || ct.StartsWith("application/x-mpegurl")
               || ct.StartsWith("application/vnd.apple.mpegurl")
               || ct.StartsWith("binary/");
    }

    /// <summary>
    /// Best-effort delete; never throws.
    /// </summary>
    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warning("Failed to delete {Path}: {Err}", path, ex.Message); }
    }
}
