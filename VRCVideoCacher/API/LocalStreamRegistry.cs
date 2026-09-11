using System.Collections.Concurrent;
using EmbedIO;
using Serilog;

namespace VRCVideoCacher.API;

/// <summary>
/// Tracks the cached-video responses this server is currently streaming, so they can be cut
/// off when video playback is blocked.
///
/// This is the reliable half of severing: the socket is ours, so closing it needs no
/// privileges and works identically on every platform.
///
/// EmbedIO gives a module no "request finished" callback when it is not the final handler,
/// so completion cannot be observed directly. The previous version simply never removed
/// anything, which made this an unbounded leak — one retained IHttpContext per video
/// request, for the lifetime of the process, and far worse for HLS where every segment is
/// its own request. Instead, entries are pruned by liveness: once EmbedIO has finished a
/// response it disposes the output stream, so a stream that can no longer be written to is
/// a stream that is done.
/// </summary>
public static class LocalStreamRegistry
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(LocalStreamRegistry));

    private sealed record Entry(IHttpContext Context, string Path, DateTime StartedAt);

    private static readonly ConcurrentDictionary<Guid, Entry> Streams = new();

    /// <summary>
    /// Backstop for anything the liveness check somehow misses. No legitimate cached-video
    /// response stays open for hours.
    /// </summary>
    private static readonly TimeSpan MaxStreamAge = TimeSpan.FromHours(6);

    public static int Count => Streams.Count;

    public static void Register(IHttpContext context, string path)
    {
        // Pruning here rather than on a timer keeps the dictionary bounded by the number of
        // genuinely concurrent streams, at the cost of a cheap sweep per media request.
        Prune();
        Streams[Guid.NewGuid()] = new Entry(context, path, DateTime.UtcNow);
    }

    /// <summary>
    /// Closes every stream currently being served and returns how many were actually live.
    /// Entries that had already finished are discarded rather than counted, so the number
    /// reported to the user reflects what was really interrupted.
    /// </summary>
    public static int CloseAll()
    {
        var closed = 0;

        foreach (var key in Streams.Keys.ToList())
        {
            if (!Streams.TryRemove(key, out var entry))
                continue;

            if (!IsLive(entry))
                continue;

            if (Close(entry))
                closed++;
        }

        return closed;
    }

    private static bool Close(Entry entry)
    {
        try
        {
            entry.Context.Response.OutputStream.Close();
            return true;
        }
        catch (Exception ex)
        {
            // Racing normal completion is expected and not worth surfacing.
            Log.Debug("Could not close stream for {Path}: {Error}", entry.Path, ex.Message);
            return false;
        }
    }

    private static void Prune()
    {
        foreach (var pair in Streams)
        {
            if (IsLive(pair.Value) && DateTime.UtcNow - pair.Value.StartedAt < MaxStreamAge)
                continue;

            Streams.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>
    /// A finished response has had its output stream disposed by EmbedIO, which makes it
    /// unwritable — and touching a disposed stream throws, which is equally conclusive.
    /// </summary>
    private static bool IsLive(Entry entry)
    {
        try
        {
            return entry.Context.Response.OutputStream.CanWrite;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Registers cached-video responses with <see cref="LocalStreamRegistry"/> as they start.
/// Passes every request straight through — it observes, it does not serve.
/// </summary>
public class ActiveStreamModule : WebModuleBase
{
    private static readonly string[] MediaExtensions = [".mp4", ".webm", ".m3u8", ".ts"];

    public ActiveStreamModule() : base("/")
    {
    }

    public override bool IsFinalHandler => false;

    protected override Task OnRequestAsync(IHttpContext context)
    {
        var path = context.Request.Url.AbsolutePath;

        if (MediaExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            LocalStreamRegistry.Register(context, path);

        return Task.CompletedTask;
    }
}
