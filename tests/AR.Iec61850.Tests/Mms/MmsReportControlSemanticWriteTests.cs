using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportControlSemanticWriteTests
{
    [Fact]
    public void ResvTms_Uses_Signed_Mms_Integer_On_Wire()
    {
        var write = MmsReportControlSemanticWrite.ReservationTime(60);

        Assert.Equal("ResvTms", write.Attribute);
        Assert.Equal(MmsDataKind.Integer, write.ExpectedKind);
        Assert.Equal(MmsDataKind.Integer, write.Value.Kind);
        Assert.Equal(60L, write.Value.Value);

        var encoded = MmsDataCodec.Encode(write.Value);

        Assert.NotEmpty(encoded);
        Assert.Equal(0x85, encoded[0]); // MMS Data integer [5], never unsigned [6] (0x86).
    }

    [Theory]
    [InlineData(-32768)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(32767)]
    public void ResvTms_Accepts_Signed_Int16_Range(int seconds)
    {
        var write = MmsReportControlSemanticWrite.ReservationTime(seconds);

        Assert.Equal(MmsDataKind.Integer, write.Value.Kind);
        Assert.Equal((long)seconds, write.Value.Value);
    }

    [Theory]
    [InlineData(-32769)]
    [InlineData(32768)]
    public void ResvTms_Rejects_Values_Outside_Signed_Int16_Range(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MmsReportControlSemanticWrite.ReservationTime(seconds));
    }

    [Fact]
    public void Boolean_Rcb_Fields_Are_Bound_To_Mms_Boolean()
    {
        var values = new[]
        {
            MmsReportControlSemanticWrite.ReportEnable(true),
            MmsReportControlSemanticWrite.Reservation(true),
            MmsReportControlSemanticWrite.GeneralInterrogation(true)
        };

        Assert.Equal(new[] { "RptEna", "Resv", "GI" }, values.Select(x => x.Attribute));
        Assert.All(values, write =>
        {
            Assert.Equal(MmsDataKind.Boolean, write.ExpectedKind);
            Assert.Equal(MmsDataKind.Boolean, write.Value.Kind);
            Assert.Equal(true, write.Value.Value);
            Assert.Equal(0x83, MmsDataCodec.Encode(write.Value)[0]); // MMS Data boolean [3].
        });
    }

    [Fact]
    public void Live_Type_Compatibility_Is_Exact_And_Does_Not_Guess()
    {
        var integer = new MmsTypeSpecificationNode { MmsType = "integer" };
        var unsigned = new MmsTypeSpecificationNode { MmsType = "unsigned" };
        var boolean = new MmsTypeSpecificationNode { MmsType = "boolean" };

        var resvTms = MmsReportControlSemanticWrite.ReservationTime(60);
        Assert.True(resvTms.IsCompatibleWith(integer));
        Assert.False(resvTms.IsCompatibleWith(unsigned));
        Assert.False(resvTms.IsCompatibleWith(boolean));

        var rptEna = MmsReportControlSemanticWrite.ReportEnable(true);
        Assert.True(rptEna.IsCompatibleWith(boolean));
        Assert.False(rptEna.IsCompatibleWith(integer));
        Assert.False(rptEna.IsCompatibleWith(null));
    }
}
