using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// IsHostAdded used to report success from the begin marker alone, so a block missing its
// mapping line or end marker looked healthy and Add() refused to repair it. These tests pin
// the "complete block" rule and that repairs only touch our own block.
public class HostsManagerTests
{
    private const string Begin = "# ----- BEGIN VRCVIDEOCACHER -----";
    private const string End = "# ----- END VRCVIDEOCACHER -----";
    private const string Mapping = "127.0.0.1 localhost.youtube.com";
    private const string UserLine = "10.0.0.5 nas.local";

    private static string Lf(params string[] lines) => string.Join('\n', lines) + "\n";
    private static string Crlf(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    [Fact]
    public void CompleteBlockIsAdded()
    {
        Assert.True(HostsManager.HasManagedBlock(Lf(UserLine, Begin, Mapping, End)));
        Assert.True(HostsManager.HasManagedBlock(Crlf(Begin, Mapping, End, UserLine)));
    }

    [Fact]
    public void MappingWithTabsCounts()
    {
        Assert.True(HostsManager.HasManagedBlock(Lf(Begin, "127.0.0.1\tlocalhost.youtube.com", End)));
    }

    [Fact]
    public void MarkerWithoutMappingIsNotAdded()
    {
        Assert.False(HostsManager.HasManagedBlock(Lf(Begin, End)));
    }

    [Fact]
    public void MarkerWithoutFooterIsNotAdded()
    {
        Assert.False(HostsManager.HasManagedBlock(Lf(Begin, Mapping)));
    }

    [Fact]
    public void MappingOutsideMarkersDoesNotCount()
    {
        Assert.False(HostsManager.HasManagedBlock(Lf(Mapping, Begin, End)));
    }

    [Fact]
    public void NoBlockIsNotAdded()
    {
        Assert.False(HostsManager.HasManagedBlock(Lf(UserLine)));
    }

    [Fact]
    public void AppendsBlockWhenAbsent()
    {
        var original = Lf(UserLine);

        var updated = HostsManager.WithManagedBlock(original);

        Assert.StartsWith(original, updated);
        Assert.True(HostsManager.HasManagedBlock(updated));
    }

    [Fact]
    public void CompleteBlockIsLeftUnchanged()
    {
        var original = Lf(UserLine, Begin, Mapping, End);

        Assert.Equal(original, HostsManager.WithManagedBlock(original));
    }

    [Fact]
    public void RestoresMissingMappingInsideMarkers()
    {
        var updated = HostsManager.WithManagedBlock(Lf(UserLine, Begin, End, UserLine));

        Assert.Equal(Lf(UserLine, Begin, Mapping, End, UserLine), updated);
    }

    [Fact]
    public void ClosesBlockMissingFooterWithoutCapturingUserLines()
    {
        var updated = HostsManager.WithManagedBlock(Lf(Begin, Mapping, UserLine));

        Assert.Equal(Lf(Begin, Mapping, End, UserLine), updated);
    }

    [Fact]
    public void RepairsHeaderOnlyBlock()
    {
        var updated = HostsManager.WithManagedBlock(Lf(UserLine, Begin, UserLine));

        Assert.Equal(Lf(UserLine, Begin, Mapping, End, UserLine), updated);
    }

    [Fact]
    public void RepairKeepsCrlfLineEndings()
    {
        var updated = HostsManager.WithManagedBlock(Crlf(Begin, End));

        Assert.Equal(Crlf(Begin, Mapping, End), updated);
    }

    [Fact]
    public void RepairTerminatesUnterminatedHeaderLine()
    {
        var updated = HostsManager.WithManagedBlock(UserLine + "\n" + Begin);

        Assert.Equal(Lf(UserLine, Begin, Mapping, End), updated);
    }
}
