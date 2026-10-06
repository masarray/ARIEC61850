using AR.Iec61850.Acse;
using AR.Iec61850.Osi;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclAssistedMmsAssociationResolutionTests
{
    [Fact]
    public void Resolve_Missing_ApTitle_And_AeQualifier_Produces_Bounded_Compatibility_Candidates()
    {
        var remote = BuildRemote(
            tsel: "0001",
            ssel: "0001",
            psel: "00000001",
            apTitle: string.Empty,
            aeQualifierText: string.Empty,
            aeQualifier: null);

        var result = SclAssistedMmsAssociationCandidateResolver.Resolve(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        Assert.InRange(result.Candidates.Count, 1, SclAssistedMmsAssociationCandidateResolver.MaximumCandidateCount);
        Assert.Contains(result.Fields, field =>
            field.Name == "OSI-AP-Title" &&
            field.State == SclAssociationFieldState.Unspecified);
        Assert.Contains(result.Fields, field =>
            field.Name == "OSI-AE-Qualifier" &&
            field.State == SclAssociationFieldState.Unspecified);
        Assert.Contains(result.Candidates, candidate =>
            candidate.AssociationProfile.Name == "BalancedApTitle" ||
            candidate.Name.Contains("BalancedApTitle", StringComparison.Ordinal));

        foreach (var candidate in result.Candidates)
        {
            Assert.Equal(new byte[] { 0x00, 0x01 }, candidate.Cotp.DestinationTsap);
        }
    }

    [Fact]
    public void Resolve_Complete_Scl_Keeps_Exact_Candidate_First_And_Golden_Bytes()
    {
        var remote = BuildRemote(
            tsel: "0001",
            ssel: "0001",
            psel: "00000001",
            apTitle: "1,1,1,999,1",
            aeQualifierText: "12",
            aeQualifier: 12);

        var exact = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);
        Assert.True(exact.IsSuccess, string.Join(" | ", exact.Errors));

        var result = SclAssistedMmsAssociationCandidateResolver.Resolve(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var first = Assert.IsType<SclAssistedMmsAssociationCandidate>(result.Candidates[0]);
        Assert.Equal(SclAssociationCandidateSource.ExactScl, first.Source);
        Assert.Equal(exact.Plan!.CotpConnectRequest, CotpConnectRequest.Build(first.Cotp));
        Assert.Equal(exact.Plan.SessionPresentationAcseMmsRequest, first.AssociationProfile.Payload);
    }

    [Fact]
    public void Resolve_Missing_Application_Identity_Preserves_Explicit_NonDefault_Selectors()
    {
        var remote = BuildRemote(
            tsel: "0002",
            ssel: "0003",
            psel: "00000004",
            apTitle: string.Empty,
            aeQualifierText: string.Empty,
            aeQualifier: null);

        var result = SclAssistedMmsAssociationCandidateResolver.Resolve(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var typed = Assert.Single(
            result.Candidates,
            candidate =>
                candidate.Source == SclAssociationCandidateSource.EngineProfileTemplate &&
                candidate.Name.Contains("ExistingRuntimeDefault", StringComparison.Ordinal));

        Assert.Equal(new byte[] { 0x00, 0x02 }, typed.Cotp.DestinationTsap);
        var wire = AcseAssociationRequestIdentityReader.Read(
            typed.AssociationProfile.Payload,
            typed.Cotp.DestinationTsap);
        Assert.Equal(new byte[] { 0x00, 0x03 }, wire.CalledSessionSelector);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x04 }, wire.CalledPresentationSelector);
        Assert.True(wire.HasQualifiedCalledApplicationIdentity);
    }

    [Fact]
    public void Resolve_Invalid_Explicit_ApTitle_Fails_Closed()
    {
        var remote = BuildRemote(
            tsel: "0001",
            ssel: "0001",
            psel: "00000001",
            apTitle: "not-an-oid",
            aeQualifierText: string.Empty,
            aeQualifier: null);

        var result = SclAssistedMmsAssociationCandidateResolver.Resolve(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Candidates);
        Assert.Contains(result.Fields, field =>
            field.Name == "OSI-AP-Title" &&
            field.State == SclAssociationFieldState.Invalid);
    }

    [Fact]
    public void Resolve_Conflicting_Duplicate_Critical_Field_Fails_Closed()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <Communication>
                <SubNetwork name="StationBus" type="8-MMS">
                  <ConnectedAP iedName="IED01" apName="AP1">
                    <Address>
                      <P type="IP">192.0.2.10</P>
                      <P type="OSI-TSEL">0001</P>
                      <P type="OSI-SSEL">0001</P>
                      <P type="OSI-PSEL">00000001</P>
                      <P type="OSI-AP-Title">1,1,1,999,1</P>
                      <P type="OSI-AP-Title">1,1,1,999,2</P>
                    </Address>
                  </ConnectedAP>
                </SubNetwork>
              </Communication>
            </SCL>
            """;

        var parsed = SclMmsAssociationProfileReader.Read(xml);
        var remote = Assert.Single(parsed.AccessPoints);
        Assert.Contains("OSI-AP-Title", remote.AmbiguousParameters);

        var result = SclAssistedMmsAssociationCandidateResolver.Resolve(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Candidates);
        Assert.Contains(result.Fields, field =>
            field.Name == "OSI-AP-Title" &&
            field.State == SclAssociationFieldState.Ambiguous);
    }

    private static SclMmsAccessPoint BuildRemote(
        string tsel,
        string ssel,
        string psel,
        string apTitle,
        string aeQualifierText,
        int? aeQualifier)
        => new()
        {
            IedName = "IED01",
            AccessPointName = "AP1",
            Endpoint = new SclMmsEndpoint { IpAddress = "192.0.2.10" },
            Association = new SclIsoAssociationAddress
            {
                TransportSelector = tsel,
                SessionSelector = ssel,
                PresentationSelector = psel,
                ApTitle = apTitle,
                AeQualifierText = aeQualifierText,
                AeQualifier = aeQualifier
            }
        };
}
