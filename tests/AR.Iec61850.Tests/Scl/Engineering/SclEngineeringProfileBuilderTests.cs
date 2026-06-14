using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Scl.Engineering;

public sealed class SclEngineeringProfileBuilderTests
{
    [Fact]
    public void Builder_Creates_Engineering_Profile_From_Minimal_Station()
    {
        var profile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());

        Assert.Equal("minimal-station.scd", profile.SourceName);
        Assert.Single(profile.Ieds);
        Assert.Single(profile.AccessPoints);
        Assert.Single(profile.LogicalDevices);
        // The minimal station contains LN0 plus two functional LNs (TCTR and XCBR).
        Assert.Equal(3, profile.LogicalNodes.Count);
        Assert.Contains(profile.LogicalNodes, ln => ln.Reference == "MU01LD0/LLN0" && ln.LnClass == "LLN0");
        Assert.Contains(profile.LogicalNodes, ln => ln.Reference == "MU01LD0/TCTR1" && ln.LnClass == "TCTR");
        Assert.Contains(profile.LogicalNodes, ln => ln.Reference == "MU01LD0/XCBR1" && ln.LnClass == "XCBR");
        Assert.Equal(2, profile.DataSetCount);
        Assert.Equal(1, profile.ReportControlCount);
        Assert.Equal(1, profile.GooseStreamCount);
        Assert.Equal(1, profile.SampledValuesStreamCount);
        Assert.True(profile.Capabilities.HasServerModel);
        Assert.True(profile.Capabilities.HasDataSets);
        Assert.True(profile.Capabilities.HasReports);
        Assert.True(profile.Capabilities.HasGoose);
        Assert.True(profile.Capabilities.HasSampledValues);
        Assert.True(profile.Capabilities.HasControlObjects);
        Assert.DoesNotContain(profile.Findings, f => f.Severity == "High");
    }

    [Fact]
    public void Builder_Extracts_Subscriber_ExtRef_Map_And_Service_Declarations()
    {
        var profile = new SclEngineeringProfileBuilder().Parse(ExtRefFixture(), "extref.scd");

        Assert.Single(profile.ExternalReferences);
        var extRef = profile.ExternalReferences[0];
        Assert.Equal("SUB01/LD0/GGIO1", extRef.SubscriberReference);
        Assert.Equal("PUB01/LD0/XCBR1.Pos.stVal", extRef.SourceSignalReference);
        Assert.Equal("GOOSE", extRef.ServiceType);
        Assert.Equal("GCB01", extRef.SourceControlBlockName);
        Assert.True(profile.Capabilities.HasExternalReferences);
        Assert.True(profile.Capabilities.FileServiceDeclared);
        Assert.True(profile.Capabilities.LogServiceDeclared);
        Assert.True(profile.Capabilities.GooseServiceDeclared);
    }

    [Fact]
    public void Builder_Flags_Incomplete_Process_Bus_Bindings()
    {
        var xml = File.ReadAllText(SclParserTests.MinimalStationPath())
            .Replace("<P type=\"APPID\">1001</P>", string.Empty, StringComparison.Ordinal)
            .Replace("<P type=\"MAC-Address\">01-0C-CD-04-00-01</P>", string.Empty, StringComparison.Ordinal);

        var profile = new SclEngineeringProfileBuilder().Parse(xml, "broken.scd");

        Assert.Contains(profile.Findings, f => f.Code == "SCL_GOOSE_ADDRESS_INCOMPLETE" && f.Severity == "High");
        Assert.Contains(profile.Findings, f => f.Code == "SCL_SV_ADDRESS_INCOMPLETE" && f.Severity == "High");
    }

    [Fact]
    public void Markdown_Includes_Profile_Sections()
    {
        var profile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());

        var markdown = profile.ToMarkdown();

        Assert.Contains("# SCL Engineering Profile", markdown);
        Assert.Contains("## Capability Matrix", markdown);
        Assert.Contains("## Expected Process Bus Streams", markdown);
        Assert.Contains("## Expected Report Sessions", markdown);
        Assert.Contains("## Subscriber External References", markdown);
        Assert.Contains("MU01LD0/LLN0$GO$GCB01", markdown);
    }

    private static string ExtRefFixture() => """
    <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
      <Header id="EXTREF_TEST" version="1" revision="0" />
      <IED name="PUB01">
        <Services>
          <FileHandling />
          <Log />
          <GOOSE />
        </Services>
        <AccessPoint name="P1">
          <Server>
            <LDevice inst="LD0">
              <LN0 lnClass="LLN0" lnType="LLN0Type">
                <DataSet name="dsGO">
                  <FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" />
                </DataSet>
                <GSEControl name="GCB01" datSet="dsGO" appID="pub-goose" confRev="1" type="GOOSE" />
              </LN0>
              <LN lnClass="XCBR" inst="1" lnType="XCBRType" />
            </LDevice>
          </Server>
        </AccessPoint>
      </IED>
      <IED name="SUB01">
        <AccessPoint name="P1">
          <Server>
            <LDevice inst="LD0">
              <LN0 lnClass="LLN0" lnType="LLN0Type" />
              <LN lnClass="GGIO" inst="1" lnType="GGIOType">
                <Inputs>
                  <ExtRef iedName="PUB01" ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" serviceType="GOOSE" srcCBName="GCB01" />
                </Inputs>
              </LN>
            </LDevice>
          </Server>
        </AccessPoint>
      </IED>
      <DataTypeTemplates>
        <LNodeType id="LLN0Type" lnClass="LLN0" />
        <LNodeType id="XCBRType" lnClass="XCBR"><DO name="Pos" type="PosType" /></LNodeType>
        <LNodeType id="GGIOType" lnClass="GGIO" />
        <DOType id="PosType" cdc="DPC"><DA name="stVal" bType="BOOLEAN" fc="ST" /></DOType>
      </DataTypeTemplates>
    </SCL>
    """;
}
