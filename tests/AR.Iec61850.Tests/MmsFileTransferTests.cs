using System.Text;
using AR.Iec61850.Asn1;
using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsFileTransferTests
{
    [Fact]
    public void FileOpenRequest_EncodesConfirmedService72AndNormalizesPath()
    {
        var request = MmsFileOpenRequest.Build(17, @"COMTRADE\TRIP_001.cfg");
        var mms = MmsPresentation.StripPresentationPrefix(request);
        var outer = ReadSingle(mms);
        Assert.Equal((byte)0xA0, outer.EncodedTag);

        var children = BerReader.ReadChildren(outer.Value);
        Assert.Equal((ulong)17, BerReader.ReadUnsignedInteger(children[0])!.Value);
        var service = Assert.Single(children.Skip(1));
        Assert.Equal(BerClass.ContextSpecific, service.Class);
        Assert.True(service.Constructed);
        Assert.Equal(72, service.TagNumber);

        var fields = BerReader.ReadChildren(service.Value);
        var fileName = Assert.Single(fields.Where(field => field.TagNumber == 0));
        var segments = BerReader.ReadChildren(fileName.Value)
            .Select(BerReader.ReadAsciiString)
            .ToArray();
        Assert.Equal(new[] { "COMTRADE", "TRIP_001.cfg" }, segments);
    }

    [Fact]
    public void FileOpenRequest_RejectsTraversalPath()
    {
        Assert.Throws<ArgumentException>(() => MmsFileOpenRequest.Build(1, "../secret.cfg"));
    }

    [Fact]
    public void FileOpenResponse_DecodesStateMachineAndAttributes()
    {
        var response = BuildConfirmedResponse(
            invokeId: 4,
            serviceTag: 72,
            constructed: true,
            serviceValue: Concat(
                BerWriter.EncodeTlv(BerClass.ContextSpecific, false, 0, BerWriter.EncodeSignedInteger(23)),
                BerWriter.EncodeTlv(
                    BerClass.ContextSpecific,
                    true,
                    1,
                    Concat(
                        BerWriter.EncodeTlv(BerClass.ContextSpecific, false, 0, PositiveInteger(4096)),
                        BerWriter.EncodeTlv(BerClass.ContextSpecific, false, 1, Encoding.ASCII.GetBytes("20260717104530Z"))))));

        var result = MmsFileOpenResponseDecoder.Decode(response, expectedInvokeId: 4);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(23, result.FileReadStateMachineId);
        Assert.Equal((uint)4096, result.FileSizeBytes!.Value);
        Assert.Equal("20260717104530Z", Encoding.ASCII.GetString(result.LastModifiedRaw));
    }

    [Fact]
    public void FileReadResponse_DecodesBlockAndCompletionState()
    {
        byte[] block = [0x01, 0x02, 0xA5, 0xFF];
        var response = BuildConfirmedResponse(
            invokeId: 5,
            serviceTag: 73,
            constructed: true,
            serviceValue: Concat(
                BerWriter.EncodeTlv(BerClass.ContextSpecific, false, 0, block),
                BerWriter.EncodeTlv(BerClass.ContextSpecific, false, 1, BerWriter.EncodeBoolean(false))));

        var result = MmsFileReadResponseDecoder.Decode(response, expectedInvokeId: 5, fileReadStateMachineId: 23);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(block, result.Data);
        Assert.False(result.MoreFollows);
        Assert.Equal(23, result.FileReadStateMachineId);
    }

    [Fact]
    public void FileCloseResponse_AcceptsEmptyServiceResponse()
    {
        var response = BuildConfirmedResponse(
            invokeId: 6,
            serviceTag: 74,
            constructed: false,
            serviceValue: Array.Empty<byte>());

        var result = MmsFileCloseResponseDecoder.Decode(response, expectedInvokeId: 6, fileReadStateMachineId: 23);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(23, result.FileReadStateMachineId);
    }

    [Fact]
    public void FaultRecordCatalog_GroupsCompanionFilesAndMarksIncompleteRecords()
    {
        var entries = new[]
        {
            Entry("COMTRADE/TRIP_001.cfg", 120, "20260717100000Z"),
            Entry("COMTRADE/TRIP_001.dat", 4096, "20260717100001Z"),
            Entry("COMTRADE/TRIP_001.hdr", 80, "20260717100002Z"),
            Entry("COMTRADE/TRIP_002.dat", 2048, "20260716120000Z"),
            Entry("COMTRADE/readme.txt", 10, "20260715120000Z")
        };

        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
            entries,
            directoriesVisited: ["", "COMTRADE"]);

        Assert.Equal(2, catalog.Records.Count);
        Assert.Equal(1, catalog.CompleteRecordCount);
        Assert.Equal(4, catalog.FileCount);

        var complete = catalog.Records.Single(record => record.BaseName == "TRIP_001");
        Assert.True(complete.IsComplete);
        Assert.Equal("CFG + DAT", complete.Completeness);
        Assert.Equal(3, complete.Files.Count);
        Assert.Equal(4296L, complete.KnownSizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 10, 0, 2, TimeSpan.Zero), complete.LastModifiedUtc!.Value);

        var incomplete = catalog.Records.Single(record => record.BaseName == "TRIP_002");
        Assert.False(incomplete.IsComplete);
        Assert.Equal("Missing CFG", incomplete.Completeness);
    }

    [Theory]
    [InlineData("record.cff", Iec61850FaultRecordFileKind.Combined, "Combined COMTRADE file")]
    [InlineData("record.zip", Iec61850FaultRecordFileKind.Archive, "COMTRADE archive")]
    public void FaultRecordCatalog_TreatsSingleFilePackagesAsComplete(
        string fileName,
        Iec61850FaultRecordFileKind expectedKind,
        string expectedCompleteness)
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
            [Entry($"records/{fileName}", 1024, "20260717120000Z")]);

        var record = Assert.Single(catalog.Records);
        var file = Assert.Single(record.Files);
        Assert.True(record.IsComplete);
        Assert.Equal(expectedCompleteness, record.Completeness);
        Assert.Equal(expectedKind, file.Kind);
    }

    private static MmsFileDirectoryEntry Entry(string path, uint size, string modified)
        => new()
        {
            Name = path[(path.LastIndexOf('/') + 1)..],
            Path = path,
            SizeBytes = size,
            LastModifiedRaw = Encoding.ASCII.GetBytes(modified)
        };

    private static byte[] BuildConfirmedResponse(
        int invokeId,
        int serviceTag,
        bool constructed,
        byte[] serviceValue)
    {
        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed,
            serviceTag,
            serviceValue);
        var confirmedResponse = BerWriter.EncodeTlv(
            0xA1,
            Concat(
                BerWriter.EncodeTlv(0x02, PositiveInteger((uint)invokeId)),
                service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedResponse);
    }

    private static BerTlv ReadSingle(ReadOnlyMemory<byte> source)
    {
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(source, ref offset, out var tlv));
        Assert.Equal(source.Length, offset);
        return tlv;
    }

    private static byte[] PositiveInteger(uint value)
    {
        var encoded = BerWriter.EncodeUnsignedInteger(value);
        return encoded.Length > 0 && (encoded[0] & 0x80) != 0
            ? Concat([0x00], encoded)
            : encoded;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
