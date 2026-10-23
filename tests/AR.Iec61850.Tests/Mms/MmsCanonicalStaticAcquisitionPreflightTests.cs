using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticAcquisitionPreflightTests
{
    [Fact]
    public void SclProjectedInventory_IsNeverPromotedToLiveOperationalTargetEvidence()
    {
        var coverage = Coverage(
            Segment(
                "LD0/LLN0.Events",
                "LD0/LLN0.BR.Rpt01",
                buffered: true,
                "LD0/XCBR1.Pos.stVal"));

        var inventory = Inventory(
            MmsReportInventoryAuthority.SclDesignProjection,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory);

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired, plan.Status);
        Assert.False(plan.HasExactOperationalTargets);
        Assert.Empty(plan.ExactTargetReportControlReferences);
        Assert.Contains(plan.Warnings, warning =>
            warning.Contains("structural evidence only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LiveInventory_ProducesOnlyExactStaticTargets()
    {
        var coverage = Coverage(
            Segment(
                "LD0/LLN0.Events",
                "LD0/LLN0.BR.Rpt01",
                buffered: true,
                "LD0/XCBR1.Pos.stVal"));

        var inventory = Inventory(
            MmsReportInventoryAuthority.LiveMmsObserved,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.BR.Other01", "LD0/LLN0.Other", buffered: true));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory);

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsReady, plan.Status);
        Assert.True(plan.HasExactOperationalTargets);
        Assert.Equal(
            new[] { "LD0/LLN0.BR.Rpt01" },
            plan.ExactTargetReportControlReferences.OrderBy(x => x).ToArray());
        Assert.Equal(1, plan.ResolvedSegmentCount);
        Assert.Equal(0, plan.UnresolvedSegmentCount);
    }

    [Fact]
    public void PartialResolution_RemainsExplicit_AndNeverBroadensUnresolvedSegment()
    {
        var coverage = Coverage(
            Segment(
                "LD0/LLN0.Events",
                "LD0/LLN0.BR.Rpt01",
                buffered: true,
                "LD0/XCBR1.Pos.stVal"),
            Segment(
                "LD0/LLN0.Analog",
                "LD0/LLN0.RP.Meas",
                buffered: false,
                "LD0/MMXU1.A.phsA.cVal.mag.f"));

        var inventory = Inventory(
            MmsReportInventoryAuthority.LiveMmsObserved,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.RP.Meas01", string.Empty, buffered: false),
            Candidate("LD0/LLN0.RP.Meas02", string.Empty, buffered: false));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory);

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.PartialExactTargetsReady, plan.Status);
        Assert.Equal(1, plan.ResolvedSegmentCount);
        Assert.Equal(1, plan.UnresolvedSegmentCount);
        Assert.Equal(new[] { "LD0/LLN0.BR.Rpt01" }, plan.ExactTargetReportControlReferences);
        Assert.DoesNotContain(
            plan.ExactTargetReportControlReferences,
            reference => reference.Contains("Meas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonOperationalPlan_DoesNotRequireConnectedSession()
    {
        var coverage = Coverage(
            Segment(
                "LD0/LLN0.Events",
                "LD0/LLN0.BR.Rpt01",
                buffered: true,
                "LD0/XCBR1.Pos.stVal"));
        var inventory = Inventory(
            MmsReportInventoryAuthority.SclDesignProjection,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));
        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory);

        await using var session = new MmsClientSession();
        var result = await session.ProbeCanonicalStaticAcquisitionAsync(plan, inventory);

        Assert.False(result.NetworkProbePerformed);
        Assert.Null(result.Availability);
        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired, result.Status);
    }

    [Fact]
    public async Task ExecutorRejectsInventoryAuthoritySwapBeforeNetworkWork()
    {
        var coverage = Coverage(
            Segment(
                "LD0/LLN0.Events",
                "LD0/LLN0.BR.Rpt01",
                buffered: true,
                "LD0/XCBR1.Pos.stVal"));
        var liveInventory = Inventory(
            MmsReportInventoryAuthority.LiveMmsObserved,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));
        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            liveInventory);

        var structuralOnlyInventory = Inventory(
            MmsReportInventoryAuthority.SclDesignProjection,
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));

        await using var session = new MmsClientSession();
        var result = await session.ProbeCanonicalStaticAcquisitionAsync(
            plan,
            structuralOnlyInventory);

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired, result.Status);
        Assert.False(result.NetworkProbePerformed);
        Assert.Null(result.Availability);
    }

    [Theory]
    [InlineData(66, 2)]
    [InlineData(300, 5)]
    public void Exact_Targets_Are_Paged_Without_The_Previous_Global_Budget_Abort(
        int count, int expectedPages)
    {
        var targetSet = Enumerable.Range(1, count)
            .Select(index => $"LD0/LLN0.BR.Rpt{index:000}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetResolution = new MmsCanonicalStaticRcbTargetResolution
        {
            ExactLiveReportControlReferences = targetSet,
            Segments =
            [
                new MmsCanonicalStaticRcbTargetSegment
                {
                    DataSetReference = "LD0/LLN0.Events",
                    ExactLiveReportControlReferences = targetSet.ToArray(),
                    Resolution = "exact-live-dataset-binding"
                }
            ]
        };
        var plan = new MmsCanonicalStaticAcquisitionProbePlan
        {
            InventoryAuthority = MmsReportInventoryAuthority.LiveMmsObserved,
            Coverage = Coverage(Segment(
                "LD0/LLN0.Events", "LD0/LLN0.BR.Rpt",
                buffered: true, "LD0/XCBR1.Pos.stVal")),
            TargetResolution = targetResolution,
            Status = MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsReady,
            ExactTargetReportControlReferences = targetSet
        };
        var inventory = Inventory(
            MmsReportInventoryAuthority.LiveMmsObserved,
            targetSet.Select(r => Candidate(r, "LD0/LLN0.Events", true)).ToArray());

        var examined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = MmsCanonicalStaticExactTargetPager.Select(
                plan, inventory, 64, cursor, "active-association");
            Assert.InRange(page.References.Count, 1, 64);
            foreach (var reference in page.References)
                Assert.True(examined.Add(reference), "Duplicate exact target across pages.");
            cursor = page.ContinuationToken;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Null(cursor);
        Assert.Equal(count, examined.Count);
        Assert.Equal(expectedPages, pages);

        var first = MmsCanonicalStaticExactTargetPager.Select(
            plan, inventory, 64, associationKey: "active-association");
        Assert.NotNull(first.ContinuationToken);
        Assert.Throws<ArgumentException>(() =>
            MmsCanonicalStaticExactTargetPager.Select(
                plan, inventory, 64, first.ContinuationToken, "reconnected-association"));
    }

    private static CanonicalStaticReportCoveragePlan Coverage(
        params CanonicalStaticReportCoverageSegment[] segments)
    {
        var signals = segments
            .SelectMany(segment => segment.SelectedSignalReferences)
            .Distinct(StringComparer.Ordinal)
            .Select(reference => new CanonicalStaticReportSignalCoverage
            {
                RequestedReference = reference,
                CanonicalReference = reference,
                Status = CanonicalStaticReportSignalCoverageStatus.Covered
            })
            .ToArray();

        return new CanonicalStaticReportCoveragePlan
        {
            Ingress = CanonicalIngressKind.SclFile,
            Signals = signals,
            Segments = segments
        };
    }

    private static CanonicalStaticReportCoverageSegment Segment(
        string dataSet,
        string rcbReference,
        bool buffered,
        string selectedSignal)
        => new()
        {
            DataSetReference = dataSet,
            SourceDataSetReference = dataSet,
            SelectedSignalReferences = [selectedSignal],
            OrderedMembers =
            [
                new CanonicalDataSetMember(
                    0,
                    selectedSignal[..selectedSignal.LastIndexOf('.')],
                    selectedSignal.Contains("MMXU", StringComparison.Ordinal) ? "MX" : "ST",
                    new CanonicalProvenance(CanonicalEvidenceSource.SclDeclared, CanonicalConfidence.Exact))
            ],
            ReportControls =
            [
                new CanonicalReportControl
                {
                    Reference = rcbReference,
                    DataSetReference = dataSet,
                    Buffered = buffered
                }
            ]
        };

    private static MmsReportInventory Inventory(
        MmsReportInventoryAuthority authority,
        params MmsReportControlCandidate[] controls)
    {
        var inventory = new MmsReportInventory
        {
            Authority = authority
        };
        inventory.ReportControls.AddRange(controls);
        return inventory;
    }

    private static MmsReportControlCandidate Candidate(
        string reference,
        string dataSet,
        bool buffered)
        => new()
        {
            Reference = reference,
            DataSetReference = dataSet,
            Buffered = buffered
        };
}
