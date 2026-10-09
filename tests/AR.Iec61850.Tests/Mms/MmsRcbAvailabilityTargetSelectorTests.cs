using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRcbAvailabilityTargetSelectorTests
{
    [Fact]
    public void Empty_Target_Set_Preserves_Broad_Diagnostic_Mode()
    {
        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.B01", buffered: true),
            Candidate("LD0/LLN0.RP.U01", buffered: false));

        var selection = MmsRcbAvailabilityTargetSelector.Select(
            inventory,
            new MmsRcbAvailabilityOptions());

        Assert.False(selection.TargetFilterApplied);
        Assert.Equal(2, selection.Candidates.Count);
        Assert.Empty(selection.Warnings);
    }

    [Fact]
    public void Exact_Targets_Probe_Only_Selected_Live_Rcbs()
    {
        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.B01", buffered: true),
            Candidate("LD0/LLN0.BR.B02", buffered: true),
            Candidate("LD0/LLN0.RP.U01", buffered: false));

        var selection = MmsRcbAvailabilityTargetSelector.Select(
            inventory,
            new MmsRcbAvailabilityOptions
            {
                TargetReportControlReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "LD0/LLN0$BR$B02",
                    "LD0/LLN0.RP.U01"
                }
            });

        Assert.True(selection.TargetFilterApplied);
        Assert.Equal(2, selection.RequestedTargetCount);
        Assert.Equal(2, selection.MatchedTargetCount);
        Assert.Equal(
            new[] { "LD0/LLN0.BR.B02", "LD0/LLN0.RP.U01" },
            selection.Candidates.Select(candidate => candidate.Reference).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Missing_Target_Is_Not_Broadened_To_Sibling_Rcb()
    {
        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.Rpt01", buffered: true),
            Candidate("LD0/LLN0.BR.Rpt02", buffered: true));

        var selection = MmsRcbAvailabilityTargetSelector.Select(
            inventory,
            new MmsRcbAvailabilityOptions
            {
                TargetReportControlReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "LD0/LLN0.BR.Rpt03"
                }
            });

        Assert.Empty(selection.Candidates);
        Assert.Equal(0, selection.MatchedTargetCount);
        Assert.Contains(
            selection.Warnings,
            warning => warning.Contains("not broadened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Targeted_Mode_Still_Respects_Hard_Maximum()
    {
        var inventory = Inventory(
            Candidate("LD0/LLN0.BR.B01", buffered: true),
            Candidate("LD0/LLN0.BR.B02", buffered: true),
            Candidate("LD0/LLN0.BR.B03", buffered: true));

        var targets = inventory.ReportControls
            .Select(candidate => candidate.Reference)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selection = MmsRcbAvailabilityTargetSelector.Select(
            inventory,
            new MmsRcbAvailabilityOptions
            {
                MaxReportControls = 2,
                TargetReportControlReferences = targets
            });

        Assert.Equal(3, selection.EligibleCountBeforeLimit);
        Assert.Equal(2, selection.Candidates.Count);
        Assert.Contains(
            selection.Warnings,
            warning => warning.Contains("bounded to 2 of 3", StringComparison.OrdinalIgnoreCase));
    }

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsReportControlCandidate Candidate(string reference, bool buffered)
    {
        var slash = reference.IndexOf('/');
        var tail = reference[(slash + 1)..].Split('.');
        return new MmsReportControlCandidate
        {
            Domain = reference[..slash],
            LogicalNode = tail[0],
            FunctionalConstraint = tail[1],
            Name = tail[2],
            Reference = reference,
            Buffered = buffered,
            DataSetReference = "LD0/LLN0.Events"
        };
    }
}
