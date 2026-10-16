using VRCVideoCacher.Integrations.VRDancing;
using Xunit;

namespace VRCVideoCacher.Tests;

// The VRDancing track code feeds the metadata API and the sheet lookup; anything past the
// path (a token, a fragment) leaking into it turns every lookup into a miss.
public class IntegrationUrlTests
{
    [Theory]
    [InlineData("https://na2.vrdancing.club/weekend/CODE123", "CODE123")]
    [InlineData("https://na2.vrdancing.club/weekend/CODE123/", "CODE123")]
    [InlineData("https://eu2.vrdancing.club/weekend/CODE123?token=x&exp=1", "CODE123")]
    [InlineData("https://eu2.vrdancing.club/weekend/CODE123#t=10", "CODE123")]
    [InlineData("https://mpegts-beta.vrdancing.club/a%20b", "a b")]
    [InlineData("https://na2.vrdancing.club/", "")]
    public void VRDancing_ExtractCode_TakesTheLastPathSegmentOnly(string url, string expected) =>
        Assert.Equal(expected, VRDancingIntegration.ExtractCode(new Uri(url)));
}
