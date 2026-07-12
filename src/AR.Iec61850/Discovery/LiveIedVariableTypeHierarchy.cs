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

internal sealed class LiveIedVariableTypeHierarchyIndex
{
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
        foreach (var result in results.Where(result => result.IsSuccess && result.TypeSpecification is not null))
            index.AddResult(directory, result);

        return index;
    }

    public bool TryResolve(MmsFcResolvedPoint point, out LiveIedVariableTypeResolution resolution)
        => _byMmsReference.TryGetValue(point.MmsReference, out resolution!);

    private void AddResult(MmsIedModelDirectory directory, MmsVariableAccessAttributesResult result)
    {
        var rootItem = result.Reference.Item.Trim();
        if (string.IsNullOrWhiteSpace(result.Reference.Domain) || string.IsNullOrWhiteSpace(rootItem) || result.TypeSpecification is null)
            return;

        var rootParts = SplitMmsItem(rootItem);
        if (rootParts.Length == 0)
            return;

        foreach (var point in directory.Points.Where(point =>
                     string.Equals(point.Domain, result.Reference.Domain, StringComparison.OrdinalIgnoreCase)))
        {
            var pointParts = SplitMmsItem(point.MmsItemName);
            if (!HasPrefix(pointParts, rootParts))
                continue;

            var remainder = pointParts[rootParts.Length..];
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
                rootParts.Length);

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

    private static string[] SplitMmsItem(string value)
        => value.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
