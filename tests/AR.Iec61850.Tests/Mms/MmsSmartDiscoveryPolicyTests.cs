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

    [Theory]
    [InlineData(5, 64, 5)]
    [InlineData(8, 64, 8)]
    [InlineData(16, 64, 8)]
    [InlineData(1, 64, 1)]
    [InlineData(5, 2, 2)]
    [InlineData(5, 0, 0)]
    [InlineData(0, 64, 1)]
    public void ReportEnrichment_UsesOnlyNegotiatedWindowAndFiniteCandidateCount(
        int effectiveWindow, int candidates, int expected)
    {
        Assert.Equal(expected,
            MmsSmartDiscoveryPolicy.ResolveReportEnrichmentWorkers(
                effectiveWindow, candidates));
    }

    [Fact]
    public void ReportEnrichment_PreservesExactSequentialPerRcbFieldsAndKpiCapture()
    {
        // Contract locks the high-throughput change to read-only RCBs.
        // Any change to required attributes, single-RCB order, or the
        // existing observable MMS request accounting must be reviewed.
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (path is not null)
        {
            var file = Path.Combine(path.FullName,
                "src", "AR.Iec61850", "Mms", "MmsClientSession.SmartDiscovery.cs");
            if (File.Exists(file)) { source = File.ReadAllText(file); break; }
            path = path.Parent;
        }
        Assert.NotNull(source);
        Assert.Contains("EnrichSingleReportControlSmartAsync", source);
        Assert.Contains("ResolveReportEnrichmentWorkers(", source);
        Assert.Contains("await Task.WhenAll(", source);
        foreach (var field in new[] {
            "DatSet", "RptID", "ConfRev", "IntgPd",
            "BufTm", "TrgOps", "OptFlds", "ResvTms", "Resv" })
            Assert.Contains($"TryReadReportAttributeSmartAsync(reportControl, \"{field}\"", source);
        Assert.Contains("ObserveSmartDiscoveryRequest(", source);
        Assert.DoesNotContain("WriteSingleVariableAsync(", source);
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
    public void TypeBudget_SelectsOnlyLogicalNodesProvenByLiveDirectory()
    {
        var directory = BuildDirectory();
        var selected = MmsSmartTypeProbePolicy.SelectLiveLogicalNodeRoots(
            directory,
            [
                new MmsObjectReference("ld0", "xcbr1", string.Empty),
                new MmsObjectReference("LD0", "MISSING1", string.Empty),
                new MmsObjectReference("LD0", "MMXU1$MX$PhV", "MX"),
                new MmsObjectReference("LD0", "MMXU1", string.Empty),
                new MmsObjectReference("LD0", "MMXU1", string.Empty)
            ]);

        Assert.Equal(2, selected.Length);
        Assert.Equal("MMXU1", selected[0].Item);
        Assert.Equal("XCBR1", selected[1].Item);
        Assert.All(selected, reference => Assert.Equal("LD0", reference.Domain));
    }

    [Fact]
    public void TypeBudget_DoesNotRepeatExactDataObjectRootAsLeafFallback()
    {
        var dataObjectPoint = new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "XCBR1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Pos",
            MmsItemName = "XCBR1$ST$Pos"
        };
        var descendantPoint = new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "MMXU1",
            FunctionalConstraint = "MX",
            DataObjectPath = "A.phsA.cVal.mag.f",
            MmsItemName = "MMXU1$MX$A$phsA$cVal$mag$f"
        };
        var alreadyProbed = new[]
        {
            MmsSmartTypeProbePolicy.BuildDataObjectRoot(dataObjectPoint),
            MmsSmartTypeProbePolicy.BuildDataObjectRoot(descendantPoint)
        };

        var fallback = MmsSmartTypeProbePolicy.BuildUnprobedExactFallbacks(
            [dataObjectPoint, descendantPoint, descendantPoint],
            alreadyProbed);

        var exact = Assert.Single(fallback);
        Assert.Equal("LD0", exact.Domain);
        Assert.Equal("MMXU1$MX$A$phsA$cVal$mag$f", exact.Item);
        Assert.DoesNotContain(fallback, reference => reference.Item == "XCBR1$ST$Pos");
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
    public void TypeHierarchyIndex_MapsLogicalNodeTreeWithoutCrossNodeLeakage()
    {
        var directory = BuildDirectory();
        var typeResult = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "XCBR1", string.Empty),
            TypeSpecification = Node(
                "",
                Node("ST",
                    Node("Pos",
                        Node("stVal"),
                        Node("q"))))
        };

        var index = LiveIedVariableTypeHierarchyIndex.Build(directory, [typeResult]);
        var xcbrPoint = directory.Points.Single(point => point.MmsItemName == "XCBR1$ST$Pos$stVal");
        var mmxuPoint = directory.Points.Single(point => point.MmsItemName == "MMXU1$MX$PhV$phsA$cVal$mag$f");

        Assert.True(index.TryResolve(xcbrPoint, out _));
        Assert.False(index.TryResolve(mmxuPoint, out _));
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


    [Fact]
    public void DataSetClosure_SelectsOnlyObservedMissingStMxRoots_NoDuplicateOrForeignLn()
    {
        var directory = BuildDirectory();
        var dataset = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            Members =
            [
                Member("LD0", "eveGGIO1", "ST", "Ind11"),
                Member("LD0", "eveGGIO1", "ST", "Ind13"),
                Member("LD0", "CSWI1", "ST", "Pos"),
                Member("LD0", "CSWI16", "ST", "Pos"),
                Member("LD0", "MMXU1", "MX", "A"),
                Member("LD0", "FOREIGN1", "CO", "Oper"),
                Member("FAKE", "eveGGIO2", "ST", "Ind1")
            ]
        };
        var already = new[]
        {
            new MmsVariableAccessAttributesResult
            {
                IsSuccess = true,
                Reference = new MmsObjectReference("LD0", "MMXU1", "")
            },
            new MmsVariableAccessAttributesResult
            {
                IsSuccess = false,
                Reference = new MmsObjectReference("LD0", "CSWI1", "")
            }
        };
        var selected = MmsSmartTypeProbePolicy.SelectUnprobedDataSetLogicalNodeRoots(
            directory, [dataset], already);
        Assert.Equal(2, selected.Length);
        Assert.Equal(["CSWI16", "eveGGIO1"], selected.Select(x => x.Item).ToArray());
    }

    private static MmsDataSetDirectoryMember Member(
        string domain, string ln, string fc, string dataObject) => new()
    {
        Domain = domain,
        LogicalNode = ln,
        FunctionalConstraint = fc,
        DataObjectPath = dataObject,
        MmsItemName = ln + "$" + fc + "$" + dataObject,
        UserReference = domain + "/" + ln + "." + dataObject
    };
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
