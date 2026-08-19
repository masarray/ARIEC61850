using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class G11MmsWriteAccessErrorTests
{
    [Fact]
    public void PhysicalSboWFailureCode3_DecodesAsObjectAccessDenied()
    {
        // Physical SIPROTEC field response carried Write-Response [5] with one
        // DataAccessError [0] value 3: A5 03 80 01 03.
        var service = BerWriter.EncodeTlv(0xA5, BerWriter.EncodeTlv(0x80, new byte[] { 0x03 }));
        var response = BerWriter.EncodeTlv(
            0xA1,
            MmsPresentation.Concat(MmsPresentation.Integer(7128), service));
        var presentation = MmsPresentation.WrapIsoPresentationPData(response);

        var result = MmsWriteResponseDecoder.Decode(presentation, expectedInvokeId: 7128);

        Assert.False(result.IsSuccess);
        var access = Assert.Single(result.AccessResults);
        Assert.False(access.IsSuccess);
        Assert.Equal(3, access.FailureCode);
        Assert.Equal("object-access-denied", access.FailureName);
        Assert.Equal("object-access-denied (3)", access.Message);
        Assert.Contains("item[0]=object-access-denied (3)", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "object-invalidated")]
    [InlineData(1, "hardware-fault")]
    [InlineData(2, "temporarily-unavailable")]
    [InlineData(3, "object-access-denied")]
    [InlineData(4, "object-undefined")]
    [InlineData(5, "invalid-address")]
    [InlineData(6, "type-unsupported")]
    [InlineData(7, "type-inconsistent")]
    [InlineData(8, "object-attribute-inconsistent")]
    [InlineData(9, "object-access-unsupported")]
    [InlineData(10, "object-non-existent")]
    [InlineData(11, "object-value-invalid")]
    [InlineData(12, "unknown")]
    public void StandardMmsDataAccessErrors_HaveExplicitNames(int code, string expected)
        => Assert.Equal(expected, MmsWriteResponseDecoder.NameDataAccessError(code));

    [Fact]
    public void VendorSpecificUnknownAccessError_RemainsNumericallyVisible()
        => Assert.Equal("data-access-error-99", MmsWriteResponseDecoder.NameDataAccessError(99));
}
