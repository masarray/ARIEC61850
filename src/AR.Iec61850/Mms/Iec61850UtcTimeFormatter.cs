using System.Globalization;

namespace AR.Iec61850.Mms;

/// <summary>
/// Central formatting policy for decoded IEC 61850 UTC-Time values.
/// The engine preserves .NET tick precision (7 fractional digits), while
/// engineering surfaces may use 5 fractional digits to make sub-millisecond
/// evidence easier to read without changing the underlying timestamp.
/// </summary>
public static class Iec61850UtcTimeFormatter
{
    public const string FullPrecisionPattern = "yyyy-MM-dd HH:mm:ss.fffffff";
    public const string EngineeringPattern = "yyyy-MM-dd HH:mm:ss.fffff";

    public static string FormatFullPrecisionUtc(Iec61850UtcTime utc, bool includeQuality = true)
    {
        var timestamp = utc.Value.ToUniversalTime().ToString(FullPrecisionPattern, CultureInfo.InvariantCulture);
        return includeQuality
            ? $"{timestamp} UTC (q=0x{utc.Quality:X2})"
            : $"{timestamp} UTC";
    }

    public static string FormatFullPrecisionLocalTimestamp(Iec61850UtcTime utc)
        => utc.Value.ToLocalTime().ToString(FullPrecisionPattern, CultureInfo.InvariantCulture);

    public static string FormatEngineeringUtcTimestamp(Iec61850UtcTime utc)
    {
        var timestamp = utc.Value.ToUniversalTime().ToString(EngineeringPattern, CultureInfo.InvariantCulture);
        return $"{timestamp} UTC";
    }

    public static string FormatEngineeringLocalTimestamp(Iec61850UtcTime utc)
        => utc.Value.ToLocalTime().ToString(EngineeringPattern, CultureInfo.InvariantCulture);
}
