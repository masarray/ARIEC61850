using System.Xml.Linq;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Tests.Scl.Engineering;

public sealed class SclCanonicalImporterTests
{
    [Fact]
    public void Import_Scopes_One_Ied_And_AccessPoint_And_Honors_LdName()
    {
        var result = SclCanonicalImporter.Import(
            XDocument.Parse(BuildSingleIedScl()),
            new SclCanonicalImportOptions
            {
                IedName = "IED_A",
                AccessPointName = "P1",
                SourceName = "sample.iid"
            });

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var model = Assert.IsType<CanonicalIedModel>(result.Model);
        Assert.Equal("IED_A", model.Identity.Name);
        Assert.Equal(CanonicalEvidenceSource.SclDeclared, model.Identity.Provenance.Source);
        Assert.Equal(CanonicalConfidence.Exact, model.Identity.Provenance.Confidence);
        Assert.Equal("P1", model.Communication.AccessPointName.Value);
        Assert.Equal("192.0.2.10", model.Communication.Host.Value);
        Assert.Equal(102, model.Communication.Port.Value);
        Assert.Single(model.LogicalDevices);
        Assert.Equal("MMS_CUSTOM_DOMAIN", model.Strings.Resolve(model.LogicalDevices[0].MmsDomain));
        Assert.NotEmpty(model.Signals);
        Assert.All(model.Signals, signal => Assert.Equal(CanonicalEvidenceSource.SclDeclared, signal.Provenance.Source));
        Assert.Equal(CanonicalIngressKind.SclFile, model.Source.Ingress);
        Assert.Contains("LT_LLN0", model.Source.OriginalTypeAliases);
    }

    [Fact]
    public void Import_Fails_Closed_When_Ied_Selection_Is_Ambiguous()
    {
        var xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED_A"><AccessPoint name="P1"><Server /></AccessPoint></IED>
              <IED name="IED_B"><AccessPoint name="P1"><Server /></AccessPoint></IED>
            </SCL>
            """;

        var result = SclCanonicalImporter.Import(XDocument.Parse(xml));

        Assert.False(result.IsSuccess);
        Assert.Equal(SclCanonicalImportStatus.AmbiguousIed, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("IED_A", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("IED_B", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_Fails_Closed_When_AccessPoint_Selection_Is_Ambiguous()
    {
        var xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED_A">
                <AccessPoint name="P1"><Server /></AccessPoint>
                <AccessPoint name="P2"><Server /></AccessPoint>
              </IED>
            </SCL>
            """;

        var result = SclCanonicalImporter.Import(XDocument.Parse(xml));

        Assert.False(result.IsSuccess);
        Assert.Equal(SclCanonicalImportStatus.AmbiguousAccessPoint, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("P1", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("P2", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_Does_Not_Invent_Absent_Communication_Values()
    {
        var xml = BuildSingleIedScl()
            .Replace("<Communication><SubNetwork name=\"StationBus\" type=\"8-MMS\"><ConnectedAP iedName=\"IED_A\" apName=\"P1\"><Address><P type=\"IP\">192.0.2.10</P><P type=\"MMS-Port\">102</P></Address></ConnectedAP></SubNetwork></Communication>", string.Empty, StringComparison.Ordinal);

        var result = SclCanonicalImporter.Import(XDocument.Parse(xml));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        var model = Assert.IsType<CanonicalIedModel>(result.Model);
        Assert.Equal(CanonicalKnowledgeState.Unknown, model.Communication.Host.State);
        Assert.Equal(CanonicalKnowledgeState.Unknown, model.Communication.Port.State);
        Assert.Null(model.Communication.Host.Value);
    }

    [Fact]
    public void Knowledge_State_Distinguishes_False_Unknown_And_NotRepresentable()
    {
        var knownFalse = CanonicalFact<bool>.Known(false, CanonicalEvidenceSource.SclDeclared);
        var unknown = CanonicalFact<bool>.Unknown(CanonicalEvidenceSource.SclDeclared);
        var notRepresentable = CanonicalFact<bool>.NotRepresentable(CanonicalEvidenceSource.SclDeclared);

        Assert.True(knownFalse.IsKnown);
        Assert.False(knownFalse.Value);
        Assert.Equal(CanonicalKnowledgeState.Unknown, unknown.State);
        Assert.Equal(CanonicalKnowledgeState.NotRepresentableInSourceProfile, notRepresentable.State);
    }

    private static string BuildSingleIedScl()
        => """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
              <Communication><SubNetwork name="StationBus" type="8-MMS"><ConnectedAP iedName="IED_A" apName="P1"><Address><P type="IP">192.0.2.10</P><P type="MMS-Port">102</P></Address></ConnectedAP></SubNetwork></Communication>
              <IED name="IED_A">
                <AccessPoint name="P1">
                  <Server>
                    <LDevice inst="LD0" ldName="MMS_CUSTOM_DOMAIN">
                      <LN0 lnClass="LLN0" inst="" lnType="LT_LLN0" />
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LT_LLN0" lnClass="LLN0"><DO name="Mod" type="DOT_Mod" /></LNodeType>
                <DOType id="DOT_Mod" cdc="ENC"><DA name="stVal" fc="ST" bType="Enum" type="EN_Mod" /></DOType>
                <EnumType id="EN_Mod"><EnumVal ord="0">off</EnumVal><EnumVal ord="1">on</EnumVal></EnumType>
              </DataTypeTemplates>
            </SCL>
            """;
}
