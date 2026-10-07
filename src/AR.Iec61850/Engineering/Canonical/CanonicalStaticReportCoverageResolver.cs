namespace AR.Iec61850.Engineering.Canonical;

public enum CanonicalStaticReportSignalCoverageStatus
{
    Covered,
    SignalNotFound,
    AmbiguousSignalIdentity,
    NoStaticDataSetMembership,
    StaticDataSetWithoutReportControl
}

public sealed class CanonicalStaticReportSelection
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
}

public sealed class CanonicalStaticReportDataSetCandidate
{
    public string DataSetReference { get; init; } = string.Empty;
    public string SourceDataSetReference { get; init; } = string.Empty;
    public int[] MemberIndexes { get; init; } = Array.Empty<int>();
    public string[] ReportControlReferences { get; init; } = Array.Empty<string>();
}

public sealed class CanonicalStaticReportSignalCoverage
{
    public string RequestedReference { get; init; } = string.Empty;
    public string RequestedFunctionalConstraint { get; init; } = string.Empty;
    public string CanonicalReference { get; init; } = string.Empty;
    public string CanonicalFunctionalConstraint { get; init; } = string.Empty;
    public CanonicalStaticReportSignalCoverageStatus Status { get; init; }
    public CanonicalStaticReportDataSetCandidate[] DataSetCandidates { get; init; } = Array.Empty<CanonicalStaticReportDataSetCandidate>();

    public bool IsCovered => Status == CanonicalStaticReportSignalCoverageStatus.Covered;
}

public sealed class CanonicalStaticReportCoverageSegment
{
    public string DataSetReference { get; init; } = string.Empty;
    public string SourceDataSetReference { get; init; } = string.Empty;
    public CanonicalDataSetMember[] OrderedMembers { get; init; } = Array.Empty<CanonicalDataSetMember>();
    public CanonicalReportControl[] ReportControls { get; init; } = Array.Empty<CanonicalReportControl>();
    public string[] SelectedSignalReferences { get; init; } = Array.Empty<string>();
}

public sealed class CanonicalStaticReportCoveragePlan
{
    public CanonicalIngressKind Ingress { get; init; }
    public CanonicalStaticReportSignalCoverage[] Signals { get; init; } = Array.Empty<CanonicalStaticReportSignalCoverage>();
    public CanonicalStaticReportCoverageSegment[] Segments { get; init; } = Array.Empty<CanonicalStaticReportCoverageSegment>();

    public int RequestedSignalCount => Signals.Length;
    public int CoveredSignalCount => Signals.Count(signal => signal.IsCovered);
    public int UncoveredSignalCount => RequestedSignalCount - CoveredSignalCount;
}

/// <summary>
/// Source-neutral static-report coverage over the shared CanonicalIedModel.
///
/// This resolver never chooses an operational RCB instance and performs no IED writes.
/// It answers one question only: which configured static DataSet/ReportControl resources
/// already cover each exact canonical signal? Discovery and Open-SCL therefore enter the
/// same coverage logic after canonicalization.
///
/// Structured DataSet members are handled by exact reference-boundary containment. A
/// member such as A.phsA covers its typed descendants but never sibling phases. Full
/// ordered DataSet membership is retained in every segment because InformationReport
/// projection remains positional even when the application selected only a subset.
/// </summary>
public static class CanonicalStaticReportCoverageResolver
{
    public static CanonicalStaticReportCoveragePlan Resolve(
        CanonicalIedModel model,
        IEnumerable<CanonicalStaticReportSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selections);

        var requested = selections.ToArray();
        var signalIndex = BuildSignalIndex(model);
        var reportByDataSet = model.ReportControls
            .Where(report => !string.IsNullOrWhiteSpace(report.DataSetReference))
            .GroupBy(report => NormalizeConfiguredReference(report.DataSetReference), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(report => NormalizeConfiguredReference(report.Reference), StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var results = new CanonicalStaticReportSignalCoverage[requested.Length];
        for (var index = 0; index < requested.Length; index++)
            results[index] = ResolveSignal(model, requested[index], signalIndex, reportByDataSet);

        var segmentDataSets = results
            .Where(result => result.IsCovered)
            .SelectMany(result => result.DataSetCandidates)
            .Select(candidate => candidate.DataSetReference)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var segments = segmentDataSets
            .Select(dataSetReference => BuildSegment(
                model,
                dataSetReference,
                results,
                reportByDataSet))
            .Where(segment => segment is not null)
            .Cast<CanonicalStaticReportCoverageSegment>()
            .ToArray();

        return new CanonicalStaticReportCoveragePlan
        {
            Ingress = model.Source.Ingress,
            Signals = results,
            Segments = segments
        };
    }

    private static CanonicalStaticReportSignalCoverage ResolveSignal(
        CanonicalIedModel model,
        CanonicalStaticReportSelection selection,
        IReadOnlyDictionary<string, CanonicalSignalRow[]> signalIndex,
        IReadOnlyDictionary<string, CanonicalReportControl[]> reportByDataSet)
    {
        var requestedReference = selection.Reference?.Trim() ?? string.Empty;
        var requestedFc = NormalizeFc(selection.FunctionalConstraint);

        if (!signalIndex.TryGetValue(requestedReference, out var referenceMatches))
        {
            return new CanonicalStaticReportSignalCoverage
            {
                RequestedReference = requestedReference,
                RequestedFunctionalConstraint = requestedFc,
                Status = CanonicalStaticReportSignalCoverageStatus.SignalNotFound
            };
        }

        var matches = string.IsNullOrWhiteSpace(requestedFc)
            ? referenceMatches
            : referenceMatches
                .Where(signal => string.Equals(
                    NormalizeFc(model.Strings.Resolve(signal.FunctionalConstraint)),
                    requestedFc,
                    StringComparison.Ordinal))
                .ToArray();

        if (matches.Length != 1)
        {
            return new CanonicalStaticReportSignalCoverage
            {
                RequestedReference = requestedReference,
                RequestedFunctionalConstraint = requestedFc,
                Status = matches.Length == 0
                    ? CanonicalStaticReportSignalCoverageStatus.SignalNotFound
                    : CanonicalStaticReportSignalCoverageStatus.AmbiguousSignalIdentity
            };
        }

        var signal = matches[0];
        var canonicalReference = model.Strings.Resolve(signal.ObjectReference);
        var canonicalFc = NormalizeFc(model.Strings.Resolve(signal.FunctionalConstraint));

        var candidates = new List<CanonicalStaticReportDataSetCandidate>();
        var anyDataSet = false;
        foreach (var dataSet in model.DataSets)
        {
            var memberIndexes = dataSet.Members
                .Where(member => MemberCoversSignal(member, canonicalReference, canonicalFc))
                .Select(member => member.Index)
                .OrderBy(memberIndex => memberIndex)
                .ToArray();
            if (memberIndexes.Length == 0)
                continue;

            anyDataSet = true;
            var canonicalDataSetReference = NormalizeConfiguredReference(dataSet.Reference);
            reportByDataSet.TryGetValue(canonicalDataSetReference, out var reports);
            candidates.Add(new CanonicalStaticReportDataSetCandidate
            {
                DataSetReference = canonicalDataSetReference,
                SourceDataSetReference = dataSet.Reference,
                MemberIndexes = memberIndexes,
                ReportControlReferences = reports?
                    .Select(report => NormalizeConfiguredReference(report.Reference))
                    .ToArray() ?? Array.Empty<string>()
            });
        }

        var reportBacked = candidates
            .Where(candidate => candidate.ReportControlReferences.Length > 0)
            .OrderBy(candidate => candidate.DataSetReference, StringComparer.Ordinal)
            .ToArray();

        return new CanonicalStaticReportSignalCoverage
        {
            RequestedReference = requestedReference,
            RequestedFunctionalConstraint = requestedFc,
            CanonicalReference = canonicalReference,
            CanonicalFunctionalConstraint = canonicalFc,
            Status = reportBacked.Length > 0
                ? CanonicalStaticReportSignalCoverageStatus.Covered
                : anyDataSet
                    ? CanonicalStaticReportSignalCoverageStatus.StaticDataSetWithoutReportControl
                    : CanonicalStaticReportSignalCoverageStatus.NoStaticDataSetMembership,
            DataSetCandidates = reportBacked.Length > 0
                ? reportBacked
                : candidates.OrderBy(candidate => candidate.DataSetReference, StringComparer.Ordinal).ToArray()
        };
    }

    private static CanonicalStaticReportCoverageSegment? BuildSegment(
        CanonicalIedModel model,
        string dataSetReference,
        IReadOnlyList<CanonicalStaticReportSignalCoverage> results,
        IReadOnlyDictionary<string, CanonicalReportControl[]> reportByDataSet)
    {
        var dataSet = model.DataSets.SingleOrDefault(candidate =>
            string.Equals(
                NormalizeConfiguredReference(candidate.Reference),
                dataSetReference,
                StringComparison.Ordinal));
        if (dataSet is null)
            return null;

        reportByDataSet.TryGetValue(dataSetReference, out var reports);
        reports ??= Array.Empty<CanonicalReportControl>();

        var selected = results
            .Where(result => result.IsCovered &&
                result.DataSetCandidates.Any(candidate =>
                    string.Equals(candidate.DataSetReference, dataSetReference, StringComparison.Ordinal)))
            .Select(result => result.CanonicalReference)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        return new CanonicalStaticReportCoverageSegment
        {
            DataSetReference = dataSetReference,
            SourceDataSetReference = dataSet.Reference,
            OrderedMembers = dataSet.Members.OrderBy(member => member.Index).ToArray(),
            ReportControls = reports,
            SelectedSignalReferences = selected
        };
    }

    private static IReadOnlyDictionary<string, CanonicalSignalRow[]> BuildSignalIndex(CanonicalIedModel model)
        => model.Signals
            .GroupBy(signal => model.Strings.Resolve(signal.ObjectReference), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    private static bool MemberCoversSignal(
        CanonicalDataSetMember member,
        string signalReference,
        string signalFc)
    {
        var memberReference = member.Reference?.Trim() ?? string.Empty;
        if (memberReference.Length == 0 || signalReference.Length == 0)
            return false;

        var memberFc = NormalizeFc(member.FunctionalConstraint);
        if (memberFc.Length > 0 &&
            !string.Equals(memberFc, signalFc, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(signalReference, memberReference, StringComparison.Ordinal) ||
               signalReference.StartsWith(memberReference + ".", StringComparison.Ordinal);
    }

    private static string NormalizeConfiguredReference(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? string.Empty : text.Replace('
, '.');
    }

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
