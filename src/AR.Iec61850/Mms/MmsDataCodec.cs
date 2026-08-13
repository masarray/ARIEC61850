using AR.Iec61850.Asn1;
using System.Buffers.Binary;
using System.Globalization;

namespace AR.Iec61850.Mms;

public static class MmsDataCodec
{
    public static byte[] EncodeAllData(IEnumerable<MmsDataValue> values)
    {
        var writer = new BerWriter();
        foreach (var value in values)
            writer.WriteRaw(Encode(value));

        return writer.ToArray();
    }

    public static byte[] Encode(MmsDataValue value)
    {
        var content = EncodeContent(value);
        var tag = TagFor(value.Kind, value.UnknownTagNumber);
        return BerWriter.EncodeTlv(tag, content);
    }

    public static IReadOnlyList<MmsDataValue> DecodeAllData(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Array.Empty<MmsDataValue>();

        return BerReader.ReadChildren(bytes).Select(Decode).ToArray();
    }

    public static MmsDataValue Decode(BerTlv tlv)
    {
        if (tlv.Class != BerClass.ContextSpecific)
            return MmsDataValue.Unknown(tlv.TagNumber, tlv.Value.Span);

        return tlv.TagNumber switch
        {
            1 => MmsDataValue.Array(DecodeAllData(tlv.Value)),
            2 => MmsDataValue.Structure(DecodeAllData(tlv.Value)),
            3 => MmsDataValue.Boolean(BerReader.ReadBoolean(tlv) ?? false),
            4 => DecodeBitString(tlv.Value.Span),
            5 => MmsDataValue.Integer(BerReader.ReadSignedInteger(tlv) ?? 0),
            6 => MmsDataValue.Unsigned(BerReader.ReadUnsignedInteger(tlv) ?? 0),
            7 => MmsDataValue.FloatingPoint(DecodeFloatingPoint(tlv.Value.Span)),
            9 => MmsDataValue.OctetString(tlv.Value.Span),
            10 => MmsDataValue.VisibleString(BerReader.ReadAsciiString(tlv)),
            12 => MmsDataValue.BinaryTime(tlv.Value.Span),
            16 => MmsDataValue.MmsString(BerReader.ReadAsciiString(tlv)),
            17 => MmsDataValue.UtcTime(Iec61850UtcTime.FromBytes(tlv.Value.Span)),
            _ => MmsDataValue.Unknown(tlv.TagNumber, tlv.Value.Span)
        };
    }

    public static string ToDisplayString(MmsDataValue value)
    {
        return value.Kind switch
        {
            MmsDataKind.Boolean => Convert.ToString(value.Value, CultureInfo.InvariantCulture)?.ToLowerInvariant() ?? string.Empty,
            MmsDataKind.Integer => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.Unsigned => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.FloatingPoint => value.Value switch
            {
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                _ => string.Empty
            },
            MmsDataKind.VisibleString or MmsDataKind.MmsString => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.BinaryTime => MmsBinaryTime.FromBytes(value.RawValue).ToDisplayString(),
            MmsDataKind.UtcTime => value.Value is Iec61850UtcTime utc ? $"{utc.Value:yyyy-MM-dd HH:mm:ss.fffffff} UTC (q=0x{utc.Quality:X2})" : string.Empty,
            MmsDataKind.Structure or MmsDataKind.Array => MmsDataValueRenderer.ToCompactString(value),
            _ => Convert.ToHexString(value.RawValue.ToArray())
        };
    }

    private static byte[] EncodeContent(MmsDataValue value)
    {
        return value.Kind switch
        {
            MmsDataKind.Array or MmsDataKind.Structure => EncodeAllData(value.Children),
            MmsDataKind.Boolean => BerWriter.EncodeBoolean((bool)value.Value!),
            MmsDataKind.BitString => value.RawValue.ToArray(),
            MmsDataKind.Integer => BerWriter.EncodeSignedInteger((long)value.Value!),
            MmsDataKind.Unsigned => BerWriter.EncodeUnsignedInteger((ulong)value.Value!),
            MmsDataKind.FloatingPoint => BerWriter.EncodeSinglePrecisionFloat(Convert.ToSingle(value.Value, CultureInfo.InvariantCulture)),
            MmsDataKind.OctetString => value.RawValue.ToArray(),
            MmsDataKind.VisibleString => BerWriter.EncodeAscii((string)value.Value!),
            MmsDataKind.MmsString => BerWriter.EncodeAscii((string)value.Value!),
            MmsDataKind.BinaryTime => value.RawValue.ToArray(),
            MmsDataKind.UtcTime => value.Value is Iec61850UtcTime utc
                ? BerWriter.EncodeUtcTime(utc.Value, utc.Quality)
                : throw new InvalidOperationException("UTC time value is missing."),
            MmsDataKind.Unknown => value.RawValue.ToArray(),
            _ => throw new NotSupportedException($"MMS data kind {value.Kind} is not supported yet.")
        };
    }

    private static byte TagFor(MmsDataKind kind, int? unknownTagNumber)
    {
        var tagNumber = kind switch
        {
            MmsDataKind.Array => 1,
            MmsDataKind.Structure => 2,
            MmsDataKind.Boolean => 3,
            MmsDataKind.BitString => 4,
            MmsDataKind.Integer => 5,
            MmsDataKind.Unsigned => 6,
            MmsDataKind.FloatingPoint => 7,
            MmsDataKind.OctetString => 9,
            MmsDataKind.VisibleString => 10,
            MmsDataKind.BinaryTime => 12,
            MmsDataKind.Bcd => 13,
            MmsDataKind.BooleanArray => 14,
            MmsDataKind.ObjectId => 15,
            MmsDataKind.MmsString => 16,
            MmsDataKind.UtcTime => 17,
            MmsDataKind.Unknown => unknownTagNumber ?? throw new InvalidOperationException("Unknown MMS values require a tag number."),
            _ => throw new NotSupportedException($"MMS data kind {kind} is not supported yet.")
        };

        var constructed = kind is MmsDataKind.Array or MmsDataKind.Structure;
        return BerWriter.EncodeIdentifier(BerClass.ContextSpecific, constructed, tagNumber);
    }

    private static MmsDataValue DecodeBitString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return MmsDataValue.BitString(0, ReadOnlySpan<byte>.Empty);

        return MmsDataValue.BitString(bytes[0], bytes[1..]);
    }

    private static float DecodeFloatingPoint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 5)
            return BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[1..])));

        if (bytes.Length == 4)
            return BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes)));

        return float.NaN;
    }
}
