using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class ComtradeRioSidecarCatalogTests
{
    private static MmsFileDirectoryEntry File(string path, uint size = 10) =>
        new() { Path = path, Name = Path.GetFileName(path), SizeBytes = size };

    [Fact]
    public void RioSidecar_JoinsOnlySameDirectoryAndBasename_WithCaseInsensitiveExtension()
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
        [
            File("dir/line_1.cfg", 100),
            File("dir/line_1.dat", 200),
            File("dir/line_1.RIO", 30),
            File("dir/line_2.RIO", 40), // no matching primary
            File("other/line_1.RIO", 50) // wrong folder
        ]);
        var record = Assert.Single(catalog.Records);
        Assert.Equal("line_1", record.BaseName);
        Assert.True(record.IsComplete);
        Assert.Equal(3, record.Files.Count);
        Assert.Contains(record.Files, f =>
            f.Name == "line_1.RIO" &&
            f.Kind == Iec61850FaultRecordFileKind.RioSidecar &&
            f.RemotePath == "dir/line_1.RIO");
        Assert.DoesNotContain(record.Files, f =>
            f.RemotePath == "other/line_1.RIO");
        Assert.Equal(330L, record.KnownSizeBytes);
    }

    [Fact]
    public void OrphanRio_IsNotAuthorizedAsFaultRecord()
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build([File("line_1.RIO")]);
        Assert.Empty(catalog.Records);
        Assert.True(Iec61850FaultRecordCatalogBuilder.IsSupportedFile("line_1.rio"));
    }

    [Fact]
    public void RioDoesNotMakeIncompleteCfgOrDatComplete()
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
            [File("line_1.cfg"), File("line_1.rio")]);
        var record = Assert.Single(catalog.Records);
        Assert.False(record.IsComplete);
        Assert.Equal(2, record.Files.Count);
        Assert.False(record.CanDownload && record.IsComplete);
    }

    [Fact]
    public void DuplicateOrDifferentlyCasedRemoteDirectoryListing_DoesNotDuplicateRio()
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
        [
            File("dir/line_1.cfg"),
            File("dir/line_1.dat"),
            File("dir/line_1.RIO"),
            File("dir/line_1.RIO")
        ]);
        Assert.Equal(3, Assert.Single(catalog.Records).Files.Count);
    }
}
