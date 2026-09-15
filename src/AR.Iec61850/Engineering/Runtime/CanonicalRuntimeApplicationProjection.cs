namespace AR.Iec61850.Engineering.Runtime;

public readonly record struct CanonicalRuntimeSignalSelection(
    string Reference,
    string FunctionalConstraint);

public sealed class CanonicalRuntimeSelectionResult
{
    public long ModelGeneration { get; init; }
    public long ValueGeneration { get; init; }
    public int RequestedCount { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<CanonicalRuntimeSignalProjection> Rows { get; init; } = Array.Empty<CanonicalRuntimeSignalProjection>();
    public IReadOnlyList<CanonicalRuntimeSignalSelection> Missing { get; init; } = Array.Empty<CanonicalRuntimeSignalSelection>();

    public string Summary =>
        $"Canonical runtime selection: modelGeneration={ModelGeneration}, valueGeneration={ValueGeneration}, requested={RequestedCount}, resolved={Rows.Count}, missing={Missing.Count}, truncated={WasTruncated}.";
}

/// <summary>
/// Application-facing bounded projection for pinned, visible, or otherwise explicitly
/// selected signals. The consumer captures one published model/value pair, resolves only
/// exact canonical identities, and never materializes a second full signal graph.
/// </summary>
public static class CanonicalRuntimeApplicationProjection
{
    public const int MaximumUiSelectionCount = 1_000;
    public const int MaximumMonitorSelectionCount = 256;
    private const int ExactLookupPageSize = 32;

    public static CanonicalRuntimeSelectionResult ForUi(
        ICanonicalRuntimeSource source,
        IEnumerable<CanonicalRuntimeSignalSelection> selections,
        int maximumSelectionCount = MaximumUiSelectionCount)
        => Resolve(source, selections, maximumSelectionCount);

    public static CanonicalRuntimeSelectionResult ForMonitor(
        ICanonicalRuntimeSource source,
        IEnumerable<CanonicalRuntimeSignalSelection> selections)
        => Resolve(source, selections, MaximumMonitorSelectionCount);

    private static CanonicalRuntimeSelectionResult Resolve(
        ICanonicalRuntimeSource source,
        IEnumerable<CanonicalRuntimeSignalSelection> selections,
        int maximumSelectionCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selections);

        var boundedMaximum = Math.Clamp(maximumSelectionCount, 1, MaximumUiSelectionCount);
        var materialized = selections
            .Select(Normalize)
            .Where(selection => !string.IsNullOrWhiteSpace(selection.Reference))
            .Distinct(SelectionComparer.Instance)
            .Take(boundedMaximum + 1)
            .ToArray();
        var truncated = materialized.Length > boundedMaximum;
        if (truncated)
            materialized = materialized.Take(boundedMaximum).ToArray();

        var current = source.Current;
        if (current is null)
        {
            return new CanonicalRuntimeSelectionResult
            {
                RequestedCount = materialized.Length,
                WasTruncated = truncated,
                Missing = materialized
            };
        }

        var rows = new List<CanonicalRuntimeSignalProjection>(materialized.Length);
        var missing = new List<CanonicalRuntimeSignalSelection>();
        foreach (var selection in materialized)
        {
            var page = current.Values.Query(new CanonicalSignalQuery
            {
                ReferencePrefix = selection.Reference,
                FunctionalConstraint = selection.FunctionalConstraint,
                Offset = 0,
                Limit = ExactLookupPageSize
            });

            var match = page.Rows.FirstOrDefault(row =>
                string.Equals(row.Reference, selection.Reference, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(selection.FunctionalConstraint) ||
                 string.Equals(row.FunctionalConstraint, selection.FunctionalConstraint, StringComparison.OrdinalIgnoreCase)));

            if (string.IsNullOrWhiteSpace(match.Reference))
                missing.Add(selection);
            else
                rows.Add(match);
        }

        return new CanonicalRuntimeSelectionResult
        {
            ModelGeneration = current.ModelGeneration,
            ValueGeneration = current.Values.ValueGeneration,
            RequestedCount = materialized.Length,
            WasTruncated = truncated,
            Rows = rows,
            Missing = missing
        };
    }

    private static CanonicalRuntimeSignalSelection Normalize(CanonicalRuntimeSignalSelection selection)
        => new(
            (selection.Reference ?? string.Empty).Trim(),
            (selection.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant());

    private sealed class SelectionComparer : IEqualityComparer<CanonicalRuntimeSignalSelection>
    {
        public static SelectionComparer Instance { get; } = new();

        public bool Equals(CanonicalRuntimeSignalSelection x, CanonicalRuntimeSignalSelection y)
            => string.Equals(x.Reference, y.Reference, StringComparison.Ordinal) &&
               string.Equals(x.FunctionalConstraint, y.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CanonicalRuntimeSignalSelection obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Reference ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FunctionalConstraint ?? string.Empty));
    }
}
