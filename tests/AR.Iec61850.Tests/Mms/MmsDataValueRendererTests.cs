using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDataValueRendererTests
{
    [Fact]
    public void Render_StructureShowsChildrenInsteadOfLastLeafOnly()
    {
        var value = MmsDataValue.Structure([
            MmsDataValue.Boolean(false),
            MmsDataValue.BitString(0, new byte[] { 0x3F }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 12, 0, 24, TimeSpan.Zero), 0x3F))
        ]);

        var rendered = MmsDataValueRenderer.Render(value, "LD0/PTOC1.Str");

        Assert.Contains("Structure(3)", rendered.Compact);
        Assert.Contains("stVal=false", rendered.Compact);
        Assert.Contains("q=bits(3F", rendered.Compact);
        Assert.Contains("t=2026-06-12", rendered.Compact);
    }

    [Fact]
    public void DecodeReadResponse_PreservesStructuredAccessResultSuccess()
    {
        var structure = MmsDataCodec.Encode(MmsDataValue.Structure([
            MmsDataValue.Boolean(true),
            MmsDataValue.BitString(0, new byte[] { 0x00 }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 12, 0, 24, TimeSpan.Zero), 0))
        ]));
        var success = BerWriter.EncodeTlv(0xA0, structure);
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, success);
        var readResponse = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var invoke = new byte[] { 0x02, 0x01, 0x0C };
        var mms = BerWriter.EncodeTlv(0xA1, invoke.Concat(readResponse).ToArray());
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        var result = MmsReadResponseDecoder.DecodeSingleVariable(payload, 12);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal(MmsDataKind.Structure, result.Value!.Kind);
        Assert.Equal(3, result.Value.Children.Count);
    }
}
