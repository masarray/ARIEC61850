using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues;

public enum SampledValuePayloadElementKind
{
    Unsupported,
    Boolean,
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt24,
    UInt32,
    UInt64,
    Float32,
    Float64,
    Enum,
    BitString,
    Quality,
    Timestamp,
    EntryTime,
    OctetString,
    VisibleString
}

public sealed class SampledValuePayloadElement
{
    public int Index { get; init; }
    public string SignalReference { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string BType { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Width { get; init; }
    public SampledValuePayloadElementKind Kind { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
    public bool IsSupported => Kind != SampledValuePayloadElementKind.Unsupported && Width > 0;
}

public sealed class SampledValuesPayloadLayout
{
    public IReadOnlyList<SampledValuePayloadElement> Elements { get; init; } = Array.Empty<SampledValuePayloadElement>();
    public int PayloadByteLength { get; init; }
    public bool IsFullySupported => UnsupportedElements.Count == 0;
    public IReadOnlyList<SampledValuePayloadElement> UnsupportedElements { get; init; } = Array.Empty<SampledValuePayloadElement>();

    public static SampledValuesPayloadLayout FromDataSet(IReadOnlyList<SclDataSetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var offset = 0;
        var elements = new List<SampledValuePayloadElement>(entries.Count);
        foreach (var entry in entries.OrderBy(x => x.Index))
        {
            var element = CreateElement(entry, offset);
            elements.Add(element);
            if (element.IsSupported)
                offset += element.Width;
        }

        return new SampledValuesPayloadLayout
        {
            Elements = elements,
            PayloadByteLength = offset,
            UnsupportedElements = elements.Where(x => !x.IsSupported).ToArray()
        };
    }

    private static SampledValuePayloadElement CreateElement(SclDataSetEntry entry, int offset)
    {
        var kind = ResolveKind(entry);
        var width = ResolveWidth(kind, entry.BType);
        var diagnostic = kind == SampledValuePayloadElementKind.Unsupported
            ? $"Unsupported SV payload bType '{entry.BType}' for {entry.SignalReference}."
            : $"SV payload element maps to {kind} at offset {offset}, width {width}.";

        return new SampledValuePayloadElement
        {
            Index = entry.Index,
            SignalReference = entry.SignalReference,
            Cdc = entry.Cdc,
            BType = entry.BType,
            Kind = kind,
            Offset = offset,
            Width = width,
            Diagnostic = diagnostic
        };
    }

    private static SampledValuePayloadElementKind ResolveKind(SclDataSetEntry entry)
    {
        if (entry.IsQuality || entry.BType.Equals("Quality", StringComparison.OrdinalIgnoreCase))
            return SampledValuePayloadElementKind.Quality;

        if (entry.IsTimestamp || entry.BType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
            return SampledValuePayloadElementKind.Timestamp;

        var normalized = NormalizeBType(entry.BType);
        return normalized switch
        {
            "BOOLEAN" or "BOOL" => SampledValuePayloadElementKind.Boolean,
            "INT8" => SampledValuePayloadElementKind.Int8,
            "INT16" => SampledValuePayloadElementKind.Int16,
            "INT32" => SampledValuePayloadElementKind.Int32,
            "INT64" => SampledValuePayloadElementKind.Int64,
            "INT8U" or "UINT8" => SampledValuePayloadElementKind.UInt8,
            "INT16U" or "UINT16" => SampledValuePayloadElementKind.UInt16,
            "INT24U" or "UINT24" => SampledValuePayloadElementKind.UInt24,
            "INT32U" or "UINT32" => SampledValuePayloadElementKind.UInt32,
            "INT64U" or "UINT64" => SampledValuePayloadElementKind.UInt64,
            "FLOAT32" or "FLOAT" => SampledValuePayloadElementKind.Float32,
            "FLOAT64" or "DOUBLE" => SampledValuePayloadElementKind.Float64,
            "ENUM" or "ENUMERATED" or "CODEDENUM" or "DBPOS" => SampledValuePayloadElementKind.Enum,
            "BITSTRING" or "BITSTR" => SampledValuePayloadElementKind.BitString,
            "QUALITY" => SampledValuePayloadElementKind.Quality,
            "TIMESTAMP" => SampledValuePayloadElementKind.Timestamp,
            "ENTRYTIME" => SampledValuePayloadElementKind.EntryTime,
            "OCTET64" => SampledValuePayloadElementKind.OctetString,
            "VISSTRING32" or "VISIBLESTRING" => SampledValuePayloadElementKind.VisibleString,
            _ => SampledValuePayloadElementKind.Unsupported
        };
    }

    private static int ResolveWidth(SampledValuePayloadElementKind kind, string bType)
        => kind switch
        {
            SampledValuePayloadElementKind.Boolean => 1,
            SampledValuePayloadElementKind.Int8 => 1,
            SampledValuePayloadElementKind.Int16 => 2,
            SampledValuePayloadElementKind.Int32 => 4,
            SampledValuePayloadElementKind.Int64 => 8,
            SampledValuePayloadElementKind.UInt8 => 1,
            SampledValuePayloadElementKind.UInt16 => 2,
            SampledValuePayloadElementKind.UInt24 => 3,
            SampledValuePayloadElementKind.UInt32 => 4,
            SampledValuePayloadElementKind.UInt64 => 8,
            SampledValuePayloadElementKind.Float32 => 4,
            SampledValuePayloadElementKind.Float64 => 8,
            SampledValuePayloadElementKind.Enum => 4,
            SampledValuePayloadElementKind.BitString => 4,
            SampledValuePayloadElementKind.Quality => 4,
            SampledValuePayloadElementKind.Timestamp => 8,
            SampledValuePayloadElementKind.EntryTime => 6,
            SampledValuePayloadElementKind.OctetString => NormalizeBType(bType) == "OCTET64" ? 64 : 0,
            SampledValuePayloadElementKind.VisibleString => 35,
            _ => 0
        };

    private static string NormalizeBType(string bType)
        => new((bType ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
