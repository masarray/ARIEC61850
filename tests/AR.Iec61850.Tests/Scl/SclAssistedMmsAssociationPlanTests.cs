using AR.Iec61850.Acse;
using AR.Iec61850.Osi;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclAssistedMmsAssociationPlanTests
{
    [Fact]
    public void BuildExact_Reproduces_Current_Default_Association_Bytes_When_Addresses_Match()
    {
        var remote = BuildRemote(
            tsel: "0001",
            ssel: "0001",
            psel: "00000001",
            apTitle: "1,1,1,999,1",
            aeQualifier: 12);

        var result = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var plan = Assert.IsType<SclAssistedMmsAssociationPlan>(result.Plan);
        Assert.Equal(CotpConnectRequest.BuildDefault(), plan.CotpConnectRequest);
        Assert.Equal(AcseMmsInitiateRequest.BuildDefaultAssociationPayload(), plan.SessionPresentationAcseMmsRequest);

        var inspection = AcseAssociationPayloadInspector.Inspect(plan.SessionPresentationAcseMmsRequest);
        Assert.True(inspection.LooksLikeClientAssociateRequest, inspection.Message);
        Assert.Equal(184, plan.SessionPresentationAcseMmsRequest.Length);
    }

    [Fact]
    public void SclInteroperabilityDefault_Uses_Observed_Calling_Identity_Without_Changing_Called_Scl_Identity()
    {
        var remote = BuildRemote(
            tsel: "0001",
            ssel: "0001",
            psel: "00000001",
            apTitle: "1,3,9999,23",
            aeQualifier: 23);

        var result = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.SclInteroperabilityDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var plan = Assert.IsType<SclAssistedMmsAssociationPlan>(result.Plan);

        Assert.Equal("SclInteroperabilityDefault", plan.LocalProfileName);
        Assert.Equal(new byte[] { 0x00, 0x00 }, plan.Cotp.SourceTsap);
        Assert.Equal(new byte[] { 0x00, 0x01 }, plan.Cotp.DestinationTsap);
        Assert.Equal(new byte[] { 0x00, 0x01 }, plan.Association.Calling.SessionSelector);
        Assert.Equal(new byte[] { 0x00, 0x01 }, plan.Association.Called.SessionSelector);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, plan.Association.Calling.PresentationSelector);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, plan.Association.Called.PresentationSelector);
        Assert.Equal(new uint[] { 1, 1, 1, 999 }, plan.Association.Calling.ApTitle);
        Assert.Equal(new uint[] { 1, 3, 9999, 23 }, plan.Association.Called.ApTitle);
        Assert.Equal(23, plan.Association.Calling.AeQualifier);
        Assert.Equal(23, plan.Association.Called.AeQualifier);
        Assert.Equal(65000, plan.Association.Initiate.LocalDetailCalling);
        Assert.Equal(10, plan.Association.Initiate.MaxOutstandingCalling);
        Assert.Equal(10, plan.Association.Initiate.MaxOutstandingCalled);
        Assert.Equal(5, plan.Association.Initiate.NestingLevel);

        // COTP LI/EOT framing is added by the transport layer; the pure builder body must
        // carry source TSEL 0000, destination TSEL 0001 and TPDU exponent 0A.
        Assert.Equal(
            new byte[]
            {
                0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
                0xC0, 0x01, 0x0A,
                0xC1, 0x02, 0x00, 0x00,
                0xC2, 0x02, 0x00, 0x01
            },
            plan.CotpConnectRequest);
    }

    [Fact]
    public void BuildExact_Keeps_Calling_And_Called_Identity_Separate()
    {
        var remote = BuildRemote(
            tsel: "0002",
            ssel: "0003",
            psel: "00000004",
            apTitle: "1,3,9999,23",
            aeQualifier: 23);

        var result = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var plan = Assert.IsType<SclAssistedMmsAssociationPlan>(result.Plan);

        Assert.Equal(new byte[] { 0x00, 0x01 }, plan.Cotp.SourceTsap);
        Assert.Equal(new byte[] { 0x00, 0x02 }, plan.Cotp.DestinationTsap);
        Assert.Equal(new byte[] { 0x00, 0x01 }, plan.Association.Calling.SessionSelector);
        Assert.Equal(new byte[] { 0x00, 0x03 }, plan.Association.Called.SessionSelector);
        Assert.Equal(new uint[] { 1, 1, 1, 999 }, plan.Association.Calling.ApTitle);
        Assert.Equal(new uint[] { 1, 3, 9999, 23 }, plan.Association.Called.ApTitle);
        Assert.Equal(12, plan.Association.Calling.AeQualifier);
        Assert.Equal(23, plan.Association.Called.AeQualifier);
        Assert.NotEqual(AcseMmsInitiateRequest.BuildDefaultAssociationPayload(), plan.SessionPresentationAcseMmsRequest);
    }

    [Fact]
    public void BuildExact_Fails_Closed_When_Remote_Scl_Association_Address_Is_Incomplete()
    {
        var remote = new SclMmsAccessPoint
        {
            IedName = "IED01",
            AccessPointName = "AP1",
            Endpoint = new SclMmsEndpoint { IpAddress = "192.0.2.10" },
            Association = new SclIsoAssociationAddress
            {
                TransportSelector = "0001"
            }
        };

        var result = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Errors, error => error.Contains("OSI-SSEL", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("OSI-PSEL", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("OSI-AP-Title", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("OSI-AE-Qualifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Parameterized_Cotp_Builder_Preserves_Existing_Default_Golden_Bytes()
    {
        byte[] expected =
        [
            0x11, 0xE0,
            0x00, 0x00,
            0x00, 0x01,
            0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, 0x00, 0x01,
            0xC2, 0x02, 0x00, 0x01
        ];

        Assert.Equal(expected, CotpConnectRequest.BuildDefault());
    }

    private static SclMmsAccessPoint BuildRemote(
        string tsel,
        string ssel,
        string psel,
        string apTitle,
        int aeQualifier)
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
                AeQualifierText = aeQualifier.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AeQualifier = aeQualifier
            }
        };
}
