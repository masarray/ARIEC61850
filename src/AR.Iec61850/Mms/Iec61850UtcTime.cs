using System.Buffers.Binary;

namespace AR.Iec61850.Mms;

public readonly record struct Iec61850UtcTime(DateTimeOffset Value, byte Quality)
{
    public static Iec61850UtcTime FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8)
            throw new ArgumentException("IEC 61850 UTC time requires exactly 8 bytes.", nameof(bytes));

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        var fraction = (bytes[4] << 16) | (bytes[5] << 8) | bytes[6];
        var quality = bytes[7];
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddSeconds(fraction / 16_777_216.0);

        return new Iec61850UtcTime(timestamp, quality);
    }
}
