namespace AR.Iec61850.Mms;

public sealed class MmsRcbAvailabilitySelection
{
    public IReadOnlyList<MmsReportControlCandidate> Candidates { get; init; } = Array.Empty<MmsReportControlCandidate>();
    public bool TargetFilterApplied { get; init; }
    public int RequestedTargetCount { get; init; }
    public int MatchedTargetCount { get; init; }
    public int EligibleCountBeforeLimit { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pure candidate selector for availability probing.
///
/// Exact target references are never broadened to family/neighbor RCBs. Family resolution
/// belongs to the canonical/report-family planner before this stage. This keeps the network
/// hot path proportional to the RCBs that can actually serve the selected signals.
/// </summary>
public static class MmsRcbAvailabilityTargetSelector
{
    public static MmsRcbAvailabilitySelection Select(
        MmsReportInventory inventory,
        MmsRcbAvailabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var targets = options.TargetReportControlReferences
            .Select(MmsRcbAvailabilityEvaluator.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targeted = targets.Count > 0;

        IEnumerable<MmsReportControlCandidate> eligible = inventory.ReportControls;
        if (targeted)
        {
            eligible = eligible.Where(candidate =>
                targets.Contains(MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.Reference)));
        }

        var eligibleArray = eligible.ToArray();
        var matched = targeted ? eligibleArray.Length : 0;
        if (targeted && matched < targets.Count)
        {
            warnings.Add(
                $"Targeted RCB availability matched {matched} of {targets.Count} requested exact live reference(s); " +
                "unmatched targets were not broadened to unrelated RCBs.");
        }

        var max = Math.Clamp(options.MaxReportControls, 1, 4096);
        var candidates = eligibleArray
            .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.DataSetReference))
            .ThenByDescending(candidate => candidate.Buffered)
            .ThenBy(candidate => candidate.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        if (eligibleArray.Length > candidates.Length)
            warnings.Add($"Availability check was bounded to {candidates.Length} of {eligibleArray.Length} eligible RCBs.");

        return new MmsRcbAvailabilitySelection
        {
            Candidates = candidates,
            TargetFilterApplied = targeted,
            RequestedTargetCount = targets.Count,
            MatchedTargetCount = matched,
            EligibleCountBeforeLimit = eligibleArray.Length,
            Warnings = warnings
        };
    }
}
