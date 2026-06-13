using AR.Iec61850.Mms;
using System.Buffers.Binary;
using System.Text;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesDecodedValue
{
    public SampledValuePayloadElement Element { get; init; } = new();
    public MmsDataValue Value { get; init; } = MmsDataValue.Unknown(0, ReadOnlySpan<byte>.Empty);
    public byte[] RawBytes { get; init; } = [];
    public string Diagnostic { get; init; } = string.Empty;
}

public sealed class SampledValuesPayloadDecodeResult
{
    public IReadOnlyList<SampledValuesDecodedValue> Values { get; init; } = Array.Empty<SampledValuesDecodedValue>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public int ExpectedPayloadBytes { get; init; }
    public int ActualPayloadBytes { get; init; }
    public bool IsComplete => Diagnostics.Count == 0;
}

public static class SampledValuesPayloadDecoder
{
    public static SampledValuesPayloadDecodeResult Decode(SampledValuesPayloadLayout layout, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var values = new List<SampledValuesDecodedValue>(layout.Elements.Count);
        var diagnostics = new List<string>();

        if (payload.Length < layout.PayloadByteLength)
        {
            diagnostics.Add(
                $"SV payload is too short. Expected at least {layout.PayloadByteLength} byte(s), got {payload.Length}.");
        }
        else if (payload.Length > layout.PayloadByteLength)
        {
            diagnostics.Add(
                $"SV payload has {payload.Length - layout.PayloadByteLength} trailing byte(s) beyond the SCL layout.");
        }

        foreach (var element in layout.Elements)
        {
            if (!element.IsSupported)
            {
                diagnostics.Add(element.Diagnostic);
                continue;
            }

            if (element.Offset + element.Width > payload.Length)
            {
                diagnostics.Add(
                    $"SV payload cannot decode {element.SignalReference}; offset {element.Offset}, width {element.Width}, payload {payload.Length}.");
                continue;
            }

            var slice = payload.Slice(element.Offset, element.Width);
            try
            {
                values.Add(new SampledValuesDecodedValue
                {
                    Element = element,
                    Value = DecodeValue(element, slice),
                    RawBytes = slice.ToArray(),
                    Diagnostic = $"Decoded {element.SignalReference} as {element.Kind} from offset {element.Offset}."
                });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
            {
                diagnostics.Add($"SV payload failed to decode {element.SignalReference}: {ex.Message}");
            }
        }

        return new SampledValuesPayloadDecodeResult
        {
            Values = values,
            Diagnostics = diagnostics,
            ExpectedPayloadBytes = layout.PayloadByteLength,
            ActualPayloadBytes = payload.Length
        };
    }

    private static MmsDataValue DecodeValue(SampledValuePayloadElement element, ReadOnlySpan<byte> source)
        => element.Kind switch
        {
            SampledValuePayloadElementKind.Boolean => MmsDataValue.Boolean(source[0] != 0),
            SampledValuePayloadElementKind.Int8 => MmsDataValue.Integer(unchecked((sbyte)source[0])),
            SampledValuePayloadElementKind.Int16 => MmsDataValue.Integer(BinaryPrimitives.ReadInt16BigEndian(source)),
            SampledValuePayloadElementKind.Int32 => MmsDataValue.Integer(BinaryPrimitives.ReadInt32BigEndian(source)),
            SampledValuePayloadElementKind.Int64 => MmsDataValue.Integer(BinaryPrimitives.ReadInt64BigEndian(source)),
            SampledValuePayloadElementKind.UInt8 => MmsDataValue.Unsigned(source[0]),
            SampledValuePayloadElementKind.UInt16 => MmsDataValue.Unsigned(BinaryPrimitives.ReadUInt16BigEndian(source)),
            SampledValuePayloadElementKind.UInt24 => MmsDataValue.Unsigned(ReadUInt24(source)),
            SampledValuePayloadElementKind.UInt32 => MmsDataValue.Unsigned(BinaryPrimitives.ReadUInt32BigEndian(source)),
            SampledValuePayloadElementKind.UInt64 => MmsDataValue.Unsigned(BinaryPrimitives.ReadUInt64BigEndian(source)),
            SampledValuePayloadElementKind.Float32 => MmsDataValue.FloatingPoint(
                BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(source))),
            SampledValuePayloadElementKind.Float64 => MmsDataValue.FloatingPoint(
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(source))),
            SampledValuePayloadElementKind.Enum => MmsDataValue.Integer(BinaryPrimitives.ReadInt32BigEndian(source)),
            SampledValuePayloadElementKind.BitString => MmsDataValue.BitString(0, source),
            SampledValuePayloadElementKind.Quality => MmsDataValue.BitString(0, source),
            SampledValuePayloadElementKind.Timestamp => MmsDataValue.UtcTime(Iec61850UtcTime.FromBytes(source)),
            SampledValuePayloadElementKind.EntryTime => MmsDataValue.BinaryTime(source),
            SampledValuePayloadElementKind.OctetString => MmsDataValue.OctetString(source),
            SampledValuePayloadElementKind.VisibleString => MmsDataValue.VisibleString(DecodeVisibleString(source)),
            _ => MmsDataValue.Unknown(0, source)
        };

    private static ulong ReadUInt24(ReadOnlySpan<byte> source)
        => ((ulong)source[0] << 16) | ((ulong)source[1] << 8) | source[2];

    private static string DecodeVisibleString(ReadOnlySpan<byte> source)
    {
        var length = source.IndexOf((byte)0);
        if (length < 0)
            length = source.Length;

        return Encoding.ASCII.GetString(source[..length]);
    }
}
