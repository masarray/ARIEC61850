using System.Globalization;

namespace AR.Iec61850.TimeSync.Ptp;

public readonly record struct ClockIdentity
{
    private readonly byte[] _bytes;

    public ClockIdentity(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PtpConstants.ClockIdentityLength)
            throw new ArgumentException("A PTP clock identity must contain exactly 8 bytes.", nameof(bytes));

        _bytes = bytes.ToArray();
    }

    public static ClockIdentity Empty => new(new byte[PtpConstants.ClockIdentityLength]);

    public static ClockIdentity Parse(string text)
    {
        if (!TryParse(text, out var identity))
            throw new FormatException($"Invalid PTP clock identity '{text}'.");

        return identity;
    }

    public static bool TryParse(string? text, out ClockIdentity identity)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace('-', ':');
        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != PtpConstants.ClockIdentityLength)
            return false;

        Span<byte> bytes = stackalloc byte[PtpConstants.ClockIdentityLength];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length != 2 || !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return false;
        }

        identity = new ClockIdentity(bytes);
        return true;
    }

    public byte[] ToArray()
        => (_bytes ?? new byte[PtpConstants.ClockIdentityLength]).ToArray();

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < PtpConstants.ClockIdentityLength)
            throw new ArgumentException("Destination span must be at least 8 bytes.", nameof(destination));

        (_bytes ?? new byte[PtpConstants.ClockIdentityLength]).CopyTo(destination);
    }

    public override string ToString()
    {
        var bytes = _bytes ?? new byte[PtpConstants.ClockIdentityLength];
        return string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
