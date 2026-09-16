using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSmartInitialFcReadPolicyTests
{
    [Fact]
    public void Defaults_UseSmallBoundedReadWindow()
    {
        var options = new MmsSmartInitialFcReadOptions();

        Assert.Equal(4, options.MaxOutstandingBatches);
        Assert.Equal(2, options.UnknownPeerMaxOutstandingBatches);
        Assert.Equal(TimeSpan.Zero, options.PerBatchTimeout);
    }

    [Fact]
    public void FcRootPlanner_RemainsBoundedAndDoesNotExpandToLeafReads()
    {
        var directory = MmsIedModelDirectoryBuilder.Build(new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] =
                [
                    "XCBR1$ST$Pos$stVal",
                    "XCBR1$ST$Pos$q",
                    "XCBR1$CF$Pos$ctlModel",
                    "MMXU1$MX$PhV$phsA$cVal$mag$f",
                    "MMXU1$MX$PhV$phsB$cVal$mag$f"
                ]
            }
        });

        var plan = InitialFcReadPlanner.FromLiveDirectory(directory);
        var references = plan.Batches.SelectMany(batch => batch.References).ToArray();

        Assert.Equal(3, references.Length);
        Assert.Contains(references, reference => reference.Domain == "LD0" && reference.Item == "XCBR1$ST");
        Assert.Contains(references, reference => reference.Domain == "LD0" && reference.Item == "XCBR1$CF");
        Assert.Contains(references, reference => reference.Domain == "LD0" && reference.Item == "MMXU1$MX");
        Assert.DoesNotContain(references, reference => reference.Item.Contains("$Pos$", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Item.Contains("$PhV$", StringComparison.Ordinal));
        Assert.All(plan.Batches, batch => Assert.InRange(batch.References.Count, 1, MmsReadBatchCodec.MaximumVariableReferencesPerRead));
    }
}
