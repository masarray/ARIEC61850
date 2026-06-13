using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsFileDirectoryTests
{
    [Fact]
    public void Request_Uses_High_Tag_Number_For_FileDirectory_Service()
    {
        var request = MmsFileDirectoryRequest.Build(7, "COMTRADE");

        Assert.Contains((byte)0xBF, request);
        Assert.Contains((byte)0x4D, request);
        Assert.Contains((byte)'C', request);
    }

    [Fact]
    public void Decode_Reads_FileDirectory_Entries()
    {
        var fileName = BerWriter.EncodeTlv(0x30,
            BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii("COMTRADE"))
                .Concat(BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii("fault.cfg")))
                .ToArray());
        var attributes = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 1,
            BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 0, BerWriter.EncodeUnsignedInteger(1234))
                .Concat(BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 1, new byte[] { 0x01, 0x02, 0x03, 0x04 }))
                .ToArray());
        var entry = BerWriter.EncodeTlv(0x30, fileName.Concat(attributes).ToArray());
        var list = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 0, entry);
        var more = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: false, 1, new byte[] { 0x00 });
        var service = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 77, list.Concat(more).ToArray());
        var response = BerWriter.EncodeTlv(0xA1,
            new byte[] { 0x02, 0x01, 0x07 }
                .Concat(service)
                .ToArray());

        var result = MmsFileDirectoryResponseDecoder.Decode(response, 7, string.Empty);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.MoreFollows);
        var entryResult = Assert.Single(result.Entries);
        Assert.Equal("COMTRADE/fault.cfg", entryResult.Name);
        Assert.Equal("COMTRADE/fault.cfg", entryResult.Path);
        Assert.Equal((uint)1234, entryResult.SizeBytes.GetValueOrDefault());
        Assert.Equal("01020304", entryResult.LastModifiedDisplay);
    }
}
