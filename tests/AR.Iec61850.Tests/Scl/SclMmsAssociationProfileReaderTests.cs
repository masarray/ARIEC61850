using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclMmsAssociationProfileReaderTests
{
    [Fact]
    public void Reader_Extracts_ConnectedAp_Mms_Endpoint_And_Association_Context()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                  <P type="IP-SUBNET">255.255.255.0</P>
                  <P type="IP-GATEWAY">192.0.2.1</P>
                  <P type="OSI-AP-Title">1,3,9999,23</P>
                  <P type="OSI-AE-Qualifier">23</P>
                  <P type="OSI-PSEL">00000001</P>
                  <P type="OSI-SSEL">0001</P>
                  <P type="OSI-TSEL">0000</P>
                  <P type="VENDOR-EXT">kept-visible</P>
                </Address>
                <GSE ldInst="LD0" cbName="GCB01">
                  <Address>
                    <P type="APPID">1001</P>
                    <P type="MAC-Address">01-0C-CD-01-00-01</P>
                  </Address>
                </GSE>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var result = SclMmsAssociationProfileReader.Read(xml);

        var profile = Assert.Single(result.AccessPoints);
        Assert.Empty(result.Warnings);
        Assert.Equal("IED01", profile.IedName);
        Assert.Equal("AP1", profile.AccessPointName);
        Assert.Equal("StationBus", profile.SubNetworkName);
        Assert.Equal("8-MMS", profile.SubNetworkType);
        Assert.True(profile.HasNetworkEndpoint);
        Assert.Equal("192.0.2.10", profile.Endpoint.IpAddress);
        Assert.Equal("255.255.255.0", profile.Endpoint.IpSubnet);
        Assert.Equal("192.0.2.1", profile.Endpoint.IpGateway);
        Assert.Equal("1,3,9999,23", profile.Association.ApTitle);
        Assert.Equal("23", profile.Association.AeQualifierText);
        Assert.Equal(23, profile.Association.AeQualifier);
        Assert.Equal("00000001", profile.Association.PresentationSelector);
        Assert.Equal("0001", profile.Association.SessionSelector);
        Assert.Equal("0000", profile.Association.TransportSelector);
        Assert.Equal("kept-visible", profile.Parameters["VENDOR-EXT"]);
        Assert.False(profile.Parameters.ContainsKey("APPID"));
        Assert.False(profile.Parameters.ContainsKey("MAC-Address"));
    }

    [Fact]
    public void Reader_Preserves_Invalid_AeQualifier_And_Reports_Warning()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1">
                <Address>
                  <P type="IP">198.51.100.10</P>
                  <P type="OSI-AE-Qualifier">not-an-integer</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var result = SclMmsAssociationProfileReader.Read(xml);

        var profile = Assert.Single(result.AccessPoints);
        Assert.Null(profile.Association.AeQualifier);
        Assert.Equal("not-an-integer", profile.Association.AeQualifierText);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("invalid OSI-AE-Qualifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Reader_Keeps_ConnectedAp_Without_Address_Explicitly_Unresolved()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1" />
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var result = SclMmsAssociationProfileReader.Read(xml);

        var profile = Assert.Single(result.AccessPoints);
        Assert.False(profile.HasNetworkEndpoint);
        Assert.Empty(profile.Parameters);
        Assert.Same(profile, result.Find("ied01", "ap1"));
    }
}
