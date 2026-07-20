using System.Text;
using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class FaultRecordVendorPackageTests
{
    [Fact]
    public void Extensionless_NonZero_Entry_Is_A_File_Not_A_Directory()
    {
        var entry = new MmsFileDirectoryEntry
        {
            Name = "FRA00019",
            Path = "FRA00019",
            SizeBytes = 119 * 1024
        };

        Assert.False(entry.IsLikelyDirectory);
    }

    [Fact]
    public void Extensionless_ZeroSize_Entry_Remains_A_Directory_Candidate()
    {
        var entry = new MmsFileDirectoryEntry
        {
            Name = "COMTRADE",
            Path = "COMTRADE",
            SizeBytes = 0
        };

        Assert.True(entry.IsLikelyDirectory);
    }

    [Fact]
    public void Catalog_Exposes_Extensionless_Fra_Record_As_Downloadable_Package()
    {
        var catalog = Iec61850FaultRecordCatalogBuilder.Build(
        [
            new MmsFileDirectoryEntry
            {
                Name = "FRA00019",
                Path = "FRA00019",
                SizeBytes = 119 * 1024,
                LastModifiedRaw = Encoding.ASCII.GetBytes("20260709135100Z")
            },
            new MmsFileDirectoryEntry
            {
                Name = "TEST FILE.txt",
                Path = "TEST FILE.txt",
                SizeBytes = 0
            }
        ]);

        var record = Assert.Single(catalog.Records);
        var file = Assert.Single(record.Files);
        Assert.Equal("FRA00019", record.BaseName);
        Assert.True(record.IsComplete);
        Assert.True(record.CanDownload);
        Assert.Equal("IED fault-record package", record.Completeness);
        Assert.Equal(Iec61850FaultRecordFileKind.VendorPackage, file.Kind);
        Assert.Equal("FRA00019", file.RemotePath);
    }

    [Fact]
    public void FileOpen_Allows_Extensionless_Remote_File_Name()
    {
        var request = MmsFileOpenRequest.Build(3, "FRA00019");

        Assert.NotEmpty(request);
    }
}
