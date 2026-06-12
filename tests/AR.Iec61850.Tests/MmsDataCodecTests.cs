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
        Assert.Equal(timestamp, decoded[5].Value);

        Assert.Equal(MmsDataKind.Structure, decoded[6].Kind);
        Assert.Equal(2, decoded[6].Children.Count);
        Assert.Equal(false, decoded[6].Children[0].Value);
        Assert.Equal(7UL, decoded[6].Children[1].Value);
    }
}
