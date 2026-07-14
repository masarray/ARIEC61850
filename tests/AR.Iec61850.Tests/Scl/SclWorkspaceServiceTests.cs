using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclWorkspaceServiceTests
{
    [Fact]
    public void Parse_Builds_Per_Ied_AccessPoint_Workspaces_With_Mms_Endpoints()
    {
        var workspace = new SclWorkspaceService().Parse(MultiIedScl(), "multi.scd");

        Assert.Equal(2, workspace.Ieds.Count);
        Assert.Equal(2, workspace.MmsEndpoints.Count);
        Assert.Equal(64, workspace.SourceSha256.Length);

        var iedA = workspace.Ieds.Single(x => x.IedName == "IED_A");
        Assert.Equal("P1", iedA.AccessPointName);
        Assert.NotNull(iedA.PreferredEndpoint);
        Assert.Equal("192.0.2.10", iedA.PreferredEndpoint!.IpAddress);
        Assert.Equal(102, iedA.PreferredEndpoint.Port);
        Assert.True(iedA.CanBrowseOffline);
        Assert.False(iedA.RequiresEndpointBinding);
        Assert.Single(iedA.DesignModel.LogicalDevices);
        Assert.Equal("IED_ALD0", iedA.DesignModel.LogicalDevices[0].MmsDomain);
        Assert.DoesNotContain(iedA.DesignModel.LogicalDevices, x => x.MmsDomain.StartsWith("IED_B", StringComparison.Ordinal));

        var iedB = workspace.Ieds.Single(x => x.IedName == "IED_B");
        Assert.Equal("P2", iedB.AccessPointName);
        Assert.Equal("192.0.2.11", iedB.PreferredEndpoint!.IpAddress);
        Assert.Equal(8102, iedB.PreferredEndpoint.Port);
        Assert.Single(iedB.DesignModel.LogicalDevices);
        Assert.Equal("IED_BLD1", iedB.DesignModel.LogicalDevices[0].MmsDomain);
    }

    [Fact]
    public void Parse_Keeps_Offline_Model_When_Communication_Is_Missing()
    {
        var workspace = new SclWorkspaceService().Parse(IcdWithoutCommunication(), "template.icd");

        var ied = Assert.Single(workspace.Ieds);
        Assert.Equal("TEMPLATE", ied.IedName);
        Assert.True(ied.CanBrowseOffline);
        Assert.True(ied.RequiresEndpointBinding);
        Assert.Null(ied.PreferredEndpoint);
        Assert.Contains(ied.Findings, x => x.Code == "SCL_MMS_ENDPOINT_UNASSIGNED");
    }

    [Fact]
    public void Parse_Retains_Duplicate_Endpoint_Assignments_And_Reports_Conflict()
    {
        var xml = MultiIedScl().Replace("192.0.2.11", "192.0.2.10", StringComparison.Ordinal)
            .Replace("<P type=\"MMS-Port\">8102</P>", string.Empty, StringComparison.Ordinal);

        var workspace = new SclWorkspaceService().Parse(xml, "duplicate.scd");

        Assert.Equal(2, workspace.MmsEndpoints.Count);
        Assert.Contains(workspace.Findings, x => x.Code == "SCL_MMS_ENDPOINT_CONFLICT" && x.Severity == "High");
    }

    [Fact]
    public void CompareLive_Accepts_Matching_Model_And_Blocks_Missing_Attribute()
    {
        var sclWorkspace = new SclWorkspaceService().Parse(MultiIedScl(), "multi.scd");
        var expected = sclWorkspace.Ieds.Single(x => x.IedName == "IED_A");

        var matching = new SclWorkspaceService().CompareLive(expected, expected.DesignModel);
        Assert.True(matching.IsCompatible);
        Assert.False(matching.RequiresFullDiscovery);
        Assert.Equal(matching.ExpectedAttributeCount, matching.MatchedAttributeCount);

        var expectedLd = expected.DesignModel.LogicalDevices.Single();
        var expectedLn = expectedLd.LogicalNodes.Single(x => x.Name == "XCBR1");
        var expectedDo = expectedLn.DataObjects.Single(x => x.Name == "Pos");
        var observed = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED_A",
            AccessPointName = "P1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = expectedLd.MmsDomain,
                    Inst = expectedLd.Inst,
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = expectedLn.Name,
                            LnClass = expectedLn.LnClass,
                            LnInst = expectedLn.LnInst,
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = expectedDo.Reference,
                                    Name = expectedDo.Name,
                                    InferredCdc = expectedDo.InferredCdc,
                                    Attributes = Array.Empty<LiveIedDataAttributeModel>()
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var mismatch = new SclWorkspaceService().CompareLive(expected, observed);
        Assert.False(mismatch.IsCompatible);
        Assert.True(mismatch.RequiresFullDiscovery);
        Assert.Contains(mismatch.Findings, x => x.Kind == SclLiveModelFindingKind.MissingLiveAttribute);
    }

    private static string MultiIedScl()
        => """
        <?xml version="1.0" encoding="UTF-8"?>
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Header id="MULTI_IED" version="1" revision="0" />
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED_A" apName="P1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                </Address>
              </ConnectedAP>
              <ConnectedAP iedName="IED_B" apName="P2">
                <Address>
                  <P type="IP">192.0.2.11</P>
                  <P type="MMS-Port">8102</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
          <IED name="IED_A" manufacturer="AR" type="RelayA" configVersion="1">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="LD0">
                  <LN0 lnClass="LLN0" lnType="LLN0Type" />
                  <LN lnClass="XCBR" inst="1" lnType="XCBRType" />
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <IED name="IED_B" manufacturer="AR" type="RelayB" configVersion="2">
            <AccessPoint name="P2">
              <Server>
                <LDevice inst="LD1">
                  <LN0 lnClass="LLN0" lnType="LLN0Type" />
                  <LN lnClass="MMXU" inst="1" lnType="MMXUType" />
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LLN0Type" lnClass="LLN0" />
            <LNodeType id="XCBRType" lnClass="XCBR">
              <DO name="Pos" type="PosType" />
            </LNodeType>
            <DOType id="PosType" cdc="DPC">
              <DA name="stVal" bType="Dbpos" fc="ST" />
              <DA name="q" bType="Quality" fc="ST" />
            </DOType>
            <LNodeType id="MMXUType" lnClass="MMXU">
              <DO name="A" type="AType" />
            </LNodeType>
            <DOType id="AType" cdc="MV">
              <DA name="mag" bType="Struct" type="AnalogueValue" fc="MX" />
            </DOType>
            <DAType id="AnalogueValue">
              <BDA name="f" bType="FLOAT32" />
            </DAType>
          </DataTypeTemplates>
        </SCL>
        """;

    private static string IcdWithoutCommunication()
        => """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Header id="TEMPLATE_ICD" />
          <IED name="TEMPLATE" manufacturer="AR" type="RelayTemplate">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="LD0">
                  <LN0 lnClass="LLN0" lnType="LLN0Type" />
                  <LN lnClass="XCBR" inst="1" lnType="XCBRType" />
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LLN0Type" lnClass="LLN0" />
            <LNodeType id="XCBRType" lnClass="XCBR">
              <DO name="Pos" type="PosType" />
            </LNodeType>
            <DOType id="PosType" cdc="DPC">
              <DA name="stVal" bType="Dbpos" fc="ST" />
            </DOType>
          </DataTypeTemplates>
        </SCL>
        """;
}
