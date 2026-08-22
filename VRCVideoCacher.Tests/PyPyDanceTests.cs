using VRCVideoCacher.Integrations.PyPyDance;
using Xunit;

namespace VRCVideoCacher.Tests;

// The cache file is named after the id derived here, so getting it wrong does not fail
// loudly — it makes different videos collide on one file and serves the wrong one back.
public class PyPyDanceTests
{
    [Theory]
    [InlineData("http://cdn.pypy.dance/qylu4Ajh6k8.mp4", "qylu4Ajh6k8")]
    [InlineData("https://cdn.pypy.dance/fquYjLA_f28.mp4", "fquYjLA_f28")]
    [InlineData("http://cdn.pypy.dance/eCN__1XLgng.mp4", "eCN__1XLgng")]
    // No extension: the whole segment is the id.
    [InlineData("http://cdn.pypy.dance/abc123", "abc123")]
    // Only the part before the first dot.
    [InlineData("http://cdn.pypy.dance/abc.def.mp4", "abc")]
    public void DeriveVideoId_TakesTheCdnFileName(string url, string expected)
    {
        Assert.Equal(expected, PyPyDanceIntegration.DeriveVideoId(new Uri(url)));
    }

    [Fact]
    public void DeriveVideoId_RejectsTheUnredirectedApiEndpoint()
    {
        // The regression this guards: api.pypy.dance answers 302 to a plain-http CDN URL,
        // and HttpClient will not follow an https -> http downgrade. The old code read the
        // file name off the *original* URL and got "video" — for every single track, so
        // they all shared one cache entry.
        Assert.Null(PyPyDanceIntegration.DeriveVideoId(new Uri("https://api.pypy.dance/video?id=1")));
        Assert.Null(PyPyDanceIntegration.DeriveVideoId(new Uri("http://api.pypy.dance/video")));
    }

    [Theory]
    [InlineData("http://cdn.pypy.dance/")]
    [InlineData("http://cdn.pypy.dance")]
    public void DeriveVideoId_RejectsAUrlWithNoFileName(string url)
    {
        Assert.Null(PyPyDanceIntegration.DeriveVideoId(new Uri(url)));
    }

    [Theory]
    [InlineData("https://api.pypy.dance/video?id=1", true)]
    [InlineData("http://api.pypy.dance/video?id=1", true)]
    [InlineData("HTTPS://API.PYPY.DANCE/video?id=1", true)]
    [InlineData("https://api.pypy.dance/bundle", false)]
    [InlineData("https://example.com/video?id=1", false)]
    public void CanHandle_MatchesOnlyTheVideoEndpoint(string url, bool expected)
    {
        Assert.Equal(expected, new PyPyDanceIntegration().CanHandle(new Uri(url)));
    }
}
