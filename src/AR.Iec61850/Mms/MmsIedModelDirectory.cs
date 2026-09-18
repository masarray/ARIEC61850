namespace AR.Iec61850.Mms;

public sealed class MmsIedModelDirectory
{
    private readonly Dictionary<string, MmsLogicalDeviceDirectory> _logicalDevices = new(StringComparer.OrdinalIgnoreCase);
    // MMS component identifiers are case-sensitive. This matters for standard
    // service-tracking CDCs where distinct members such as "t" and "T" coexist.
    // Keep exact keys here and provide a conservative case-insensitive fallback
    // only when that fallback is unambiguous.
    private readonly Dictionary<string, List<MmsFcResolvedPoint>> _pointsByUserReference = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MmsFcResolvedPoint> _pointsByMmsReference = new(StringComparer.Ordinal);

    public MmsIedModelDirectory(IEnumerable<MmsFcResolvedPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Rebuild(points);
    }

    public IReadOnlyList<MmsFcResolvedPoint> Points { get; private set; } = Array.Empty<MmsFcResolvedPoint>();
    public IReadOnlyDictionary<string, MmsLogicalDeviceDirectory> LogicalDevices => _logicalDevices;
    public int LogicalDeviceCount => _logicalDevices.Count;
    public int LogicalNodeCount => _logicalDevices.Values.Sum(x => x.LogicalNodes.Count);
    public int PointCount => Points.Count;
    public int ReportAttributeCount => Points.Count(x => x.IsReportAttribute);
    public int ControlAttributeCount => Points.Count(x => x.IsControlAttribute);

    internal int AddSupplementalPoints(IEnumerable<MmsFcResolvedPoint> supplementalPoints)
    {
        ArgumentNullException.ThrowIfNull(supplementalPoints);

        var combined = new List<MmsFcResolvedPoint>(Points);
        var knownMmsReferences = new HashSet<string>(
            Points.Select(point => point.MmsReference),
            StringComparer.Ordinal);
        var added = 0;

        foreach (var point in supplementalPoints)
        {
            if (string.IsNullOrWhiteSpace(point.Domain) ||
                string.IsNullOrWhiteSpace(point.MmsItemName) ||
                !knownMmsReferences.Add(point.MmsReference))
            {
                continue;
            }

            combined.Add(point);
            added++;
        }

        if (added > 0)
            Rebuild(combined);

        return added;
    }

    public IReadOnlyList<MmsFcResolvedPoint> FindByUserReference(string reference)
    {
        var normalized = MmsFcReferenceNormalizer.NormalizeUserReference(reference);
        if (_pointsByUserReference.TryGetValue(normalized, out var matches))
            return matches;

        // Preserve the historical convenience of case-insensitive user lookup,
        // but never collapse exact case-distinct MMS members in storage.
        return _pointsByUserReference
            .Where(pair => string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.MmsReference, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryFindByMmsReference(string reference, out MmsFcResolvedPoint point)
    {
        var normalized = MmsFcReferenceNormalizer.NormalizeMmsReference(reference);
        if (_pointsByMmsReference.TryGetValue(normalized, out point!))
            return true;

        var fallback = _pointsByMmsReference
            .Where(pair => string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Take(2)
            .ToArray();
        if (fallback.Length == 1)
        {
            point = fallback[0];
            return true;
        }

        point = null!;
        return false;
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

    private void Rebuild(IEnumerable<MmsFcResolvedPoint> points)
    {
        Points = points
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain) && !string.IsNullOrWhiteSpace(x.MmsItemName))
            .OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logicalDevices.Clear();
        foreach (var group in Points.GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase))
            _logicalDevices[group.Key] = new MmsLogicalDeviceDirectory(group.Key, group);

        _pointsByUserReference.Clear();
        foreach (var group in Points.GroupBy(x => x.UserReference, StringComparer.Ordinal))
            _pointsByUserReference[group.Key] = group.ToList();

        _pointsByMmsReference.Clear();
        foreach (var group in Points.GroupBy(x => x.MmsReference, StringComparer.Ordinal))
            _pointsByMmsReference[group.Key] = group.OrderByDescending(point => point.Confidence).First();
    }
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
