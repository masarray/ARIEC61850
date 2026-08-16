using System.Reflection;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsInformationReportDataReferenceOrderingTests
{
    [Fact]
    public void Decoder_Normalizes_DataReferences_Before_Values_Without_Leaking_Metadata_As_Process_Value()
    {
        var wireItems = new MmsInformationReportItem[]
        {
            Item(0, MmsDataValue.VisibleString("LD0/LLN0$RP$urcb01")),
            // Six meaningful OptFlds bits; bit 5 (DataRef) is set.
            Item(1, MmsDataValue.BitString(2, [0x04])),
            // Two DataSet members are included.
            Item(2, MmsDataValue.BitString(6, [0xC0])),
            // IEC 61850 wire order: DataRef block BEFORE value block.
            Item(3, MmsDataValue.VisibleString("LD0/GGIO1$ST$Ind1$stVal")),
            Item(4, MmsDataValue.VisibleString("LD0/GGIO1$ST$Ind2$stVal")),
            Item(5, MmsDataValue.Boolean(false)),
            Item(6, MmsDataValue.Boolean(true))
        };

        var method = typeof(MmsInformationReportDecoder).GetMethod(
            "NormalizeIec61850ReportAccessResultOrder",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var normalized = Assert.IsAssignableFrom<IReadOnlyList<MmsInformationReportItem>>(
            method!.Invoke(null, [wireItems]));

        Assert.Equal(MmsDataKind.Boolean, normalized[3].Value!.Kind);
        Assert.False((bool)normalized[3].Value!.Value!);
        Assert.Equal(MmsDataKind.Boolean, normalized[4].Value!.Kind);
        Assert.True((bool)normalized[4].Value!.Value!);
        Assert.Equal("LD0/GGIO1$ST$Ind1$stVal", normalized[5].Value!.Value);
        Assert.Equal("LD0/GGIO1$ST$Ind2$stVal", normalized[6].Value!.Value);
        Assert.Equal(Enumerable.Range(0, normalized.Count), normalized.Select(item => item.Index));
    }

    private static MmsInformationReportItem Item(int index, MmsDataValue value)
        => new() { Index = index, Value = value };
}
