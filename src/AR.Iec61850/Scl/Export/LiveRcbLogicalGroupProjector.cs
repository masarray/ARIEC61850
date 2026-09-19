using System.Globalization;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Projects concrete runtime RCB instances discovered over MMS into logical SCL
/// ReportControl definitions without changing the authoritative live model itself.
/// A collapse is allowed only when numbered siblings are contiguous from 01 and their
/// static configuration is compatible. Otherwise every runtime instance remains a
/// separate non-indexed ReportControl.
/// </summary>
internal static class LiveRcbLogicalGroupProjector
{
    internal sealed class Projection
    {
        public required LiveIedReportControlModel Representative { get; init; }
        public required string LogicalName { get; init; }
        public required string ReportId { get; init; }
        public required IReadOnlyList<LiveIedReportControlModel> RuntimeInstances { get; init; }
        public bool Indexed => RuntimeInstances.Count > 1;
        public int MaxInstances => RuntimeInstances.Count;
    }

    private sealed record Candidate(
        LiveIedReportControlModel Control,
        int OriginalIndex,
        string BaseName,
        int InstanceIndex);

    public static IReadOnlyList<Projection> Project(
        IReadOnlyList<LiveIedReportControlModel> runtimeControls)
    {
        ArgumentNullException.ThrowIfNull(runtimeControls);
        if (runtimeControls.Count == 0)
            return Array.Empty<Projection>();

        var candidates = runtimeControls
            .Select((control, index) => TryParseInstance(control, index))
            .Where(candidate => candidate is not null)
            .Cast<Candidate>()
            .GroupBy(
                candidate => new GroupKey(
                    candidate.Control.Domain.Trim(),
                    candidate.Control.LogicalNode.Trim(),
                    candidate.Control.Buffered,
                    candidate.BaseName),
                GroupKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.ToArray(), GroupKeyComparer.Instance);

        var consumed = new HashSet<LiveIedReportControlModel>(ReferenceEqualityComparer.Instance);
        var projections = new List<(int Order, Projection Projection)>();

        foreach (var group in candidates.Values)
        {
            if (!TryBuildLogicalGroup(group, out var projection, out var firstIndex))
                continue;

            foreach (var runtime in projection.RuntimeInstances)
                consumed.Add(runtime);
            projections.Add((firstIndex, projection));
        }

        for (var index = 0; index < runtimeControls.Count; index++)
        {
            var control = runtimeControls[index];
            if (consumed.Contains(control))
                continue;

            projections.Add((index, new Projection
            {
                Representative = control,
                LogicalName = control.Name.Trim(),
                ReportId = control.ReportId.Trim(),
                RuntimeInstances = new[] { control }
            }));
        }

        return projections
            .OrderBy(item => item.Order)
            .Select(item => item.Projection)
            .ToArray();
    }

    private static bool TryBuildLogicalGroup(
        IReadOnlyList<Candidate> source,
        out Projection projection,
        out int firstIndex)
    {
        projection = null!;
        firstIndex = int.MaxValue;
        if (source.Count < 2)
            return false;

        var ordered = source
            .OrderBy(candidate => candidate.InstanceIndex)
            .ThenBy(candidate => candidate.OriginalIndex)
            .ToArray();

        if (ordered.Select(candidate => candidate.InstanceIndex).Distinct().Count() != ordered.Length)
            return false;

        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].InstanceIndex != index + 1)
                return false;
        }

        var representative = ordered[0].Control;
        if (ordered.Skip(1).Any(candidate => !StaticConfigurationMatches(representative, candidate.Control)))
            return false;

        if (!TryResolveLogicalReportId(ordered, out var logicalReportId))
            return false;

        firstIndex = ordered.Min(candidate => candidate.OriginalIndex);
        projection = new Projection
        {
            Representative = representative,
            LogicalName = ordered[0].BaseName,
            ReportId = logicalReportId,
            RuntimeInstances = ordered.Select(candidate => candidate.Control).ToArray()
        };
        return true;
    }

    private static bool StaticConfigurationMatches(
        LiveIedReportControlModel left,
        LiveIedReportControlModel right)
        => left.Buffered == right.Buffered &&
           Same(left.Domain, right.Domain) &&
           Same(left.LogicalNode, right.LogicalNode) &&
           Same(left.DataSetReference, right.DataSetReference) &&
           SameNumericText(left.ConfRev, right.ConfRev) &&
           SameNumericText(left.BufferTimeMs, right.BufferTimeMs) &&
           SameNumericText(left.IntegrityPeriodMs, right.IntegrityPeriodMs) &&
           Same(left.TriggerOptions, right.TriggerOptions) &&
           Same(left.OptionalFields, right.OptionalFields);

    private static bool TryResolveLogicalReportId(
        IReadOnlyList<Candidate> ordered,
        out string reportId)
    {
        reportId = ordered[0].Control.ReportId.Trim();
        var initialReportId = reportId;
        if (ordered.All(candidate => Same(candidate.Control.ReportId, initialReportId)))
            return true;

        string? baseReportId = null;
        foreach (var candidate in ordered)
        {
            var value = candidate.Control.ReportId.Trim();
            if (string.IsNullOrWhiteSpace(value) || !TrySplitTwoDigitSuffix(value, out var currentBase, out var instanceIndex))
                return false;
            if (instanceIndex != candidate.InstanceIndex)
                return false;

            baseReportId ??= currentBase;
            if (!Same(baseReportId, currentBase))
                return false;
        }

        reportId = baseReportId ?? string.Empty;
        return true;
    }

    private static Candidate? TryParseInstance(LiveIedReportControlModel control, int originalIndex)
    {
        if (control is null || !TrySplitTwoDigitSuffix(control.Name.Trim(), out var baseName, out var instanceIndex))
            return null;
        if (instanceIndex <= 0 || string.IsNullOrWhiteSpace(baseName))
            return null;
        return new Candidate(control, originalIndex, baseName, instanceIndex);
    }

    private static bool TrySplitTwoDigitSuffix(string value, out string baseName, out int instanceIndex)
    {
        baseName = string.Empty;
        instanceIndex = 0;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
            return false;

        var suffix = value.AsSpan(value.Length - 2, 2);
        if (!char.IsDigit(suffix[0]) || !char.IsDigit(suffix[1]) ||
            !int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out instanceIndex))
        {
            return false;
        }

        baseName = value[..^2];
        return !string.IsNullOrWhiteSpace(baseName);
    }

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameNumericText(string? left, string? right)
    {
        var leftText = left?.Trim() ?? string.Empty;
        var rightText = right?.Trim() ?? string.Empty;
        if (ulong.TryParse(leftText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber) &&
            ulong.TryParse(rightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return Same(leftText, rightText);
    }

    private readonly record struct GroupKey(
        string Domain,
        string LogicalNode,
        bool Buffered,
        string BaseName);

    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        public static GroupKeyComparer Instance { get; } = new();

        public bool Equals(GroupKey x, GroupKey y)
            => x.Buffered == y.Buffered &&
               Same(x.Domain, y.Domain) &&
               Same(x.LogicalNode, y.LogicalNode) &&
               Same(x.BaseName, y.BaseName);

        public int GetHashCode(GroupKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Buffered);
            hash.Add(obj.Domain, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.LogicalNode, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.BaseName, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
