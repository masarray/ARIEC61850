namespace AR.Iec61850.Mms;

public sealed class MmsDataValue
{
    private MmsDataValue(
        MmsDataKind kind,
        object? value = null,
        IReadOnlyList<MmsDataValue>? children = null,
        byte[]? rawValue = null,
        int? unknownTagNumber = null)
    {
        Kind = kind;
        Value = value;
        Children = children ?? System.Array.Empty<MmsDataValue>();
        RawValue = rawValue ?? System.Array.Empty<byte>();
        UnknownTagNumber = unknownTagNumber;
    }

    public MmsDataKind Kind { get; }
    public object? Value { get; }
    public IReadOnlyList<MmsDataValue> Children { get; }
    public IReadOnlyList<byte> RawValue { get; }
    public int? UnknownTagNumber { get; }

    public static MmsDataValue Array(IEnumerable<MmsDataValue> values)
        => new(MmsDataKind.Array, children: values.ToArray());

    public static MmsDataValue Structure(IEnumerable<MmsDataValue> values)
        => new(MmsDataKind.Structure, children: values.ToArray());

    public static MmsDataValue Boolean(bool value)
        => new(MmsDataKind.Boolean, value);

    public static MmsDataValue BitString(byte unusedBits, ReadOnlySpan<byte> data)
        => new(MmsDataKind.BitString, rawValue: new[] { unusedBits }.Concat(data.ToArray()).ToArray());

    public static MmsDataValue Integer(long value)
        => new(MmsDataKind.Integer, value);

    public static MmsDataValue Unsigned(ulong value)
        => new(MmsDataKind.Unsigned, value);

    public static MmsDataValue FloatingPoint(float value)
        => new(MmsDataKind.FloatingPoint, value);

    public static MmsDataValue FloatingPoint(double value)
        => new(MmsDataKind.FloatingPoint, value);

    public static MmsDataValue OctetString(ReadOnlySpan<byte> value)
        => new(MmsDataKind.OctetString, rawValue: value.ToArray());

    public static MmsDataValue VisibleString(string value)
        => new(MmsDataKind.VisibleString, value);

    public static MmsDataValue MmsString(string value)
        => new(MmsDataKind.MmsString, value);

    /// <summary>
    /// Creates a UTC-Time value without claiming wire provenance. Use the raw-value
    /// overload when the value originates from an MMS/IEC 61850 payload.
    /// </summary>
    public static MmsDataValue UtcTime(Iec61850UtcTime value)
        => new(MmsDataKind.UtcTime, value);

    /// <summary>
    /// Creates a UTC-Time value while preserving the exact 8-byte IEC 61850
    /// UTC-Time payload that produced the decoded timestamp.
    /// </summary>
    public static MmsDataValue UtcTime(Iec61850UtcTime value, ReadOnlySpan<byte> rawValue)
    {
        if (rawValue.Length != 8)
            throw new ArgumentException("IEC 61850 UTC-Time wire provenance requires exactly 8 bytes.", nameof(rawValue));

        return new MmsDataValue(MmsDataKind.UtcTime, value, rawValue: rawValue.ToArray());
    }

    public static MmsDataValue BinaryTime(ReadOnlySpan<byte> value)
        => new(MmsDataKind.BinaryTime, rawValue: value.ToArray());

    public static MmsDataValue Unknown(int tagNumber, ReadOnlySpan<byte> rawValue)
        => new(MmsDataKind.Unknown, rawValue: rawValue.ToArray(), unknownTagNumber: tagNumber);
}
