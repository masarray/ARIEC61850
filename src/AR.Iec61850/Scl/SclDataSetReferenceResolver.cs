namespace AR.Iec61850.Scl;

internal sealed record SclDataSetBindingResolution(
    SclDataSetBindingStatus Status,
    SclDataSet? DataSet,
    string CanonicalReference,
    string LocalName);

internal static class SclDataSetReferenceResolver
{
    public static SclDataSetBindingResolution Resolve(
        IEnumerable<SclDataSet> dataSets,
        string iedName,
        string ldInst,
        string logicalNodePath,
        string rawReference)
    {
        if (string.IsNullOrWhiteSpace(rawReference))
        {
            return new SclDataSetBindingResolution(
                SclDataSetBindingStatus.NotSpecified,
                null,
                string.Empty,
                string.Empty);
        }

        var parts = Parse(rawReference, iedName, ldInst, logicalNodePath);
        var candidates = dataSets
            .Where(dataSet => Same(dataSet.IedName, iedName))
            .Where(dataSet => Same(dataSet.LdInst, parts.LdInst))
            .Where(dataSet => Same(dataSet.LogicalNodePath, parts.LogicalNodePath))
            .Where(dataSet => Same(dataSet.Name, parts.LocalName))
            .Take(2)
            .ToArray();

        if (candidates.Length == 1)
        {
            var resolved = candidates[0];
            return new SclDataSetBindingResolution(
                resolved.Entries.Count == 0
                    ? SclDataSetBindingStatus.ResolvedEmpty
                    : SclDataSetBindingStatus.Resolved,
                resolved,
                resolved.Reference,
                resolved.Name);
        }

        return new SclDataSetBindingResolution(
            SclDataSetBindingStatus.Unresolved,
            null,
            BuildCanonicalReference(iedName, parts.LdInst, parts.LogicalNodePath, parts.LocalName),
            parts.LocalName);
    }

    private static ParsedReference Parse(
        string rawReference,
        string iedName,
        string fallbackLdInst,
        string fallbackLogicalNodePath)
    {
        var normalized = rawReference
            .Trim()
            .Replace('\\', '/');

        normalized = normalized.Replace("$DS$", "$", StringComparison.OrdinalIgnoreCase);

        var path = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var leaf = path.Length == 0 ? normalized : path[^1];
        var ldInst = ResolveLdInst(path, iedName, fallbackLdInst);
        var logicalNodePath = fallbackLogicalNodePath;
        var localName = leaf;

        var dollarParts = leaf.Split(
            '$',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (dollarParts.Length >= 2)
        {
            logicalNodePath = dollarParts[0];
            localName = dollarParts[^1];
        }
        else
        {
            var dot = leaf.LastIndexOf('.');
            if (dot > 0 && dot < leaf.Length - 1)
            {
                logicalNodePath = leaf[..dot].Trim();
                localName = leaf[(dot + 1)..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(logicalNodePath))
            logicalNodePath = fallbackLogicalNodePath;
        if (string.IsNullOrWhiteSpace(ldInst))
            ldInst = fallbackLdInst;

        return new ParsedReference(ldInst, logicalNodePath, localName.Trim());
    }

    private static string ResolveLdInst(
        IReadOnlyList<string> path,
        string iedName,
        string fallbackLdInst)
    {
        if (path.Count < 2)
            return fallbackLdInst;

        var domain = path[^2].Trim();
        if (Same(domain, iedName) && path.Count >= 3)
            domain = path[^3].Trim();

        if (!string.IsNullOrWhiteSpace(iedName) &&
            domain.StartsWith(iedName, StringComparison.OrdinalIgnoreCase) &&
            domain.Length > iedName.Length)
        {
            domain = domain[iedName.Length..];
        }

        return string.IsNullOrWhiteSpace(domain) ? fallbackLdInst : domain;
    }

    private static string BuildCanonicalReference(
        string iedName,
        string ldInst,
        string logicalNodePath,
        string localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
            return string.Empty;

        return $"{iedName}{ldInst}/{logicalNodePath}${localName}";
    }

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record ParsedReference(
        string LdInst,
        string LogicalNodePath,
        string LocalName);
}
