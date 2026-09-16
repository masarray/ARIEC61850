using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LegacySasSourceIdentityExportTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void SourceBackedExport_Preserves_Logical_Rcb_And_RptEnabled_Max()
    {
        var source = XDocument.Parse(Fixture());
        var descriptor = SclReportControlFilter.Inspect(source, "relay.cid", "AA1E1F06R4", "AP1")
            .ReportControls.Single();

        var result = LegacySasSclExporter.Build(
            source,
            "relay.cid",
            new LegacySasSclExportOptions
            {
                IedName = "AA1E1F06R4",
                AccessPointName = "AP1",
                SelectedReportControl = new SclReportControlSelection(descriptor.SelectionKey, "Buffer02"),
                PreserveSourceReportControlIdentity = true
            });

        var retained = Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.Equal("Buffer", (string?)retained.Attribute("name"));
        Assert.Equal("2", (string?)Assert.Single(retained.Elements(Scl + "RptEnabled")).Attribute("max"));
        Assert.EndsWith(".Buffer", result.RetainedReportControlReference, StringComparison.Ordinal);
        Assert.DoesNotContain("Buffer02", result.Document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultExport_Still_Uses_Exact_Runtime_Rcb_Name()
    {
        var source = XDocument.Parse(Fixture());
        var descriptor = SclReportControlFilter.Inspect(source, "relay.cid", "AA1E1F06R4", "AP1")
            .ReportControls.Single();

        var result = LegacySasSclExporter.Build(
            source,
            "relay.cid",
            new LegacySasSclExportOptions
            {
                IedName = "AA1E1F06R4",
                AccessPointName = "AP1",
                SelectedReportControl = new SclReportControlSelection(descriptor.SelectionKey, "Buffer02")
            });

        var retained = Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.Equal("Buffer02", (string?)retained.Attribute("name"));
        Assert.Equal("false", (string?)retained.Attribute("indexed"));
        Assert.Empty(retained.Elements(Scl + "RptEnabled"));
        Assert.EndsWith(".Buffer02", result.RetainedReportControlReference, StringComparison.Ordinal);
    }

    private static string Fixture()
        => """
           <?xml version="1.0" encoding="utf-8"?>
           <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
             <Header id="AA1E1F06R4" version="1" revision="0" toolID="Synthetic" nameStructure="IEDName" />
             <IED name="AA1E1F06R4">
               <Services><ConfReportControl max="32" /></Services>
               <AccessPoint name="AP1"><Server><LDevice inst="Application"><LN0 lnClass="LLN0" inst="" lnType="LN0_TYPE">
                 <DataSet name="Digital">
                   <FCDA ldInst="Application" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                 </DataSet>
                 <ReportControl name="Buffer" buffered="true" datSet="Digital" confRev="1">
                   <TrgOps dchg="true" qchg="true" dupd="false" period="false" gi="true" />
                   <OptFields seqNum="true" timeStamp="true" reasonCode="true" dataSet="true" dataRef="true" entryID="true" configRef="true" />
                   <RptEnabled max="2" />
                 </ReportControl>
               </LN0></LDevice></Server></AccessPoint>
             </IED>
             <DataTypeTemplates><LNodeType id="LN0_TYPE" lnClass="LLN0" /></DataTypeTemplates>
           </SCL>
           """;
}