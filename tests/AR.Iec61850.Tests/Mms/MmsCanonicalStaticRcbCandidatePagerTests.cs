using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsCanonicalStaticRcbCandidatePagerTests
{
    [Fact]
    public void SixtySixRcbs_WithTwoExactLiveDatSetMatches_ScansOnlyMatchingTwo()
    {
        var catalog = Enumerable.Range(0, 66).Select(index => Candidate(
            $"LABLD/LLN0.BR.Rpt{index:000}",
            index is 19 or 64 ? "LABLD/LLN0.Events" : "LABLD/LLN0.Unrelated")).ToArray();
        catalog[19].EnabledState = "true"; // busy candidate is never written to by this planner
        catalog[64].EnabledState = "false";

        var page = MmsCanonicalStaticRcbCandidatePager.BuildPage(
            Coverage(), catalog, maxPerPage: 8);

        Assert.Equal(2, page.EligibleCandidateCount);
        Assert.Equal(2, page.ExaminedCandidateCount);
        Assert.Equal(0, page.DeferredCandidateCount);
        Assert.False(page.HasContinuation);
        Assert.Equal(
            ["LABLD/LLN0.BR.Rpt019", "LABLD/LLN0.BR.Rpt064"],
            page.Candidates.Select(candidate => candidate.Reference).ToArray());
        Assert.Equal("true", catalog[19].EnabledState);
        Assert.Equal("false", catalog[64].EnabledState);
    }

    [Fact]
    public void ThreeHundredUnknownBindings_AreBoundedResumableAndOrderIndependent()
    {
        var catalog = Enumerable.Range(0, 300)
            .Select(index => Candidate($"LABLD/LLN0.BR.Rpt{index:000}", ""))
            .ToArray();
        const int window = 37;
        var first = MmsCanonicalStaticRcbCandidatePager.BuildPage(
            Coverage(), catalog, maxPerPage: window);
        var permuted = MmsCanonicalStaticRcbCandidatePager.BuildPage(
            Coverage(), catalog.Reverse(), maxPerPage: window);

        Assert.Equal(300, first.EligibleCandidateCount);
        Assert.Equal(window, first.ExaminedCandidateCount);
        Assert.Equal(263, first.DeferredCandidateCount);
        Assert.Equal(37, first.NextOffset);
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.Reference),
            permuted.Candidates.Select(candidate => candidate.Reference));
        Assert.Equal("LABLD/LLN0.BR.Rpt299", first.Candidates[0].Reference);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages = 0;
        int? next = 0;
        while (next.HasValue)
        {
            var page = MmsCanonicalStaticRcbCandidatePager.BuildPage(
                Coverage(), catalog, maxPerPage: window, offset: next.Value);
            pages++;
            Assert.InRange(page.ExaminedCandidateCount, 1, window);
            foreach (var candidate in page.Candidates)
                Assert.True(visited.Add(candidate.Reference));
            next = page.NextOffset;
        }

        Assert.Equal(300, visited.Count);
        Assert.Equal(9, pages);
    }

    [Fact]
    public void MixedFamiliesAndKnownWrongBindings_DoNotConsumeTheSelectedBudget()
    {
        var catalog = Enumerable.Range(0, 200)
            .Select(index => Candidate($"LABLD/LLN0.RP.Other{index:000}", ""))
            .Concat(Enumerable.Range(0, 100)
                .Select(index => Candidate($"LABLD/LLN0.BR.Rpt{index:000}",
                    "LABLD/LLN0.Other", buffered: true)))
            .Append(Candidate("LABLD/LLN0.BR.Rpt299", "LABLD/LLN0.Events"))
            .ToArray();
        var page = MmsCanonicalStaticRcbCandidatePager.BuildPage(
            Coverage(), catalog, maxPerPage: 1);

        Assert.Equal(1, page.EligibleCandidateCount);
        Assert.Equal("LABLD/LLN0.BR.Rpt299", Assert.Single(page.Candidates).Reference);
        Assert.False(page.HasContinuation);
    }

    private static CanonicalStaticReportCoveragePlan Coverage()
        => new()
        {
            Segments =
            [
                new CanonicalStaticReportCoverageSegment
                {
                    DataSetReference = "LABLD/LLN0.Events",
                    SelectedSignalReferences = ["LABLD/XCBR1.Pos.stVal"],
                    ReportControls =
                    [
                        new CanonicalReportControl
                        {
                            MmsDomain = "LABLD",
                            Buffered = true,
                            Reference = "LABLD/LLN0.BR.Rpt299",
                            DataSetReference = "LABLD/LLN0.Events"
                        }
                    ]
                }
            ]
        };

    private static MmsReportControlCandidate Candidate(
        string reference, string dataSet, bool buffered = true)
        => new()
        {
            Domain = "LABLD",
            Reference = reference,
            Buffered = buffered,
            DataSetReference = dataSet
        };
}
