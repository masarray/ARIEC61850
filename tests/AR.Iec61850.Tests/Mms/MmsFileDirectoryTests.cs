using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsFileDirectoryTests
{
    [Fact]
    public void Request_Uses_High_Tag_Number_And_Preserves_Full_File_Specification()
    {
        var request = MmsFileDirectoryRequest.Build(7, @"COMTRADE\FRA00019.cfg");
        var mms = MmsPresentation.StripPresentationPrefix(request);
        var outer = ReadSingle(mms);
        var service = Assert.Single(BerReader.ReadChildren(outer.Value).Skip(1));
        Assert.Equal(BerClass.ContextSpecific, service.Class);
        Assert.True(service.Constructed);
        Assert.Equal(77, service.TagNumber);

        var specification = Assert.Single(BerReader.ReadChildren(service.Value));
        var graphicString = Assert.Single(BerReader.ReadChildren(specification.Value));
        Assert.Equal((byte)0x19, graphicString.EncodedTag);
        Assert.Equal("COMTRADE/FRA00019.cfg", BerReader.ReadAsciiString(graphicString));
    }

    [Fact]
    public void Decode_Reads_Standard_Nested_List_Without_Collapsing_Companion_Files()
    {
        var cfg = BuildDirectoryEntry("COMTRADE/FRA00019.cfg", 2048, [0x01, 0x02]);
        var dat = BuildDirectoryEntry("COMTRADE/FRA00019.dat", 119_000, [0x03, 0x04]);
        var sequenceOfEntries = BerWriter.EncodeTlv(0x30, cfg.Concat(dat).ToArray());
        var list = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 0, sequenceOfEntries);
        var more = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 1, [0x00]);
        var response = BuildConfirmedResponse(7, list.Concat(more).ToArray());

        var result = MmsFileDirectoryResponseDecoder.Decode(response, 7, "COMTRADE");

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.MoreFollows);
        Assert.Equal(2, result.Entries.Count);
        Assert.Collection(
            result.Entries,
            entry =>
            {
                Assert.Equal("COMTRADE/FRA00019.cfg", entry.Name);
                Assert.Equal("COMTRADE/FRA00019.cfg", entry.Path);
                Assert.Equal((uint)2048, entry.SizeBytes);
            },
            entry =>
            {
                Assert.Equal("COMTRADE/FRA00019.dat", entry.Name);
                Assert.Equal("COMTRADE/FRA00019.dat", entry.Path);
                Assert.Equal((uint)119_000, entry.SizeBytes);
            });
    }

    [Fact]
    public void Decode_Accepts_Directory_Entries_Directly_Under_List_Wrapper()
    {
        var entry = BuildDirectoryEntry("fault.cfg", 1234, [0x01, 0x02, 0x03, 0x04]);
        var list = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 0, entry);
        var more = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 1, [0x00]);
        var response = BuildConfirmedResponse(8, list.Concat(more).ToArray());

        var result = MmsFileDirectoryResponseDecoder.Decode(response, 8, "COMTRADE");

        Assert.True(result.IsSuccess, result.Message);
        var decoded = Assert.Single(result.Entries);
        Assert.Equal("fault.cfg", decoded.Name);
        Assert.Equal("COMTRADE/fault.cfg", decoded.Path);
        Assert.Equal((uint)1234, decoded.SizeBytes);
        Assert.Equal("01020304", decoded.LastModifiedDisplay);
    }

    private static byte[] BuildDirectoryEntry(string fileName, uint size, byte[] modified)
    {
        var name = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            0,
            BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(fileName)));
        var attributes = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            1,
            BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 0, BerWriter.EncodeUnsignedInteger(size))
                .Concat(BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 1, modified))
                .ToArray());
        return BerWriter.EncodeTlv(0x30, name.Concat(attributes).ToArray());
    }

    private static byte[] BuildConfirmedResponse(int invokeId, byte[] serviceValue)
    {
        var service = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 77, serviceValue);
        return BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger((uint)invokeId))
                .Concat(service)
                .ToArray());
    }

    private static BerTlv ReadSingle(ReadOnlyMemory<byte> source)
    {
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(source, ref offset, out var tlv));
        Assert.Equal(source.Length, offset);
        return tlv;
    }
}