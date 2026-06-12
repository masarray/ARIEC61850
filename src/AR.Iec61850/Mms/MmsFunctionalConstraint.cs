namespace AR.Iec61850.Mms;

public static class MmsFunctionalConstraint
{
    public static readonly IReadOnlyList<string> StandardConstraints =
    [
        "ST", "MX", "CO", "SP", "SG", "SE", "SV", "CF", "DC", "EX", "SR", "OR", "BL",
        "RP", "BR", "LG", "GO", "GS", "MS", "US"
    ];

    private static readonly HashSet<string> KnownConstraints = new(StandardConstraints, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string value)
        => !string.IsNullOrWhiteSpace(value) && KnownConstraints.Contains(value.Trim());

    public static string Normalize(string value)
        => value.Trim().ToUpperInvariant();

    public static bool IsReportConstraint(string value)
        => string.Equals(value, "RP", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "BR", StringComparison.OrdinalIgnoreCase);

    public static bool IsControlConstraint(string value)
        => string.Equals(value, "CO", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SuggestByPath(string userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
            return Array.Empty<string>();

        var path = userPath.Trim().Replace('$', '.');
        var leaf = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

        if (leaf.Equals("stVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("q", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("t", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("general", StringComparison.OrdinalIgnoreCase))
            return ["ST", "MX"];

        if (leaf.Equals("mag", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("cVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("instMag", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("f", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("i", StringComparison.OrdinalIgnoreCase))
            return ["MX", "ST"];

        if (path.Contains(".Oper", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(".SBO", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(".Cancel", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("ctlVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("ctlNum", StringComparison.OrdinalIgnoreCase))
            return ["CO"];

        if (leaf.EndsWith("Set", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("setVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("setMag", StringComparison.OrdinalIgnoreCase))
            return ["SP", "SG", "SE"];

        if (leaf.Contains("desc", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("dU", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("vendor", StringComparison.OrdinalIgnoreCase))
            return ["DC"];

        if (leaf.Equals("ctlModel", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("sboTimeout", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("minVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("maxVal", StringComparison.OrdinalIgnoreCase))
            return ["CF"];

        if (leaf.Equals("RptEna", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("DatSet", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("GI", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("RptID", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("ConfRev", StringComparison.OrdinalIgnoreCase))
            return ["RP", "BR"];

        return ["ST", "MX", "CF", "DC", "CO", "SP"];
    }
}
