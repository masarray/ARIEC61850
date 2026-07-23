using System.Buffers.Binary;

namespace AR.Iec61850.SampledValues;

/// <summary>
/// Compact IEC 61850 quality bit helper used by Sampled Values payload publishers.
/// It exposes the common simulation controls while preserving the standardized network encoding.
/// </summary>
public readonly record struct SampledValueQuality(
    SampledValueValidity Validity,
    bool Overflow = false,
    bool OutOfRange = false,
    bool BadReference = false,
    bool Oscillatory = false,
    bool Failure = false,
    bool OldData = false,
    bool Inconsistent = false,
    bool Inaccurate = false,
    bool Test = false,
    bool OperatorBlocked = false)
{
    public static SampledValueQuality Good { get; } = new(SampledValueValidity.Good);
    public static SampledValueQuality Invalid { get; } = new(SampledValueValidity.Invalid);
    public static SampledValueQuality Questionable { get; } = new(SampledValueValidity.Questionable);
    public static SampledValueQuality TestGood { get; } = new(SampledValueValidity.Good, Test: true);
    public static SampledValueQuality OldDataGood { get; } = new(SampledValueValidity.Good, OldData: true);
    public static SampledValueQuality OperatorBlockedGood { get; } = new(SampledValueValidity.Good, OperatorBlocked: true);

    public uint ToUInt32()
    {
        var value = (uint)Validity;
        if (Overflow) value |= 1u << 2;
        if (OutOfRange) value |= 1u << 3;
        if (BadReference) value |= 1u << 4;
        if (Oscillatory) value |= 1u << 5;
        if (Failure) value |= 1u << 6;
        if (OldData) value |= 1u << 7;
        if (Inconsistent) value |= 1u << 8;
        if (Inaccurate) value |= 1u << 9;
        if (Test) value |= 1u << 11;
        if (OperatorBlocked) value |= 1u << 12;
        return value;
    }

    public byte[] ToBytes(int width = 4)
    {
        if (width <= 0)
            return [];

        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(encoded, ToUInt32());

        if (width == 4)
            return encoded.ToArray();

        var result = new byte[width];
        encoded[..Math.Min(4, width)].CopyTo(result);
        return result;
    }

    public static SampledValueQuality FromUInt32(uint encoded)
    {
        var validity = (SampledValueValidity)(encoded & 0x03);
        return new SampledValueQuality(
            validity,
            Overflow: (encoded & (1u << 2)) != 0,
            OutOfRange: (encoded & (1u << 3)) != 0,
            BadReference: (encoded & (1u << 4)) != 0,
            Oscillatory: (encoded & (1u << 5)) != 0,
            Failure: (encoded & (1u << 6)) != 0,
            OldData: (encoded & (1u << 7)) != 0,
            Inconsistent: (encoded & (1u << 8)) != 0,
            Inaccurate: (encoded & (1u << 9)) != 0,
            Test: (encoded & (1u << 11)) != 0,
            OperatorBlocked: (encoded & (1u << 12)) != 0);
    }
}

public enum SampledValueValidity : uint
{
    Good = 0,
    Invalid = 1,
    Reserved = 2,
    Questionable = 3
}
