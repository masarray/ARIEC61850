using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticRcbTargetResolverTests
{
    [Fact]
    public void Exact_Live_Dataset_Binding_Targets_Only_Covered_Rcb_Family()
    {
        var coverage = Coverage(
            dataSet: "LD0/LLN0.Events",
            configured: new CanonicalReportControl
            {
                Reference = "LD0/LLN0.BR.Rpt",
                DataSetReference = "LD0/LLN0.Events",
                Buffered = true
            });

        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.Rpt01", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.BR.Rpt02", "LD0/LLN0.Events", buffered: true),
            Candidate("LD0/LLN0.RP.Rpt01", "LD0/LLN0.Events", buffered: false),
            Candidate("LD0/LLN0.BR.Other01", "LD0/LLN0.Other", buffered: true));

        var resolution = MmsCanonicalStaticRcbTargetResolver.Resolve(coverage, inventory);

        Assert.True(resolution.HasTargets);
        Assert.Equal(
            new[] { "LD0/LLN0.BR.Rpt01", "LD0/LLN0.BR.Rpt02" },
            resolution.ExactLiveReportControlReferences.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal("exact-live-dataset-binding", resolution.Segments.Single().Resolution);
    }

    [Fact]
    public void Exact_Configured_Reference_Can_Target_When_Live_Dataset_Binding_Is_Not_Yet_Populated()
    {
        var coverage = Coverage(
            dataSet: "LD0/LLN0.Events",
            configured: new CanonicalReportControl
            {
                Reference = "LD0/LLN0.BR.Rpt01",
                DataSetReference = "LD0/LLN0.Events",
                Buffered = true
            });

        var inventory = Inventory(
            Candidate("LD0/LLN0$BR$Rpt01", string.Empty, buffered: true),
            Candidate("LD0/LLN0.BR.Rpt02", string.Empty, buffered: true));

        var resolution = MmsCanonicalStaticRcbTargetResolver.Resolve(coverage, inventory);

        Assert.Equal(new[] { "LD0/LLN0$BR$Rpt01" }, resolution.ExactLiveReportControlReferences);
        Assert.Equal("exact-configured-rcb-reference", resolution.Segments.Single().Resolution);
    }

    [Fact]
    public void Unresolved_Segment_Does_Not_Guess_Name_Family()
    {
        var coverage = Coverage(
            dataSet: "LD0/LLN0.Events",
            configured: new CanonicalReportControl
            {
                Reference = "LD0/LLN0.BR.Rpt",
                DataSetReference = "LD0/LLN0.Events",
                Buffered = true
            });

        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.Rpt01", string.Empty, buffered: true),
            Candidate("LD0/LLN0.BR.Rpt02", string.Empty, buffered: true));

        var resolution = MmsCanonicalStaticRcbTargetResolver.Resolve(coverage, inventory);

        Assert.False(resolution.HasTargets);
        Assert.Empty(resolution.ExactLiveReportControlReferences);
        Assert.Equal("unresolved", resolution.Segments.Single().Resolution);
        Assert.Contains(
            resolution.Warnings,
            warning => warning.Contains("No sibling/name-family guess", StringComparison.OrdinalIgnoreCase));
    }

    private static CanonicalStaticReportCoveragePlan Coverage(
        string dataSet,
        CanonicalReportControl configured)
        => new()
        {
            Segments = new[]
            {
                new CanonicalStaticReportCoverageSegment
                {
                    DataSetReference = dataSet,
                    SourceDataSetReference = dataSet,
                    SelectedSignalReferences = new[] { "LD0/XCBR1.Pos.stVal" },
                    OrderedMembers = new[]
                    {
                        new CanonicalDataSetMember(
                            0,
                            "LD0/XCBR1.Pos",
                            "ST",
                            new CanonicalProvenance(CanonicalEvidenceSource.LiveMms, CanonicalConfidence.Exact))
                    },
                    ReportControls = new[] { configured }
                }
            }
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
