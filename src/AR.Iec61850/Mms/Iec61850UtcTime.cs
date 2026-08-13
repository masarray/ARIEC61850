using System.Buffers.Binary;

namespace AR.Iec61850.Mms;

public readonly record struct Iec61850UtcTime(DateTimeOffset Value, byte Quality)
{
    private const long FractionDenominator = 16_777_216L;

    public static Iec61850UtcTime FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8)
            throw new ArgumentException("IEC 61850 UTC time requires exactly 8 bytes.", nameof(bytes));

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        var fraction = (bytes[4] << 16) | (bytes[5] << 8) | bytes[6];
        var quality = bytes[7];

        // IEC 61850 UTC-Time carries the fractional second as a 24-bit fraction of
        // one second. Convert it directly to .NET 100 ns ticks with integer
        // arithmetic so sub-millisecond information is not lost through floating
        // point conversion before the value reaches report/binding consumers.
        var fractionalTicks = ((long)fraction * TimeSpan.TicksPerSecond + (FractionDenominator / 2)) / FractionDenominator;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(fractionalTicks);

        return new Iec61850UtcTime(timestamp, quality);
    }
}
