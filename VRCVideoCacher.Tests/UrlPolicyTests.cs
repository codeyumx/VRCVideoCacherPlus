using System.Net;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// The guard exists to keep cloud metadata endpoints (169.254.169.254) out of reach of URLs
// chosen by anyone in a VRChat instance. A dual-mode socket reaches an IPv4 address given
// in its IPv4-mapped IPv6 form, so that form has to be classified like the IPv4 address.
public class UrlPolicyTests
{
    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:a9fe:a9fe")]
    public void IsBlockedAddress_BlocksLinkLocal(string address) =>
        Assert.True(UrlPolicy.IsBlockedAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("192.168.1.10")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:93.184.216.34")]
    [InlineData("::ffff:192.168.1.10")]
    public void IsBlockedAddress_AllowsPublicAndLanAddresses(string address) =>
        Assert.False(UrlPolicy.IsBlockedAddress(IPAddress.Parse(address)));
}
