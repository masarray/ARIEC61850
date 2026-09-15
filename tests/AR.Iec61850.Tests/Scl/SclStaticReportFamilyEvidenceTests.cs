using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclStaticReportFamilyEvidenceTests
{
    [Theory]
    [InlineData("http://www.iec.ch/61850/2003/SCL")]
    [InlineData("http://www.iec.ch/61850/2003/SCL", true)]
    public void Parser_KeepsDeclarativeRcbBaseNames_WhileLiveResolverUsesConcreteInstances(
        string sclNamespace,
        bool edition2Shape = false)
    {
        var document = new SclParser().Parse(BuildScl(sclNamespace, edition2Shape), edition2Shape ? "synthetic-ed2.iid" : "synthetic-ed1.icd");

        var brcb = Assert.Single(document.ReportControls, report => report.Buffered);
        var urcb = Assert.Single(document.ReportControls, report => !report.Buffered);

        Assert.Equal("IED1Application/LLN0$BR$Buffer", brcb.ControlBlockReference);
        Assert.Equal("IED1Application/LLN0$RP$Unbuffer", urcb.ControlBlockReference);
        Assert.True(brcb.Indexed);
        Assert.True(urcb.Indexed);

        var live = new[]
        {
            Candidate("Buffer01", "BR", buffered: true),
            Candidate("Buffer02", "BR", buffered: true),
            Candidate("Unbuffer01", "RP", buffered: false),
            Candidate("Unbuffer02", "RP", buffered: false)
        };

        var brcbResolution = MmsSclRcbFamilyResolver.Resolve(brcb, live);
        var urcbResolution = MmsSclRcbFamilyResolver.Resolve(urcb, live);

        Assert.Equal(2, brcbResolution.Candidates.Count);
        Assert.All(brcbResolution.Candidates, candidate => Assert.StartsWith("Buffer", candidate.Name, StringComparison.Ordinal));
        Assert.Equal(2, urcbResolution.Candidates.Count);
        Assert.All(urcbResolution.Candidates, candidate => Assert.StartsWith("Unbuffer", candidate.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(brcbResolution.Candidates, candidate => candidate.Name == "Buffer");
        Assert.DoesNotContain(urcbResolution.Candidates, candidate => candidate.Name == "Unbuffer");
    }

    [Fact]
    public void Resolver_DoesNotUseRptEnabledCapacityAsPermissionToInventRuntimeNames()
    {
        var document = new SclParser().Parse(BuildScl("http://www.iec.ch/61850/2003/SCL", edition2Shape: true));
        var brcb = Assert.Single(document.ReportControls, report => report.Buffered);

        var resolution = MmsSclRcbFamilyResolver.Resolve(brcb, Array.Empty<MmsReportControlCandidate>());

        Assert.False(resolution.IsSuccess);
        Assert.Empty(resolution.Candidates);
        Assert.Contains("No indexed name was synthesized", resolution.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildScl(string sclNamespace, bool edition2Shape)
    {
        var trigger = edition2Shape
            ? "<TrgOps dchg=\"true\" qchg=\"true\" dupd=\"true\" period=\"true\" gi=\"true\"/>"
            : "<TrgOps dchg=\"true\" qchg=\"true\" dupd=\"true\" period=\"true\"/>";

        return $$"""
            <SCL xmlns="{{sclNamespace}}">
              <Header id="synthetic-static-report-family"/>
              <IED name="IED1">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="Application">
                      <LN0 lnClass="LLN0" inst="" lnType="LLN0Type">
                        <DataSet name="Digital"/>
                        <DataSet name="Analog"/>
                        <ReportControl name="Buffer" rptID="IED1/Application/LLN0$BR$Buffer" buffered="true" bufTime="100" datSet="Digital" intgPd="0" confRev="100001">
                          {{trigger}}
                          <RptEnabled max="2"/>
                        </ReportControl>
                        <ReportControl name="Unbuffer" rptID="IED1/Application/LLN0$RP$Unbuffer" buffered="false" bufTime="100" datSet="Analog" intgPd="0" confRev="110001">
                          {{trigger}}
                          <RptEnabled max="2"/>
                        </ReportControl>
                      </LN0>
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LLN0Type" lnClass="LLN0"/>
              </DataTypeTemplates>
            </SCL>
            """;
    }

    private static MmsReportControlCandidate Candidate(string name, string fc, bool buffered)
        => new()
        {
            Domain = "IED1Application",
            LogicalNode = "LLN0",
            FunctionalConstraint = fc,
            Name = name,
            Reference = $"IED1Application/LLN0.{fc}.{name}",
            Buffered = buffered,
            DataSetReference = buffered ? "IED1Application/LLN0.Digital" : "IED1Application/LLN0.Analog",
            EnabledState = "false",
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            Status = "Attribute-probed"
        };
}
