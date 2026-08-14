using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalPathFileTransferTests
{
    [Fact]
    public void CanonicalFileOpen_EncodesNestedPathAsOneGraphicString()
    {
        var request = MmsSingleGraphicStringFileOpenRequest.Build(
            invokeId: 2,
            remotePath: "COMTRADE/FRA00163.cfg");

        var strings = DecodeFileNameStrings(request);

        var item = Assert.Single(strings);
        Assert.Equal("COMTRADE/FRA00163.cfg", item);
    }

    [Fact]
    public void CanonicalFileOpen_NormalizesSeparatorsButPreservesCase()
    {
        var request = MmsSingleGraphicStringFileOpenRequest.Build(
            invokeId: 3,
            remotePath: @"COMTRADE\FRA00163.CFG");

        var strings = DecodeFileNameStrings(request);

        var item = Assert.Single(strings);
        Assert.Equal("COMTRADE/FRA00163.CFG", item);
    }

    [Fact]
    public void LegacyFileOpen_RemainsSegmentedForCompatibilityFallback()
    {
        var request = MmsFileOpenRequest.Build(
            invokeId: 4,
            remotePath: "COMTRADE/FRA00163.cfg");

        var strings = DecodeFileNameStrings(request);

        Assert.Equal(["COMTRADE", "FRA00163.cfg"], strings);
    }

    [Fact]
    public void CanonicalAndLegacyFileOpen_AreWireDistinctForNestedPath()
    {
        var canonical = MmsSingleGraphicStringFileOpenRequest.Build(
            invokeId: 5,
            remotePath: "COMTRADE/FRA00163.cfg");
        var legacy = MmsFileOpenRequest.Build(
            invokeId: 5,
            remotePath: "COMTRADE/FRA00163.cfg");

        Assert.NotEqual(Convert.ToHexString(canonical), Convert.ToHexString(legacy));
        Assert.Equal(["COMTRADE/FRA00163.cfg"], DecodeFileNameStrings(canonical));
        Assert.Equal(["COMTRADE", "FRA00163.cfg"], DecodeFileNameStrings(legacy));
    }

    private static string[] DecodeFileNameStrings(byte[] presentationRequest)
    {
        var mms = MmsPresentation.StripPresentationPrefix(presentationRequest);
        var outer = ReadSingle(mms);
        Assert.Equal((byte)0xA0, outer.EncodedTag);

        var confirmedRequest = BerReader.ReadChildren(outer.Value);
        Assert.True(confirmedRequest.Count >= 2);
        Assert.Equal((byte)0x02, confirmedRequest[0].EncodedTag);

        var service = confirmedRequest[1];
        Assert.Equal(BerClass.ContextSpecific, service.Class);
        Assert.True(service.Constructed);
        Assert.Equal(72, service.TagNumber);

        var fields = BerReader.ReadChildren(service.Value);
        var fileName = Assert.Single(fields.Where(field =>
            field.Class == BerClass.ContextSpecific &&
            field.Constructed &&
            field.TagNumber == 0));

        return BerReader.ReadChildren(fileName.Value)
            .Select(item =>
            {
                Assert.Equal((byte)0x19, item.EncodedTag);
                return BerReader.ReadAsciiString(item);
            })
            .ToArray();
    }

    private static BerTlv ReadSingle(ReadOnlyMemory<byte> source)
    {
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(source, ref offset, out var tlv));
        Assert.Equal(source.Length, offset);
        return tlv;
    }
}
