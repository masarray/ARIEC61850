using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclReportControlFilterTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void Inspect_Provides_Stable_Keys_Type_And_DataSet_Member_Count()
    {
        var source = XDocument.Parse(Fixture());

        var inventory = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1");

        Assert.Equal(3, inventory.ReportControls.Count);
        var first = inventory.ReportControls.First(item => item.Name == "BRCB_EVENTS");
        Assert.Equal("Buffered", first.Type);
        Assert.Equal("Events", first.DataSetName);
        Assert.Equal(2, first.DataSetMemberCount);
        Assert.True(first.HasPopulatedDataSet);
        Assert.Contains("IED1|AP1|LD0|LLN0|BRCB_EVENTS", first.SelectionKey, StringComparison.Ordinal);
        Assert.Contains(inventory.Findings, finding => finding.Code == "SCL.REPORT_DATASET_EMPTY");
    }

    [Fact]
    public void Filter_Retains_Exactly_One_Rcb_And_Does_Not_Mutate_Source()
    {
        var source = XDocument.Parse(Fixture());
        var original = source.ToString(SaveOptions.DisableFormatting);
        var descriptor = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1")
            .ReportControls.Single(item => item.Name == "URCB_STATUS");

        var result = SclReportControlFilter.Filter(
            source,
            new SclReportControlFilterOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SelectedReportControls = new[] { new SclReportControlSelection(descriptor.SelectionKey) },
                RequireExactlyOneReportControl = true
            },
            "IED1.cid");

        Assert.Equal(original, source.ToString(SaveOptions.DisableFormatting));
        var retained = Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.Equal("URCB_STATUS", (string?)retained.Attribute("name"));
        Assert.Equal(2, result.RemovedReportControlCount);
        Assert.Equal(0, result.RemovedDataSetCount);
        Assert.Equal(2, result.Document.Descendants(Scl + "DataSet").Count());
        Assert.Single(new SclParser().Parse(result.Document, "filtered.cid").ReportControls);
    }

    [Fact]
    public void Filter_Collapses_Indexed_Rcb_To_Exact_Runtime_Name_And_Max_One()
    {
        var source = XDocument.Parse(Fixture());
        var descriptor = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1")
            .ReportControls.Single(item => item.Name == "BRCB_EVENTS");

        var result = SclReportControlFilter.Filter(
            source,
            new SclReportControlFilterOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SelectedReportControls = new[]
                {
                    new SclReportControlSelection(descriptor.SelectionKey, "BRCB_EVENTS03")
                }
            },
            "IED1.cid");

        var retained = Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.Equal("BRCB_EVENTS03", (string?)retained.Attribute("name"));
        Assert.Equal("false", (string?)retained.Attribute("indexed"));
        Assert.Equal("1", (string?)Assert.Single(retained.Elements(Scl + "RptEnabled")).Attribute("max"));
    }

    [Fact]
    public void Filter_Rejects_Missing_Or_Empty_DataSet()
    {
        var source = XDocument.Parse(Fixture());
        var inventory = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1");
        var empty = inventory.ReportControls.Single(item => item.Name == "URCB_EMPTY");

        var error = Assert.Throws<InvalidOperationException>(() => SclReportControlFilter.Filter(
            source,
            new SclReportControlFilterOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SelectedReportControls = new[] { new SclReportControlSelection(empty.SelectionKey) }
            },
            "IED1.cid"));

        Assert.Contains("empty DataSet", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyExporter_Applies_Edition1_Profile_And_Filter_With_Evidence()
    {
        var source = XDocument.Parse(Fixture());
        var descriptor = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1")
            .ReportControls.Single(item => item.Name == "URCB_STATUS");

        var result = LegacySasSclExporter.Build(
            source,
            "IED1.cid",
            new LegacySasSclExportOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SchemaProfile = SclSchemaProfile.Edition1V16,
                SelectedReportControl = new SclReportControlSelection(descriptor.SelectionKey)
            });

        Assert.Equal("Edition 1 (Schema V1.6)", result.SclSchema);
        Assert.Null(result.Document.Root!.Attribute("version"));
        Assert.Null(result.Document.Root.Attribute("revision"));
        Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.All(result.Document.Descendants(Scl + "TrgOps"), element => Assert.Null(element.Attribute("gi")));
        Assert.Equal(2, result.RemovedReportControlCount);
        Assert.Equal("Events", result.RetainedDataSetName);
        Assert.DoesNotContain(result.Findings, finding => finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegacyExporter_Preserves_Exact_Runtime_Rcb_Name_Without_Double_Indexing()
    {
        var source = XDocument.Parse(Fixture());
        var descriptor = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1")
            .ReportControls.Single(item => item.Name == "BRCB_EVENTS");

        var result = LegacySasSclExporter.Build(
            source,
            "IED1.cid",
            new LegacySasSclExportOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SchemaProfile = SclSchemaProfile.Edition1V16,
                SelectedReportControl = new SclReportControlSelection(
                    descriptor.SelectionKey,
                    "A_BRCB_1201")
            });

        var retained = Assert.Single(result.Document.Descendants(Scl + "ReportControl"));
        Assert.Equal("A_BRCB_1201", (string?)retained.Attribute("name"));
        Assert.Equal("false", (string?)retained.Attribute("indexed"));
        Assert.Empty(retained.Elements(Scl + "RptEnabled"));
        Assert.EndsWith(".A_BRCB_1201", result.RetainedReportControlReference, StringComparison.Ordinal);
        Assert.DoesNotContain("A_BRCB_120101", result.Document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterLiveModel_Retains_One_Rcb_And_Validates_DataSet()
    {
        var dataSet = new LiveIedDataSetModel
        {
            Reference = "IED1LD0/LLN0.Events",
            Name = "Events",
            MemberCount = 1,
            Members = new[] { new LiveIedDataSetMemberModel { Reference = "IED1LD0/XCBR1.Pos.stVal" } }
        };
        var selected = new LiveIedReportControlModel
        {
            Reference = "IED1LD0/LLN0.BR.BRCB01",
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            Name = "BRCB01",
            Buffered = true,
            DataSetReference = dataSet.Reference
        };
        var source = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED1",
            DataSets = new[] { dataSet },
            ReportControls = new[]
            {
                selected,
                new LiveIedReportControlModel
                {
                    Reference = "IED1LD0/LLN0.RP.URCB01",
                    Domain = "IED1LD0",
                    LogicalNode = "LLN0",
                    Name = "URCB01",
                    DataSetReference = dataSet.Reference
                }
            },
            Coverage = new LiveIedModelDiscoveryCoverage
            {
                ReportControlCount = 2,
                BufferedReportControlCount = 1,
                UnbufferedReportControlCount = 1,
                DataSetCount = 1
            }
        };

        var filtered = SclReportControlFilter.FilterLiveModel(source, selected.Reference);

        Assert.Same(selected, Assert.Single(filtered.ReportControls));
        Assert.Equal(1, filtered.Coverage.ReportControlCount);
        Assert.Equal(1, filtered.Coverage.BufferedReportControlCount);
        Assert.Equal(0, filtered.Coverage.UnbufferedReportControlCount);
        Assert.Equal(2, source.ReportControls.Count);
    }

    private static string Fixture()
        => """
           <?xml version="1.0" encoding="utf-8"?>
           <SCL xmlns="http://www.iec.ch/61850/2003/SCL" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" version="2007" revision="B">
             <Header id="IED1" version="1" revision="0" toolID="Synthetic" nameStructure="IEDName" />
             <IED name="IED1">
               <Services><ConfReportControl max="3" bufConf="true" /><ReportSettings owner="true" resvTms="true" /></Services>
               <AccessPoint name="AP1"><Server><LDevice inst="LD0"><LN0 lnClass="LLN0" inst="" lnType="LN0_TYPE">
                 <DataSet name="Events">
                   <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                   <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="q" fc="ST" />
                 </DataSet>
                 <DataSet name="Empty" />
                 <ReportControl name="BRCB_EVENTS" buffered="true" indexed="true" datSet="Events" confRev="1">
                   <TrgOps dchg="true" qchg="true" dupd="false" period="false" gi="true" />
                   <OptFields seqNum="true" timeStamp="true" reasonCode="true" dataSet="true" dataRef="true" entryID="true" configRef="true" />
                   <RptEnabled max="4" />
                 </ReportControl>
                 <ReportControl name="URCB_STATUS" buffered="false" indexed="false" datSet="Events" confRev="1">
                   <TrgOps dchg="true" qchg="true" dupd="false" period="false" gi="true" />
                   <OptFields seqNum="true" timeStamp="true" reasonCode="true" dataSet="true" dataRef="true" entryID="false" configRef="true" />
                   <RptEnabled max="1" />
                 </ReportControl>
                 <ReportControl name="URCB_EMPTY" buffered="false" indexed="false" datSet="Empty" confRev="1"><RptEnabled max="1" /></ReportControl>
               </LN0></LDevice></Server></AccessPoint>
             </IED>
             <DataTypeTemplates><LNodeType id="LN0_TYPE" lnClass="LLN0" /></DataTypeTemplates>
           </SCL>
           """;
}