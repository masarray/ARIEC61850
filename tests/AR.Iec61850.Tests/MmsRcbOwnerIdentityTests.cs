using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsRcbOwnerIdentityTests
{
    [Fact]
    public void PhysicalSiprotecOwner_DecodesToExpectedIpv4()
    {
        var ok = MmsRcbOwnerIdentity.TryDecodeIpAddress("C0A851F0", out var address);

        Assert.True(ok);
        Assert.NotNull(address);
        Assert.Equal("192.168.81.240", address!.ToString());
    }

    [Fact]
    public void PhysicalSiprotecOwner_MatchesSameAssociationLocalAddress()
    {
        var ok = MmsRcbOwnerIdentity.MatchesLocalTcpAddress(
            "C0A851F0",
            "192.168.81.240",
            out var reason);

        Assert.True(ok, reason);
        Assert.Contains("matches", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OwnerMismatch_FailsClosed()
    {
        var ok = MmsRcbOwnerIdentity.MatchesLocalTcpAddress(
            "C0A851F0",
            "192.168.81.241",
            out var reason);

        Assert.False(ok);
        Assert.Contains("does not match", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("010203")]
    [InlineData("NOT-HEX")]
    public void UnsupportedOwnerEncoding_FailsClosed(string owner)
    {
        var ok = MmsRcbOwnerIdentity.MatchesLocalTcpAddress(owner, "192.168.81.240", out _);

        Assert.False(ok);
    }
}
