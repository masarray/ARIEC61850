using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclParserTests
{
    [Fact]
    public void Parser_Extracts_Sv_Goose_Report_And_Dataset_Order()
    {
        var document = LoadMinimalStation();

        Assert.Equal("AR_MINIMAL_STATION", document.HeaderId);
        Assert.Equal(SclEdition.Edition2, document.Edition);
        Assert.Single(document.Ieds);
        Assert.Equal("MU01", document.Ieds[0].Name);
        Assert.Equal(2, document.DataSets.Count);
        Assert.Single(document.SampledValuesStreams);
        Assert.Single(document.GooseStreams);
        Assert.Single(document.ReportControls);
        Assert.Empty(document.Conflicts);

        var sv = document.SampledValuesStreams[0];
        Assert.Equal("MU01LD0/LLN0$SV$MSVCB01", sv.ControlBlockReference);
        Assert.Equal("MU01LD0/LLN0$MSVCB01", sv.SvId);
        Assert.Equal("MU01LD0/LLN0$dsSV", sv.DataSetReference);
        Assert.Equal((ushort)0x4001, sv.Address.AppId);
        Assert.Equal("01:0C:CD:04:00:01", sv.Address.DestinationMacText);
        Assert.Equal((ushort)200, sv.Address.VlanId);
        Assert.Equal((byte)4, sv.Address.VlanPriority);
        Assert.Equal(3U, sv.ConfigurationRevision);
        Assert.Equal((ushort)4000, sv.SampleRate);
        Assert.Equal("SmpPerSec", sv.SampleMode);
        Assert.Equal(2, sv.Entries.Count);
        Assert.Equal("MU01/LD0/TCTR1.Amp.instMag.i [MX]", sv.Entries[0].SignalReference);
        Assert.Equal("SAV", sv.Entries[0].Cdc);
        Assert.Equal("INT32", sv.Entries[0].BType);
        Assert.True(sv.Entries[1].IsQuality);

        var goose = document.GooseStreams[0];
        Assert.Equal("MU01LD0/LLN0$GO$GCB01", goose.ControlBlockReference);
        Assert.Equal("trip-goose", goose.GoId);
        Assert.Equal("MU01LD0/LLN0$dsGO", goose.DataSetReference);
        Assert.Equal((ushort)0x1001, goose.Address.AppId);
        Assert.Equal("01:0C:CD:01:00:01", goose.Address.DestinationMacText);
        Assert.Equal(3, goose.Entries.Count);
        Assert.Equal("DPC", goose.Entries[0].Cdc);
        Assert.True(goose.Entries[1].IsQuality);
        Assert.True(goose.Entries[2].IsTimestamp);

        var report = document.ReportControls[0];
        Assert.Equal("MU01LD0/LLN0$RP$URCB01", report.ControlBlockReference);
        Assert.False(report.Buffered);
        Assert.Equal("MU01LD0/LLN0$dsGO", report.DataSetReference);
        Assert.Equal(3, report.Entries.Count);
    }

    [Fact]
    public void Parser_Detects_Duplicate_AppId_Conflicts()
    {
        var xml = File.ReadAllText(MinimalStationPath())
            .Replace("</SCL>", "<IED name=\"MU01\" /></SCL>", StringComparison.Ordinal);

        var document = new SclParser().Parse(xml, "duplicate-ied.scd");

        Assert.Contains(document.Conflicts, c => c.Kind == "IED" && c.Key == "MU01");
        Assert.DoesNotContain(document.Warnings, w => w.Contains("missing DataSet", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void Parser_Detects_Edition_From_Root_Version_Not_Historical_Namespace()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Header id="ED2_TEST" version="1" revision="0" />
        </SCL>
        """;

        var document = new SclParser().Parse(xml, "ed2.iid");

        Assert.Equal(SclEdition.Edition2, document.Edition);
    }

    [Fact]
    public void Parser_Detects_Edition1_When_Historical_Namespace_Has_No_Root_Version()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Header id="ED1_TEST" version="" revision="" />
        </SCL>
        """;

        var document = new SclParser().Parse(xml, "ed1.icd");

        Assert.Equal(SclEdition.Edition1, document.Edition);
    }

    internal static SclDocument LoadMinimalStation()
        => new SclParser().Load(MinimalStationPath());

    internal static string MinimalStationPath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scl", "minimal-station.scd");
}
