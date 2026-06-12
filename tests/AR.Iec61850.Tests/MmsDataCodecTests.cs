using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsDataCodecTests
{
    [Fact]
    public void AllData_Codec_RoundTrips_Common_Goose_Value_Types()
    {
        var timestamp = new Iec61850UtcTime(
            new DateTimeOffset(2026, 6, 12, 3, 4, 5, 250, TimeSpan.Zero),
            Quality: 0x0A);

        var values = new[]
        {
            MmsDataValue.Boolean(true),
            MmsDataValue.Integer(-3),
            MmsDataValue.Unsigned(42),
            MmsDataValue.FloatingPoint(12.5f),
            MmsDataValue.VisibleString("OK"),
            MmsDataValue.BinaryTime([0x12, 0x34, 0x56, 0x78]),
            MmsDataValue.UtcTime(timestamp),
            MmsDataValue.Structure(new[]
            {
                MmsDataValue.Boolean(false),
                MmsDataValue.Unsigned(7)
            })
        };

        var encoded = MmsDataCodec.EncodeAllData(values);
        var decoded = MmsDataCodec.DecodeAllData(encoded);

        Assert.Equal(values.Length, decoded.Count);
        Assert.Equal(MmsDataKind.Boolean, decoded[0].Kind);
        Assert.Equal(true, decoded[0].Value);
        Assert.Equal(-3L, decoded[1].Value);
        Assert.Equal(42UL, decoded[2].Value);
        Assert.Equal(12.5f, decoded[3].Value);
        Assert.Equal("OK", decoded[4].Value);
        Assert.Equal(MmsDataKind.BinaryTime, decoded[5].Kind);
        Assert.Equal([0x12, 0x34, 0x56, 0x78], decoded[5].RawValue);
        Assert.Equal(timestamp, decoded[6].Value);

        Assert.Equal(MmsDataKind.Structure, decoded[7].Kind);
        Assert.Equal(2, decoded[7].Children.Count);
        Assert.Equal(false, decoded[7].Children[0].Value);
        Assert.Equal(7UL, decoded[7].Children[1].Value);
    }
}
