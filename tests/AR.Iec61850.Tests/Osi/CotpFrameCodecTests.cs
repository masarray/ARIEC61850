using AR.Iec61850.Osi;

namespace AR.Iec61850.Tests.Osi;

public class CotpFrameCodecTests
{
    [Fact]
    public void Decode_DefaultConnectRequest()
    {
        var request = CotpFrameCodec.EncodeDefaultConnectRequest();

        var decoded = CotpFrameCodec.Decode(request);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(CotpTpduKind.ConnectionRequest, decoded.Kind);
        Assert.Equal(0x0001, decoded.SourceReference);
        Assert.Contains(decoded.Parameters, b => b == 0xC1);
        Assert.Contains(decoded.Parameters, b => b == 0xC2);
    }

    [Fact]
    public void EncodeDecode_DataTpdu()
    {
        byte[] userData = [0x0D, 0x01, 0x02, 0x03];

        var encoded = CotpFrameCodec.EncodeData(userData);
        var decoded = CotpFrameCodec.Decode(encoded);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(CotpTpduKind.Data, decoded.Kind);
        Assert.True(decoded.EndOfTransmission);
        Assert.Equal(userData, decoded.UserData);
    }

    [Fact]
    public void EncodeDecode_ConnectionConfirm()
    {
        var encoded = CotpFrameCodec.EncodeConnectionConfirm(0x0001, 0x1001);

        var decoded = CotpFrameCodec.Decode(encoded);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, decoded.Kind);
        Assert.Equal(0x0001, decoded.DestinationReference);
        Assert.Equal(0x1001, decoded.SourceReference);
    }

    [Fact]
    public void EncodeConnectionConfirm_Mirrors_Request_Tsap_Selectors_For_Responder()
    {
        byte[] requestPayload =
        [
            0x12, CotpFrameCodec.ConnectionRequestCode, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x09,
            0xC1, 0x02, 0x11, 0x22,
            0xC2, 0x03, 0x33, 0x44, 0x55
        ];
        var request = CotpFrameCodec.Decode(requestPayload);

        var encoded = CotpFrameCodec.EncodeConnectionConfirm(request, 0x1001);
        var decoded = CotpFrameCodec.Decode(encoded);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, decoded.Kind);
        Assert.Equal(0x0001, decoded.DestinationReference);
        Assert.Equal(0x1001, decoded.SourceReference);
        Assert.Equal(new byte[] { 0x33, 0x44, 0x55 }, ReadParameter(decoded, 0xC1));
        Assert.Equal(new byte[] { 0x11, 0x22 }, ReadParameter(decoded, 0xC2));
    }

    [Fact]
    public void EncodeConnectionConfirm_Selects_No_Larger_Tpdu_Size_Than_Client_Proposed()
    {
        byte[] requestPayload =
        [
            0x11, CotpFrameCodec.ConnectionRequestCode, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x09,
            0xC1, 0x02, 0x00, 0x01,
            0xC2, 0x02, 0x00, 0x01
        ];
        var request = CotpFrameCodec.Decode(requestPayload);

        var encoded = CotpFrameCodec.EncodeConnectionConfirm(request, 0x1001);
        var decoded = CotpFrameCodec.Decode(encoded);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(0x09, ReadSingleByteParameter(decoded, 0xC0));
    }

    private static byte ReadSingleByteParameter(CotpTpdu tpdu, byte code)
    {
        var offset = 0;
        while (offset + 2 <= tpdu.Parameters.Length)
        {
            var candidateCode = tpdu.Parameters[offset];
            var length = tpdu.Parameters[offset + 1];
            var next = offset + 2 + length;
            if (next > tpdu.Parameters.Length)
                break;

            if (candidateCode == code && length == 1)
                return tpdu.Parameters[offset + 2];

            offset = next;
        }

        throw new InvalidOperationException($"Parameter 0x{code:X2} was not found.");
    }

    private static byte[] ReadParameter(CotpTpdu tpdu, byte code)
    {
        var offset = 0;
        while (offset + 2 <= tpdu.Parameters.Length)
        {
            var candidateCode = tpdu.Parameters[offset];
            var length = tpdu.Parameters[offset + 1];
            var next = offset + 2 + length;
            if (next > tpdu.Parameters.Length)
                break;

            if (candidateCode == code)
                return tpdu.Parameters.AsSpan(offset + 2, length).ToArray();

            offset = next;
        }

        throw new InvalidOperationException($"Parameter 0x{code:X2} was not found.");
    }
}
