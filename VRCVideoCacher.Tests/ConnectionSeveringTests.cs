using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// `ss` rejects a bare IPv6 literal in a dst filter, and it does so on stderr while still
// exiting 0 — the exact shape of failure that made severing silently do nothing before.
// These pin the one character that fixes it.
public class ConnectionSeveringTests
{
    [Theory]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    [InlineData("::1", "[::1]")]
    [InlineData("fe80::1ff:fe23:4567:890a", "[fe80::1ff:fe23:4567:890a]")]
    public void BracketsIpV6Destinations(string address, string expected)
    {
        Assert.Equal(expected, ConnectionSevering.FormatSsDestination(address));
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("127.0.0.1")]
    [InlineData("172.217.16.142")]
    public void LeavesIpV4DestinationsAlone(string address)
    {
        Assert.Equal(address, ConnectionSevering.FormatSsDestination(address));
    }

    [Fact]
    public void LeavesUnparseableInputAlone()
    {
        // Never reaches ss — the elevated helper drops anything that is not an IP — but the
        // formatter must not invent brackets around whatever it is handed.
        Assert.Equal("not-an-address", ConnectionSevering.FormatSsDestination("not-an-address"));
    }

    [Fact]
    public void ParsesTheElevatedSeverCommand()
    {
        LaunchArgs.SetupArguments("--sever-connections=1.2.3.4,2001:db8::1");

        Assert.True(LaunchArgs.IsSeverCommand);
        Assert.True(LaunchArgs.IsPrivilegedHelper);
        Assert.Equal(["1.2.3.4", "2001:db8::1"], LaunchArgs.SeverAddresses);

        LaunchArgs.SeverAddresses = [];
    }

    [Fact]
    public void ASeverCommandIsNotCarriedIntoRelaunches()
    {
        // The updater re-emits launch flags. A one-shot privileged command must never be
        // among them, or every update would try to elevate.
        LaunchArgs.SetupArguments("--sever-connections=1.2.3.4");
        Assert.DoesNotContain(LaunchArgs.BuildArgs(), a => a.Contains("sever"));
        LaunchArgs.SeverAddresses = [];
    }

    [Fact]
    public void CapabilityHintNamesTheRealBinary()
    {
        var hint = ConnectionSevering.CapabilityHint;
        Assert.False(string.IsNullOrWhiteSpace(hint));
        if (OperatingSystem.IsLinux())
        {
            Assert.Contains("cap_net_admin", hint);
            Assert.Contains(Environment.ProcessPath!, hint);
        }
    }
}
