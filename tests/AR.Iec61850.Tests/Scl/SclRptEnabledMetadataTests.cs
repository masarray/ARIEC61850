using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclRptEnabledMetadataTests
{
    [Fact]
    public void Parser_Preserves_RptEnabled_Max_As_Declarative_Metadata()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Header id="RPT_ENABLED_MAX" version="1" revision="0" />
          <IED name="IED_A">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="LD0">
                  <LN0 lnClass="LLN0">
                    <ReportControl name="Unbuffer" rptID="IED_A/LD0/LLN0$RP$Unbuffer" buffered="false" indexed="true">
                      <RptEnabled max="2" />
                    </ReportControl>
                    <ReportControl name="Invalid" buffered="false">
                      <RptEnabled max="not-a-number" />
                    </ReportControl>
                    <ReportControl name="Missing" buffered="false" />
                  </LN0>
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
        </SCL>
        """;

        var document = new SclParser().Parse(xml, "rpt-enabled.iid");

        var declared = Assert.Single(document.ReportControls, report => report.Name == "Unbuffer");
        Assert.Equal(2U, declared.RptEnabledMax);
        Assert.True(declared.Indexed);

        var invalid = Assert.Single(document.ReportControls, report => report.Name == "Invalid");
        Assert.Null(invalid.RptEnabledMax);

        var missing = Assert.Single(document.ReportControls, report => report.Name == "Missing");
        Assert.Null(missing.RptEnabledMax);
    }
}
