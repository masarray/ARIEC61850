using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSmartDiscoveryPolicyTests
{
    [Fact]
    public void Defaults_KeepExpensiveEnrichmentOffCriticalPath()
    {
        var options = new MmsSmartDiscoveryOptions();

        Assert.False(options.ProbeReportAttributes);
        Assert.False(options.ReadDataSetDirectories);
        Assert.Equal(8, options.MaxConcurrentChains);
        Assert.Equal(4, options.UnknownPeerMaxConcurrentChains);
        Assert.Equal(64, options.MaxNameListPages);
    }

    [Fact]
    public void ResolveWindow_RespectsNegotiatedLimitAndConservativeUnknownFallback()
    {
        Assert.Equal(8, MmsSmartDiscoveryPolicy.ResolveWindow(8, 4, 10));
        Assert.Equal(10, MmsSmartDiscoveryPolicy.ResolveWindow(16, 4, 10));
        Assert.Equal(4, MmsSmartDiscoveryPolicy.ResolveWindow(8, 4, null));
        Assert.Equal(1, MmsSmartDiscoveryPolicy.ResolveWindow(0, 0, null));
    }

    [Fact]
    public void DomainPriority_ReordersOnlyAlreadyObservedDomains()
    {
        var selected = MmsSmartDiscoveryPolicy.SelectPublishedDomains(
            ["LD1", "EXTRA", "LD0"],
            maxDomains: 256);

        var scheduled = MmsSmartDiscoveryPolicy.OrderDomainsForScheduling(
            selected,
            ["LD1", "MISSING", "LD1"]);

        Assert.Equal(["EXTRA", "LD0", "LD1"], selected);
        Assert.Equal(["LD1", "EXTRA", "LD0"], scheduled);
        Assert.Equal(
            selected.OrderBy(x => x, StringComparer.Ordinal),
            scheduled.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void DomainSelection_CollapsesCaseOnlyCollisionBeforeDictionaryPublication()
    {
        var selected = MmsSmartDiscoveryPolicy.SelectPublishedDomains(
            ["ld0", "LD0", "LD1", "LD1"],
            maxDomains: 256);

        Assert.Equal(["LD0", "LD1"], selected);
    }

    [Fact]
    public void LogicalNodePlanner_UsesOneCandidatePerLogicalNodeNotPerLeaf()
    {
        var directory = BuildDirectory();

        var roots = LiveIedVariableTypeProbePlanner.BuildLogicalNodeRootCandidates(directory);

        Assert.Equal(2, roots.Count);
        Assert.Contains(roots, root => root.Domain == "LD0" && root.Item == "MMXU1");
        Assert.Contains(roots, root => root.Domain == "LD0" && root.Item == "XCBR1");
    }

    [Fact]
    public void TypeCoverage_UsesParentHierarchyAndRejectsUnknownBranch()
    {
        var root = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "MMXU1", string.Empty),
            TypeSpecification = Node(
                "",
                Node("MX",
                    Node("PhV",
                        Node("phsA",
                            Node("cVal",
                                Node("mag",
                                    Node("f")))))))
        };

        Assert.True(MmsSmartTypeProbePolicy.Covers(
            root,
            "MMXU1$MX$PhV$phsA$cVal$mag$f"));
        Assert.False(MmsSmartTypeProbePolicy.Covers(
            root,
            "MMXU1$MX$PhV$phsA$q"));
        Assert.False(MmsSmartTypeProbePolicy.Covers(
            root,
            "XCBR1$ST$Pos$stVal"));
    }

    [Fact]
    public void DataObjectFallbackRoot_StopsAtLnFcDo()
    {
        var point = new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "MMXU1",
            FunctionalConstraint = "MX",
            DataObjectPath = "PhV.phsA.cVal.mag.f",
            MmsItemName = "MMXU1$MX$PhV$phsA$cVal$mag$f"
        };

        var root = MmsSmartTypeProbePolicy.BuildDataObjectRoot(point);

        Assert.Equal("LD0", root.Domain);
        Assert.Equal("MMXU1$MX$PhV", root.Item);
        Assert.Equal("MX", root.FunctionalConstraint);
    }

    private static MmsIedModelDirectory BuildDirectory()
    {
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] =
                [
                    "XCBR1$ST$Pos$stVal",
                    "XCBR1$ST$Pos$q",
                    "MMXU1$MX$PhV$phsA$cVal$mag$f",
                    "MMXU1$MX$A$phsA$cVal$mag$f"
                ]
            }
        };

        return MmsIedModelDirectoryBuilder.Build(snapshot);
    }

    private static MmsTypeSpecificationNode Node(
        string name,
        params MmsTypeSpecificationNode[] children)
        => new()
        {
            Name = name,
            MmsType = children.Length == 0 ? "floating-point" : "structure",
            Children = children
        };
}
