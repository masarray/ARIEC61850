using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Tests.Scl.Engineering;

public sealed class SclLiveModelProjectionBuilderDataSetTests
{
    [Fact]
    public void Build_Canonicalizes_CrossLogicalDevice_DataSet_Member_References()
    {
        var model = SclLiveModelProjectionBuilder.Build(XDocument.Parse(CrossLogicalDeviceFixture()), "siemens-cross-ld.cid");

        var dataSet = Assert.Single(model.DataSets);
        Assert.Equal("AA1C1F13R4Application", dataSet.Domain);
        Assert.Equal(2, dataSet.Members.Count);

        var objectLevel = dataSet.Members[0];
        Assert.Equal("AA1C1F13R4ADD/GGIO6.CBOpnd", objectLevel.Reference);
        Assert.Equal("AA1C1F13R4ADD/GGIO6.CBOpnd", objectLevel.MmsReference);
        Assert.Equal("ST", objectLevel.FunctionalConstraint);
        Assert.DoesNotContain("[ST]", objectLevel.Reference, StringComparison.Ordinal);
        Assert.DoesNotEndWith(".stVal", objectLevel.Reference, StringComparison.Ordinal);

        var explicitLeaf = dataSet.Members[1];
        Assert.Equal("AA1C1F13R4ADD/GGIO6.Other.stVal", explicitLeaf.Reference);
        Assert.Equal("AA1C1F13R4ADD/GGIO6.Other.stVal", explicitLeaf.MmsReference);

        var ggio = model.LogicalDevices
            .Single(ld => ld.Inst == "ADD")
            .LogicalNodes.Single(ln => ln.Name == "GGIO6");
        Assert.Contains(ggio.DataObjects, dataObject => dataObject.Reference == objectLevel.Reference);
    }

    private static string CrossLogicalDeviceFixture() => """
    <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
      <Header id="SIEMENS_CROSS_LD" version="1" revision="0" />
      <IED name="AA1C1F13R4">
        <AccessPoint name="E">
          <Server>
            <LDevice inst="Application">
              <LN0 lnClass="LLN0" lnType="LLN0Type">
                <DataSet name="Digital">
                  <FCDA ldInst="ADD" lnClass="GGIO" lnInst="6" doName="CBOpnd" fc="ST" />
                  <FCDA ldInst="ADD" lnClass="GGIO" lnInst="6" doName="Other" daName="stVal" fc="ST" />
                </DataSet>
              </LN0>
            </LDevice>
            <LDevice inst="ADD">
              <LN0 lnClass="LLN0" lnType="LLN0Type" />
              <LN lnClass="GGIO" inst="6" lnType="GGIOType" />
            </LDevice>
          </Server>
        </AccessPoint>
      </IED>
      <DataTypeTemplates>
        <LNodeType id="LLN0Type" lnClass="LLN0" />
        <LNodeType id="GGIOType" lnClass="GGIO">
          <DO name="CBOpnd" type="SpsType" />
          <DO name="Other" type="SpsType" />
        </LNodeType>
        <DOType id="SpsType" cdc="SPS">
          <DA name="stVal" bType="BOOLEAN" fc="ST" />
          <DA name="q" bType="Quality" fc="ST" />
          <DA name="t" bType="Timestamp" fc="ST" />
        </DOType>
      </DataTypeTemplates>
    </SCL>
    """;
}
