namespace AR.Iec61850.Mms;

public static class MmsReportDiscoveryMapper
{
    private static readonly string[] ReportAttributeNames =
    [
        "RptID", "RptEna", "Resv", "ResvTms", "DatSet", "ConfRev", "OptFlds", "BufTm", "SqNum",
        "TrgOps", "IntgPd", "GI", "PurgeBuf", "EntryID", "TimeOfEntry"
    ];

    public static MmsReportInventory BuildInventory(MmsDiscoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var inventory = new MmsReportInventory();
        inventory.DataSets.AddRange(BuildDataSets(snapshot.DomainVariableLists));
        inventory.ReportControls.AddRange(BuildReportControls(snapshot.DomainVariables, inventory.DataSets));
        return inventory;
    }

    private static IEnumerable<MmsDataSetCandidate> BuildDataSets(IReadOnlyDictionary<string, IReadOnlyList<string>> domainVariableLists)
    {
        foreach (var domainPair in domainVariableLists.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var (logicalNode, name, reference) = NormalizeDataSetReference(domain, raw);
                yield return new MmsDataSetCandidate
                {
                    Domain = domain,
                    LogicalNode = logicalNode,
                    Name = name,
                    RawMmsName = raw,
                    Reference = reference
                };
            }
        }
    }

    private static IEnumerable<MmsReportControlCandidate> BuildReportControls(
        IReadOnlyDictionary<string, IReadOnlyList<string>> domainVariables,
        IReadOnlyList<MmsDataSetCandidate> dataSets)
    {
        var map = new Dictionary<string, MmsReportControlCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var domainPair in domainVariables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseReportVariable(domain, raw, out var parsed))
                    continue;

                if (!map.TryGetValue(parsed.Reference, out var candidate))
                {
                    candidate = parsed;
                    candidate.DataSetReference = InferLikelyDataSet(candidate, dataSets);
                    map[candidate.Reference] = candidate;
                }

                foreach (var attr in parsed.Attributes)
                {
                    if (!candidate.Attributes.Contains(attr, StringComparer.OrdinalIgnoreCase))
                        candidate.Attributes.Add(attr);
                }
            }
        }

        return map.Values
            .OrderByDescending(x => x.Buffered)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseReportVariable(string domain, string raw, out MmsReportControlCandidate candidate)
    {
        candidate = new MmsReportControlCandidate();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return false;

        var fcIndex = Array.FindIndex(parts, p =>
            p.Equals("RP", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("BR", StringComparison.OrdinalIgnoreCase));

        if (fcIndex < 1 || fcIndex + 1 >= parts.Length)
            return false;

        var logicalNode = parts[0];
        var functionalConstraint = parts[fcIndex].ToUpperInvariant();
        var name = parts[fcIndex + 1];
        if (string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(name))
            return false;

        candidate = new MmsReportControlCandidate
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = functionalConstraint,
            Name = name,
            Buffered = functionalConstraint.Equals("BR", StringComparison.OrdinalIgnoreCase),
            Reference = $"{domain}/{logicalNode}.{functionalConstraint}.{name}",
            Attributes = parts.Skip(fcIndex + 2)
                .Where(p => IsKnownReportAttribute(p) || !string.IsNullOrWhiteSpace(p))
                .ToList()
        };

        return true;
    }

    private static string InferLikelyDataSet(MmsReportControlCandidate reportControl, IReadOnlyList<MmsDataSetCandidate> dataSets)
    {
        var sameDomain = dataSets
            .Where(ds => ds.Domain.Equals(reportControl.Domain, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (sameDomain.Length == 0)
            return string.Empty;

        var sameLogicalNode = sameDomain
            .Where(ds => ds.LogicalNode.Equals(reportControl.LogicalNode, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (sameLogicalNode.Length == 1)
            return sameLogicalNode[0].Reference;

        if (sameDomain.Length == 1)
            return sameDomain[0].Reference;

        var byName = sameDomain.FirstOrDefault(ds =>
            !string.IsNullOrWhiteSpace(ds.Name) &&
            (reportControl.Name.Contains(ds.Name, StringComparison.OrdinalIgnoreCase) ||
             ds.Name.Contains(reportControl.Name, StringComparison.OrdinalIgnoreCase)));

        return byName?.Reference ?? string.Empty;
    }

    private static bool IsKnownReportAttribute(string text)
        => ReportAttributeNames.Contains(text, StringComparer.OrdinalIgnoreCase);

    private static (string LogicalNode, string Name, string Reference) NormalizeDataSetReference(string domain, string raw)
    {
        var cleaned = raw.Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(cleaned))
            return ("LLN0", raw, $"{domain}/LLN0.{raw}");

        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
            return (parts[0], parts[^1], $"{domain}/{cleaned}");

        return ("LLN0", cleaned, $"{domain}/LLN0.{cleaned}");
    }
}
