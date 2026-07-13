using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportControlFieldCodecTests
{
    [Fact]
    public void TriggerOptions_Encodes_Dchg_Qchg_Dupd_Integrity_And_Gi()
    {
        Assert.True(MmsReportControlFieldCodec.TryEncodeTriggerOptions(
            "dchg qchg dupd integrity GI",
            out var value));

        Assert.Equal(MmsDataKind.BitString, value.Kind);
        Assert.Equal(new byte[] { 2, 0xF8 }, value.RawValue);
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
}
