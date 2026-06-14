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
}
