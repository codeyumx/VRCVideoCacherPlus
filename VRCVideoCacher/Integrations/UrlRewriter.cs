namespace VRCVideoCacher.Integrations;

/// <summary>
/// Rewrites a URL before any <see cref="Integration"/> looks at it.
///
/// Deliberately not an Integration. Rewriting is a different job: these have no video id to
/// derive, no format arguments to supply and nothing to resolve. Under the single interface
/// this replaced, every rewriter still had to implement CanHandle (returning false) and
/// GetVideoInfo (returning null) purely to satisfy it — three classes carrying two dead
/// members each, and nothing preventing a fourth from implementing them by accident.
///
/// Prefer a Rewrite rule in the Rules tab wherever a regex suffices. A class here is for
/// rewrites that genuinely need code — following a redirect chain, for instance.
/// </summary>
public abstract class UrlRewriter
{
    /// <summary>
    /// Returns the rewritten URL, or the original when this rewriter does not apply.
    /// Rewriters run in order, each seeing the previous one's output.
    /// </summary>
    public abstract Task<string> RewriteAsync(string url, Uri uri);
}
