using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclAssistedMmsConvergenceHardeningTests
{
    [Fact]
    public void Conflicting_Duplicate_Critical_Association_Parameter_Fails_Closed()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                  <P type="OSI-AP-Title">1,3,9999,23</P>
                  <P type="OSI-AE-Qualifier">23</P>
                  <P type="OSI-PSEL">00000001</P>
                  <P type="OSI-SSEL">0001</P>
                  <P type="OSI-TSEL">0000</P>
                  <P type="OSI-TSEL">0001</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var profiles = SclMmsAssociationProfileReader.Read(xml);
        var remote = Assert.Single(profiles.AccessPoints);

        Assert.Equal(string.Empty, remote.Association.TransportSelector);
        Assert.Contains(profiles.Warnings, warning =>
            warning.Contains("conflicting duplicate OSI-TSEL", StringComparison.Ordinal));

        var plan = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault);

        Assert.False(plan.IsSuccess);
        Assert.Contains(plan.Errors, error => error.Contains("OSI-TSEL", StringComparison.Ordinal));
    }

    [Fact]
    public void Identical_Duplicate_Association_Parameter_Is_Collapsed_Deterministically()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                  <P type="OSI-AP-Title">1,3,9999,23</P>
                  <P type="OSI-AE-Qualifier">23</P>
                  <P type="OSI-PSEL">00000001</P>
                  <P type="OSI-SSEL">0001</P>
                  <P type="OSI-TSEL">0000</P>
                  <P type="OSI-TSEL">0000</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var profiles = SclMmsAssociationProfileReader.Read(xml);
        var remote = Assert.Single(profiles.AccessPoints);

        Assert.Equal("0000", remote.Association.TransportSelector);
        Assert.Contains(profiles.Warnings, warning =>
            warning.Contains("repeats OSI-TSEL with the same value", StringComparison.Ordinal));
        Assert.True(SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault).IsSuccess);
    }

    [Theory]
    [InlineData("65536")]
    [InlineData("-1")]
    public void AeQualifier_Outside_Unsigned16_Range_Is_Rejected(string qualifier)
    {
        var xml = $$"""
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED01" apName="AP1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                  <P type="OSI-AP-Title">1,3,9999,23</P>
                  <P type="OSI-AE-Qualifier">{{qualifier}}</P>
                  <P type="OSI-PSEL">00000001</P>
                  <P type="OSI-SSEL">0001</P>
                  <P type="OSI-TSEL">0000</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
        </SCL>
        """;

        var profiles = SclMmsAssociationProfileReader.Read(xml);
        var remote = Assert.Single(profiles.AccessPoints);

        Assert.Null(remote.Association.AeQualifier);
        Assert.Contains(profiles.Warnings, warning =>
            warning.Contains("expected 0..65535", StringComparison.Ordinal));
        Assert.False(SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.ExistingRuntimeDefault).IsSuccess);
    }
}
