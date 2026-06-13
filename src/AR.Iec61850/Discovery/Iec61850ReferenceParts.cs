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

        for (var index = 0; index <= normalized.Length - 4; index++)
        {
            if (!IsUpperAsciiLetter(normalized[index]) ||
                !IsUpperAsciiLetter(normalized[index + 1]) ||
                !IsUpperAsciiLetter(normalized[index + 2]) ||
                !IsUpperAsciiLetter(normalized[index + 3]))
            {
                continue;
            }

            var prefix = normalized[..index];
            var lnClass = normalized.Substring(index, 4);
            var lnInst = normalized[(index + 4)..];
            return new Iec61850LogicalNodeName(normalized, prefix, lnClass, lnInst);
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

    private static bool IsUpperAsciiLetter(char value)
        => value is >= 'A' and <= 'Z';
}
