using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticLiveReconciliationPlannerTests
{
    [Fact]
    public void Planner_Collapses_Selected_Static_Coverage_To_Minimum_Domain_Set()
    {
        var coverage = Coverage(
            Segment(
                "IEDLD0/LLN0.Events",
                Report("IEDLD0/LLN0.BR.Rpt", "IEDLD0", buffered: true),
                "IEDLD0/XCBR1.Pos.stVal"),
            Segment(
                "IEDLD0/LLN0.Analog",
                Report("IEDLD0/LLN0.RP.Meas", "IEDLD0", buffered: false),
                "IEDLD0/MMXU1.A.phsA.cVal.mag.f"),
            Segment(
                "IEDLD1/LLN0.Events",
                Report("IEDLD1/LLN0.BR.Rpt", "IEDLD1", buffered: true),
                "IEDLD1/GGIO1.Alm.stVal"));

        var plan = MmsCanonicalStaticLiveReconciliationPlanner.Build(coverage);

        Assert.Equal(MmsCanonicalStaticLiveReconciliationStatus.Ready, plan.Status);
        Assert.True(plan.RequiresNetwork);
        Assert.Equal(2, plan.Domains.Count);

        var ld0 = plan.Domains.Single(target => target.Domain == "IEDLD0");
        Assert.True(ld0.NeedsBuffered);
        Assert.True(ld0.NeedsUnbuffered);

        var ld1 = plan.Domains.Single(target => target.Domain == "IEDLD1");
        Assert.True(ld1.NeedsBuffered);
        Assert.False(ld1.NeedsUnbuffered);
    }

    [Fact]
    public void Planner_Uses_Exact_Reference_Domain_When_MmsDomain_Is_Not_Populated()
    {
        var coverage = Coverage(
            Segment(
                "IEDLD0/LLN0.Events",
                Report("IEDLD0/LLN0.BR.Rpt", mmsDomain: string.Empty, buffered: true),
                "IEDLD0/XCBR1.Pos.stVal"));

        var plan = MmsCanonicalStaticLiveReconciliationPlanner.Build(coverage);

        var target = Assert.Single(plan.Domains);
        Assert.Equal("IEDLD0", target.Domain);
        Assert.True(target.NeedsBuffered);
    }

    [Fact]
    public void Planner_FailsClosed_When_Domain_Budget_Would_Require_Truncation()
    {
        var coverage = Coverage(
            Segment(
                "IEDLD0/LLN0.Events",
                Report("IEDLD0/LLN0.BR.Rpt", "IEDLD0", true),
                "IEDLD0/XCBR1.Pos.stVal"),
            Segment(
                "IEDLD1/LLN0.Events",
                Report("IEDLD1/LLN0.BR.Rpt", "IEDLD1", true),
                "IEDLD1/XCBR1.Pos.stVal"));

        var plan = MmsCanonicalStaticLiveReconciliationPlanner.Build(
            coverage,
            new MmsCanonicalStaticLiveReconciliationOptions
            {
                MaxDomains = 1
            });

        Assert.Equal(MmsCanonicalStaticLiveReconciliationStatus.DomainBudgetExceeded, plan.Status);
        Assert.False(plan.RequiresNetwork);
        Assert.Equal(2, plan.Domains.Count);
        Assert.Contains(plan.Warnings, warning =>
            warning.Contains("no partial domain subset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Planner_DoesNot_Request_Network_When_No_Configured_Static_Coverage_Exists()
    {
        var plan = MmsCanonicalStaticLiveReconciliationPlanner.Build(
            new CanonicalStaticReportCoveragePlan
            {
                Signals =
                [
                    new CanonicalStaticReportSignalCoverage
                    {
                        RequestedReference = "IEDLD0/MMXU1.A.phsA.cVal.mag.f",
                        CanonicalReference = "IEDLD0/MMXU1.A.phsA.cVal.mag.f",
                        Status = CanonicalStaticReportSignalCoverageStatus.NoStaticDataSetMembership
                    }
                ]
            });

        Assert.Equal(MmsCanonicalStaticLiveReconciliationStatus.NoStaticCoverage, plan.Status);
        Assert.False(plan.RequiresNetwork);
        Assert.Empty(plan.Domains);
    }

    [Fact]
    public async Task Reconcile_NoStaticCoverage_Performs_Zero_Network_Work_On_Disconnected_Session()
    {
        await using var session = new MmsClientSession();

        var result = await session.ReconcileCanonicalStaticReportInventoryAsync(
            new CanonicalStaticReportCoveragePlan());

        Assert.Equal(MmsCanonicalStaticLiveReconciliationStatus.NoStaticCoverage, result.Status);
        Assert.Equal(MmsReportInventoryAuthority.LiveMmsObserved, result.Inventory.Authority);
        Assert.Equal(0, result.EnumeratedVariableCount);
        Assert.Equal(0, result.CandidateReportControlCount);
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
        string dataSetReference,
        CanonicalReportControl report,
        string selectedSignal)
        => new()
        {
            DataSetReference = dataSetReference,
            SourceDataSetReference = dataSetReference,
            SelectedSignalReferences = [selectedSignal],
            ReportControls = [report],
            OrderedMembers =
            [
                new CanonicalDataSetMember(
                    0,
                    selectedSignal[..selectedSignal.LastIndexOf('.')],
                    selectedSignal.Contains("MMXU", StringComparison.Ordinal) ? "MX" : "ST",
                    new CanonicalProvenance(CanonicalEvidenceSource.SclDeclared, CanonicalConfidence.Exact))
            ]
        };

    private static CanonicalReportControl Report(
        string reference,
        string mmsDomain,
        bool buffered)
        => new()
        {
            Reference = reference,
            MmsDomain = mmsDomain,
            LogicalNode = "LLN0",
            Name = reference.Split('.').Last(),
            Buffered = buffered,
            DataSetReference = reference.Contains("Meas", StringComparison.Ordinal)
                ? $"{mmsDomain}/LLN0.Analog"
                : $"{mmsDomain}/LLN0.Events",
            Provenance = new CanonicalProvenance(
                CanonicalEvidenceSource.SclDeclared,
                CanonicalConfidence.Exact)
        };
}
