using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

/// <summary>Independent synthetic BER fixtures; no device-specific captured bytes.</summary>
public sealed class P0CTypedMmsPrimitiveEvidenceTests
{
    [Theory]
    [InlineData("8300", 3)]
    [InlineData("83020001", 3)]
    [InlineData("8500", 5)]
    [InlineData("8600", 6)]
    [InlineData("8509000000000000000000", 5)]
    [InlineData("8609000000000000000000", 6)]
    [InlineData("8703000000", 7)]
    [InlineData("8400", 4)]
    [InlineData("840108", 4)]
    public void Malformed_Primitive_Is_Unknown_Not_False_Or_Zero(string berHex, int tag)
    {
        var values = MmsDataCodec.DecodeAllData(Convert.FromHexString(berHex));
        var decoded = Assert.Single(values);
        Assert.Equal(MmsDataKind.Unknown, decoded.Kind);
        Assert.Equal(tag, decoded.UnknownTagNumber);
    }

    [Fact]
    public void Valid_Zero_And_False_Are_Real_Observed_Typed_Values()
    {
        var encoded = MmsDataCodec.EncodeAllData([
            MmsDataValue.Boolean(false), MmsDataValue.Integer(0),
            MmsDataValue.Unsigned(0), MmsDataValue.Integer(-3),
            MmsDataValue.Unsigned(4294967295)]);
        var decoded = MmsDataCodec.DecodeAllData(encoded);
        Assert.Equal(false, decoded[0].Value);
        Assert.Equal(0L, decoded[1].Value);
        Assert.Equal(0UL, decoded[2].Value);
        Assert.Equal(-3L, decoded[3].Value);
        Assert.Equal(4294967295UL, decoded[4].Value);
        Assert.All(decoded, x => Assert.NotEqual(MmsDataKind.Unknown, x.Kind));
    }

    [Fact]
    public void Raw_Unknown_Wire_Kind_Never_Produces_A_Projected_Value()
    {
        var unknown = Assert.Single(MmsDataCodec.DecodeAllData(
            Convert.FromHexString("8500")));
        Assert.Equal(MmsDataKind.Unknown, unknown.Kind);
        Assert.Equal("85", MmsDataCodec.Encode(unknown).Length > 0 ?
            Convert.ToHexString(MmsDataCodec.Encode(unknown))[..2] : "");
    }
}
