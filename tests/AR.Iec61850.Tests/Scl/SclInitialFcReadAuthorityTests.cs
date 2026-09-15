using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclInitialFcReadAuthorityTests
{
    [Fact]
    public void Scoped_Initial_Design_Preserves_DataSets_Rcbs_LdName_And_Indexed_Semantics()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <IED name="IED1">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="APP" ldName="EXACT_APP_DOMAIN">
                  <LN0 lnClass="LLN0" lnType="LN0_T">
                    <DataSet name="Digital">
                      <FCDA ldInst="APP" lnClass="MMXU" lnInst="1" doName="TotW" daName="mag.f" fc="MX" />
                    </DataSet>
                    <ReportControl name="Buffer" buffered="true" indexed="true" datSet="Digital" rptID="BR" confRev="1" />
                    <ReportControl name="FixedUrcb" buffered="false" indexed="false" datSet="Digital" rptID="UR" confRev="2" />
                  </LN0>
                  <LN prefix="" lnClass="MMXU" inst="1" lnType="MMXU_T" />
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LN0_T" lnClass="LLN0" />
            <LNodeType id="MMXU_T" lnClass="MMXU">
              <DO name="TotW" type="MV_T" />
            </LNodeType>
            <DOType id="MV_T" cdc="MV">
              <DA name="mag" bType="Struct" type="ANALOG_T" fc="MX" />
            </DOType>
            <DAType id="ANALOG_T">
              <BDA name="f" bType="FLOAT32" />
            </DAType>
          </DataTypeTemplates>
        </SCL>
        """;

        var design = SclInitialFcReadDesignBuilder.Read(xml, "IED1", "P1");

        Assert.True(design.IsSuccess, string.Join(" | ", design.Errors));
        Assert.Equal(new[] { "EXACT_APP_DOMAIN" }, design.DomainInventory.ExpectedDomains);

        var dataSet = Assert.Single(design.Model.DataSets);
        Assert.Equal("EXACT_APP_DOMAIN/LLN0$Digital", dataSet.Reference);
        Assert.Equal("EXACT_APP_DOMAIN", dataSet.Domain);
        var member = Assert.Single(dataSet.Members);
        Assert.Equal("EXACT_APP_DOMAIN/MMXU1.TotW.mag.f", member.Reference);

        Assert.Equal(2, design.Model.ReportControls.Count);
        var buffered = Assert.Single(design.Model.ReportControls.Where(report => report.Buffered));
        Assert.Equal("EXACT_APP_DOMAIN/LLN0$BR$Buffer", buffered.Reference);
        Assert.Equal("EXACT_APP_DOMAIN/LLN0$Digital", buffered.DataSetReference);
        Assert.True(buffered.Indexed);

        var unbuffered = Assert.Single(design.Model.ReportControls.Where(report => !report.Buffered));
        Assert.Equal("EXACT_APP_DOMAIN/LLN0$RP$FixedUrcb", unbuffered.Reference);
        Assert.Equal("EXACT_APP_DOMAIN/LLN0$Digital", unbuffered.DataSetReference);
        Assert.False(unbuffered.Indexed);

        Assert.Equal(1, design.Model.Coverage.DataSetCount);
        Assert.Equal(2, design.Model.Coverage.ReportControlCount);
    }
}
