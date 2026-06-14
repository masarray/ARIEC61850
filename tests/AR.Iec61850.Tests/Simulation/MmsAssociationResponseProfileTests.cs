using AR.Iec61850.Acse;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsAssociationResponseProfileTests
{
    [Fact]
    public void ResponseProfile_Contains_Acse_Aare_And_Mms_InitiateResponse_Marker()
    {
        var response = AcseMmsAssociateResponse.Select("DeterministicInitiateResponse");
        var inspection = AcseAssociationPayloadInspector.Inspect(response.Payload);

        Assert.True(inspection.LooksLikeServerAssociateResponse, inspection.Message);
        Assert.True(inspection.HasAcseAare);
        Assert.True(inspection.HasUserInformation);
        Assert.True(inspection.HasMmsInitiateResponseMarker);
        Assert.True(response.MaxMmsPduSize > 0);
    }

    [Fact]
    public async Task LoopbackProbe_Verifies_Aare_And_Mms_InitiateResponse()
    {
        var profile = await new MmsAssociationResponseProfileBuilder().RunLoopbackProbeAsync(new MmsAssociationResponseOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.True(profile.BoundPort > 0);
        Assert.Equal(1, profile.AcceptedConnectionCount);
        Assert.True(profile.TpktExchangeVerified);
        Assert.True(profile.CotpConnectionConfirmed);
        Assert.True(profile.ClientAssociateRequestObserved);
        Assert.True(profile.ServerAssociateResponseSent);
        Assert.True(profile.ClientAssociateResponseAccepted);
        Assert.True(profile.MmsInitiateResponseObserved);
        Assert.Contains(profile.Steps, x => x.Side == "server" && x.Layer == "ACSE-SEND-AARE" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Side == "client" && x.Layer == "ACSE-INSPECT-AARE" && x.IsPass);
    }

    [Fact]
    public async Task LoopbackProbe_Supports_Compact_Response_Profile()
    {
        var profile = await new MmsAssociationResponseProfileBuilder().RunLoopbackProbeAsync(new MmsAssociationResponseOptions
        {
            Port = 0,
            ResponseProfileName = "CompactInitiateResponse",
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.Equal("CompactInitiateResponse", profile.ResponseProfileName);
        Assert.True(profile.MmsInitiateResponseObserved);
    }

    [Fact]
    public async Task ToMarkdown_Renders_Association_Gates()
    {
        var profile = await new MmsAssociationResponseProfileBuilder().RunLoopbackProbeAsync(new MmsAssociationResponseOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Association Response Profile", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Association gates", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MMS initiate response marker observed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ACSE-SEND-AARE", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
