using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticRcbCatalogPagerTests
{
    [Theory]
    [InlineData(66)]
    [InlineData(300)]
    public void Proven_Target_DataSet_Does_Not_Probe_Unrelated_Rcb_Inventory(int total)
    {
        var catalog = Enumerable.Range(0, total - 2)
            .Select(i => Candidate($"LD0/LLN0.BR.Noise{i:000}", "", enabled: false))
            .Append(Candidate("LD0/LLN0.BR.Events01", "LD0/LLN0.Events", enabled: true))
            .Append(Candidate("LD0/LLN0.BR.Events02", "LD0/LLN0.Events", enabled: false))
            .ToArray();
        var first = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), Inventory(catalog), 4);
        var reversed = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), Inventory(catalog.Reverse().ToArray()), 4);

        Assert.Equal(2, first.Candidates.Count);
        Assert.Equal(first.Candidates.Select(x => x.Reference), reversed.Candidates.Select(x => x.Reference));
        Assert.False(first.HasMore);
        Assert.Equal(total, first.CatalogCount);
        Assert.Equal(2, first.TargetBoundCount);
        Assert.DoesNotContain(first.Candidates, x => x.Name.StartsWith("Noise", StringComparison.Ordinal));
        Assert.All(first.Candidates, x => Assert.Equal("LD0/LLN0.Events", x.DataSetReference));

        // Read-only discovery sees both matching RCBs; activation policy must
        // reject the occupied one and choose the independently proven free one.
        var occupied = first.Candidates.Single(x => x.EnabledState == "true");
        var free = first.Candidates.Single(x => x.EnabledState == "false");
        Assert.False(MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            VerifiedAvailability(occupied, MmsRcbOperationalAvailability.InUse, "60"),
            allowCallerOwned: false,
            allowReducedMissingReservationEvidence: false).IsEligible);
        var eligibility = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            VerifiedAvailability(free, MmsRcbOperationalAvailability.Available, "0"),
            allowCallerOwned: false,
            allowReducedMissingReservationEvidence: false);
        Assert.Equal(MmsConfiguredStaticRcbEligibilityKind.ExplicitFree, eligibility.Kind);
    }

    private static MmsRcbAvailabilitySnapshot VerifiedAvailability(
        MmsReportControlCandidate candidate,
        MmsRcbOperationalAvailability state,
        string reservation)
        => new()
        {
            Reference = candidate.Reference,
            Domain = candidate.Domain,
            LogicalNode = candidate.LogicalNode,
            Buffered = candidate.Buffered,
            DataSetReference = candidate.DataSetReference,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = candidate.EnabledState,
            ReservationTimeSeconds = reservation,
            DataSetDirectorySuccess = true,
            DataSetMembers = [new MmsDataSetDirectoryMember
            {
                Domain = "LD0",
                MmsItemName = "XCBR1$ST$Pos",
                UserReference = "LD0/XCBR1.Pos",
                FunctionalConstraint = "ST"
            }],
            Availability = state,
            Confidence = MmsRcbAvailabilityConfidence.Exact
        };

    [Fact]
    public void Unresolved_300_Rcbs_Continues_In_Bounded_Deterministic_Pages_Without_Duplicates()
    {
        var catalog = Enumerable.Range(0, 300)
            .Select(i => Candidate($"LD0/LLN0.BR.Unbound{i:000}", ""))
            .ToArray();
        var inventory = Inventory(catalog);
        string? continuation = null;
        var visited = new List<string>();
        var pages = 0;
        do
        {
            var page = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), inventory, 64, continuation);
            Assert.InRange(page.Candidates.Count, 1, 64);
            visited.AddRange(page.Candidates.Select(x => x.Reference));
            continuation = page.ContinuationToken;
            pages++;
        } while (continuation is not null && pages < 10);

        Assert.Null(continuation);
        Assert.Equal(5, pages);
        Assert.Equal(300, visited.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(300, visited.Count);
    }

    [Fact]
    public void Catalog_Or_Target_Mutation_Invalidates_Continuation()
    {
        var catalog = Enumerable.Range(0, 66)
            .Select(i => Candidate($"LD0/LLN0.BR.Unbound{i:000}", ""))
            .ToArray();
        var initial = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), Inventory(catalog), 16);
        Assert.NotNull(initial.ContinuationToken);
        var changed = catalog[..^1];
        Assert.Throws<ArgumentException>(() =>
            MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), Inventory(changed), 16, initial.ContinuationToken));
    }

    [Fact]
    public void Reconnect_Must_Reject_Prior_Association_Cursor()
    {
        var catalog = Enumerable.Range(0, 66)
            .Select(i => Candidate($"LD0/LLN0.BR.Unbound{i:000}", ""))
            .ToArray();
        var inventory = Inventory(catalog);
        var old = MmsCanonicalStaticRcbCatalogPager.Select(
            Coverage(), inventory, 16, associationKey: "epoch-a");
        Assert.NotNull(old.ContinuationToken);
        Assert.Throws<ArgumentException>(() =>
            MmsCanonicalStaticRcbCatalogPager.Select(
                Coverage(), inventory, 16, old.ContinuationToken, "epoch-b"));
    }

    [Fact]
    public void Wrong_Dataset_And_Different_Functional_Constraint_Are_Not_Admitted()
    {
        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.Events", "LD0/LLN0.Wrong"),
            Candidate("LD0/LLN0.RP.Events", "LD0/LLN0.Events", buffered: false),
            Candidate("LD0/LLN0.BR.Events02", "LD0/LLN0.Events"),
            Candidate("LD1/LLN0.BR.Events03", "LD0/LLN0.Events"));
        var page = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), inventory, 32);
        Assert.Equal("LD0/LLN0.BR.Events02", Assert.Single(page.Candidates).Reference);
    }

    [Fact]
    public void Exact_Configured_Reference_Is_Probe_Priority_But_Not_Proof_Of_Dataset()
    {
        var catalog = Enumerable.Range(0, 66)
            .Select(i => Candidate($"LD0/LLN0.BR.Noise{i:000}", ""))
            .Append(Candidate("LD0/LLN0.BR.Events", ""))
            .ToArray();
        var page = MmsCanonicalStaticRcbCatalogPager.Select(Coverage(), Inventory(catalog), 1);
        Assert.Equal("LD0/LLN0.BR.Events", Assert.Single(page.Candidates).Reference);
        Assert.Equal(0, page.TargetBoundCount);
        Assert.True(page.HasMore);
    }

    private static CanonicalStaticReportCoveragePlan Coverage() => new()
    {
        Segments =
        [
            new CanonicalStaticReportCoverageSegment
            {
                DataSetReference = "LD0/LLN0.Events",
                SourceDataSetReference = "LD0/LLN0.Events",
                SelectedSignalReferences = ["LD0/XCBR1.Pos.stVal"],
                ReportControls =
                [
                    new CanonicalReportControl
                    {
                        Reference = "LD0/LLN0.BR.Events",
                        MmsDomain = "LD0",
                        Buffered = true,
                        DataSetReference = "LD0/LLN0.Events"
                    }
                ]
            }
        ]
    };

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory { Authority = MmsReportInventoryAuthority.LiveMmsObserved };
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsReportControlCandidate Candidate(
        string reference, string dataSet, bool enabled = false, bool buffered = true)
        => new()
        {
            Domain = reference[..reference.IndexOf('/')],
            LogicalNode = "LLN0",
            FunctionalConstraint = buffered ? "BR" : "RP",
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            Reference = reference,
            Buffered = buffered,
            DataSetReference = dataSet,
            EnabledState = enabled ? "true" : "false"
        };
}
