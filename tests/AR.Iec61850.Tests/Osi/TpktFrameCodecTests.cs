using AR.Iec61850.Osi;

namespace AR.Iec61850.Tests.Osi;

public class TpktFrameCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsPayload()
    {
        byte[] payload = [0x02, 0xF0, 0x80, 0xA8, 0x01, 0x00];

        var frame = TpktFrameCodec.Encode(payload);
        var decoded = TpktFrameCodec.Decode(frame);

        Assert.True(decoded.IsValid, decoded.Message);
        Assert.Equal(0x03, decoded.Version);
        Assert.Equal(frame.Length, decoded.DeclaredLength);
        Assert.Equal(payload, decoded.Payload);
    }

    [Fact]
    public void Decode_RejectsLengthMismatch()
    {
        byte[] frame = [0x03, 0x00, 0x00, 0x08, 0x02, 0xF0, 0x80];

        var decoded = TpktFrameCodec.Decode(frame);

        Assert.False(decoded.IsValid);
        Assert.Contains("does not match", decoded.Message, StringComparison.OrdinalIgnoreCase);
    }
}
