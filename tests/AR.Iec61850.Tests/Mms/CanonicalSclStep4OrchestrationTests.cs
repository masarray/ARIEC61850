using System.Xml.Linq;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Tests.Mms;

public sealed class CanonicalSclStep4OrchestrationTests
{
    private const string Scl = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <IED name="IED01">
            <AccessPoint name="AP1">
              <Server>
                <LDevice inst="LD0" ldName="CUSTOM_LD">
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
              <DA name="q" bType="Quality" fc="ST" />
              <DA name="t" bType="Timestamp" fc="ST" />
            </DOType>
          </DataTypeTemplates>
        </SCL>
        """;

    [Fact]
    public void Planner_Uses_Canonical_LdName_And_Preserves_Leaf_Order()
    {
        var imported = SclCanonicalImporter.Import(
            XDocument.Parse(Scl),
            new SclCanonicalImportOptions
            {
                IedName = "IED01",
                AccessPointName = "AP1",
                SourceName = "canonical-step4-test.cid"
            });

        Assert.True(imported.IsSuccess, string.Join(" | ", imported.Errors));
        var design = CanonicalSclStep4Planner.Build(Assert.IsType<AR.Iec61850.Engineering.Canonical.CanonicalIedModel>(imported.Model));

        Assert.True(design.IsValid, string.Join(" | ", design.Errors));
        Assert.Equal(new[] { "CUSTOM_LD" }, design.DomainInventory.ExpectedDomains);
        Assert.Equal(1, design.ReadPlan.MaximumOutstandingReads);
        Assert.Equal(MmsReadBatchCodec.MaximumVariableReferencesPerRead, design.ReadPlan.MaximumVariableReferencesPerRead);

        var target = Assert.Single(design.ReadPlan.Targets);
        Assert.Equal("CUSTOM_LD/LLN0$ST", target.MmsReference);
        var dataObject = Assert.Single(target.DataObjects);
        Assert.Equal("Beh", dataObject.Name);
        Assert.Equal(new[] { "stVal", "q", "t" }, dataObject.Leaves.Select(leaf => leaf.AttributePath).ToArray());
    }

    [Fact]
    public async Task Active_Orchestrator_Rejects_Association_Identity_Before_Network_IO()
    {
        var imported = SclCanonicalImporter.Import(
            XDocument.Parse(Scl),
            new SclCanonicalImportOptions { IedName = "IED01", AccessPointName = "AP1" });
        Assert.True(imported.IsSuccess, string.Join(" | ", imported.Errors));

        await using var session = new MmsClientSession();
        var result = await session.ConnectAndExecuteCanonicalSclStep4Async(
            new SclAssistedMmsAssociationPlan
            {
                IedName = "OTHER_IED",
                AccessPointName = "AP1",
                Host = "192.0.2.99",
                Port = 102
            },
            Assert.IsType<AR.Iec61850.Engineering.Canonical.CanonicalIedModel>(imported.Model));

        Assert.Equal(CanonicalSclStep4ExecutionStatus.InvalidAssociationPlan, result.Status);
        Assert.False(result.SessionRemainsOpen);
        Assert.False(session.IsTcpConnected);
    }

    [Fact]
    public async Task Scl_Document_Entrypoint_Uses_Canonical_Import_And_Fails_Closed_Before_Network()
    {
        await using var session = new MmsClientSession();
        var result = await session.ConnectAndExecuteSclAssistedStep4Async(
            new SclAssistedMmsAssociationPlan
            {
                IedName = "IED01",
                AccessPointName = "MISSING_AP",
                Host = "192.0.2.99",
                Port = 102
            },
            XDocument.Parse(Scl));

        Assert.Equal(CanonicalSclStep4ExecutionStatus.InvalidCanonicalModel, result.Status);
        Assert.True(result.Message.Contains("AccessPoint", StringComparison.Ordinal));
        Assert.False(result.SessionRemainsOpen);
        Assert.False(session.IsTcpConnected);
    }
}
