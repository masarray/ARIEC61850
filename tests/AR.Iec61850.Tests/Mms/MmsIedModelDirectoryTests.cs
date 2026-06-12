using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsIedModelDirectoryTests
{
    [Fact]
    public void Build_ParsesFunctionalConstraintsFromLiveMmsNames()
    {
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] =
                [
                    "XCBR1$ST$Pos$stVal",
                    "XCBR1$ST$Pos$q",
                    "CSWI1$CO$Pos$Oper$ctlVal",
                    "MMXU1$MX$PhV$phsA$cVal$mag$f",
                    "LLN0$BR$brcbA01$RptEna"
                ]
            }
        };

        var directory = MmsIedModelDirectoryBuilder.Build(snapshot);

        Assert.Equal(5, directory.PointCount);
        Assert.Equal(1, directory.LogicalDeviceCount);
        Assert.Equal(4, directory.LogicalNodeCount);
        Assert.Equal(1, directory.ReportAttributeCount);
        Assert.Equal(1, directory.ControlAttributeCount);

        var point = Assert.Single(directory.FindByUserReference("LD0/MMXU1.PhV.phsA.cVal.mag.f"));
        Assert.Equal("MX", point.FunctionalConstraint);
        Assert.Equal("MMXU1$MX$PhV$phsA$cVal$mag$f", point.MmsItemName);
    }

    [Fact]
    public void Resolve_UsesLiveDirectoryWithoutUserSupplyingFc()
    {
        var directory = BuildDemoDirectory();

        var result = MmsFcResolver.Resolve(directory, "LD0/XCBR1.Pos.stVal");

        Assert.True(result.IsResolved);
        Assert.False(result.IsAmbiguous);
        Assert.Equal("ST", result.BestCandidate?.FunctionalConstraint);
        Assert.Equal("LD0/XCBR1$ST$Pos$stVal", result.BestCandidate?.MmsReference);
    }

    [Fact]
    public void Resolve_AcceptsReferenceWithEmbeddedFc()
    {
        var directory = BuildDemoDirectory();

        var result = MmsFcResolver.Resolve(directory, "LD0/MMXU1.MX.PhV.phsA.cVal.mag.f");

        Assert.True(result.IsResolved);
        Assert.Equal("MX", result.BestCandidate?.FunctionalConstraint);
    }

    [Fact]
    public void Resolve_ReturnsHeuristicOnlyWhenLiveDirectoryHasNoMatch()
    {
        var directory = BuildDemoDirectory();

        var result = MmsFcResolver.Resolve(directory, "LD0/MMXU1.A.phsA.cVal.mag.f");

        Assert.False(result.IsResolved);
        Assert.Contains("MX", result.HeuristicFunctionalConstraints);
    }

    private static MmsIedModelDirectory BuildDemoDirectory()
    {
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] =
                [
                    "XCBR1$ST$Pos$stVal",
                    "XCBR1$ST$Pos$q",
                    "CSWI1$CO$Pos$Oper$ctlVal",
                    "MMXU1$MX$PhV$phsA$cVal$mag$f"
                ]
            }
        };

        return MmsIedModelDirectoryBuilder.Build(snapshot);
    }
}
