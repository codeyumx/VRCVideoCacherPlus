using VRCVideoCacher.Models;

namespace VRCVideoCacher.Integrations;

/// <summary>
/// Site-specific handling that a URL rule cannot express.
///
/// The Rules tab already covers matching, rewriting, redirecting and blocking by pattern,
/// and that is the right tool whenever a regex is enough. An Integration exists for what
/// comes after: deriving a stable video id that names the cached file, canonicalising a URL
/// so history and the cache agree, calling a site's own API for a title, and choosing the
/// yt-dlp format arguments that site needs.
///
/// If a site needs none of that, it needs a rule, not a class here.
/// </summary>
public abstract class Integration
{
    /// <summary>
    /// The identifier a rule's Integration field binds to, letting a rule force a URL
    /// through this integration regardless of what <see cref="CanHandle"/> says. Null means
    /// it is not selectable from a rule and is reached only by CanHandle.
    ///
    /// These names are persisted in the user's rule list: renaming one silently detaches
    /// every saved rule that referenced it.
    /// </summary>
    public virtual string? Name => null;

    /// <summary>
    /// Whether this integration claims the URL on shape alone. Must not touch the network —
    /// it is called for every integration on every request.
    /// </summary>
    public abstract bool CanHandle(Uri uri);

    /// <summary>
    /// Establishes the cache identity for a URL: the video id that names the cached file,
    /// the canonical URL to record, the type, and the container to download. Returns null
    /// when the URL turns out not to point at anything playable.
    /// </summary>
    public abstract Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro);

    /// <summary>
    /// Extra yt-dlp arguments for this site — format selection, client impersonation.
    /// One argv token per element, never pre-quoted; see YtdlManager.GenerateYtdlArgs.
    /// </summary>
    public virtual List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];
}
