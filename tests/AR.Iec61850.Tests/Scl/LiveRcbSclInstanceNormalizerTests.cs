using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveRcbSclInstanceNormalizerTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void Normalize_Exports_Exact_Runtime_Name_Without_Appending_Another_01()
    {
        var document = XDocument.Parse(
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

        var result = LiveRcbSclInstanceNormalizer.Normalize(document, "A_BRCB_1201");

        var reportControl = Assert.Single(document.Descendants(Scl + "ReportControl"));
        Assert.Equal("A_BRCB_1201", (string?)reportControl.Attribute("name"));
        Assert.Equal("false", (string?)reportControl.Attribute("indexed"));
        Assert.Equal("1", (string?)Assert.Single(reportControl.Elements(Scl + "RptEnabled")).Attribute("max"));
        Assert.Equal("1", (string?)Assert.Single(document.Descendants(Scl + "ConfReportControl")).Attribute("max"));
        Assert.Equal("A_BRCB_1201", result.ExactRuntimeReportControlName);

        var importedRuntimeName = string.Equals(
            (string?)reportControl.Attribute("indexed"),
            "true",
            StringComparison.OrdinalIgnoreCase)
                ? $"{(string?)reportControl.Attribute("name")}01"
                : (string?)reportControl.Attribute("name");
        Assert.Equal("A_BRCB_1201", importedRuntimeName);
        Assert.DoesNotContain("A_BRCB_120101", document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_Rejects_Multiple_ReportControl_Definitions()
    {
        var document = XDocument.Parse(
            """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED"><AccessPoint name="AP"><Server><LDevice inst="LD"><LN0 lnClass="LLN0" lnType="T">
                <ReportControl name="A" buffered="true" datSet="DS" confRev="1" />
                <ReportControl name="B" buffered="true" datSet="DS" confRev="1" />
              </LN0></LDevice></Server></AccessPoint></IED>
            </SCL>
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            LiveRcbSclInstanceNormalizer.Normalize(document, "A_BRCB_1201"));

        Assert.Contains("exactly one ReportControl", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}