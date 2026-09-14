using System.Xml.Linq;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Tests.Mms;

public sealed class CanonicalInitialFcReadPlannerTests
{
    [Fact]
    public void Canonical_Scl_Path_Preserves_LdName_And_Ordered_Leaf_Shape()
    {
        var imported = SclCanonicalImporter.Import(
            XDocument.Parse(Fixture()),
            new SclCanonicalImportOptions
            {
                IedName = "IED01",
                AccessPointName = "AP1",
                SourceName = "canonical-step4.cid"
            });

        Assert.True(imported.IsSuccess, string.Join(" | ", imported.Errors));
        var plan = CanonicalInitialFcReadPlanner.FromCanonicalModel(imported.Model!);

        Assert.True(plan.IsValid, string.Join(" | ", plan.Errors));
        var target = Assert.Single(plan.Targets);
        Assert.Equal("CUSTOM_LD/LLN0$ST", target.MmsReference);
        Assert.Equal(1, plan.MaximumOutstandingReads);
        Assert.Equal(10, plan.MaximumVariableReferencesPerRead);

        var dataObject = Assert.Single(target.DataObjects);
        Assert.Equal("Beh", dataObject.Name);
        Assert.Equal("CUSTOM_LD/LLN0.Beh", dataObject.Reference);
        Assert.Equal(new[] { "stVal", "q", "t" }, dataObject.Leaves.Select(leaf => leaf.AttributePath).ToArray());
        Assert.All(dataObject.Leaves, leaf => Assert.StartsWith("CUSTOM_LD/", leaf.Reference, StringComparison.Ordinal));
    }

    [Fact]
    public void Canonical_Planner_Is_Semantically_Equivalent_To_Legacy_Step4_Planner()
    {
        const string iedName = "IED01";
        const string accessPoint = "AP1";
        var xml = Fixture();

        var legacyDesign = SclInitialFcReadDesignBuilder.Read(xml, iedName, accessPoint);
        Assert.True(legacyDesign.IsSuccess, string.Join(" | ", legacyDesign.Errors));
        var legacyPlan = InitialFcReadPlanner.FromSclModel(
            legacyDesign.Model,
            legacyDesign.DomainInventory.ExpectedDomains);

        var imported = SclCanonicalImporter.Import(
            XDocument.Parse(xml),
            new SclCanonicalImportOptions
            {
                IedName = iedName,
                AccessPointName = accessPoint,
                SourceName = "canonical-step4.cid"
            });
        Assert.True(imported.IsSuccess, string.Join(" | ", imported.Errors));
        var canonicalPlan = CanonicalInitialFcReadPlanner.FromCanonicalModel(
            imported.Model!,
            legacyDesign.DomainInventory.ExpectedDomains);

        Assert.Equal(
            legacyPlan.Targets.Select(target => target.MmsReference).ToArray(),
            canonicalPlan.Targets.Select(target => target.MmsReference).ToArray());
        Assert.Equal(
            legacyPlan.Batches.Select(batch => batch.Targets.Count).ToArray(),
            canonicalPlan.Batches.Select(batch => batch.Targets.Count).ToArray());

        var legacyLeaves = legacyPlan.Targets
            .SelectMany(target => target.DataObjects)
            .SelectMany(dataObject => dataObject.Leaves)
            .Select(leaf => $"{leaf.Reference}|{leaf.AttributePath}|{leaf.FunctionalConstraint}|{leaf.SclBType}")
            .ToArray();
        var canonicalLeaves = canonicalPlan.Targets
            .SelectMany(target => target.DataObjects)
            .SelectMany(dataObject => dataObject.Leaves)
            .Select(leaf => $"{leaf.Reference}|{leaf.AttributePath}|{leaf.FunctionalConstraint}|{leaf.SclBType}")
            .ToArray();

        Assert.Equal(legacyLeaves, canonicalLeaves);
    }

    [Fact]
    public void Canonical_Planner_Applies_Exact_Domain_Allowlist()
    {
        var imported = SclCanonicalImporter.Import(
            XDocument.Parse(Fixture()),
            new SclCanonicalImportOptions
            {
                IedName = "IED01",
                AccessPointName = "AP1"
            });

        Assert.True(imported.IsSuccess, string.Join(" | ", imported.Errors));

        var accepted = CanonicalInitialFcReadPlanner.FromCanonicalModel(imported.Model!, new[] { "CUSTOM_LD" });
        var rejected = CanonicalInitialFcReadPlanner.FromCanonicalModel(imported.Model!, new[] { "custom_ld" });

        Assert.True(accepted.IsValid, string.Join(" | ", accepted.Errors));
        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Errors, error => error.Contains("No valid FC-root Read targets", StringComparison.Ordinal));
    }

    private static string Fixture() => """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
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
}
