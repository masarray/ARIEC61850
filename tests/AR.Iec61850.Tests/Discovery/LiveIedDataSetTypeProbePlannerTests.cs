using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class LiveIedDataSetTypeProbePlannerTests
{
    [Fact]
    public void TruncatedNameList_StillAdmitsVerifiedStaticFeedbackRoots()
    {
        var discovery = Sample();
        Assert.Equal(1, discovery.IedDirectory.LogicalNodeCount);
        var hints = LiveIedDataSetTypeProbePlanner.BuildVerifiedMemberHints(discovery);

        Assert.Contains(hints, x => x.LogicalNode == "eveGGIO1" && x.MmsItemName == "eveGGIO1$ST$Ind11");
        Assert.Contains(hints, x => x.LogicalNode == "CSWI16" && x.MmsItemName == "CSWI16$ST$Pos");
        Assert.Contains(hints, x => x.LogicalNode == "MMXU1" && x.MmsItemName == "MMXU1$MX$A");
        Assert.DoesNotContain(hints, x => x.LogicalNode is "UNTRUSTED" or "BADFC" or "BADLN");

        // The MMXU membership was already enumerated and must not be duplicated.
        Assert.Equal(2, discovery.IedDirectory.AddSupplementalPoints(hints));
        Assert.Equal(0, discovery.IedDirectory.AddSupplementalPoints(hints));
        var roots = LiveIedVariableTypeProbePlanner.BuildLogicalNodeRootCandidates(discovery.IedDirectory);
        Assert.Equal(3, roots.Count);
        Assert.Contains(roots, root => root.Item == "CSWI16");
        Assert.Contains(roots, root => root.Item == "eveGGIO1");
    }

    [Fact]
    public void Budget_BoundsMissingRoots_AndDeduplicatesExactMemberships()
    {
        var discovery = Sample();
        var hints = LiveIedDataSetTypeProbePlanner.BuildVerifiedMemberHints(
            discovery, maxExtraLogicalNodes: 1, maxMemberHints: 16);

        Assert.Contains(hints, x => x.LogicalNode == "MMXU1");
        Assert.Contains(hints, x => x.LogicalNode == "CSWI16");
        Assert.DoesNotContain(hints, x => x.LogicalNode == "eveGGIO1");
        Assert.Equal(hints.Count, hints.Select(x => x.MmsReference)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(LiveIedDataSetTypeProbePlanner.BuildVerifiedMemberHints(
            discovery, maxExtraLogicalNodes: 0, maxMemberHints: 0));
    }

    private static MmsDiscoveryResult Sample()
    {
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>
            {
                ["IEDLD0"] = ["MMXU1$MX$A"]
            }
        };
        return new MmsDiscoveryResult
        {
            Snapshot = snapshot,
            IedDirectory = MmsIedModelDirectoryBuilder.Build(snapshot),
            DataSetDirectories =
            [
                new MmsDataSetDirectoryResult
                {
                    IsSuccess = true,
                    DataSetReference = "IEDLD0/LLN0.Analog",
                    Members =
                    [
                        Member("MMXU1$MX$A", "MMXU1", "A", "MX"),
                        Member("CSWI16$ST$Pos", "CSWI16", "Pos", "ST"),
                        Member("CSWI16$ST$Pos", "CSWI16", "Pos", "ST"),
                        Member("eveGGIO1$ST$Ind11", "eveGGIO1", "Ind11", "ST"),
                        Member("UNTRUSTED$ST$Pos", "UNTRUSTED", "Pos", "ST", "OTHER"),
                        Member("BADFC$MX$A", "BADFC", "A", "ST")
                    ]
                },
                new MmsDataSetDirectoryResult
                {
                    IsSuccess = false,
                    DataSetReference = "IEDLD0/LLN0.Bad",
                    Members = [Member("BADLN$ST$Pos", "BADLN", "Pos", "ST")]
                }
            ]
        };
    }

    private static MmsDataSetDirectoryMember Member(string name, string ln, string path,
        string fc, string domain = "IEDLD0")
        => new()
        {
            Domain = domain, MmsItemName = name, LogicalNode = ln,
            DataObjectPath = path, FunctionalConstraint = fc, Confidence = 100
        };
}
