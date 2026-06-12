namespace AR.Iec61850.Mms;

public sealed class MmsIedModelDirectory
{
    private readonly Dictionary<string, MmsLogicalDeviceDirectory> _logicalDevices;
    private readonly Dictionary<string, List<MmsFcResolvedPoint>> _pointsByUserReference;
    private readonly Dictionary<string, MmsFcResolvedPoint> _pointsByMmsReference;

    public MmsIedModelDirectory(IEnumerable<MmsFcResolvedPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        Points = points
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain) && !string.IsNullOrWhiteSpace(x.MmsItemName))
            .OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logicalDevices = Points
            .GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new MmsLogicalDeviceDirectory(x.Key, x),
                StringComparer.OrdinalIgnoreCase);

        _pointsByUserReference = Points
            .GroupBy(x => x.UserReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.ToList(),
                StringComparer.OrdinalIgnoreCase);

        _pointsByMmsReference = Points
            .GroupBy(x => x.MmsReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(p => p.Confidence).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MmsFcResolvedPoint> Points { get; }
    public IReadOnlyDictionary<string, MmsLogicalDeviceDirectory> LogicalDevices => _logicalDevices;
    public int LogicalDeviceCount => _logicalDevices.Count;
    public int LogicalNodeCount => _logicalDevices.Values.Sum(x => x.LogicalNodes.Count);
    public int PointCount => Points.Count;
    public int ReportAttributeCount => Points.Count(x => x.IsReportAttribute);
    public int ControlAttributeCount => Points.Count(x => x.IsControlAttribute);

    public IReadOnlyList<MmsFcResolvedPoint> FindByUserReference(string reference)
    {
        var normalized = MmsFcReferenceNormalizer.NormalizeUserReference(reference);
        return _pointsByUserReference.TryGetValue(normalized, out var matches) ? matches : Array.Empty<MmsFcResolvedPoint>();
    }

    public bool TryFindByMmsReference(string reference, out MmsFcResolvedPoint point)
    {
        var normalized = MmsFcReferenceNormalizer.NormalizeMmsReference(reference);
        return _pointsByMmsReference.TryGetValue(normalized, out point!);
    }

    public IReadOnlyList<MmsFcResolvedPoint> FindByPathSuffix(string reference)
    {
        var normalized = MmsFcReferenceNormalizer.NormalizeUserReference(reference);
        var slash = normalized.IndexOf('/');
        var suffix = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        if (string.IsNullOrWhiteSpace(suffix))
            return Array.Empty<MmsFcResolvedPoint>();

        return Points
            .Where(x => x.UserReference.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase) ||
                        x.UserPath.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyDictionary<string, int> CountByFunctionalConstraint()
        => Points
            .GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

    public string Summary =>
        $"IED directory: LD={LogicalDeviceCount}, LN={LogicalNodeCount}, FC-points={PointCount}, reportAttrs={ReportAttributeCount}, controlAttrs={ControlAttributeCount}";
}

public sealed class MmsLogicalDeviceDirectory
{
    private readonly Dictionary<string, MmsLogicalNodeDirectory> _logicalNodes;

    public MmsLogicalDeviceDirectory(string name, IEnumerable<MmsFcResolvedPoint> points)
    {
        Name = name;
        var materialized = points.ToArray();
        Points = materialized;
        _logicalNodes = materialized
            .GroupBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new MmsLogicalNodeDirectory(name, x.Key, x),
                StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public IReadOnlyList<MmsFcResolvedPoint> Points { get; }
    public IReadOnlyDictionary<string, MmsLogicalNodeDirectory> LogicalNodes => _logicalNodes;
}

public sealed class MmsLogicalNodeDirectory
{
    public MmsLogicalNodeDirectory(string domain, string name, IEnumerable<MmsFcResolvedPoint> points)
    {
        Domain = domain;
        Name = name;
        Points = points
            .OrderBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Domain { get; }
    public string Name { get; }
    public IReadOnlyList<MmsFcResolvedPoint> Points { get; }
    public IReadOnlyDictionary<string, int> CountByFunctionalConstraint()
        => Points
            .GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
}

public sealed class MmsFcResolvedPoint
{
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string DataObjectPath { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string Source { get; init; } = "LiveMmsGetNameList";
    public int Confidence { get; init; } = 100;

    public string UserPath => string.IsNullOrWhiteSpace(DataObjectPath)
        ? LogicalNode
        : $"{LogicalNode}.{DataObjectPath}";

    public string UserReference => string.IsNullOrWhiteSpace(Domain)
        ? UserPath
        : $"{Domain}/{UserPath}";

    public string MmsReference => string.IsNullOrWhiteSpace(Domain)
        ? MmsItemName
        : $"{Domain}/{MmsItemName}";

    public bool IsReportAttribute => MmsFunctionalConstraint.IsReportConstraint(FunctionalConstraint);
    public bool IsControlAttribute => MmsFunctionalConstraint.IsControlConstraint(FunctionalConstraint);

    public MmsObjectReference ToObjectReference()
        => new(Domain, MmsItemName, FunctionalConstraint);

    public override string ToString()
        => $"{UserReference} [{FunctionalConstraint}] mms={MmsReference}";
}
