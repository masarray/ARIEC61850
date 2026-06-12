using AR.Iec61850.Asn1;
using System.Text;

namespace AR.Iec61850.Tests;

public sealed class BerCodecTests
{
    [Fact]
    public void Writer_Uses_Long_Form_Length_When_Value_Exceeds_127_Bytes()
    {
        var value = Enumerable.Range(0, 130).Select(i => (byte)i).ToArray();

        var encoded = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: false,
            tagNumber: 3,
            value);

        Assert.Equal(0x83, encoded[0]);
        Assert.Equal(0x81, encoded[1]);
        Assert.Equal(130, encoded[2]);

        var offset = 0;
        Assert.True(BerReader.TryReadTlv(encoded, ref offset, out var tlv));
        Assert.Equal(BerClass.ContextSpecific, tlv.Class);
        Assert.Equal(3, tlv.TagNumber);
        Assert.Equal(value, tlv.Value.ToArray());
        Assert.Equal(encoded.Length, offset);
    }

    [Theory]
    [InlineData(0, "00")]
    [InlineData(127, "7F")]
    [InlineData(128, "0080")]
    [InlineData(-1, "FF")]
    [InlineData(-129, "FF7F")]
    public void Signed_Integer_Encoding_Is_Minimal(long value, string expectedHex)
    {
        var encoded = BerWriter.EncodeSignedInteger(value);

        Assert.Equal(expectedHex, Convert.ToHexString(encoded));

        var tlv = BerWriter.EncodeTlv(0x85, encoded);
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(tlv, ref offset, out var parsed));
        Assert.Equal(value, BerReader.ReadSignedInteger(parsed));
    }

    [Fact]
    public void Context_String_RoundTrips()
    {
        var encoded = BerWriter.EncodeTlv(0x80, Encoding.ASCII.GetBytes("IED1LD0/LLN0$GO$gcb01"));

        var offset = 0;
        Assert.True(BerReader.TryReadTlv(encoded, ref offset, out var tlv));

        Assert.Equal(BerClass.ContextSpecific, tlv.Class);
        Assert.False(tlv.Constructed);
        Assert.Equal(0, tlv.TagNumber);
        Assert.Equal("IED1LD0/LLN0$GO$gcb01", BerReader.ReadAsciiString(tlv));
    }
}
