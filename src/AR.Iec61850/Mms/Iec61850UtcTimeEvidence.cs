using System.Buffers.Binary;
using System.Globalization;

namespace AR.Iec61850.Mms;

/// <summary>
/// Typed forensic view of an IEC 61850 UTC-Time value. Decoded time and
/// TimeQuality remain available even for synthetic values, while wire-only
/// fields are explicitly marked unavailable unless the original 8-byte payload
/// survived MMS decoding.
/// </summary>
public sealed class Iec61850UtcTimeEvidence
{
    public bool IsDecoded { get; init; }
    public bool HasWireProvenance { get; init; }
    public DateTimeOffset Value { get; init; }
    public string RawHex { get; init; } = string.Empty;
    public uint? SecondsSinceEpoch { get; init; }
    public int? FractionOfSecond24 { get; init; }
    public string FractionOfSecondHex { get; init; } = string.Empty;
    public byte Quality { get; init; }
    public string QualityHex => $"0x{Quality:X2}";
    public bool LeapSecondsKnown { get; init; }
    public bool ClockFailure { get; init; }
    public bool ClockNotSynchronized { get; init; }
    public int AccuracyCode { get; init; }
    public string TimeAccuracy { get; init; } = string.Empty;
    public string FullPrecisionUtc { get; init; } = string.Empty;
    public string EngineeringUtc { get; init; } = string.Empty;
    public string FullPrecisionLocal { get; init; } = string.Empty;
    public string EngineeringLocal { get; init; } = string.Empty;

    public bool ClockSynchronized => !ClockFailure && !ClockNotSynchronized;

    public string Summary => !IsDecoded
        ? "UTC-Time: unavailable"
        : $"UTC-Time={EngineeringUtc}; quality={QualityHex}; sync={ClockSynchronized.ToString().ToLowerInvariant()}; raw={(HasWireProvenance ? RawHex : "unavailable")}";

    public static Iec61850UtcTimeEvidence Decode(MmsDataValue? value)
    {
        var utcValue = FindFirstUtcTime(value);
        if (utcValue?.Value is not Iec61850UtcTime utc)
            return new Iec61850UtcTimeEvidence();

        var raw = utcValue.RawValue.Count == 8 ? utcValue.RawValue.ToArray() : Array.Empty<byte>();
        uint? seconds = null;
        int? fraction = null;
        var fractionHex = string.Empty;
        if (raw.Length == 8)
        {
            seconds = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(0, 4));
            fraction = (raw[4] << 16) | (raw[5] << 8) | raw[6];
            fractionHex = Convert.ToHexString(raw.AsSpan(4, 3));
        }

        var quality = utc.Quality;
        var accuracyCode = quality & 0x1F;
        var accuracy = accuracyCode == 31
            ? "unspecified"
            : $"2^-{accuracyCode.ToString(CultureInfo.InvariantCulture)} s";

        return new Iec61850UtcTimeEvidence
        {
            IsDecoded = true,
            HasWireProvenance = raw.Length == 8,
            Value = utc.Value,
            RawHex = raw.Length == 8 ? Convert.ToHexString(raw) : string.Empty,
            SecondsSinceEpoch = seconds,
            FractionOfSecond24 = fraction,
            FractionOfSecondHex = fractionHex,
            Quality = quality,
            LeapSecondsKnown = (quality & 0x80) != 0,
            ClockFailure = (quality & 0x40) != 0,
            ClockNotSynchronized = (quality & 0x20) != 0,
            AccuracyCode = accuracyCode,
            TimeAccuracy = accuracy,
            FullPrecisionUtc = Iec61850UtcTimeFormatter.FormatFullPrecisionUtc(utc, includeQuality: false),
            EngineeringUtc = Iec61850UtcTimeFormatter.FormatEngineeringUtcTimestamp(utc),
            FullPrecisionLocal = Iec61850UtcTimeFormatter.FormatFullPrecisionLocalWithOffset(utc),
            EngineeringLocal = Iec61850UtcTimeFormatter.FormatEngineeringLocalWithOffset(utc)
        };
    }

    private static MmsDataValue? FindFirstUtcTime(MmsDataValue? value)
    {
        if (value == null)
            return null;
        if (value.Kind == MmsDataKind.UtcTime)
            return value;
        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            return null;

        foreach (var child in value.Children)
        {
            var match = FindFirstUtcTime(child);
            if (match != null)
                return match;
        }

        return null;
    }
}
