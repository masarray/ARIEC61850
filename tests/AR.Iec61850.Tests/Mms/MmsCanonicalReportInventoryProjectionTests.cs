using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalReportInventoryProjectionTests
{
    [Fact]
    public void SclCanonicalProjection_PreservesExactConfiguredIdentity_WithoutInventingLiveEvidence()
    {
        var model = Model(
            CanonicalIngressKind.SclFile,
            reportReference: "LD0/LLN0.BR.Rpt",
            dataSetReference: "LD0/LLN0.Events");

        var inventory = MmsCanonicalReportInventoryProjection.Build(model);

        Assert.Equal(MmsReportInventoryAuthority.SclDesignProjection, inventory.Authority);
        var report = Assert.Single(inventory.ReportControls);
        Assert.Equal("LD0/LLN0.BR.Rpt", report.Reference);
        Assert.DoesNotContain("01", report.Reference, StringComparison.Ordinal);
        Assert.Equal("LD0/LLN0.Events", report.DataSetReference);
        Assert.True(report.Buffered);
        Assert.Equal("BR", report.FunctionalConstraint);
        Assert.Empty(report.Attributes);
        Assert.Equal(string.Empty, report.EnabledState);
        Assert.Equal(string.Empty, report.ReservationTimeSeconds);
        Assert.Equal(string.Empty, report.Owner);
    }

    [Fact]
    public void DiscoveryDerivedCanonicalProjection_IsStillStructural_NotLiveRuntimeAuthority()
    {
        var model = Model(
            CanonicalIngressKind.LiveMmsDiscovery,
            reportReference: "LD0/LLN0.RP.Rpt01",
            dataSetReference: "LD0/LLN0.Analog");

        var inventory = MmsCanonicalReportInventoryProjection.Build(model);

        Assert.Equal(MmsReportInventoryAuthority.CanonicalModelProjection, inventory.Authority);
        var report = Assert.Single(inventory.ReportControls);
        Assert.Equal("LD0/LLN0.RP.Rpt01", report.Reference);
        Assert.False(report.Buffered);
        Assert.Equal("RP", report.FunctionalConstraint);
        Assert.Empty(report.Attributes);
    }

    [Fact]
    public void CanonicalProjection_CannotAuthorizeOperationalStaticTargeting()
    {
        var model = Model(
            CanonicalIngressKind.SclFile,
            reportReference: "LD0/LLN0.BR.Rpt01",
            dataSetReference: "LD0/LLN0.Events");
        var inventory = MmsCanonicalReportInventoryProjection.Build(model);

        var coverage = new CanonicalStaticReportCoveragePlan
        {
            Ingress = CanonicalIngressKind.SclFile,
            Signals =
            [
                new CanonicalStaticReportSignalCoverage
                {
                    RequestedReference = "LD0/XCBR1.Pos.stVal",
                    CanonicalReference = "LD0/XCBR1.Pos.stVal",
                    Status = CanonicalStaticReportSignalCoverageStatus.Covered
                }
            ],
            Segments =
            [
                new CanonicalStaticReportCoverageSegment
                {
                    DataSetReference = "LD0/LLN0.Events",
                    SourceDataSetReference = "LD0/LLN0.Events",
                    SelectedSignalReferences = ["LD0/XCBR1.Pos.stVal"],
                    ReportControls = model.ReportControls,
                    OrderedMembers = model.DataSets[0].Members
                }
            ]
        };

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(coverage, inventory);

        Assert.Equal(MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired, plan.Status);
        Assert.False(plan.HasExactOperationalTargets);
    }

    private static CanonicalIedModel Model(
        CanonicalIngressKind ingress,
        string reportReference,
        string dataSetReference)
        => new()
        {
            Source = new CanonicalSourceEnvelope
            {
                Ingress = ingress,
                SourceName = ingress == CanonicalIngressKind.SclFile ? "relay.cid" : "LiveMmsDiscovery"
            },
            DataSets =
            [
                new CanonicalDataSet
                {
                    Reference = dataSetReference,
                    MmsDomain = "LD0",
                    LogicalNode = "LLN0",
                    Name = dataSetReference.Split('.').Last(),
                    Members =
                    [
                        new CanonicalDataSetMember(
                            0,
                            "LD0/XCBR1.Pos",
                            "ST",
                            new CanonicalProvenance(
                                ingress == CanonicalIngressKind.SclFile
                                    ? CanonicalEvidenceSource.SclDeclared
                                    : CanonicalEvidenceSource.LiveMms,
                                CanonicalConfidence.Exact))
                    ]
                }
            ],
            ReportControls =
            [
                new CanonicalReportControl
                {
                    Reference = reportReference,
                    MmsDomain = "LD0",
                    LogicalNode = "LLN0",
                    Name = reportReference.Split('.').Last(),
                    Buffered = reportReference.Contains(".BR.", StringComparison.Ordinal),
                    DataSetReference = dataSetReference,
                    ReportId = "RID",
                    ConfRev = "1",
                    TriggerOptions = "dchg,qchg,gi",
                    OptionalFields = "seqNum,timeStamp,dataSet,reasonCode",
                    BufferTimeMs = "100",
                    IntegrityPeriodMs = "0",
                    Provenance = new CanonicalProvenance(
                        ingress == CanonicalIngressKind.SclFile
                            ? CanonicalEvidenceSource.SclDeclared
                            : CanonicalEvidenceSource.LiveMms,
                        CanonicalConfidence.Exact)
                }
            ]
        };
}
