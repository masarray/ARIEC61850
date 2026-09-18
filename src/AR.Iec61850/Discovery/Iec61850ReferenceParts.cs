namespace AR.Iec61850.Discovery;

public readonly record struct Iec61850LogicalNodeName(string Name, string Prefix, string LnClass, string LnInst)
{
    public string SclLnClass => string.IsNullOrWhiteSpace(LnClass) ? Name : LnClass;
}

public static class Iec61850ReferenceParts
{
    public static Iec61850LogicalNodeName ParseLogicalNodeName(string logicalNodeName)
    {
        if (string.IsNullOrWhiteSpace(logicalNodeName))
            return new Iec61850LogicalNodeName(string.Empty, string.Empty, string.Empty, string.Empty);

        var normalized = logicalNodeName.Trim();
        if (string.Equals(normalized, "LLN0", StringComparison.OrdinalIgnoreCase))
            return new Iec61850LogicalNodeName(normalized, string.Empty, "LLN0", string.Empty);

        // IEC 61850 LN names are prefix + four-letter LN class + numeric instance.
        // Prefixes are allowed to contain uppercase letters and digits themselves, so
        // selecting the first four-uppercase run corrupts names such as RPRE_MMXU1
        // (RPRE/_MMXU1) and I01ATCTR1 (I01/ATCT/R1). Anchor the class immediately
        // before the trailing numeric instance instead.
        var instanceStart = normalized.Length;
        while (instanceStart > 0 && char.IsAsciiDigit(normalized[instanceStart - 1]))
            instanceStart--;

        if (instanceStart < normalized.Length && instanceStart >= 4)
        {
            var classStart = instanceStart - 4;
            if (IsFourLetterLnClass(normalized, classStart))
            {
                return new Iec61850LogicalNodeName(
                    normalized,
                    normalized[..classStart],
                    normalized.Substring(classStart, 4),
                    normalized[instanceStart..]);
            }
        }

        // Keep a conservative fallback for unusual live names without a numeric
        // instance. Prefer the final four-letter class candidate so an uppercase
        // prefix cannot steal the LN class boundary.
        for (var index = normalized.Length - 4; index >= 0; index--)
        {
            if (!IsFourLetterLnClass(normalized, index))
                continue;

            return new Iec61850LogicalNodeName(
                normalized,
                normalized[..index],
                normalized.Substring(index, 4),
                normalized[(index + 4)..]);
        }

        return new Iec61850LogicalNodeName(normalized, string.Empty, normalized, string.Empty);
    }

    public static string TopDataObjectName(string dataObjectPath)
    {
        if (string.IsNullOrWhiteSpace(dataObjectPath))
            return string.Empty;

        var normalized = dataObjectPath.Trim();
        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? normalized : normalized[..dot];
    }

    public static string DataAttributePath(string dataObjectPath)
    {
        if (string.IsNullOrWhiteSpace(dataObjectPath))
            return string.Empty;

        var normalized = dataObjectPath.Trim();
        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 || dot >= normalized.Length - 1 ? string.Empty : normalized[(dot + 1)..];
    }

    public static string SafeIdPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "X";

        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }

    private static bool IsFourLetterLnClass(string value, int start)
        => start >= 0 &&
           start + 4 <= value.Length &&
           IsUpperAsciiLetter(value[start]) &&
           IsUpperAsciiLetter(value[start + 1]) &&
           IsUpperAsciiLetter(value[start + 2]) &&
           IsUpperAsciiLetter(value[start + 3]);

    private static bool IsUpperAsciiLetter(char value)
        => value is >= 'A' and <= 'Z';
}
