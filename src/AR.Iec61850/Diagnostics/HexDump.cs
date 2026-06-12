namespace AR.Iec61850.Diagnostics;

public static class HexDump
{
    public static byte[] Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();

        var tokens = hex
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var bytes = new byte[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            bytes[i] = Convert.ToByte(tokens[i], 16);

        return bytes;
    }

    public static string ToCompactString(ReadOnlySpan<byte> data, int maxBytes = 96)
    {
        if (data.IsEmpty)
            return string.Empty;

        var length = Math.Min(data.Length, Math.Max(0, maxBytes));
        var text = string.Join(" ", data[..length].ToArray().Select(b => b.ToString("X2")));
        return data.Length > length
            ? $"{text} ... (+{data.Length - length} byte)"
            : text;
    }

    public static bool Contains(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        if (pattern.IsEmpty)
            return true;

        if (pattern.Length > data.Length)
            return false;

        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.Slice(i, pattern.Length).SequenceEqual(pattern))
                return true;
        }

        return false;
    }
}
