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
/// Entries are removed through <see cref="IHttpContext.OnClose"/> when EmbedIO finishes
/// processing the request, so everything still registered is a response in flight. Output
/// stream state cannot be used for this: EmbedIO's stream reports CanWrite regardless of
/// whether the response is complete.
/// </summary>
public static class LocalStreamRegistry
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(LocalStreamRegistry));

    private sealed record Entry(IHttpContext Context, string Path, DateTime StartedAt);

    private static readonly ConcurrentDictionary<Guid, Entry> Streams = new();

    /// <summary>
    /// Backstop in case a close callback never fires. No legitimate cached-video response
    /// stays open for hours.
    /// </summary>
    private static readonly TimeSpan MaxStreamAge = TimeSpan.FromHours(6);

    public static int Count => Streams.Count;

    public static void Register(IHttpContext context, string path)
    {
        Prune();

        var id = Guid.NewGuid();
        Streams[id] = new Entry(context, path, DateTime.UtcNow);
        context.OnClose(ctx => Streams.TryRemove(id, out _));
    }

    /// <summary>
    /// Closes every stream currently being served and returns how many were actually closed.
    /// </summary>
    public static int CloseAll()
    {
        var closed = 0;

        foreach (var key in Streams.Keys.ToList())
        {
            if (Streams.TryRemove(key, out var entry) && Close(entry))
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
            if (DateTime.UtcNow - pair.Value.StartedAt >= MaxStreamAge)
                Streams.TryRemove(pair.Key, out _);
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
