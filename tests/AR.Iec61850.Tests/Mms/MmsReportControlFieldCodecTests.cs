using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportControlFieldCodecTests
{
    [Fact]
    public void TriggerOptions_Encodes_Dchg_Qchg_Dupd_Integrity_And_Gi_After_Reserved_Bit()
    {
        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions(
            "dchg qchg dupd integrity GI",
            out var value));

        Assert.Equal(MmsDataKind.BitString, value.Kind);
        Assert.Equal(new byte[] { 2, 0x7C }, value.RawValue);
    }

    [Fact]
    public void OptionalFields_Encodes_Event_Diagnostics_And_ConfRev()
    {
        Assert.True(MmsReportControlFieldCodec.TryEncodeOptionalFields(
            "sequence-number report-timestamp reason-for-inclusion data-set data-reference conf-revision",
            out var value));

        Assert.Equal(MmsDataKind.BitString, value.Kind);
        Assert.Equal(new byte[] { 6, 0x7C, 0x80 }, value.RawValue);
    }

    [Fact]
    public void TriggerOptions_Decodes_Gi_Only_From_Live_Rendered_BitString()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("bits(04, unused=2)");

        Assert.False(flags.Reserved);
        Assert.False(flags.DataChange);
        Assert.False(flags.QualityChange);
        Assert.False(flags.DataUpdate);
        Assert.False(flags.Integrity);
        Assert.True(flags.GeneralInterrogation);
        Assert.False(flags.ApplicationTrigger);
    }

    [Fact]
    public void TriggerOptions_Decodes_ReservedPlusIntegrity_Without_Mislabeling_DchgOrGi()
    {
        var flags = MmsReportControlFieldCodec.DecodeTriggerOptions("bits(88, unused=2)");

        Assert.True(flags.Reserved);
        Assert.False(flags.DataChange);
        Assert.True(flags.Integrity);
        Assert.False(flags.GeneralInterrogation);
    }

    [Fact]
    public void OptionalFields_Decodes_BufferOverflow_Only_From_Live_Rendered_BitString()
    {
        var flags = MmsReportControlFieldCodec.DecodeOptionalFields("bits(0200, unused=6)");

        Assert.False(flags.SequenceNumber);
        Assert.False(flags.ReportTimestamp);
        Assert.False(flags.ReasonForInclusion);
        Assert.False(flags.DataSetName);
        Assert.False(flags.DataReference);
        Assert.True(flags.BufferOverflow);
        Assert.False(flags.EntryId);
        Assert.False(flags.ConfigurationRevision);
        Assert.False(flags.Segmentation);
    }

    [Fact]
    public void Decoders_Also_Accept_Engineer_Readable_Names()
    {
        var trigger = MmsReportControlFieldCodec.DecodeTriggerOptions("dchg qchg");
        var optional = MmsReportControlFieldCodec.DecodeOptionalFields("sequence-number buffer-overflow");

        Assert.True(trigger.DataChange);
        Assert.True(trigger.QualityChange);
        Assert.True(optional.SequenceNumber);
        Assert.True(optional.BufferOverflow);
    }
}
