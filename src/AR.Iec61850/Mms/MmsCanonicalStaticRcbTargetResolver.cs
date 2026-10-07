using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

public sealed class MmsCanonicalStaticRcbTargetSegment
{
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> ConfiguredReportControlReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExactLiveReportControlReferences { get; init; } = Array.Empty<string>();
    public string Resolution { get; init; } = string.Empty;
}

public sealed class MmsCanonicalStaticRcbTargetResolution
{
    public IReadOnlySet<string> ExactLiveReportControlReferences { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MmsCanonicalStaticRcbTargetSegment> Segments { get; init; }
        = Array.Empty<MmsCanonicalStaticRcbTargetSegment>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasTargets => ExactLiveReportControlReferences.Count > 0;
    public int CoveredDataSetCount => Segments.Count;
}

/// <summary>
/// Resolves canonical configured-static coverage to concrete live RCB references without
/// inventing indexed names.
///
/// Exact live identity is accepted from either:
/// 1) an exact configured RCB reference match, or
/// 2) a live RCB whose live DatSet binding exactly matches the canonical covered DataSet
///    and whose BRCB/URCB family is declared for that segment.
///
/// If neither evidence path is available the segment remains unresolved. The caller may
/// explicitly choose a broader diagnostic probe, but this resolver never guesses a sibling
/// RCB merely because its name looks similar.
/// </summary>
public static class MmsCanonicalStaticRcbTargetResolver
{
    public static MmsCanonicalStaticRcbTargetResolution Resolve(
        CanonicalStaticReportCoveragePlan coverage,
        MmsReportInventory liveInventory)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(liveInventory);

        var allTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<MmsCanonicalStaticRcbTargetSegment>();
        var warnings = new List<string>();

        foreach (var segment in coverage.Segments)
        {
            if (segment.SelectedSignalReferences.Length == 0)
                continue;

            var canonicalDataSet = Normalize(segment.DataSetReference);
            var configuredReferences = segment.ReportControls
                .Select(report => Normalize(report.Reference))
                .Where(reference => reference.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var configuredBufferedFamilies = segment.ReportControls
                .Select(report => report.Buffered)
                .Distinct()
                .ToHashSet();

            var exactReferenceMatches = liveInventory.ReportControls
                .Where(candidate => configuredReferences.Contains(Normalize(candidate.Reference)))
                .ToArray();

            var exactDataSetMatches = canonicalDataSet.Length == 0
                ? Array.Empty<MmsReportControlCandidate>()
                : liveInventory.ReportControls
                    .Where(candidate =>
                        string.Equals(
                            Normalize(candidate.DataSetReference),
                            canonicalDataSet,
                            StringComparison.OrdinalIgnoreCase))
                    .Where(candidate =>
                        configuredBufferedFamilies.Count == 0 ||
                        configuredBufferedFamilies.Contains(candidate.Buffered))
                    .ToArray();

            var matched = exactReferenceMatches
                .Concat(exactDataSetMatches)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Reference))
                .DistinctBy(candidate => Normalize(candidate.Reference), StringComparer.OrdinalIgnoreCase)
                .OrderBy(candidate => candidate.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var candidate in matched)
                allTargets.Add(candidate.Reference);

            var resolution = matched.Length > 0
                ? exactDataSetMatches.Length > 0
                    ? "exact-live-dataset-binding"
                    : "exact-configured-rcb-reference"
                : "unresolved";

            if (matched.Length == 0)
            {
                warnings.Add(
                    $"Canonical static DataSet {segment.DataSetReference} covers selected signal(s), but no concrete live RCB could be proven by exact reference or exact live DatSet binding. No sibling/name-family guess was made.");
            }

            segments.Add(new MmsCanonicalStaticRcbTargetSegment
            {
                DataSetReference = segment.DataSetReference,
                ConfiguredReportControlReferences = configuredReferences
                    .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ExactLiveReportControlReferences = matched
                    .Select(candidate => candidate.Reference)
                    .ToArray(),
                Resolution = resolution
            });
        }

        return new MmsCanonicalStaticRcbTargetResolution
        {
            ExactLiveReportControlReferences = allTargets,
            Segments = segments,
            Warnings = warnings
        };
    }

    private static string Normalize(string? value)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(value);
}
