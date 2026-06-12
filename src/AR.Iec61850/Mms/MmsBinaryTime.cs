using System.Globalization;

namespace AR.Iec61850.Mms;

public readonly record struct MmsBinaryTime(DateTimeOffset? UtcValue, TimeSpan? TimeOfDay, string RawHex, string Message)
{
    private static readonly DateTimeOffset Epoch = new(1984, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static MmsBinaryTime FromBytes(IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var raw = Convert.ToHexString(bytes.ToArray());
        if (bytes.Count == 6)
        {
            var milliseconds = ReadUInt32BigEndian(bytes, 0);
            var days = ReadUInt16BigEndian(bytes, 4);
            try
            {
                var timestamp = Epoch.AddDays(days).AddMilliseconds(milliseconds);
                return new MmsBinaryTime(timestamp, timestamp.TimeOfDay, raw, string.Empty);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new MmsBinaryTime(null, null, raw, "binary-time value is outside DateTimeOffset range");
            }
        }

        if (bytes.Count == 4)
        {
            var milliseconds = ReadUInt32BigEndian(bytes, 0);
            try
            {
                var timeOfDay = TimeSpan.FromMilliseconds(milliseconds);
                return new MmsBinaryTime(null, timeOfDay, raw, string.Empty);
            }
            catch (OverflowException)
            {
                return new MmsBinaryTime(null, null, raw, "binary-time time-of-day value is outside TimeSpan range");
            }
        }

        return new MmsBinaryTime(null, null, raw, $"unsupported binary-time length {bytes.Count}");
    }

    public string ToDisplayString()
    {
        if (UtcValue.HasValue)
            return $"{UtcValue.Value:yyyy-MM-dd HH:mm:ss.fff} UTC (binary-time={RawHex})";

        if (TimeOfDay.HasValue)
            return $"time-of-day={TimeOfDay.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)} (binary-time={RawHex})";

        return string.IsNullOrWhiteSpace(Message)
            ? $"binary-time={RawHex}"
            : $"binary-time={RawHex} ({Message})";
    }

    private static uint ReadUInt32BigEndian(IReadOnlyList<byte> bytes, int offset)
        => ((uint)bytes[offset] << 24) |
           ((uint)bytes[offset + 1] << 16) |
           ((uint)bytes[offset + 2] << 8) |
           bytes[offset + 3];

    private static ushort ReadUInt16BigEndian(IReadOnlyList<byte> bytes, int offset)
        => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
}
