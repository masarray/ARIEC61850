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
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory,
            MmsReportInventoryAuthority.SclDesignProjection);

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
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.BR.Other01", "LD0/LLN0.Other", buffered: true));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory,
            MmsReportInventoryAuthority.LiveMmsObserved);

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
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.RP.Meas01", string.Empty, buffered: false),
            Candidate("LD0/LLN0.RP.Meas02", string.Empty, buffered: false));

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory,
            MmsReportInventoryAuthority.LiveMmsObserved);

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
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true));
        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            inventory,
            MmsReportInventoryAuthority.SclDesignProjection);

        await using var session = new MmsClientSession();
        var result = await session.ProbeCanonicalStaticAcquisitionAsync(plan, inventory);

        Assert.False(result.NetworkProbePerformed);
        Assert.Null(result.Availability);
        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired, result.Status);
    }

    [Fact]
    public async Task TargetBudgetFailsBeforeAnyNetworkWork_AndNeverRunsPartialProbe()
    {
        var targetSet = Enumerable.Range(1, 3)
            .Select(index => $"LD0/LLN0.BR.Rpt{index:00}")
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
            Coverage = Coverage(
                Segment(
                    "LD0/LLN0.Events",
                    "LD0/LLN0.BR.Rpt01",
                    buffered: true,
                    "LD0/XCBR1.Pos.stVal")),
            TargetResolution = targetResolution,
            Status = MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsReady,
            ExactTargetReportControlReferences = targetSet
        };

        await using var session = new MmsClientSession();
        var result = await session.ProbeCanonicalStaticAcquisitionAsync(
            plan,
            Inventory(
                Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", true),
                Candidate("LD0/LLN0.BR.Rpt02", "LD0/LLN0.Events", true),
                Candidate("LD0/LLN0.BR.Rpt03", "LD0/LLN0.Events", true)),
            options: new MmsCanonicalStaticAcquisitionProbeOptions
            {
                MaxExactTargets = 2
            });

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.TargetBudgetExceeded, result.Status);
        Assert.False(result.NetworkProbePerformed);
        Assert.Null(result.Availability);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("no partial probe", StringComparison.OrdinalIgnoreCase));
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
                    new CanonicalProvenance(CanonicalEvidenceSource.Scl, CanonicalConfidence.Exact))
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

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] controls)
    {
        var inventory = new MmsReportInventory();
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
