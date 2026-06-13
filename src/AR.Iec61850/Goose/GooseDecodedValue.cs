using AR.Iec61850.Mms;

namespace AR.Iec61850.Goose;

public sealed class GooseDecodedValue
{
    public int Index { get; init; }
    public string SignalReference { get; init; } = string.Empty;
    public string Fc { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string BType { get; init; } = string.Empty;
    public MmsDataValue Value { get; init; } = MmsDataValue.Unknown(0, ReadOnlySpan<byte>.Empty);
    public string DisplayValue { get; init; } = string.Empty;
    public bool IsChanged { get; init; }
    public string PreviousDisplayValue { get; init; } = string.Empty;
    public bool IsMappedToScl => !string.IsNullOrWhiteSpace(SignalReference);
}
