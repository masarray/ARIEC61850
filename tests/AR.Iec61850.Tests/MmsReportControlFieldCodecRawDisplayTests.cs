using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsReportControlFieldCodecRawDisplayTests
{
    [Fact]
    public void LiveTrgOps_0204_Decodes_As_GiOnly_WithReservedBitClear()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("0204");

        Assert.False(flags.Reserved);
        Assert.False(flags.DataChange);
        Assert.False(flags.QualityChange);
        Assert.False(flags.DataUpdate);
        Assert.False(flags.Integrity);
        Assert.True(flags.GeneralInterrogation);
        Assert.False(flags.ApplicationTrigger);
    }

    [Fact]
    public void CorrectWireMapping_Encodes_StandardTriggerOptions_AfterReservedBitZero()
    {
        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("gi", out var gi));
        Assert.Equal("0204", Convert.ToHexString(gi.RawValue.ToArray()));

        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("dchg", out var dchg));
        Assert.Equal("0240", Convert.ToHexString(dchg.RawValue.ToArray()));

        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("dchg gi", out var dchgGi));
        Assert.Equal("0244", Convert.ToHexString(dchgGi.RawValue.ToArray()));

        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("dchg qchg dupd integrity gi", out var all));
        Assert.Equal("027C", Convert.ToHexString(all.RawValue.ToArray()));
    }

    [Fact]
    public void PriorWrongTemporaryValue_0288_Is_ReservedPlusIntegrity_Not_DchgPlusGi()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("0288");

        Assert.True(flags.Reserved);
        Assert.False(flags.DataChange);
        Assert.False(flags.QualityChange);
        Assert.False(flags.DataUpdate);
        Assert.True(flags.Integrity);
        Assert.False(flags.GeneralInterrogation);

        // Re-encoding strips the reserved bit and produces canonical integrity-only TrgOps.
        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("0288", out var encoded));
        Assert.Equal("0208", Convert.ToHexString(encoded.RawValue.ToArray()));
    }

    [Fact]
    public void TrgOpsComparison_Ignores_OnlyDeclaredUnusedPaddingBits()
    {
        var expected = MmsDataValue.BitString(2, [0x04]);
        var paddingVariant = MmsDataValue.BitString(2, [0x07]);
        var differentSignificantValue = MmsDataValue.BitString(2, [0x44]);

        var paddingComparison = MmsReportControlFieldCodec.CompareTriggerOptions(expected, paddingVariant);
        Assert.True(paddingComparison.IsComparable);
        Assert.True(paddingComparison.IsSemanticMatch);
        Assert.False(paddingComparison.IsRawExact);
        Assert.True(paddingComparison.PaddingOnlyDifference);
        Assert.Equal("0204", paddingComparison.ExpectedHex);
        Assert.Equal("0207", paddingComparison.ActualHex);

        var differentComparison = MmsReportControlFieldCodec.CompareTriggerOptions(expected, differentSignificantValue);
        Assert.True(differentComparison.IsComparable);
        Assert.False(differentComparison.IsSemanticMatch);
        Assert.False(differentComparison.PaddingOnlyDifference);
    }

    [Fact]
    public void LiveOptFlds_060000_Decodes_As_No_Optional_Fields()
    {
        var flags = MmsReportControlFieldCodec.DecodeOptionalFields("060000");

        Assert.False(flags.Reserved);
        Assert.False(flags.ReasonForInclusion);
        Assert.False(flags.DataSetName);
        Assert.False(flags.DataReference);
    }

    [Fact]
    public void TemporaryOptFlds_061800_Decodes_And_Reencodes_ReasonPlusDataSetName()
    {
        var flags = MmsReportControlFieldCodec.DecodeOptionalFields("061800");

        Assert.True(flags.ReasonForInclusion);
        Assert.True(flags.DataSetName);
        Assert.False(flags.DataReference);

        Assert.True(MmsReportControlFieldCodec.TryEncodeOptionalFields("061800", out var encoded));
        Assert.Equal("061800", Convert.ToHexString(encoded.RawValue.ToArray()));
    }

    [Fact]
    public void OptFldsComparison_Ignores_OnlySixDeclaredPaddingBits()
    {
        var expected = MmsDataValue.BitString(6, [0x18, 0x00]);
        var paddingVariant = MmsDataValue.BitString(6, [0x18, 0x3F]);
        var differentSignificantValue = MmsDataValue.BitString(6, [0x10, 0x00]);

        var paddingComparison = MmsReportControlFieldCodec.CompareOptionalFields(expected, paddingVariant);
        Assert.True(paddingComparison.IsSemanticMatch);
        Assert.False(paddingComparison.IsRawExact);
        Assert.True(paddingComparison.PaddingOnlyDifference);

        Assert.False(MmsReportControlFieldCodec.CompareOptionalFields(expected, differentSignificantValue).IsSemanticMatch);
    }

    [Theory]
    [InlineData("020400")]
    [InlineData("0004")]
    [InlineData("zz04")]
    [InlineData("1234")]
    public void RawDisplayParser_Rejects_WrongShape_Or_InvalidUnusedBits(string text)
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions(text);

        Assert.False(flags.Reserved);
        Assert.False(flags.DataChange);
        Assert.False(flags.QualityChange);
        Assert.False(flags.DataUpdate);
        Assert.False(flags.Integrity);
        Assert.False(flags.GeneralInterrogation);
        Assert.False(flags.ApplicationTrigger);
    }
}
