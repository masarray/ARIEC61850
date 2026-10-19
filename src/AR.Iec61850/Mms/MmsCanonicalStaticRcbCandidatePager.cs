using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

/// <summary>
/// Deterministic read-only candidate window. Known nonmatching DatSet bindings are
/// excluded; unknown bindings remain eligible for a bounded, resumable MMS read.
/// A catalog size is never an operation-size failure by itself.
/// </summary>
public static class MmsCanonicalStaticRcbCandidatePager
{
    public static MmsCanonicalStaticRcbCandidatePage BuildPage(
        CanonicalStaticReportCoveragePlan coverage,
        IEnumerable<MmsReportControlCandidate> liveCandidates,
        int maxPerPage,
        int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(liveCandidates);

        var targets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var configuredReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in coverage.Segments)
        {
            if (segment.SelectedSignalReferences.Length == 0)
                continue;
            var dataSet = MmsRcbAvailabilityEvaluator.NormalizeReference(segment.DataSetReference);
            if (string.IsNullOrWhiteSpace(dataSet))
                continue;

            foreach (var report in segment.ReportControls)
            {
                var domain = DomainOf(report.MmsDomain, report.Reference);
                if (domain.Length == 0)
                    continue;
                var key = FamilyKey(domain, report.Buffered);
                if (!targets.TryGetValue(key, out var bindings))
                    targets[key] = bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bindings.Add(dataSet);

                var configured = MmsRcbAvailabilityEvaluator.NormalizeReference(report.Reference);
                if (configured.Length > 0)
                    configuredReferences.Add(configured);
            }
        }

        var eligible = liveCandidates
            .Where(candidate =>
            {
                var domain = DomainOf(candidate.Domain, candidate.Reference);
                if (!targets.TryGetValue(FamilyKey(domain, candidate.Buffered), out var bindings))
                    return false;
                var boundDataSet = MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.DataSetReference);
                return boundDataSet.Length == 0 || bindings.Contains(boundDataSet);
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Reference))
            .DistinctBy(candidate => MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.Reference),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate =>
            {
                var binding = MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.DataSetReference);
                if (binding.Length > 0) return 0; // already bound to selected DataSet by live evidence
                return configuredReferences.Contains(
                    MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.Reference)) ? 1 : 2;
            })
            .ThenBy(candidate => candidate.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Reference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();

        var start = Math.Clamp(offset, 0, eligible.Length);
        var windowSize = Math.Clamp(maxPerPage, 1, 512);
        var page = eligible.Skip(start).Take(windowSize).ToArray();
        var next = start + page.Length;

        return new MmsCanonicalStaticRcbCandidatePage
        {
            Candidates = page,
            EligibleCandidateCount = eligible.Length,
            ExaminedCandidateCount = page.Length,
            DeferredCandidateCount = eligible.Length - next,
            NextOffset = next < eligible.Length ? next : null
        };
    }

    private static string DomainOf(string? domain, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return domain.Trim();
        var value = reference?.Trim() ?? string.Empty;
        var slash = value.IndexOf('/');
        return slash > 0 ? value[..slash] : string.Empty;
    }

    private static string FamilyKey(string domain, bool buffered)
        => domain + (buffered ? "|BR" : "|RP");
}

public sealed class MmsCanonicalStaticRcbCandidatePage
{
    public IReadOnlyList<MmsReportControlCandidate> Candidates { get; init; }
        = Array.Empty<MmsReportControlCandidate>();
    public int EligibleCandidateCount { get; init; }
    public int ExaminedCandidateCount { get; init; }
    public int DeferredCandidateCount { get; init; }
    public int? NextOffset { get; init; }
    public bool HasContinuation => NextOffset.HasValue;
}
