using System.Buffers.Binary;

namespace AR.Iec61850.SampledValues.Measurements;

public enum SvQualityValidity
{
    Good,
    Reserved,
    Invalid,
    Questionable
}

public enum SvQualitySeverity
{
    Good,
    Information,
    Warning,
    Bad,
    Unknown
}

public enum SvQualityWordPlacement
{
    AllZero,
    TwoByteWord,
    HighWord,
    LowWord,
    Ambiguous
}

/// <summary>
/// Semantic interpretation of the IEC 61850 Quality bit field carried beside a sampled value.
/// Bit 13 is treated as the installed-base 9-2LE Derived extension only when explicitly enabled.
/// </summary>
public sealed record SvQualityState
{
    public ushort Word { get; init; }
    public SvQualityWordPlacement Placement { get; init; }
    public SvQualityValidity Validity { get; init; }
    public bool Overflow { get; init; }
    public bool OutOfRange { get; init; }
    public bool BadReference { get; init; }
    public bool Oscillatory { get; init; }
    public bool Failure { get; init; }
    public bool OldData { get; init; }
    public bool Inconsistent { get; init; }
    public bool Inaccurate { get; init; }
    public bool IsSubstituted { get; init; }
    public bool Test { get; init; }
    public bool OperatorBlocked { get; init; }
    public bool Derived { get; init; }
    public bool HasReservedBits { get; init; }
    public bool IsEncodingAmbiguous { get; init; }
    public SvQualitySeverity Severity { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();

    public bool IsGood => Severity == SvQualitySeverity.Good;
    public bool IsStrictlyUsable => Validity == SvQualityValidity.Good &&
                                    !Failure &&
                                    !BadReference &&
                                    !OldData &&
                                    !Inconsistent &&
                                    !HasReservedBits &&
                                    !IsEncodingAmbiguous;
}

public static class SvQualityDecoder
{
    private const ushort StandardMask = 0x1FFF;
    private const ushort LegacyDerivedMask = 0x2000;
    private const ushort ReservedMask = 0xC000;

    public static bool TryDecodeHex(string? rawHex, out SvQualityState state, bool allowLegacyDerived = true)
    {
        state = Unknown("Quality bytes are unavailable.");
        if (string.IsNullOrWhiteSpace(rawHex))
            return false;

        try
        {
            var bytes = Convert.FromHexString(rawHex.Trim());
            state = DecodeNetworkBytes(bytes, allowLegacyDerived);
            return true;
        }
        catch (FormatException)
        {
            state = Unknown("Quality bytes are not valid hexadecimal data.");
            return false;
        }
    }

    public static SvQualityState DecodeNetworkBytes(ReadOnlySpan<byte> bytes, bool allowLegacyDerived = true)
    {
        if (bytes.Length == 2)
            return DecodeWord(BinaryPrimitives.ReadUInt16BigEndian(bytes), SvQualityWordPlacement.TwoByteWord, allowLegacyDerived);

        if (bytes.Length != 4)
            return Unknown($"Expected two or four quality bytes, got {bytes.Length}.");

        var high = BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]);
        var low = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);

        if (high == 0 && low == 0)
            return DecodeWord(0, SvQualityWordPlacement.AllZero, allowLegacyDerived);

        var highPlausible = IsPlausible(high, allowLegacyDerived);
        var lowPlausible = IsPlausible(low, allowLegacyDerived);

        if (high != 0 && low == 0 && highPlausible)
            return DecodeWord(high, SvQualityWordPlacement.HighWord, allowLegacyDerived);
        if (low != 0 && high == 0 && lowPlausible)
            return DecodeWord(low, SvQualityWordPlacement.LowWord, allowLegacyDerived);
        if (highPlausible && !lowPlausible)
            return DecodeWord(high, SvQualityWordPlacement.HighWord, allowLegacyDerived);
        if (lowPlausible && !highPlausible)
            return DecodeWord(low, SvQualityWordPlacement.LowWord, allowLegacyDerived);

        if (high == low && highPlausible)
            return DecodeWord(high, SvQualityWordPlacement.HighWord, allowLegacyDerived);

        return new SvQualityState
        {
            Placement = SvQualityWordPlacement.Ambiguous,
            Validity = SvQualityValidity.Reserved,
            Severity = SvQualitySeverity.Unknown,
            IsEncodingAmbiguous = true,
            Summary = $"Quality placement ambiguous (high 0x{high:X4}, low 0x{low:X4})"
        };
    }

    public static SvQualityState DecodeWord(
        ushort word,
        SvQualityWordPlacement placement = SvQualityWordPlacement.TwoByteWord,
        bool allowLegacyDerived = true)
    {
        var validity = (word & 0x0003) switch
        {
            0 => SvQualityValidity.Good,
            1 => SvQualityValidity.Reserved,
            2 => SvQualityValidity.Invalid,
            _ => SvQualityValidity.Questionable
        };

        var flags = new List<string>();
        AddFlag(flags, word, 2, "overflow");
        AddFlag(flags, word, 3, "out-of-range");
        AddFlag(flags, word, 4, "bad-reference");
        AddFlag(flags, word, 5, "oscillatory");
        AddFlag(flags, word, 6, "failure");
        AddFlag(flags, word, 7, "old-data");
        AddFlag(flags, word, 8, "inconsistent");
        AddFlag(flags, word, 9, "inaccurate");
        AddFlag(flags, word, 10, "substituted");
        AddFlag(flags, word, 11, "test");
        AddFlag(flags, word, 12, "operator-blocked");
        if (allowLegacyDerived)
            AddFlag(flags, word, 13, "derived");

        var hasReservedBits = (word & ReservedMask) != 0 ||
                              (!allowLegacyDerived && (word & LegacyDerivedMask) != 0);
        if (hasReservedBits)
            flags.Add("reserved-bits");

        var state = new SvQualityState
        {
            Word = word,
            Placement = placement,
            Validity = validity,
            Overflow = IsSet(word, 2),
            OutOfRange = IsSet(word, 3),
            BadReference = IsSet(word, 4),
            Oscillatory = IsSet(word, 5),
            Failure = IsSet(word, 6),
            OldData = IsSet(word, 7),
            Inconsistent = IsSet(word, 8),
            Inaccurate = IsSet(word, 9),
            IsSubstituted = IsSet(word, 10),
            Test = IsSet(word, 11),
            OperatorBlocked = IsSet(word, 12),
            Derived = allowLegacyDerived && IsSet(word, 13),
            HasReservedBits = hasReservedBits,
            ActiveFlags = flags.ToArray()
        };

        var severity = ResolveSeverity(state);
        return state with
        {
            Severity = severity,
            Summary = BuildSummary(state.Validity, state.ActiveFlags)
        };
    }

    private static SvQualitySeverity ResolveSeverity(SvQualityState state)
    {
        if (state.HasReservedBits ||
            state.Validity is SvQualityValidity.Invalid or SvQualityValidity.Reserved ||
            state.Failure ||
            state.BadReference ||
            state.Inconsistent)
            return SvQualitySeverity.Bad;

        if (state.Validity == SvQualityValidity.Questionable ||
            state.Overflow ||
            state.OutOfRange ||
            state.Oscillatory ||
            state.OldData ||
            state.Inaccurate ||
            state.IsSubstituted ||
            state.Test ||
            state.OperatorBlocked)
            return SvQualitySeverity.Warning;

        if (state.Derived)
            return SvQualitySeverity.Information;

        return SvQualitySeverity.Good;
    }

    private static bool IsPlausible(ushort word, bool allowLegacyDerived)
    {
        var allowedMask = (ushort)(StandardMask | (allowLegacyDerived ? LegacyDerivedMask : 0));
        return (word & ~allowedMask) == 0;
    }

    private static bool IsSet(ushort word, int bit) => (word & (1 << bit)) != 0;

    private static void AddFlag(ICollection<string> flags, ushort word, int bit, string name)
    {
        if (IsSet(word, bit))
            flags.Add(name);
    }

    private static string BuildSummary(SvQualityValidity validity, IReadOnlyCollection<string> flags)
    {
        var validityText = validity.ToString();
        return flags.Count == 0
            ? validityText
            : $"{validityText} · {string.Join(", ", flags)}";
    }

    private static SvQualityState Unknown(string reason) => new()
    {
        Placement = SvQualityWordPlacement.Ambiguous,
        Validity = SvQualityValidity.Reserved,
        Severity = SvQualitySeverity.Unknown,
        IsEncodingAmbiguous = true,
        Summary = reason
    };
}
