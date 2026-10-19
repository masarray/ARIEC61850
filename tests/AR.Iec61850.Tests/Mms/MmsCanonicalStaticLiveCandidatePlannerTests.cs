using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticLiveCandidatePlannerTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(66)]
    [InlineData(300)]
    public void Exact_Target_Reads_Are_Independent_Of_Global_Rcb_Count(int catalogSize)
    {
        var catalog = Enumerable.Range(0, catalogSize)
            .Select(i => Candidate($"IEDLD0/LLN0.BR.R{i:000}"))
            .Reverse().ToArray();
        var exact = $"IEDLD0/LLN0.BR.R{catalogSize-1:000}";
        var page = MmsCanonicalStaticLiveCandidatePlanner.Select(
            Coverage(exact), catalog, maxReads: 4);

        Assert.Equal(catalogSize, page.CatalogCount);
        Assert.Equal(1, page.ExactReferenceCount);
        Assert.Equal(exact, Assert.Single(page.Candidates).Reference);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void Unresolved_Catalog_Pages_Resume_Without_Repeats_And_Never_Claim_Completeness()
    {
        var catalog = Enumerable.Range(0, 300)
            .Select(i => Candidate($"IEDLD0/LLN0.BR.R{i:000}")).Reverse().ToArray();
        var coverage = Coverage("IEDLD0/LLN0.BR.UNKNOWN");
        var all = new List<string>();
        int? offset = 0;
        for (var pageNumber = 0; offset is not null; pageNumber++)
        {
            Assert.True(pageNumber < 10);
            var page = MmsCanonicalStaticLiveCandidatePlanner.Select(
                coverage, catalog, 64, offset.Value);
            Assert.InRange(page.Candidates.Count, 1, 64);
            all.AddRange(page.Candidates.Select(item => item.Reference));
            offset = page.NextOffset;
        }
        Assert.Equal(300, all.Count);
        Assert.Equal(300, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.OrderBy(item => item, StringComparer.Ordinal), all);
    }

    [Fact]
    public void Exact_Multi_Target_Order_Is_Deterministic()
    {
        var catalog = Enumerable.Range(0, 66)
            .Select(i => Candidate($"IEDLD0/LLN0.BR.R{i:000}")).Reverse().ToArray();
        var coverage = Coverage("IEDLD0/LLN0.BR.R030", "IEDLD0/LLN0.BR.R010");
        var page = MmsCanonicalStaticLiveCandidatePlanner.Select(coverage, catalog, 8);
        Assert.Equal(new[] {"IEDLD0/LLN0.BR.R010", "IEDLD0/LLN0.BR.R030"},
            page.Candidates.Select(x => x.Reference));
        Assert.Null(page.NextOffset);
    }

    private static MmsReportControlCandidate Candidate(string reference) => new()
    {
        Reference = reference,
        Domain = "IEDLD0",
        LogicalNode = "LLN0",
        Buffered = true,
        FunctionalConstraint = "BR",
        Name = reference.Split('.').Last()
    };

    private static CanonicalStaticReportCoveragePlan Coverage(params string[] reports) => new()
    {
        Signals = [new CanonicalStaticReportSignalCoverage
        {
            RequestedReference = "IEDLD0/XCBR1.Pos.stVal",
            CanonicalReference = "IEDLD0/XCBR1.Pos.stVal",
            Status = CanonicalStaticReportSignalCoverageStatus.Covered
        }],
        Segments = [new CanonicalStaticReportCoverageSegment
        {
            DataSetReference = "IEDLD0/LLN0.Events",
            SourceDataSetReference = "IEDLD0/LLN0.Events",
            SelectedSignalReferences = ["IEDLD0/XCBR1.Pos.stVal"],
            ReportControls = reports.Select(reference => new CanonicalReportControl
            {
                Reference = reference,
                MmsDomain = "IEDLD0",
                LogicalNode = "LLN0",
                Buffered = true,
                DataSetReference = "IEDLD0/LLN0.Events",
                Provenance = new CanonicalProvenance(
                    CanonicalEvidenceSource.SclDeclared, CanonicalConfidence.Exact)
            }).ToArray()
        }]
    };
}
