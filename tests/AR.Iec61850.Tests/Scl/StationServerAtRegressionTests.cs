using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;

namespace AR.Iec61850.Tests.Scl;

public sealed class StationServerAtRegressionTests
{
    [Fact]
    public void Parse_SiemensStyle_7Sx85_ServerAt_Preserves_Model_On_Both_Mms_Endpoints()
    {
        // Sanitized topology derived from a real Siemens station SCD.
        // Do not commit the customer's exported SCD or network configuration.
        var document = new SclWorkspaceService().Parse(StationFixture(), "station.scd");

        Assert.Equal(2, document.MmsEndpoints.Count);
        Assert.Equal(2, document.Ieds.Count);

        var j = document.Ieds.Single(ied => ied.AccessPointName == "J");
        var f = document.Ieds.Single(ied => ied.AccessPointName == "F");

        Assert.Equal("192.0.2.11", j.PreferredEndpoint!.IpAddress);
        Assert.Equal("198.51.100.12", f.PreferredEndpoint!.IpAddress);
        Assert.Equal(102, j.PreferredEndpoint.Port);
        Assert.Equal(102, f.PreferredEndpoint.Port);
        Assert.False(j.RequiresEndpointBinding);
        Assert.False(f.RequiresEndpointBinding);
        Assert.True(j.CanBrowseOffline);
        Assert.True(f.CanBrowseOffline);
        Assert.Single(j.DesignModel.LogicalDevices);
        Assert.Single(f.DesignModel.LogicalDevices);
        Assert.Equal(j.DesignModel.LogicalDevices[0].MmsDomain, f.DesignModel.LogicalDevices[0].MmsDomain);
        Assert.Equal("GR_X_7SX85Application", f.DesignModel.LogicalDevices[0].MmsDomain);
        Assert.Equal("F", f.DesignModel.AccessPointName);
        Assert.Equal("198.51.100.12", f.DesignModel.Host);
        Assert.Single(f.DataSets);
        Assert.Single(f.ReportControls);
        Assert.DoesNotContain(f.Findings, finding => finding.Code == "SCL_ACCESS_POINT_WITHOUT_SERVER");
        Assert.DoesNotContain(f.Findings, finding => finding.Code == "SCL_SERVER_AT_UNRESOLVED");
    }

    [Fact]
    public void Engineering_Profile_Valid_ServerAt_Is_Not_Reported_As_Missing_Server()
    {
        var profile = new SclEngineeringProfileBuilder().Parse(StationFixture(), "station.scd");

        Assert.Equal(2, profile.AccessPoints.Count);
        var f = profile.AccessPoints.Single(ap => ap.Name == "F");
        Assert.False(f.HasServer); // Not a physical Server: references J via ServerAt.
        Assert.Equal(1, f.LogicalDeviceCount);
        Assert.DoesNotContain(profile.Findings, item =>
            item.Code == "SCL_ACCESS_POINT_WITHOUT_SERVER" ||
            item.Code == "SCL_SERVER_AT_UNRESOLVED");
    }

    [Fact]
    public void SclAssisted_Preparation_Resolves_F_ServerAt_To_J_Domains_And_Fc_Roots()
    {
        var xml = StationFixture();
        var domainsJ = SclMmsDomainInventoryReader.Read(xml, "GR_X_7SX85", "J");
        var domainsF = SclMmsDomainInventoryReader.Read(xml, "GR_X_7SX85", "F");

        Assert.True(domainsJ.IsSuccess);
        Assert.True(domainsF.IsSuccess);
        Assert.Equal(domainsJ.ExpectedDomains, domainsF.ExpectedDomains);
        Assert.Equal("F", domainsF.AccessPointName);
        Assert.Single(domainsF.ExpectedDomains);

        var initialJ = SclInitialFcReadDesignBuilder.Read(xml, "GR_X_7SX85", "J");
        var initialF = SclInitialFcReadDesignBuilder.Read(xml, "GR_X_7SX85", "F");
        Assert.True(initialJ.IsSuccess, string.Join(" | ", initialJ.Errors));
        Assert.True(initialF.IsSuccess, string.Join(" | ", initialF.Errors));
        Assert.Equal("F", initialF.Model.AccessPointName);
        Assert.Equal(initialJ.Model.LogicalDevices.Count, initialF.Model.LogicalDevices.Count);
        Assert.Equal(initialJ.Model.DataSets.Count, initialF.Model.DataSets.Count);
        Assert.Equal(initialJ.Model.ReportControls.Count, initialF.Model.ReportControls.Count);
        Assert.Single(initialF.Model.LogicalDevices);
    }

    [Fact]
    public void Dangling_ServerAt_Fails_Closed_Without_Fabricating_Mms_Model_Or_Losing_Ip()
    {
        var broken = StationFixture().Replace(
            "<ServerAt apName=\"J\" />",
            "<ServerAt apName=\"NOT_PRESENT\" />",
            StringComparison.Ordinal);
        var document = new SclWorkspaceService().Parse(broken, "dangling.scd");

        var j = document.Ieds.Single(ied => ied.AccessPointName == "J");
        var f = document.Ieds.Single(ied => ied.AccessPointName == "F");
        Assert.True(j.CanBrowseOffline);
        Assert.False(f.CanBrowseOffline);
        Assert.Equal("198.51.100.12", f.PreferredEndpoint!.IpAddress);
        Assert.Contains(document.Findings, finding =>
            finding.Code == "SCL_SERVER_AT_UNRESOLVED" && finding.Severity == "High");
        var invalidDomains = SclMmsDomainInventoryReader.Read(broken, "GR_X_7SX85", "F");
        var invalidInitial = SclInitialFcReadDesignBuilder.Read(broken, "GR_X_7SX85", "F");
        Assert.False(invalidDomains.IsSuccess);
        Assert.False(invalidInitial.IsSuccess);
        Assert.Empty(invalidDomains.ExpectedDomains);
    }

    private static string StationFixture() => """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="C">
          <Header id="SanitizedStation" version="1" revision="1"/>
          <Communication>
            <SubNetwork name="Station_J" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="J">
                <Address><P type="IP">192.0.2.11</P></Address>
              </ConnectedAP>
            </SubNetwork>
            <SubNetwork name="Station_F" type="8-MMS">
              <ConnectedAP iedName="GR_X_7SX85" apName="F">
                <Address><P type="IP">198.51.100.12</P></Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
          <IED name="GR_X_7SX85" manufacturer="SIEMENS" type="7SX85">
            <AccessPoint name="J">
              <Server>
                <LDevice inst="Application">
                  <LN0 lnClass="LLN0" lnType="LLN0Type">
                    <DataSet name="Indications">
                      <FCDA ldInst="Application" lnClass="XCBR" lnInst="1" doName="Pos" fc="ST"/>
                    </DataSet>
                    <ReportControl name="Rpt_ind" datSet="Indications" buffered="true" confRev="1">
                      <TrgOps dchg="true" gi="true"/>
                      <RptEnabled max="2"/>
                    </ReportControl>
                  </LN0>
                  <LN lnClass="XCBR" inst="1" lnType="XCBRType"/>
                </LDevice>
              </Server>
            </AccessPoint>
            <AccessPoint name="F">
              <ServerAt apName="J" />
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LLN0Type" lnClass="LLN0"/>
            <LNodeType id="XCBRType" lnClass="XCBR"><DO name="Pos" type="PosType"/></LNodeType>
            <DOType id="PosType" cdc="DPC"><DA name="stVal" bType="Dbpos" fc="ST"/></DOType>
          </DataTypeTemplates>
        </SCL>
        """;
}
