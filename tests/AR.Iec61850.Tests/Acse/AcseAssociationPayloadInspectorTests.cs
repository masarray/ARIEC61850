using AR.Iec61850.Acse;

namespace AR.Iec61850.Tests.Acse;

public class AcseAssociationPayloadInspectorTests
{
    [Fact]
    public void Inspect_DefaultAssociationPayloadLooksLikeClientRequest()
    {
        var payload = AcseMmsInitiateRequest.BuildDefaultAssociationPayload();

        var inspection = AcseAssociationPayloadInspector.Inspect(payload);

        Assert.Equal(AcseAssociationPayloadKind.SessionConnect, inspection.Kind);
        Assert.True(inspection.LooksLikeClientAssociateRequest, inspection.Message);
        Assert.True(inspection.HasAcseAarq);
        Assert.True(inspection.HasUserInformation);
        Assert.True(inspection.HasMmsInitiateRequestMarker);
    }

    [Fact]
    public void Inspect_RejectSpduIsNotClientRequest()
    {
        byte[] payload = [0x0A, 0x00, 0x00];

        var inspection = AcseAssociationPayloadInspector.Inspect(payload);

        Assert.Equal(AcseAssociationPayloadKind.SessionRejectOrRefuse, inspection.Kind);
        Assert.False(inspection.LooksLikeClientAssociateRequest);
    }
}
