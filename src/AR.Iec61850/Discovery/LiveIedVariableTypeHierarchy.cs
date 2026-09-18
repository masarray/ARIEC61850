using System.Globalization;
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

/// <summary>
/// Materializes named IEC 61850 leaf paths that are present in an authoritative
/// logical-node TypeSpecification but absent from the flat GetNameList inventory.
/// Existing live GetNameList/DataSet points always win; this only fills proven gaps.
/// </summary>
internal static class LiveIedTypeHierarchyPointMaterializer
{
    private static readonly HashSet<string> FunctionalConstraints = new(
        [
            "ST", "MX", "SP", "SV", "CF", "DC", "SG", "SE", "SR", "OR",
            "BL", "EX", "CO", "RP", "BR", "LG", "GO", "GS", "MS", "US"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static int Augment(
        MmsIedModelDirectory directory,
        IReadOnlyList<MmsVariableAccessAttributesResult> results)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(results);

        if (directory.PointCount == 0 || results.Count == 0)
            return 0;

        var supplemental = new List<MmsFcResolvedPoint>();

        foreach (var result in results.Where(result =>
                     result.IsSuccess &&
                     result.TypeSpecification is not null &&
                     !string.IsNullOrWhiteSpace(result.Reference.Domain) &&
                     !string.IsNullOrWhiteSpace(result.Reference.Item) &&
                     !result.Reference.Item.Contains('$')))
        {
            var domain = result.Reference.Domain.Trim();
            var logicalNode = result.Reference.Item.Trim();
            if (!directory.LogicalDevices.TryGetValue(domain, out var logicalDevice) ||
                !logicalDevice.LogicalNodes.ContainsKey(logicalNode))
            {
                continue;
            }

            foreach (var fcNode in result.TypeSpecification!.Children)
            {
                var functionalConstraint = NormalizeFunctionalConstraint(fcNode.Name);
                if (string.IsNullOrWhiteSpace(functionalConstraint))
                    continue;

                foreach (var child in fcNode.Children)
                    Visit(child, functionalConstraint, []);
            }

            void Visit(
                MmsTypeSpecificationNode node,
                string functionalConstraint,
                IReadOnlyList<string> parentPath)
            {
                var name = (node.Name ?? string.Empty).Trim();
                if (!IsUsableComponentName(name) ||
                    string.Equals(node.MmsType, "array", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var path = new string[parentPath.Count + 1];
                for (var index = 0; index < parentPath.Count; index++)
                    path[index] = parentPath[index];
                path[^1] = name;

                if (node.Children.Count > 0)
                {
                    foreach (var child in node.Children)
                        Visit(child, functionalConstraint, path);
                    return;
                }

                // An IEC 61850 leaf below an FC must contain at least DO + DA.
                if (path.Length < 2)
                    return;

                supplemental.Add(new MmsFcResolvedPoint
                {
                    Domain = domain,
                    LogicalNode = logicalNode,
                    FunctionalConstraint = functionalConstraint,
                    DataObjectPath = string.Join(".", path),
                    MmsItemName = $"{logicalNode}${functionalConstraint}${string.Join("$", path)}",
                    Source = "GetVariableAccessAttributesLogicalNodeTree",
                    Confidence = 100
                });
            }
        }

        return directory.AddSupplementalPoints(supplemental);
    }

    private static string NormalizeFunctionalConstraint(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return FunctionalConstraints.Contains(normalized) ? normalized : string.Empty;
    }

    private static bool IsUsableComponentName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.Equals(value, "element", StringComparison.OrdinalIgnoreCase) &&
           value[0] != '[' &&
           !value.Contains('$') &&
           !value.Contains('.');
}

internal sealed class LiveIedVariableTypeHierarchyIndex
{
    private const char CompositeKeySeparator = '\u001F';

    // MMS member names are case-sensitive; LTRK service-tracking structures may
    // legally contain both "t" and "T", so type resolutions must not coalesce them.
    private readonly Dictionary<string, LiveIedVariableTypeResolution> _byMmsReference =
        new(StringComparer.Ordinal);

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

        // The live LN-root GVA tree can contain semantic descendants that are not
        // individually enumerated by GetNameList. Materialize those proven leaves
        // before indexing so the canonical model and SCL builder can see them without
        // issuing per-leaf MMS requests.
        LiveIedTypeHierarchyPointMaterializer.Augment(directory, results);

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
            if (!TryResolvePath(
                    result.TypeSpecification,
                    remainder,
                    out var type,
                    out var declarationOrderKey))
            {
                continue;
            }

            var source = remainder.Length == 0
                ? "GetVariableAccessAttributes"
                : "GetVariableAccessAttributesLogicalNodeTree";
            var resolution = new LiveIedVariableTypeResolution(
                type,
                source,
                $"Mapped from {result.ReferenceKey} type hierarchy. {result.Message}",
                rootParts.Count,
                declarationOrderKey);

            if (!_byMmsReference.TryGetValue(point.MmsReference, out var existing) ||
                resolution.Specificity > existing.Specificity)
            {
                _byMmsReference[point.MmsReference] = resolution;
            }
        }
    }

    private static bool TryResolvePath(
        MmsTypeSpecificationNode root,
        IReadOnlyList<string> path,
        out MmsTypeSpecificationNode type,
        out string declarationOrderKey)
    {
        var current = root;
        var declarationPath = new List<int>(path.Count);
        foreach (var part in path)
        {
            var matchIndex = -1;
            for (var index = 0; index < current.Children.Count; index++)
            {
                if (!string.Equals(current.Children[index].Name, part, StringComparison.OrdinalIgnoreCase))
                    continue;

                matchIndex = index;
                break;
            }

            if (matchIndex < 0)
            {
                type = root;
                declarationOrderKey = string.Empty;
                return false;
            }

            declarationPath.Add(matchIndex);
            current = current.Children[matchIndex];
        }

        type = current;
        declarationOrderKey = string.Join(
            ".",
            declarationPath.Select(index => index.ToString("D6", CultureInfo.InvariantCulture)));
        return true;
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
    int Specificity,
    string DeclarationOrderKey);
