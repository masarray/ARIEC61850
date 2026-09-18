using AR.Iec61850.Acse;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Tests.Acse;

public sealed class AcseAssociationRequestIdentityReaderTests
{
    [Fact]
    public void BalancedApTitle_DecodesCalledIdentityFromExactWirePayload()
    {
        var profile = AcseMmsInitiateRequest
            .BuildAssociationProfiles()
            .Single(candidate => candidate.Name == "BalancedApTitle");

        var evidence = AcseAssociationRequestIdentityReader.Read(
            profile.Payload,
            new CotpConnectParameters().DestinationTsap);

        Assert.Equal(new uint[] { 1, 1, 1, 999, 1 }, evidence.CalledApTitle);
        Assert.Equal("1,1,1,999,1", evidence.CalledApTitleText);
        Assert.Equal(12, evidence.CalledAeQualifier);
        Assert.Equal("00000001", Convert.ToHexString(evidence.CalledPresentationSelector));
        Assert.Equal("0001", Convert.ToHexString(evidence.CalledSessionSelector));
        Assert.Equal("0001", Convert.ToHexString(evidence.CalledTransportSelector));
        Assert.True(evidence.HasQualifiedCalledApplicationIdentity);
    }

    [Fact]
    public void LegacyMinimal_DoesNotInventMissingCalledApTitle()
    {
        var profile = AcseMmsInitiateRequest
            .BuildAssociationProfiles()
            .Single(candidate => candidate.Name == "LegacyMinimal");

        var evidence = AcseAssociationRequestIdentityReader.Read(
            profile.Payload,
            new CotpConnectParameters().DestinationTsap);

        Assert.Empty(evidence.CalledApTitle);
        Assert.Equal(string.Empty, evidence.CalledApTitleText);
        Assert.Equal(12, evidence.CalledAeQualifier);
        Assert.Equal("00000001", Convert.ToHexString(evidence.CalledPresentationSelector));
        Assert.Equal("0001", Convert.ToHexString(evidence.CalledSessionSelector));
        Assert.Equal("0001", Convert.ToHexString(evidence.CalledTransportSelector));
        Assert.False(evidence.HasQualifiedCalledApplicationIdentity);
    }

    [Fact]
    public void WirePayload_IsAuthority_NotProfileNameMapping()
    {
        var profile = AcseMmsInitiateRequest
            .BuildAssociationProfiles()
            .Single(candidate => candidate.Name == "BalancedApTitle");
        var mutated = profile.Payload.ToArray();

        var oidPattern = new byte[] { 0xA2, 0x07, 0x06, 0x05, 0x29, 0x01, 0x87, 0x67, 0x01 };
        var offset = FindPattern(mutated, oidPattern);
        Assert.True(offset >= 0);

        mutated[offset + oidPattern.Length - 1] = 0x02;
        var evidence = AcseAssociationRequestIdentityReader.Read(
            mutated,
            new CotpConnectParameters().DestinationTsap);

        Assert.Equal(new uint[] { 1, 1, 1, 999, 2 }, evidence.CalledApTitle);
        Assert.Equal("1,1,1,999,2", evidence.CalledApTitleText);
        Assert.Equal(12, evidence.CalledAeQualifier);
    }

    private static int FindPattern(byte[] source, byte[] pattern)
    {
        for (var offset = 0; offset <= source.Length - pattern.Length; offset++)
        {
            if (source.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
                return offset;
        }

        return -1;
    }
}
