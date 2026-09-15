using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LegacySasMultiRcbExportTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void Build_Retains_Selected_Analog_And_Digital_Rcbs_With_Separate_DataSets()
    {
        var source = XDocument.Parse(Fixture());
        var inventory = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1");
        var analog = inventory.ReportControls.Single(item => item.Name == "URCB_ANALOG");
        var digital = inventory.ReportControls.Single(item => item.Name == "BRCB_DIGITAL");

        var result = LegacySasSclExporter.Build(
            source,
            "IED1.cid",
            new LegacySasSclExportOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SchemaProfile = SclSchemaProfile.Edition1V16,
                SelectedReportControls = new[]
                {
                    new SclReportControlSelection(analog.SelectionKey),
                    new SclReportControlSelection(digital.SelectionKey)
                }
            });

        Assert.Equal(2, result.RetainedReportControlCount);
        Assert.Equal(2, result.Document.Descendants(Scl + "ReportControl").Count());
        Assert.Contains(result.RetainedReportControls, item => item.DataSetName == "Analog" && item.DataSetMemberCount == 2);
        Assert.Contains(result.RetainedReportControls, item => item.DataSetName == "Digital" && item.DataSetMemberCount == 3);
        Assert.Equal(5, result.RetainedDataSetMemberCount);
        Assert.Equal(1, result.RemovedReportControlCount);
        Assert.Contains("Analog", result.RetainedDataSetName, StringComparison.Ordinal);
        Assert.Contains("Digital", result.RetainedDataSetName, StringComparison.Ordinal);
    }

    private static string Fixture()
        => """
           <?xml version="1.0" encoding="utf-8"?>
           <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
             <Header id="IED1" />
             <IED name="IED1">
               <AccessPoint name="AP1"><Server><LDevice inst="LD0"><LN0 lnClass="LLN0" inst="" lnType="LN0_TYPE">
                 <DataSet name="Analog">
                   <FCDA ldInst="LD0" lnClass="MMXU" lnInst="1" doName="A.phsA" daName="cVal.mag.f" fc="MX" />
                   <FCDA ldInst="LD0" lnClass="MMXU" lnInst="1" doName="PhV.phsA" daName="cVal.mag.f" fc="MX" />
                 </DataSet>
                 <DataSet name="Digital">
                   <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                   <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="q" fc="ST" />
                   <FCDA ldInst="LD0" lnClass="CSWI" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                 </DataSet>
                 <ReportControl name="URCB_ANALOG" buffered="false" indexed="false" datSet="Analog" confRev="1" />
                 <ReportControl name="BRCB_DIGITAL" buffered="true" indexed="false" datSet="Digital" confRev="1" />
                 <ReportControl name="URCB_UNUSED" buffered="false" indexed="false" datSet="Analog" confRev="1" />
               </LN0></LDevice></Server></AccessPoint>
             </IED>
             <DataTypeTemplates><LNodeType id="LN0_TYPE" lnClass="LLN0" /></DataTypeTemplates>
           </SCL>
           """;
}
