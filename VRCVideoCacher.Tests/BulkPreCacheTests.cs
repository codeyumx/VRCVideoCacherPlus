using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// PreCacheUrls is a list of independent mirrors. The original bug was a `return` where a
// `continue` belonged, so a single dead host silently cancelled every manifest after it —
// with no error, just missing videos. These tests exist to keep that loop control honest:
// each one asserts that a later manifest is still reached after an earlier one fails.
public class BulkPreCacheTests
{
    private static Task<string?> Manifest(params string[] fileNames)
    {
        var entries = fileNames.Select(f =>
            $$"""{"fileName":"{{f}}","url":"https://example.com/{{f}}","lastModified":0,"size":0}""");
        return Task.FromResult<string?>($"[{string.Join(',', entries)}]");
    }

    // Records which manifests reached the download stage, in order.
    private static (Func<List<BulkPreCache.DownloadInfo>, Task> Downloader, List<string> Downloaded) Recorder()
    {
        var downloaded = new List<string>();
        return (files =>
        {
            downloaded.AddRange(files.Select(f => f.FileName));
            return Task.CompletedTask;
        }, downloaded);
    }

    [Fact]
    public async Task ProcessesEveryManifest()
    {
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["a", "b"],
            url => url == "a" ? Manifest("a.mp4") : Manifest("b.mp4"),
            downloader);

        Assert.Equal(["a.mp4", "b.mp4"], downloaded);
    }

    [Fact]
    public async Task ContinuesAfterFailedFetch()
    {
        // A null return is the "non-success status code" path — the original `return` bug.
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["bad", "good"],
            url => url == "bad" ? Task.FromResult<string?>(null) : Manifest("good.mp4"),
            downloader);

        Assert.Equal(["good.mp4"], downloaded);
    }

    [Fact]
    public async Task ContinuesAfterEmptyManifest()
    {
        // The second `return` bug: an empty list is a legitimate manifest, not a stop signal.
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["empty", "good"],
            url => url == "empty" ? Task.FromResult<string?>("[]") : Manifest("good.mp4"),
            downloader);

        Assert.Equal(["good.mp4"], downloaded);
    }

    [Fact]
    public async Task ContinuesAfterThrowingFetch()
    {
        // An unreachable host throws rather than returning a status code. Before the
        // refactor this escaped the loop entirely and killed the remaining manifests.
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["dns-failure", "good"],
            url => url == "dns-failure"
                ? throw new HttpRequestException("no such host")
                : Manifest("good.mp4"),
            downloader);

        Assert.Equal(["good.mp4"], downloaded);
    }

    [Fact]
    public async Task ContinuesAfterMalformedJson()
    {
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["garbage", "good"],
            url => url == "garbage"
                ? Task.FromResult<string?>("not json at all")
                : Manifest("good.mp4"),
            downloader);

        Assert.Equal(["good.mp4"], downloaded);
    }

    [Fact]
    public async Task FailureInTheMiddleStillReachesTheLast()
    {
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests(
            ["a", "bad", "c"],
            url => url switch
            {
                "a" => Manifest("a.mp4"),
                "bad" => Task.FromResult<string?>(null),
                _ => Manifest("c.mp4")
            },
            downloader);

        Assert.Equal(["a.mp4", "c.mp4"], downloaded);
    }

    [Fact]
    public async Task NoUrlsIsANoOp()
    {
        var (downloader, downloaded) = Recorder();

        await BulkPreCache.ProcessManifests([], _ => Manifest("never.mp4"), downloader);

        Assert.Empty(downloaded);
    }

    // Manifest names are untrusted and the publish step overwrites, so only the cache's own
    // "<videoId>.mp4|webm" form may get through — not traversal, and not other files that
    // live in the cache folder such as index.html.
    [Theory]
    [InlineData("--QOnlGckhs.mp4")]
    [InlineData("dQw4w9WgXcQ.webm")]
    [InlineData("abc_DEF-123.MP4")]
    [InlineData("4fGh7Jk9LmNoPqRsTuVwXyZ0aBcDeFgHiJkLmNoPq.mp4")]
    public void AcceptsVideoCacheFileNames(string fileName)
    {
        Assert.True(BulkPreCache.IsSafeCacheFileName(fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("index.html")]
    [InlineData("video")]
    [InlineData(".mp4")]
    [InlineData("../evil.mp4")]
    [InlineData("..\\evil.mp4")]
    [InlineData("C:evil.mp4")]
    [InlineData("/etc/evil.mp4")]
    [InlineData("_tempVideo.abc.mp4")]
    [InlineData("abc.mp4.exe")]
    [InlineData("a b.mp4")]
    [InlineData("abc.mp4 ")]
    public void RejectsOtherFileNames(string? fileName)
    {
        Assert.False(BulkPreCache.IsSafeCacheFileName(fileName));
    }

    [Fact]
    public void RejectsOverlongFileName()
    {
        Assert.False(BulkPreCache.IsSafeCacheFileName(new string('a', 200) + ".mp4"));
    }
}
