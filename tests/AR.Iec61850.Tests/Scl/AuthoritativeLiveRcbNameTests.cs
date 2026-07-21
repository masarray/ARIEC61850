using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class AuthoritativeLiveRcbNameTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void ApplyReportControlConfiguration_Preserves_A_BRCB_1201_Without_Second_Index()
    {
        var source = XDocument.Parse(
            """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="BCU7SL">
                <Services><ConfReportControl max="57" /></Services>
                <AccessPoint name="AP1"><Server><LDevice inst="CTRL"><LN0 lnClass="LLN0" lnType="LN0_TYPE">
                  <DataSet name="ARIED_8550BE6C"><FCDA ldInst="CTRL" lnClass="CSWI" lnInst="1" doName="Pos" daName="stVal" fc="ST" /></DataSet>
                  <ReportControl name="A_BRCB_1201" buffered="true" datSet="ARIED_8550BE6C" confRev="4">
                    <TrgOps dchg="false" qchg="false" dupd="false" period="false" />
                    <OptFields seqNum="false" timeStamp="false" reasonCode="false" dataSet="false" dataRef="false" entryID="false" configRef="false" />
                    <RptEnabled max="1" />
                  </ReportControl>
                </LN0></LDevice></Server></AccessPoint>
              </IED>
              <DataTypeTemplates><LNodeType id="LN0_TYPE" lnClass="LLN0" /></DataTypeTemplates>
            </SCL>
            """);
        var model = new LiveIedModelDiscoveryDocument
        {
            ReportControls =
            [
                new LiveIedReportControlModel
                {
                    Reference = "BCU7SLCTRL/LLN0.BR.A_BRCB_1201",
                    Domain = "BCU7SLCTRL",
                    LogicalNode = "LLN0",
                    Name = "A_BRCB_1201",
                    Buffered = true,
                    DataSetReference = "BCU7SLCTRL/LLN0.ARIED_8550BE6C",
                    ConfRev = "4"
                }
            ]
        };

        var result = AuthoritativeLiveIedSclExporter.ApplyReportControlConfiguration(
            source,
            model,
            SclSchemaProfiles.Get(SclSchemaProfile.Edition1V16));

        var reportControl = Assert.Single(result.Descendants(Scl + "ReportControl"));
        Assert.Equal("A_BRCB_1201", (string?)reportControl.Attribute("name"));
        Assert.Equal("false", (string?)reportControl.Attribute("indexed"));
        Assert.Equal("1", (string?)Assert.Single(reportControl.Elements(Scl + "RptEnabled")).Attribute("max"));
        Assert.DoesNotContain("A_BRCB_120101", result.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }
}