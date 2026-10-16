using System.Diagnostics;
using Xunit;

namespace VRCVideoCacher.Tests;

// --kill-existing-instance finds the running instance by process name. The binary was
// renamed, and Linux truncates process names to 15 characters, so both the current and the
// legacy name must match, including the truncated form.
public class KillExistingInstanceTests
{
    private static readonly string[] Names = ["VRCVideoCacherPlus", "VRCVideoCacher"];

    [Theory]
    [InlineData("VRCVideoCacherPlus")]
    [InlineData("vrcvideocacherplus")]
    [InlineData("VRCVideoCacher")]
    [InlineData("VRCVideoCacherP")] // Linux comm truncation of the Plus name
    public void MatchesImageName_AcceptsCurrentLegacyAndTruncatedNames(string processName) =>
        Assert.True(Program.MatchesImageName(processName, Names));

    [Theory]
    [InlineData("VRCVideoCacherX")]
    [InlineData("VRCVideoCach")]
    [InlineData("VRChat")]
    [InlineData("")]
    public void MatchesImageName_RejectsOtherProcesses(string processName) =>
        Assert.False(Program.MatchesImageName(processName, Names));

    [Fact]
    public void MatchesImageName_IgnoresEmptyImageName() =>
        Assert.False(Program.MatchesImageName("", [""]));

    // A process that has exited (or was never associated) throws from Id/ProcessName; the scan
    // used to read those outside any try, so one process exiting mid-scan aborted the whole kill.
    [Fact]
    public void IsOtherInstance_ProcessWhosePropertiesThrowIsNotATarget()
    {
        using var process = new Process();
        Assert.False(Program.IsOtherInstance(process, Names));
    }

    [Fact]
    public void IsOtherInstance_NeverTargetsTheCurrentProcess()
    {
        using var current = Process.GetCurrentProcess();
        Assert.False(Program.IsOtherInstance(current, [current.ProcessName]));
    }
}
