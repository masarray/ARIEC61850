namespace AR.Iec61850.Mms;

public static class MmsIedModelDirectoryBuilder
{
    public static MmsIedModelDirectory Build(MmsDiscoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var points = new List<MmsFcResolvedPoint>();
        foreach (var domainPair in snapshot.DomainVariables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (TryParseLiveMmsVariable(domain, raw, out var point))
                    points.Add(point);
            }
        }

        return new MmsIedModelDirectory(points);
    }

    public static bool TryParseLiveMmsVariable(string domain, string rawMmsName, out MmsFcResolvedPoint point)
    {
        point = new MmsFcResolvedPoint();
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(rawMmsName))
            return false;

        var parts = rawMmsName.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return false;

        var fcIndex = Array.FindIndex(parts, 1, p => MmsFunctionalConstraint.IsKnown(p));
        if (fcIndex < 1 || fcIndex + 1 >= parts.Length)
            return false;

        var logicalNode = string.Join('$', parts.Take(fcIndex));
        var functionalConstraint = MmsFunctionalConstraint.Normalize(parts[fcIndex]);
        var dataObjectPath = string.Join('.', parts.Skip(fcIndex + 1));
        if (string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(dataObjectPath))
            return false;

        point = new MmsFcResolvedPoint
        {
            Domain = domain.Trim(),
            LogicalNode = logicalNode,
            FunctionalConstraint = functionalConstraint,
            DataObjectPath = dataObjectPath,
            MmsItemName = string.Join('$', parts),
            Source = "LiveMmsGetNameList",
            Confidence = 100
        };
        return true;
    }
}
