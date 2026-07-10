using AR.Iec61850.Acse;
using AR.Iec61850.Asn1;

namespace AR.Iec61850.Tests.Acse;

public sealed class AcseMmsAssociateResponseTests
{
    [Fact]
    public void SelectForRequest_Mirrors_Client_Session_Parameters()
    {
        var request = AcseMmsInitiateRequest.BuildDefaultAssociationPayload();
        var mutatedRequest = request.ToArray();
        mutatedRequest[17] = 0x7E;

        var response = AcseMmsAssociateResponse.SelectForRequest("DeterministicInitiateResponse", mutatedRequest);

        Assert.Equal("DeterministicInitiateResponse+SessionMirror", response.Name);
        Assert.Equal(0x0E, response.Payload[0]);
        Assert.Equal(response.Payload.Length - 2, response.Payload[1]);
        Assert.Equal(0x7E, response.Payload[17]);

        var inspection = AcseAssociationPayloadInspector.Inspect(response.Payload);
        Assert.True(inspection.LooksLikeServerAssociateResponse, inspection.Message);
        Assert.True(inspection.HasMmsInitiateResponseMarker, inspection.Message);
    }

    [Fact]
    public void SelectForRequest_Emits_Presentation_Context_Results_And_Fully_Encoded_Aare()
    {
        var request = AcseMmsInitiateRequest.BuildDefaultAssociationPayload();

        var response = AcseMmsAssociateResponse.SelectForRequest("DeterministicInitiateResponse", request);
        var presentationPayload = ExtractSessionUserData(response.Payload);
        var cpa = ReadSingleTlv(presentationPayload, 0x31);
        var normalMode = BerReader.ReadChildren(cpa.Value).Single(x => x.EncodedTag == 0xA2);
        var normalChildren = BerReader.ReadChildren(normalMode.Value);

        var contextResultList = normalChildren.Single(x => x.EncodedTag == 0xA5);
        Assert.Equal(2, BerReader.ReadChildren(contextResultList.Value).Count(x => x.EncodedTag == 0x30));

        var fullyEncodedData = normalChildren.Single(x => x.EncodedTag == 0x61);
        var pdvList = BerReader.ReadChildren(fullyEncodedData.Value).Single(x => x.EncodedTag == 0x30);
        var pdvChildren = BerReader.ReadChildren(pdvList.Value);
        var contextId = pdvChildren.Single(x => x.EncodedTag == 0x02);
        Assert.Equal((ulong)1, BerReader.ReadUnsignedInteger(contextId));

        var singleAsn1Type = pdvChildren.Single(x => x.EncodedTag == 0xA0);
        Assert.Equal(0x61, singleAsn1Type.Value.Span[0]);
    }

    [Fact]
    public void SelectForRequest_Falls_Back_When_Client_Request_Is_Not_Session_Connect()
    {
        var response = AcseMmsAssociateResponse.SelectForRequest("DeterministicInitiateResponse", [0x01, 0x00]);

        Assert.Equal("DeterministicInitiateResponse", response.Name);
    }

    private static ReadOnlyMemory<byte> ExtractSessionUserData(byte[] payload)
    {
        for (var i = 2; i + 2 <= payload.Length; i++)
        {
            if (payload[i] != 0xC1)
                continue;

            var length = payload[i + 1];
            if (i + 2 + length == payload.Length)
                return payload.AsMemory(i + 2, length);
        }

        throw new InvalidOperationException("Session user-data parameter was not found.");
    }

    private static BerTlv ReadSingleTlv(ReadOnlyMemory<byte> payload, byte expectedTag)
    {
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(payload, ref offset, out var tlv));
        Assert.Equal(expectedTag, tlv.EncodedTag);
        Assert.Equal(payload.Length, offset);
        return tlv;
    }
}
