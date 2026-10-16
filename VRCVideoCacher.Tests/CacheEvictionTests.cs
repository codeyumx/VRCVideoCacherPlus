using Xunit;

namespace VRCVideoCacher.Tests;

// Eviction used to check "is it pinned?" and then delete without holding anything, so a
// downloader could pin and publish a new file in between and have it deleted. Pinning now
// shares a lock with eviction's check-and-delete; these tests pin that contract down.
public class CacheEvictionTests
{
    private static string UniqueName() => $"{Guid.NewGuid():N}.mp4";

    [Fact]
    public void APinnedFileStaysPinnedUntilEveryHandleIsDisposed()
    {
        var name = UniqueName();

        var first = CacheFilePins.Acquire(name);
        var second = CacheFilePins.Acquire(name);
        Assert.True(CacheFilePins.IsPinned(name));

        first.Dispose();
        first.Dispose(); // a second dispose must not release the other holder's pin
        Assert.True(CacheFilePins.IsPinned(name));

        second.Dispose();
        Assert.False(CacheFilePins.IsPinned(name));
    }

    [Fact]
    public void PinsAreTrackedPerFile()
    {
        var pinned = UniqueName();
        var other = UniqueName();

        using (CacheFilePins.Acquire(pinned))
        {
            Assert.True(CacheFilePins.IsPinned(pinned));
            Assert.False(CacheFilePins.IsPinned(other));
        }
    }

    [Fact]
    public async Task PinningWaitsWhileEvictionHoldsTheLock()
    {
        var name = UniqueName();
        Task<IDisposable> pinTask;

        lock (CacheFilePins.SyncRoot)
        {
            Assert.False(CacheFilePins.IsPinned(name));

            pinTask = Task.Run(() => CacheFilePins.Acquire(name));

            // The pin must not land between eviction's check and its delete.
            Assert.False(pinTask.Wait(TimeSpan.FromMilliseconds(200)));
            Assert.False(CacheFilePins.IsPinned(name));
        }

        using var pin = await pinTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(CacheFilePins.IsPinned(name));
    }
}
