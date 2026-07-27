using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;
using System.Buffers.Binary;
using System.Globalization;

namespace AR.Iec61850.SampledValues;

public static class SampledValuesPayloadBuilder
{
    public static byte[] BuildPayload(SampledValuesPayloadLayout layout, IReadOnlyList<MmsDataValue> values)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(values);

        if (!layout.IsFullySupported)
            throw new InvalidOperationException(BuildUnsupportedLayoutMessage(layout));

        if (values.Count != layout.Elements.Count)
            throw new ArgumentException($"SV payload value count mismatch. Expected {layout.Elements.Count}, got {values.Count}.", nameof(values));

        var payload = new byte[layout.PayloadByteLength];
        for (var index = 0; index < layout.Elements.Count; index++)
        {
            var element = layout.Elements[index];
            WriteValue(payload.AsSpan(element.Offset, element.Width), element, values[index]);
        }

        return payload;
    }

    public static byte[] BuildDefaultPayload(
        SampledValuesPayloadLayout layout,
        Iec61850UtcTime? timestamp = null,
        SampledValueQuality? quality = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.IsFullySupported)
            throw new InvalidOperationException(BuildUnsupportedLayoutMessage(layout));

        var payload = new byte[layout.PayloadByteLength];
        foreach (var element in layout.Elements)
        {
            var destination = payload.AsSpan(element.Offset, element.Width);
            if (element.Kind == SampledValuePayloadElementKind.Timestamp && timestamp is { } time)
                BerWriter.EncodeUtcTime(time.Value, time.Quality).CopyTo(destination);

            if (element.Kind == SampledValuePayloadElementKind.Quality)
                (quality ?? SampledValueQuality.Good).ToBytes(element.Width).CopyTo(destination);
        }

        return payload;
    }

    public static byte[] BuildDemoPayload(
        SampledValuesPayloadLayout layout,
        long sampleIndex,
        double sampleRateHz,
        double nominalHz,
        Iec61850UtcTime? timestamp = null,
        SampledValueQuality? quality = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.IsFullySupported)
            throw new InvalidOperationException(BuildUnsupportedLayoutMessage(layout));

        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be greater than 0 and finite.");
        if (!double.IsFinite(nominalHz) || nominalHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(nominalHz), "Nominal frequency must be greater than 0 and finite.");

        var payload = new byte[layout.PayloadByteLength];
        foreach (var element in layout.Elements)
        {
            var destination = payload.AsSpan(element.Offset, element.Width);
            if (element.Kind == SampledValuePayloadElementKind.Quality)
            {
                (quality ?? SampledValueQuality.Good).ToBytes(element.Width).CopyTo(destination);
                continue;
            }

            if (element.Kind == SampledValuePayloadElementKind.BitString ||
                element.Kind == SampledValuePayloadElementKind.EntryTime)
            {
                continue;
            }

            if (element.Kind == SampledValuePayloadElementKind.Timestamp)
            {
                if (timestamp is { } time)
                    BerWriter.EncodeUtcTime(time.Value, time.Quality).CopyTo(destination);
                continue;
            }

            var value = ComputeDemoValue(element, sampleIndex, sampleRateHz, nominalHz);
            WriteNumeric(destination, element.Kind, value);
        }

        return payload;
    }

    private static void WriteValue(Span<byte> destination, SampledValuePayloadElement element, MmsDataValue value)
    {
        switch (element.Kind)
        {
            case SampledValuePayloadElementKind.Boolean:
                destination[0] = value.Kind == MmsDataKind.Boolean && value.Value is true ? (byte)1 : (byte)0;
                return;

            case SampledValuePayloadElementKind.Int8:
            case SampledValuePayloadElementKind.Int16:
            case SampledValuePayloadElementKind.Int32:
            case SampledValuePayloadElementKind.Int64:
            case SampledValuePayloadElementKind.Enum:
                WriteNumeric(destination, element.Kind, ToInt64(value));
                return;

            case SampledValuePayloadElementKind.UInt8:
            case SampledValuePayloadElementKind.UInt16:
            case SampledValuePayloadElementKind.UInt24:
            case SampledValuePayloadElementKind.UInt32:
            case SampledValuePayloadElementKind.UInt64:
                WriteUnsigned(destination, element.Kind, ToUInt64(value));
                return;

            case SampledValuePayloadElementKind.Float32:
                WriteFloat32(destination, ToSingle(value));
                return;

            case SampledValuePayloadElementKind.Float64:
                WriteFloat64(destination, ToDouble(value));
                return;

            case SampledValuePayloadElementKind.Quality:
            case SampledValuePayloadElementKind.BitString:
            case SampledValuePayloadElementKind.OctetString:
            case SampledValuePayloadElementKind.VisibleString:
            case SampledValuePayloadElementKind.EntryTime:
                CopyRawPayloadBytes(destination, value);
                return;

            case SampledValuePayloadElementKind.Timestamp:
                if (value.Kind != MmsDataKind.UtcTime || value.Value is not Iec61850UtcTime utc)
                    throw new ArgumentException($"SV element {element.SignalReference} expects UTC time.");
                BerWriter.EncodeUtcTime(utc.Value, utc.Quality).CopyTo(destination);
                return;

            default:
                throw new NotSupportedException($"SV payload element kind {element.Kind} is not supported.");
        }
    }

    private static void WriteNumeric(Span<byte> destination, SampledValuePayloadElementKind kind, long value)
    {
        switch (kind)
        {
            case SampledValuePayloadElementKind.Int8:
                destination[0] = unchecked((byte)(sbyte)value);
                break;
            case SampledValuePayloadElementKind.Int16:
                BinaryPrimitives.WriteInt16BigEndian(destination, checked((short)value));
                break;
            case SampledValuePayloadElementKind.Int32:
            case SampledValuePayloadElementKind.Enum:
                BinaryPrimitives.WriteInt32BigEndian(destination, checked((int)value));
                break;
            case SampledValuePayloadElementKind.Int64:
                BinaryPrimitives.WriteInt64BigEndian(destination, value);
                break;
            case SampledValuePayloadElementKind.UInt8:
            case SampledValuePayloadElementKind.UInt16:
            case SampledValuePayloadElementKind.UInt24:
            case SampledValuePayloadElementKind.UInt32:
            case SampledValuePayloadElementKind.UInt64:
                WriteUnsigned(destination, kind, checked((ulong)value));
                break;
            case SampledValuePayloadElementKind.Float32:
                WriteFloat32(destination, value);
                break;
            case SampledValuePayloadElementKind.Float64:
                WriteFloat64(destination, value);
                break;
            default:
                throw new NotSupportedException($"SV numeric kind {kind} is not supported.");
        }
    }

    private static void WriteUnsigned(Span<byte> destination, SampledValuePayloadElementKind kind, ulong value)
    {
        switch (kind)
        {
            case SampledValuePayloadElementKind.UInt8:
                destination[0] = checked((byte)value);
                break;
            case SampledValuePayloadElementKind.UInt16:
                BinaryPrimitives.WriteUInt16BigEndian(destination, checked((ushort)value));
                break;
            case SampledValuePayloadElementKind.UInt24:
                if (value > 0xFFFFFF)
                    throw new OverflowException("UINT24 value is out of range.");
                destination[0] = (byte)((value >> 16) & 0xFF);
                destination[1] = (byte)((value >> 8) & 0xFF);
                destination[2] = (byte)(value & 0xFF);
                break;
            case SampledValuePayloadElementKind.UInt32:
                BinaryPrimitives.WriteUInt32BigEndian(destination, checked((uint)value));
                break;
            case SampledValuePayloadElementKind.UInt64:
                BinaryPrimitives.WriteUInt64BigEndian(destination, value);
                break;
            default:
                throw new NotSupportedException($"SV unsigned kind {kind} is not supported.");
        }
    }

    private static void WriteFloat32(Span<byte> destination, double value)
        => BinaryPrimitives.WriteUInt32BigEndian(destination, unchecked((uint)BitConverter.SingleToInt32Bits((float)value)));

    private static void WriteFloat64(Span<byte> destination, double value)
        => BinaryPrimitives.WriteUInt64BigEndian(destination, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    private static void CopyRawPayloadBytes(Span<byte> destination, MmsDataValue value)
    {
        var raw = value.RawValue.ToArray();
        if (value.Kind == MmsDataKind.BitString && raw.Length == destination.Length + 1)
            raw = raw[1..];

        if (raw.Length > destination.Length)
            throw new ArgumentException($"SV raw value has {raw.Length} bytes but the payload slot has {destination.Length} bytes.");
        raw.CopyTo(destination);
    }

    private static long ToInt64(MmsDataValue value)
        => value.Kind switch
        {
            MmsDataKind.Integer when value.Value is long signed => signed,
            MmsDataKind.Unsigned when value.Value is ulong unsigned => checked((long)unsigned),
            MmsDataKind.Boolean when value.Value is bool boolean => boolean ? 1 : 0,
            _ => Convert.ToInt64(value.Value, CultureInfo.InvariantCulture)
        };

    private static ulong ToUInt64(MmsDataValue value)
        => value.Kind switch
        {
            MmsDataKind.Unsigned when value.Value is ulong unsigned => unsigned,
            MmsDataKind.Integer when value.Value is long signed => checked((ulong)signed),
            MmsDataKind.Boolean when value.Value is bool boolean => boolean ? 1UL : 0UL,
            _ => Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture)
        };

    private static float ToSingle(MmsDataValue value)
        => value.Kind == MmsDataKind.FloatingPoint && value.Value is float number
            ? number
            : Convert.ToSingle(value.Value, CultureInfo.InvariantCulture);

    private static double ToDouble(MmsDataValue value)
        => value.Kind == MmsDataKind.FloatingPoint && value.Value is float number
            ? number
            : Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);

    private static long ComputeDemoValue(SampledValuePayloadElement element, long sampleIndex, double sampleRateHz, double nominalHz)
    {
        var amplitude = ResolveDemoAmplitude(element);
        var angle = (2.0 * Math.PI * nominalHz * sampleIndex / sampleRateHz) + ResolvePhaseRadians(element.SignalReference);
        return (long)Math.Round(amplitude * Math.Sin(angle));
    }

    private static int ResolveDemoAmplitude(SampledValuePayloadElement element)
    {
        var reference = element.SignalReference;
        if (reference.Contains("/TVTR", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains(".Vol", StringComparison.OrdinalIgnoreCase))
            return 100_000;

        if (reference.Contains("/TCTR", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains(".Amp", StringComparison.OrdinalIgnoreCase))
            return 10_000;

        return 1_000;
    }

    private static double ResolvePhaseRadians(string signalReference)
    {
        var slash = signalReference.LastIndexOf('/');
        var dot = signalReference.IndexOf('.', slash + 1);
        if (slash < 0 || dot <= slash)
            return 0;

        var logicalNode = signalReference[(slash + 1)..dot];
        var digits = new string(logicalNode.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var instance))
            return 0;

        return instance switch
        {
            2 => -2.0 * Math.PI / 3.0,
            3 => 2.0 * Math.PI / 3.0,
            _ => 0
        };
    }

    private static string BuildUnsupportedLayoutMessage(SampledValuesPayloadLayout layout)
        => "SV payload layout has unsupported DataSet entries: " +
           string.Join("; ", layout.UnsupportedElements.Select(x => $"{x.SignalReference} bType={x.BType}"));
}
