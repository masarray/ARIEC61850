using System.Xml.Linq;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class InteroperableSclConverterTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void Convert_Selects_Header_Ied_And_Preserves_Reachable_Standard_Model()
    {
        var result = InteroperableSclConverter.Convert(XDocument.Parse(Fixture()), "LOCAL Siemens.cid");
        var root = Assert.IsType<XElement>(result.Document.Root);

        Assert.Equal(Scl + "SCL", root.Name);
        Assert.Equal("2007", (string?)root.Attribute("version"));
        Assert.Equal("B", (string?)root.Attribute("revision"));
        Assert.Null(root.Attribute("release"));
        Assert.Equal("LOCAL", result.SelectedIedName);
        Assert.Equal(new[] { "PEER" }, result.RemovedIedNames);

        var ied = Assert.Single(root.Elements(Scl + "IED"));
        Assert.Equal("LOCAL", (string?)ied.Attribute("name"));
        Assert.Equal("ACME", (string?)ied.Attribute("manufacturer"));
        Assert.Null(ied.Attribute(XName.Get("mode", "urn:vendor")));
        Assert.Empty(root.Descendants(Scl + "Private"));
        Assert.DoesNotContain(root.Descendants(), x => x.Name.NamespaceName == "urn:vendor");
        Assert.Empty(root.Descendants(Scl + "ClientServices"));
        Assert.Single(root.Descendants(Scl + "DataObjectDirectory"));
        Assert.Empty(root.Descendants(Scl + "Inputs"));
        Assert.Empty(root.Descendants(Scl + "IEDName"));
        Assert.Empty(root.Descendants(Scl + "Substation"));

        var connectedAp = Assert.Single(root.Descendants(Scl + "ConnectedAP"));
        Assert.Equal("LOCAL", (string?)connectedAp.Attribute("iedName"));
        Assert.Equal(new[] { "stVal", "q" }, root.Descendants(Scl + "FCDA").Select(x => (string?)x.Attribute("daName")));
        Assert.Single(root.Descendants(Scl + "ReportControl"));
        Assert.Single(root.Descendants(Scl + "GSEControl"));
        Assert.Single(root.Descendants(Scl + "DOI"));
        Assert.Equal("true", root.Descendants(Scl + "Val").Single().Value);

        Assert.Contains(root.Descendants(Scl + "LNodeType"), x => (string?)x.Attribute("id") == "LN_LOCAL");
        Assert.Contains(root.Descendants(Scl + "DOType"), x => (string?)x.Attribute("id") == "DO_LOCAL");
        Assert.Contains(root.Descendants(Scl + "DAType"), x => (string?)x.Attribute("id") == "DA_LOCAL");
        Assert.Contains(root.Descendants(Scl + "EnumType"), x => (string?)x.Attribute("id") == "ENUM_LOCAL");
        Assert.DoesNotContain(root.Descendants(), x => ((string?)x.Attribute("id"))?.Contains("PEER", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(root.Descendants(), x => ((string?)x.Attribute("id"))?.Contains("ORPHAN", StringComparison.Ordinal) == true);
        Assert.True(result.RemovedPrivateElementCount > 0);
        Assert.True(result.RemovedExternalInputCount > 0);
        Assert.True(result.RemovedUnusedTypeTemplateCount > 0);
    }

    [Fact]
    public void Convert_Preserves_DataSet_Order_And_Exact_Cdc_On_Reparse()
    {
        var result = InteroperableSclConverter.Convert(XDocument.Parse(Fixture()), "LOCAL.cid");
        var parsed = new SclParser().Parse(result.Document, "converted.iid");

        var ied = Assert.Single(parsed.Ieds);
        Assert.Equal("LOCAL", ied.Name);
        var dataSet = Assert.Single(parsed.DataSets);
        Assert.Equal(new[] { "stVal", "q" }, dataSet.Entries.Select(x => x.DaName));
        Assert.All(dataSet.Entries, entry => Assert.Equal("DPS", entry.Cdc));
        Assert.Single(parsed.GooseStreams);
        Assert.Single(parsed.ReportControls);
        Assert.DoesNotContain(result.Findings, x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Convert_Requires_Explicit_Ied_When_Multi_Ied_Source_Is_Ambiguous()
    {
        var source = XDocument.Parse(Fixture());
        source.Root!.Element(Scl + "Header")!.SetAttributeValue("id", "STATION");

        var error = Assert.Throws<InvalidOperationException>(() =>
            InteroperableSclConverter.Convert(source, "station.scd"));

        Assert.Contains("multiple IEDs", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCAL", error.Message, StringComparison.Ordinal);
        Assert.Contains("PEER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Explicit_Ied_Selection_Wins_And_PreserveAll_Is_Available()
    {
        var selected = InteroperableSclConverter.Convert(
            XDocument.Parse(Fixture()),
            "station.scd",
            new InteroperableSclConversionOptions { IedName = "PEER" });
        Assert.Equal("PEER", selected.SelectedIedName);
        Assert.Equal("PEER", Assert.Single(selected.Document.Root!.Elements(Scl + "IED")).Attribute("name")?.Value);

        var all = InteroperableSclConverter.Convert(
            XDocument.Parse(Fixture()),
            "station.scd",
            new InteroperableSclConversionOptions { PreserveAllIeds = true });
        Assert.Equal(2, all.Document.Root!.Elements(Scl + "IED").Count());
        Assert.Empty(all.RemovedIedNames);
        Assert.Equal(2, all.Document.Root.Descendants(Scl + "ConnectedAP").Count());
    }

    [Fact]
    public void Convert_Normalizes_NoNamespace_Scl_Into_The_Standard_Namespace()
    {
        var xml = Fixture().Replace("xmlns=\"http://www.iec.ch/61850/2003/SCL\"", string.Empty, StringComparison.Ordinal);

        var result = InteroperableSclConverter.Convert(XDocument.Parse(xml), "LOCAL.cid");

        Assert.Equal(Scl + "SCL", result.Document.Root!.Name);
        Assert.All(result.Document.Root.Descendants(), element => Assert.Equal(Scl, element.Name.Namespace));
        Assert.Single(new SclParser().Parse(result.Document, "normalized.iid").Ieds);
    }

    [Fact]
    public void WriteFiles_Writes_Interoperable_Iid_And_Audit_Evidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ariec61850-scl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "LOCAL.cid");
            var output = Path.Combine(directory, "LOCAL.interoperable.iid");
            File.WriteAllText(input, Fixture());

            var result = InteroperableSclConverter.WriteFiles(input, output);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.ReportPath));
            Assert.True(File.Exists(result.SummaryPath));
            Assert.Single(new SclParser().Load(result.OutputPath).Ieds);
            Assert.Contains("Interoperable SCL Conversion", File.ReadAllText(result.SummaryPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Fixture()
        => """
           <?xml version="1.0" encoding="UTF-8"?>
           <SCL xmlns="http://www.iec.ch/61850/2003/SCL"
                xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:v="urn:vendor"
                version="2007" revision="B" release="4">
             <Private type="Vendor-Root"><v:Data key="root" /></Private>
             <Header id="LOCAL" version="9" revision="4" toolID="Vendor Tool" nameStructure="IEDName">
               <History><Hitem version="9" revision="4" what="vendor export" /></History>
             </Header>
             <Substation name="S1"><VoltageLevel name="V1" /></Substation>
             <Communication>
               <SubNetwork name="Station" type="8-MMS">
                 <ConnectedAP iedName="PEER" apName="AP1"><Address><P type="IP">192.0.2.2</P></Address></ConnectedAP>
                 <ConnectedAP iedName="LOCAL" apName="AP1">
                   <Address><P type="IP" xsi:type="tP_IP">192.0.2.1</P></Address>
                   <GSE ldInst="LD0" cbName="GCB1"><Address><P type="APPID">0001</P></Address></GSE>
                 </ConnectedAP>
               </SubNetwork>
             </Communication>
             <IED name="LOCAL" manufacturer="ACME" type="Relay" configVersion="1" originalSclVersion="2007" originalSclRevision="B" originalSclRelease="4" v:mode="private">
               <Private type="Vendor-Ied"><v:Data key="ied" /></Private>
               <Services>
                 <ClientServices goose="true" />
                 <DataObjectDirectory />
                 <DataSetDirectory />
                 <ReadWrite />
               </Services>
               <AccessPoint name="AP1">
                 <Server>
                   <LDevice inst="LD0">
                     <LN0 lnClass="LLN0" inst="" lnType="LN_LOCAL">
                       <DataSet name="Events">
                         <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                         <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="q" fc="ST" />
                       </DataSet>
                       <ReportControl name="URCB1" datSet="Events" confRev="1">
                         <TrgOps dchg="true" qchg="true" dupd="false" period="false" gi="true" />
                         <OptFields seqNum="true" timeStamp="true" dataSet="true" reasonCode="true" dataRef="true" entryID="false" configRef="true" />
                       </ReportControl>
                       <GSEControl name="GCB1" type="GOOSE" datSet="Events" appID="LOCAL/LD0/LLN0/GCB1" confRev="1">
                         <IEDName apRef="AP1">PEER</IEDName>
                       </GSEControl>
                       <DOI name="Mod"><DAI name="stVal"><Val>true</Val></DAI></DOI>
                       <Inputs><ExtRef iedName="PEER" ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" serviceType="GOOSE" /></Inputs>
                     </LN0>
                     <LN prefix="" lnClass="XCBR" inst="1" lnType="LN_LOCAL" />
                   </LDevice>
                 </Server>
               </AccessPoint>
             </IED>
             <IED name="PEER" manufacturer="OTHER" type="Relay">
               <AccessPoint name="AP1"><Server><LDevice inst="LD0"><LN0 lnClass="LLN0" inst="" lnType="LN_PEER" /></LDevice></Server></AccessPoint>
             </IED>
             <DataTypeTemplates>
               <LNodeType id="LN_LOCAL" lnClass="LLN0"><DO name="Pos" type="DO_LOCAL" /></LNodeType>
               <LNodeType id="LN_PEER" lnClass="LLN0"><DO name="Mod" type="DO_PEER" /></LNodeType>
               <DOType id="DO_LOCAL" cdc="DPS">
                 <DA name="stVal" bType="Dbpos" fc="ST" dchg="true" />
                 <DA name="q" bType="Quality" fc="ST" qchg="true" />
                 <DA name="detail" bType="Struct" type="DA_LOCAL" fc="MX" />
                 <DA name="mode" bType="Enum" type="ENUM_LOCAL" fc="CF" />
               </DOType>
               <DOType id="DO_PEER" cdc="SPS"><DA name="stVal" bType="BOOLEAN" fc="ST" /></DOType>
               <DOType id="DO_ORPHAN" cdc="INS"><DA name="stVal" bType="INT32" fc="ST" /></DOType>
               <DAType id="DA_LOCAL"><BDA name="value" bType="INT32" /><BDA name="nested" bType="Struct" type="DA_NESTED" /></DAType>
               <DAType id="DA_NESTED"><BDA name="flag" bType="BOOLEAN" /></DAType>
               <DAType id="DA_ORPHAN"><BDA name="value" bType="INT32" /></DAType>
               <EnumType id="ENUM_LOCAL"><EnumVal ord="0">off</EnumVal><EnumVal ord="1">on</EnumVal></EnumType>
               <EnumType id="ENUM_ORPHAN"><EnumVal ord="0">none</EnumVal></EnumType>
             </DataTypeTemplates>
           </SCL>
           """;
}
