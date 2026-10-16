using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// The YouTube cookie jar is written through AtomicFile with an owner-only mode. If the mode
// stops being applied, every other local user can read a live YouTube session.
public class AtomicFileTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _dir = Path.Join(Path.GetTempPath(), "atomicfile-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ReplacesWorldReadableFileWithOwnerOnlyOne()
    {
        if (OperatingSystem.IsWindows())
            return;

        var path = Path.Join(_dir, "cookies.txt");
        File.WriteAllText(path, "old");
        File.SetUnixFileMode(path, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        AtomicFile.WriteAllText(path, "new", OwnerOnly);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
    }

    [Fact]
    public void StaleTempFileDoesNotWidenTheMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        // A temp left behind by a crash keeps its own permissions if it is reused as is.
        var path = Path.Join(_dir, "cookies.txt");
        File.WriteAllText(path + ".tmp", "stale");
        File.SetUnixFileMode(path + ".tmp", OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        AtomicFile.WriteAllText(path, "new", OwnerOnly);

        Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WritesContentsWithoutLeavingTempFile()
    {
        var path = Path.Join(_dir, "cookies.txt");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new", OwnerOnly);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
