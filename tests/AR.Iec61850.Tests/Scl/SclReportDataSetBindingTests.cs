using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclReportDataSetBindingTests
{
    [Theory]
    [InlineData("Events")]
    [InlineData("LLN0$Events")]
    [InlineData("LD0/LLN0$Events")]
    [InlineData("IED1LD0/LLN0$Events")]
    [InlineData("IED1/LD0/LLN0.Events")]
    public void Parser_Resolves_Common_Report_DataSet_Reference_Forms(string dataSetReference)
    {
        var document = new SclParser().Parse(BuildScl(dataSetReference, includeDataSet: true, includeMember: true));

        var report = Assert.Single(document.ReportControls);
        Assert.Equal(SclDataSetBindingStatus.Resolved, report.DataSetBindingStatus);
        Assert.Equal("IED1LD0/LLN0$Events", report.DataSetReference);
        Assert.Single(report.Entries);
    }

    [Fact]
    public void EngineeringProfile_Classifies_Unresolved_Report_DataSet_As_Warning_Not_Empty()
    {
        var profile = new SclEngineeringProfileBuilder().Parse(
            BuildScl("LLN0$MissingEvents", includeDataSet: true, includeMember: true));

        var finding = Assert.Single(profile.Findings.Where(f => f.Code == "SCL_REPORT_DATASET_UNRESOLVED"));
        Assert.Equal("Warning", finding.Severity);
        Assert.DoesNotContain(profile.Findings, f => f.Code == "SCL_REPORT_DATASET_EMPTY");
    }

    [Fact]
    public void EngineeringProfile_Uses_Empty_Only_For_A_Resolved_DataSet_Without_Fcda()
    {
        var document = new SclParser().Parse(
            BuildScl("LLN0$Events", includeDataSet: true, includeMember: false));
        var report = Assert.Single(document.ReportControls);
        Assert.Equal(SclDataSetBindingStatus.ResolvedEmpty, report.DataSetBindingStatus);

        var profile = new SclEngineeringProfileBuilder().Parse(
            BuildScl("LLN0$Events", includeDataSet: true, includeMember: false));
        var finding = Assert.Single(profile.Findings.Where(f => f.Code == "SCL_REPORT_DATASET_EMPTY"));
        Assert.Equal("High", finding.Severity);
        Assert.DoesNotContain(profile.Findings, f => f.Code == "SCL_REPORT_DATASET_UNRESOLVED");
    }

    [Fact]
    public void EngineeringProfile_Classifies_Unassigned_Indexed_Report_As_Warning()
    {
        var profile = new SclEngineeringProfileBuilder().Parse(
            BuildScl(null, includeDataSet: false, includeMember: false, indexed: true));

        var finding = Assert.Single(profile.Findings.Where(f => f.Code == "SCL_REPORT_DATASET_UNASSIGNED"));
        Assert.Equal("Warning", finding.Severity);
        Assert.DoesNotContain(profile.Findings, f => f.Code == "SCL_REPORT_DATASET_EMPTY");
    }

    private static string BuildScl(
        string? reportDataSet,
        bool includeDataSet,
        bool includeMember,
        bool indexed = true)
    {
        var dataSet = includeDataSet
            ? $"""
              <DataSet name="Events">
                {(includeMember ? "<FCDA ldInst=\"LD0\" lnClass=\"LLN0\" doName=\"Beh\" daName=\"stVal\" fc=\"ST\" />" : string.Empty)}
              </DataSet>
              """
            : string.Empty;
        var dataSetAttribute = reportDataSet is null ? string.Empty : $" datSet=\"{reportDataSet}\"";

        return $"""
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Header id="REPORT_DATASET_BINDING" />
          <IED name="IED1">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="LD0">
                  <LN0 lnClass="LLN0" lnType="LN0_TYPE">
                    {dataSet}
                    <ReportControl name="RCB01" buffered="false" indexed="{indexed.ToString().ToLowerInvariant()}" confRev="1"{dataSetAttribute} />
                  </LN0>
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
        </SCL>
        """;
    }
}
