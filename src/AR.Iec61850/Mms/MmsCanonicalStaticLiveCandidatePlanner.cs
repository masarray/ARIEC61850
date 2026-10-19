using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

/// <summary>
/// Selects exact configured RCB references ahead of bounded discovery fallback.
/// A reference match authorizes READ-only DatSet verification, not activation.
/// Unknown candidates remain deferred with a stable continuation offset.
/// </summary>
public static class MmsCanonicalStaticLiveCandidatePlanner
{
    public static MmsCanonicalStaticLiveCandidatePage Select(
        CanonicalStaticReportCoveragePlan coverage,
        IReadOnlyList<MmsReportControlCandidate> candidates,
        int maxReads,
        int continuationOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(candidates);
        if (maxReads < 1) throw new ArgumentOutOfRangeException(nameof(maxReads));
        if (continuationOffset < 0) throw new ArgumentOutOfRangeException(nameof(continuationOffset));

        var expected = coverage.Segments
            .SelectMany(segment => segment.ReportControls)
            .Select(report => report.Reference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.Ordinal);

        var ordered = candidates
            .OrderBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();
        var exact = ordered.Where(candidate => expected.Contains(candidate.Reference)).ToArray();
        var unknown = ordered.Where(candidate => !expected.Contains(candidate.Reference)).ToArray();

        // Discovery without an exact catalog reference is not evidence that an
        // arbitrary first RCB belongs to the selected DataSet. Only bounded
        // read-only DatSet checks may resolve it, and completion remains partial.
        // If every configured target has an exact live catalog reference,
        // never probe unrelated siblings. A failed DatSet verification remains
        // unresolved rather than silently falling back to broad mutation.
        var allTargetsMapped = expected.Count > 0 && expected.All(reference =>
            exact.Any(candidate => candidate.Reference == reference));
        var scope = allTargetsMapped ? exact : exact.Concat(unknown).ToArray();
        var selected = scope.Skip(continuationOffset).Take(maxReads).ToArray();
        var next = Math.Min(scope.Length, continuationOffset + selected.Length);
        return new MmsCanonicalStaticLiveCandidatePage(
            selected, ordered.Length, exact.Length,
            next < scope.Length ? next : null);
    }
}

public sealed record MmsCanonicalStaticLiveCandidatePage(
    IReadOnlyList<MmsReportControlCandidate> Candidates,
    int CatalogCount,
    int ExactReferenceCount,
    int? NextOffset);
