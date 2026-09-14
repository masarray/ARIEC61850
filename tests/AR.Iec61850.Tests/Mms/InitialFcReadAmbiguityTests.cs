using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Mms;

public sealed class InitialFcReadAmbiguityTests
{
    [Fact]
    public void Scl_Design_Fails_Closed_When_One_Server_Reuses_Inst_For_Different_Domains()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED01">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="LD0" ldName="DOMAIN_A">
                      <LN0 lnClass="LLN0" inst="" lnType="LNT0" />
                    </LDevice>
                    <LDevice inst="LD0" ldName="DOMAIN_B">
                      <LN0 lnClass="LLN0" inst="" lnType="LNT0" />
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LNT0" lnClass="LLN0">
                  <DO name="Beh" type="DOT_Beh" />
                </LNodeType>
                <DOType id="DOT_Beh" cdc="INS">
                  <DA name="stVal" bType="INT32" fc="ST" />
                </DOType>
              </DataTypeTemplates>
            </SCL>
            """;

        var design = SclInitialFcReadDesignBuilder.Read(xml, "IED01", "AP1");

        Assert.False(design.IsSuccess);
        Assert.Contains(design.Errors, error =>
            error.Contains("conflicting MMS domains", StringComparison.Ordinal));
    }
}
