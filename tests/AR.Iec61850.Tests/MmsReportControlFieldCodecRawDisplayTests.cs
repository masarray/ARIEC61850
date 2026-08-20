using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsReportControlFieldCodecRawDisplayTests
{
    [Fact]
    public void LiveTrgOps_0204_Decodes_As_ApplicationTrigger_Without_Gi()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("0204");

        Assert.False(flags.DataChange);
        Assert.False(flags.GeneralInterrogation);
        Assert.True(flags.ApplicationTrigger);
    }

    [Fact]
    public void TemporaryTrgOps_0288_Decodes_And_Reencodes_DchgPlusGi()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("0288");

        Assert.True(flags.DataChange);
        Assert.True(flags.GeneralInterrogation);
        Assert.False(flags.ApplicationTrigger);

        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions("0288", out var encoded));
        Assert.Equal("0288", Convert.ToHexString(encoded.RawValue.ToArray()));
    }

    [Fact]
    public void LiveOptFlds_060000_Decodes_As_No_Optional_Fields()
    {
        var flags = MmsReportControlFieldCodec.DecodeOptionalFields("060000");

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

    [Theory]
    [InlineData("020400")]
    [InlineData("0004")]
    [InlineData("zz04")]
    [InlineData("1234")]
    public void RawDisplayParser_Rejects_WrongShape_Or_InvalidUnusedBits(string text)
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions(text);

        Assert.False(flags.DataChange);
        Assert.False(flags.QualityChange);
        Assert.False(flags.DataUpdate);
        Assert.False(flags.Integrity);
        Assert.False(flags.GeneralInterrogation);
        Assert.False(flags.ApplicationTrigger);
    }
}
