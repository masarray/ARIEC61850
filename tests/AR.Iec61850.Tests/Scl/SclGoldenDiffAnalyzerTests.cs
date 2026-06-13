using AR.Iec61850.Scl.Analysis;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclGoldenDiffAnalyzerTests
{
    [Fact]
    public void Analyze_Returns_No_Material_Difference_For_Identical_Scl()
    {
        using var scope = new TempSclScope();
        var path = scope.Write("same.iid", BuildScl("INS", includeReport: true));

        var report = SclGoldenDiffAnalyzer.Analyze(path, path);

        Assert.False(report.HasMaterialDifferences);
        Assert.Equal(1, report.LogicalDevices.GoldenCount);
        Assert.Equal(1, report.Reports.GoldenCount);
        Assert.Empty(report.CdcDifferences);
    }

    [Fact]
    public void Analyze_Detects_Cdc_Difference_For_Shared_Data_Object()
    {
        using var scope = new TempSclScope();
        var golden = scope.Write("golden.iid", BuildScl("INS", includeReport: true));
        var candidate = scope.Write("candidate.iid", BuildScl("SPS", includeReport: true));

        var report = SclGoldenDiffAnalyzer.Analyze(golden, candidate);

        var diff = Assert.Single(report.CdcDifferences);
        Assert.Equal("LLN0.Beh", diff.Key);
        Assert.Contains("INS", diff.GoldenCdc);
        Assert.Contains("SPS", diff.CandidateCdc);
    }

    [Fact]
    public void WriteReport_Writes_Markdown_And_Json_Evidence()
    {
        using var scope = new TempSclScope();
        var golden = scope.Write("golden.iid", BuildScl("INS", includeReport: true));
        var candidate = scope.Write("candidate.iid", BuildScl("INS", includeReport: false));
        var output = Path.Combine(scope.DirectoryPath, "diff");

        var files = SclGoldenDiffAnalyzer.WriteReport(golden, candidate, output);

        Assert.Contains(files, x => x.EndsWith("scl-golden-diff-report.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, x => x.EndsWith("scl-golden-diff-report.json", StringComparison.OrdinalIgnoreCase));
        Assert.All(files, file => Assert.True(File.Exists(file), file));
    }

    private static string BuildScl(string cdc, bool includeReport)
    {
        var report = includeReport ? "<ReportControl name=\"brcbA01\" buffered=\"true\" indexed=\"false\" datSet=\"DataSet\" confRev=\"1\" />" : string.Empty;
        return $$"""
            <SCL version="2007" revision="B" xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED1">
                <Services><DynAssociation /><GetDirectory /></Services>
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="PROT">
                      <LN0 lnClass="LLN0" lnType="LT_LLN0">
                        <DataSet name="DataSet"><FCDA ldInst="PROT" lnClass="LLN0" doName="Beh" fc="ST" /></DataSet>
                        {{report}}
                        <SettingControl numOfSGs="1" />
                      </LN0>
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LT_LLN0" lnClass="LLN0"><DO name="Beh" type="DO_Beh" /></LNodeType>
                <DOType id="DO_Beh" cdc="{{cdc}}"><DA name="stVal" fc="ST" bType="Enum" type="BehaviourKind" /></DOType>
                <EnumType id="BehaviourKind"><EnumVal ord="1">on</EnumVal></EnumType>
              </DataTypeTemplates>
            </SCL>
            """;
    }

    private sealed class TempSclScope : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ar61850-scldiff-" + Guid.NewGuid().ToString("N"));

        public TempSclScope()
        {
            Directory.CreateDirectory(DirectoryPath);
        }

        public string Write(string name, string content)
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for Windows test runners that may still hold file handles.
            }
        }
    }
}
