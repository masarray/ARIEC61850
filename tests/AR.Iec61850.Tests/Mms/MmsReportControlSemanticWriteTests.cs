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
    [Fact]
    public void Exact_Live_Type_Mismatch_Is_A_Positive_Blocker()
    {
        var write = MmsReportControlSemanticWrite.ReservationTime(60);
        var live = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "LLN0$BR$Brcb01$ResvTms", "BR"),
            TypeSpecification = new MmsTypeSpecificationNode
            {
                MmsType = "unsigned",
                SclBType = "INT32U"
            }
        };

        var evidence = MmsReportSemanticTypePolicy.Evaluate(
            write,
            "LD0/LLN0.BR.Brcb01.ResvTms [BR]",
            live);

        Assert.Equal(MmsReportSemanticTypeEvidenceStatus.ExactMismatch, evidence.Status);
        Assert.True(evidence.IsExact);
        Assert.False(evidence.AllowsMutation);
        Assert.Equal("integer", evidence.ExpectedMmsType);
        Assert.Equal("unsigned", evidence.LiveMmsType);
    }

    [Fact]
    public void Missing_Live_Type_Evidence_Does_Not_Become_A_False_Blocker()
    {
        var write = MmsReportControlSemanticWrite.ReportEnable(true);
        var live = new MmsVariableAccessAttributesResult
        {
            IsSuccess = false,
            Reference = new MmsObjectReference("LD0", "LLN0$BR$Brcb01$RptEna", "BR"),
            Message = "GetVariableAccessAttributes unsupported by this server."
        };

        var evidence = MmsReportSemanticTypePolicy.Evaluate(
            write,
            "LD0/LLN0.BR.Brcb01.RptEna [BR]",
            live);

        Assert.Equal(MmsReportSemanticTypeEvidenceStatus.Unavailable, evidence.Status);
        Assert.False(evidence.IsExact);
        Assert.True(evidence.AllowsMutation);
        Assert.Contains("IEC semantic contract", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exact_Live_Type_Match_Confirms_Semantic_Mutation()
    {
        var write = MmsReportControlSemanticWrite.ReportEnable(true);
        var live = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "LLN0$BR$Brcb01$RptEna", "BR"),
            TypeSpecification = new MmsTypeSpecificationNode
            {
                MmsType = "boolean",
                SclBType = "BOOLEAN"
            }
        };

        var evidence = MmsReportSemanticTypePolicy.Evaluate(
            write,
            "LD0/LLN0.BR.Brcb01.RptEna [BR]",
            live);

        Assert.Equal(MmsReportSemanticTypeEvidenceStatus.ExactMatch, evidence.Status);
        Assert.True(evidence.IsExact);
        Assert.True(evidence.AllowsMutation);
    }

}
