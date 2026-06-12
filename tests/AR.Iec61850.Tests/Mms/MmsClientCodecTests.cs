using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public class MmsClientCodecTests
{
    [Fact]
    public void ObjectReferenceInsertsFunctionalConstraintAfterLogicalNode()
    {
        var reference = MmsObjectReference.Parse("LD0/LLN0.Mod.stVal", "ST");

        Assert.Equal("LD0", reference.Domain);
        Assert.Equal("LLN0$ST$Mod$stVal", reference.Item);
        Assert.Equal("LD0/LLN0.ST.Mod.stVal [ST]", reference.ToString());
    }

    [Fact]
    public void GetNameListResponseDecoderReadsNamesAndMoreFollows()
    {
        var response = BuildGetNameListResponse(
            invokeId: 17,
            names: ["LD0", "LD1"],
            moreFollows: true);

        var result = MmsGetNameListResponseDecoder.Decode(response, expectedInvokeId: 17);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(["LD0", "LD1"], result.Names);
        Assert.True(result.MoreFollows);
    }

    [Fact]
    public void ReadResponseDecoderReturnsMmsDataValue()
    {
        var response = BuildReadResponse(
            invokeId: 23,
            MmsDataValue.VisibleString("OCR7SR12PROT/LLN0$BR$brcbA01"));

        var result = MmsReadResponseDecoder.DecodeSingleVariable(response, expectedInvokeId: 23);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal(MmsDataKind.VisibleString, result.Value.Kind);
        Assert.Equal("OCR7SR12PROT/LLN0$BR$brcbA01", result.Value.Value);
    }

    private static byte[] BuildGetNameListResponse(int invokeId, IReadOnlyList<string> names, bool moreFollows)
    {
        var identifiers = Concat(names
            .Select(name => BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(name)))
            .ToArray());

        var service = BerWriter.EncodeTlv(
            0xA1,
            Concat(
                BerWriter.EncodeTlv(0xA0, identifiers),
                BerWriter.EncodeTlv(0x81, [(byte)(moreFollows ? 1 : 0)])));

        var mms = BerWriter.EncodeTlv(
            0xA1,
            Concat(Integer(invokeId), service));

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] BuildReadResponse(int invokeId, MmsDataValue value)
    {
        var data = MmsDataCodec.Encode(value);
        var accessResultSuccess = BerWriter.EncodeTlv(0xA0, data);
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, accessResultSuccess);
        var readService = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var mms = BerWriter.EncodeTlv(
            0xA1,
            Concat(Integer(invokeId), readService));

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] Integer(int value)
    {
        if (value <= 0x7F)
            return [0x02, 0x01, (byte)value];

        if (value <= 0xFF)
            return [0x02, 0x02, 0x00, (byte)value];

        return [0x02, 0x02, (byte)(value >> 8), (byte)value];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(part => part.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
