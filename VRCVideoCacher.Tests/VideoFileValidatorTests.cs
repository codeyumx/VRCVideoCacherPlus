using System.Text;
using VRCVideoCacher.YTDL;
using Xunit;

namespace VRCVideoCacher.Tests;

// The validator is what stops an HTML error page served with a 200 from being committed to
// the cache under a .mp4 name and then handed to VRChat on every subsequent play.
public class VideoFileValidatorTests : IDisposable
{
    private readonly List<string> _paths = [];

    private string WriteFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vvc-validator-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, content);
        _paths.Add(path);
        return path;
    }

    private string WriteFile(string content) => WriteFile(Encoding.UTF8.GetBytes(content));

    /// <summary>A body that clears the minimum-size bar, padded with plausible binary.</summary>
    private string WriteLargeFile(string prefix)
    {
        var bytes = new byte[VideoFileValidator.MinValidBytes + 1024];
        Encoding.UTF8.GetBytes(prefix).CopyTo(bytes, 0);
        return WriteFile(bytes);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RejectsAMissingFile() =>
        Assert.False(VideoFileValidator.IsLikelyValidVideo(Path.Combine(Path.GetTempPath(), "vvc-does-not-exist")));

    [Fact]
    public void RejectsATinyErrorBody() =>
        Assert.False(VideoFileValidator.IsLikelyValidVideo(WriteFile("not a video, 166 bytes of nothing")));

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Error</body></html>")]
    [InlineData("<html><head><title>404</title></head></html>")]
    [InlineData("<?xml version=\"1.0\"?><Error><Code>AccessDenied</Code></Error>")]
    [InlineData("<Error><Message>nope</Message></Error>")]
    public void RejectsMarkupEvenAboveTheSizeFloor(string markup) =>
        Assert.False(VideoFileValidator.IsLikelyValidVideo(WriteLargeFile(markup)));

    [Fact]
    public void RejectsMarkupPrecededByWhitespaceOrABom()
    {
        // "\n<html>" must not slip past a naive prefix check.
        Assert.False(VideoFileValidator.IsLikelyValidVideo(WriteLargeFile("\n\r\t  <html><body>x</body></html>")));
        Assert.False(VideoFileValidator.IsLikelyValidVideo(WriteLargeFile("﻿<!doctype html>")));
    }

    [Fact]
    public void RejectsAJsonErrorEnvelope() =>
        Assert.False(VideoFileValidator.IsLikelyValidVideo(WriteLargeFile("{\"error\":\"forbidden\",\"code\":403}")));

    [Fact]
    public void AcceptsBinaryAboveTheSizeFloor()
    {
        // The check is inverted on purpose: real containers start with arbitrary bytes, so
        // anything large that is not recognisably markup or a JSON error passes.
        var bytes = new byte[VideoFileValidator.MinValidBytes + 1024];
        bytes[0] = 0x00; bytes[1] = 0x00; bytes[2] = 0x00; bytes[3] = 0x20;
        bytes[4] = (byte)'f'; bytes[5] = (byte)'t'; bytes[6] = (byte)'y'; bytes[7] = (byte)'p';
        Assert.True(VideoFileValidator.IsLikelyValidVideo(WriteFile(bytes)));
    }

    [Fact]
    public void TrustsADeclaredVideoContentType()
    {
        // A binary body that happens to open with something the markup sniff would reject
        // still passes when the server explicitly declared it as video.
        var path = WriteLargeFile("<html><body>looks like markup</body></html>");
        Assert.False(VideoFileValidator.IsLikelyValidVideo(path));
        Assert.True(VideoFileValidator.IsLikelyValidVideo(path, "video/mp4"));
        Assert.True(VideoFileValidator.IsLikelyValidVideo(path, "application/mp4"));
    }

    [Fact]
    public void OnlyRejectsRecognisedMarkupPrefixes()
    {
        // The check is a deliberately small deny-list, not "anything starting with '<'":
        // real containers can begin with arbitrary bytes and must not be thrown away.
        Assert.True(VideoFileValidator.IsLikelyValidVideo(WriteLargeFile("<not-a-known-markup-prefix")));
    }

    [Fact]
    public void DetectsAnHlsManifestRegardlessOfSize()
    {
        // Distinct from "too small": a manifest tells the caller to retry via the HLS path.
        var path = WriteFile("#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:9.0,\nseg0.ts\n#EXT-X-ENDLIST\n");
        Assert.True(VideoFileValidator.LooksLikeHlsManifest(path));
        Assert.False(VideoFileValidator.IsLikelyValidVideo(path));
    }

    [Fact]
    public void DetectsAnHlsManifestFromContentTypeAlone() =>
        Assert.True(VideoFileValidator.LooksLikeHlsManifest(WriteFile("whatever"), "application/vnd.apple.mpegurl"));

    [Theory]
    [InlineData(null, true)]              // absent: let the magic-byte check decide
    [InlineData("", true)]
    [InlineData("video/mp4", true)]
    [InlineData("application/octet-stream", true)]
    [InlineData("binary/octet-stream", true)]
    [InlineData("text/html", false)]
    [InlineData("application/json", false)]
    public void IsAcceptableContentType_GatesOnTheDeclaredType(string? contentType, bool expected) =>
        Assert.Equal(expected, VideoFileValidator.IsAcceptableContentType(contentType));

    [Fact]
    public void TryDelete_NeverThrows()
    {
        var exception = Record.Exception(() => VideoFileValidator.TryDelete(Path.Combine(Path.GetTempPath(), "vvc-absent")));
        Assert.Null(exception);
    }
}
