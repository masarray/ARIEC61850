using System.Globalization;

namespace AR.Iec61850.Ethernet;

public readonly record struct MacAddress
{
    private readonly byte[] _bytes;

    public MacAddress(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 6)
            throw new ArgumentException("A MAC address must contain exactly 6 bytes.", nameof(bytes));

        _bytes = bytes.ToArray();
    }

    public static MacAddress Parse(string text)
    {
        if (!TryParse(text, out var address))
            throw new FormatException($"Invalid MAC address '{text}'.");

        return address;
    }

    public static bool TryParse(string? text, out MacAddress address)
    {
        address = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace('-', ':');
        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 6)
            return false;

        Span<byte> bytes = stackalloc byte[6];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length != 2 ||
                !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return false;
            }
        }

        address = new MacAddress(bytes);
        return true;
    }

    public byte[] ToArray()
        => _bytes?.ToArray() ?? new byte[6];

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < 6)
            throw new ArgumentException("Destination span must be at least 6 bytes.", nameof(destination));

        (_bytes ?? new byte[6]).CopyTo(destination);
    }

    public override string ToString()
    {
        var bytes = _bytes ?? new byte[6];
        return string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
