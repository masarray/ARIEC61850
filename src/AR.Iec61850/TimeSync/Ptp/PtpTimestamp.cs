using System.Buffers.Binary;

namespace AR.Iec61850.TimeSync.Ptp;

public readonly record struct PtpTimestamp(ulong Seconds, uint Nanoseconds)
{
    public static PtpTimestamp Zero => new(0, 0);

    public static PtpTimestamp Now()
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = (ulong)now.ToUnixTimeSeconds();
        var nanos = (uint)((now.Ticks % TimeSpan.TicksPerSecond) * 100);
        return new PtpTimestamp(seconds, nanos);
    }

    public static PtpTimestamp Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < 10)
            throw new ArgumentException("A PTP timestamp requires 10 bytes.", nameof(source));

        var secondsHigh = (ulong)BinaryPrimitives.ReadUInt16BigEndian(source[..2]);
        var secondsLow = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(2, 4));
        var seconds = (secondsHigh << 32) | secondsLow;
        var nanoseconds = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(6, 4));
        return new PtpTimestamp(seconds, nanoseconds);
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < 10)
            throw new ArgumentException("A PTP timestamp requires 10 bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], (ushort)(Seconds >> 32));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(2, 4), (uint)(Seconds & 0xFFFF_FFFF));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(6, 4), Nanoseconds);
    }
}
