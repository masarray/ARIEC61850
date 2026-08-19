using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class G1MmsSizeConstraintTests
{
    [Fact]
    public void NegativeVisibleStringSize_IsDecodedAsVariableMaximum_NotUnsignedWidth()
    {
        var reference = new MmsObjectReference("LD0", "LLN0$DC$NamPlt$vendor", "DC");
        var explicitType = BerWriter.EncodeTlv(
            0xA2,
            BerWriter.EncodeTlv(0x8A, new byte[] { 0xFE })); // signed MMS Integer32 = -2
        var response = BuildResponse(11, explicitType);

        var result = MmsVariableAccessAttributesResponseDecoder.Decode(response, 11, reference);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("visible-string", result.TypeSpecification!.MmsType);
        Assert.Equal(-2, result.TypeSpecification.RawSizeConstraint);
        Assert.Equal(2, result.TypeSpecification.Size);
        Assert.Equal(MmsTypeSizeConstraintKind.Maximum, result.TypeSpecification.SizeConstraintKind);
        Assert.True(result.TypeSpecification.IsVariableLength);
    }

    private static byte[] BuildResponse(int invokeId, params byte[][] serviceFields)
    {
        var service = BerWriter.EncodeTlv(0xA6, Concat(serviceFields));
        var confirmed = BerWriter.EncodeTlv(
            0xA1,
            Concat(BerWriter.EncodeTlv(0x02, new[] { (byte)invokeId }), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmed);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }
}
