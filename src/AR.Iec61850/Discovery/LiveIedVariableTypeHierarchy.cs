using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Plans and resolves MMS GetVariableAccessAttributes probes at logical-node
/// scope. IEC 61850 servers commonly expose the FC/DO/DA type hierarchy from
/// the LN root, which gives a complete type tree with far fewer requests than
/// one probe per leaf variable.
/// </summary>
public static class LiveIedVariableTypeProbePlanner
{
    public static IReadOnlyList<MmsObjectReference> BuildLogicalNodeRootCandidates(MmsIedModelDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return directory.LogicalDevices.Values
            .OrderBy(logicalDevice => logicalDevice.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(logicalDevice => logicalDevice.LogicalNodes.Values
                .OrderBy(logicalNode => logicalNode.Name, StringComparer.OrdinalIgnoreCase)
                .Select(logicalNode => new MmsObjectReference(logicalDevice.Name, logicalNode.Name, string.Empty)))
            .ToArray();
    }
}

/// <summary>
/// Canonical live-discovery entry point for bounded type enrichment. Planning stays
/// in the Discovery layer while the MMS session only executes the supplied evidence
/// probes. This prevents SCL/live semantic logic from leaking into the wire layer.
/// </summary>
public static class LiveIedVariableTypeProbeExecutor
{
    public static Task<IReadOnlyList<MmsVariableAccessAttributesResult>> ProbeSmartAsync(
        MmsClientSession session,
        MmsIedModelDirectory directory,
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(directory);

        var roots = LiveIedVariableTypeProbePlanner.BuildLogicalNodeRootCandidates(directory);
        return session.GetVariableAccessAttributesSmartAsync(
            directory,
            roots,
            options,
            cancellationToken);
    }
}

internal sealed class LiveIedVariableTypeHierarchyIndex
{
    private const char CompositeKeySeparator = '\u001F';

    private readonly Dictionary<string, LiveIedVariableTypeResolution> _byMmsReference =
        new(StringComparer.OrdinalIgnoreCase);

    private LiveIedVariableTypeHierarchyIndex()
    {
    }

    public int ResolvedAttributeCount => _byMmsReference.Count;

    public static LiveIedVariableTypeHierarchyIndex Build(
        MmsIedModelDirectory directory,
        IReadOnlyList<MmsVariableAccessAttributesResult> results)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(results);

        var index = new LiveIedVariableTypeHierarchyIndex();
        if (directory.PointCount == 0 || results.Count == 0)
            return index;

        // Build one cheap LN-local candidate index. The previous implementation scanned
        // every point in the entire IED for every successful GVA result, which made CPU
        // mapping approach O(points * typeResults) on large models. Every MMS variable
        // path begins at one logical node, so results can be constrained to that LN.
        var pointsByLogicalNode = directory.Points
            .GroupBy(
                point => BuildLogicalNodeKey(point.Domain, point.LogicalNode),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MmsFcResolvedPoint>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(result => result.IsSuccess && result.TypeSpecification is not null))
        {
            var rootParts = SplitMmsItem(result.Reference.Item);
            if (rootParts.Length == 0 || string.IsNullOrWhiteSpace(result.Reference.Domain))
                continue;

            var logicalNodeKey = BuildLogicalNodeKey(result.Reference.Domain, rootParts[0]);
            if (!pointsByLogicalNode.TryGetValue(logicalNodeKey, out var candidates))
                continue;

            index.AddResult(candidates, result, rootParts);
        }

        return index;
    }

    public bool TryResolve(MmsFcResolvedPoint point, out LiveIedVariableTypeResolution resolution)
        => _byMmsReference.TryGetValue(point.MmsReference, out resolution!);

    private void AddResult(
        IReadOnlyList<MmsFcResolvedPoint> candidatePoints,
        MmsVariableAccessAttributesResult result,
        IReadOnlyList<string> rootParts)
    {
        if (result.TypeSpecification is null)
            return;

        foreach (var point in candidatePoints)
        {
            var pointParts = SplitMmsItem(point.MmsItemName);
            if (!HasPrefix(pointParts, rootParts))
                continue;

            var remainder = pointParts[rootParts.Count..];
            var type = ResolvePath(result.TypeSpecification, remainder);
            if (type is null)
                continue;

            var source = remainder.Length == 0
                ? "GetVariableAccessAttributes"
                : "GetVariableAccessAttributesLogicalNodeTree";
            var resolution = new LiveIedVariableTypeResolution(
                type,
                source,
                $"Mapped from {result.ReferenceKey} type hierarchy. {result.Message}",
                rootParts.Count);

            if (!_byMmsReference.TryGetValue(point.MmsReference, out var existing) ||
                resolution.Specificity > existing.Specificity)
            {
                _byMmsReference[point.MmsReference] = resolution;
            }
        }
    }

    private static MmsTypeSpecificationNode? ResolvePath(MmsTypeSpecificationNode root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var part in path)
        {
            var next = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
                return null;

            current = next;
        }

        return current;
    }

    private static string BuildLogicalNodeKey(string domain, string logicalNode)
        => string.Concat(domain ?? string.Empty, CompositeKeySeparator, logicalNode ?? string.Empty);

    private static string[] SplitMmsItem(string value)
        => (value ?? string.Empty).Split(
            '$',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool HasPrefix(IReadOnlyList<string> value, IReadOnlyList<string> prefix)
    {
        if (value.Count < prefix.Count)
            return false;

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(value[index], prefix[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

internal sealed record LiveIedVariableTypeResolution(
    MmsTypeSpecificationNode TypeSpecification,
    string Source,
    string Message,
    int Specificity);
