using System.Collections.Concurrent;

namespace VRCVideoCacher.Services;

// Which browsers the VRCVideoCacherPlus extension is currently talking to the app from. The
// extension's refresh long-poll returns every 25 s and is re-opened at once, and its failure
// backoff and keep-alive alarm both cap at 60 s, so a browser unseen for longer than the
// window has closed, lost the extension, or can't reach the app. The old extension never
// long-polls, so it cannot be tracked here.
public static class ExtensionPresence
{
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(90);

    private static readonly ConcurrentDictionary<string, DateTime> LastSeenUtc = new();

    // Raised when a browser appears (or reappears after dropping out of the window). Going
    // stale raises nothing; readers re-check Connected() on their own schedule.
    public static event Action? Changed;

    public static void Seen(string browser)
    {
        var now = DateTime.UtcNow;
        var wasConnected = LastSeenUtc.TryGetValue(browser, out var previous) && now - previous <= ConnectedWindow;
        LastSeenUtc[browser] = now;
        if (!wasConnected)
            Changed?.Invoke();
    }

    public static IReadOnlyList<string> Connected()
    {
        var now = DateTime.UtcNow;
        return LastSeenUtc
            .Where(entry => now - entry.Value <= ConnectedWindow)
            .Select(entry => entry.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
