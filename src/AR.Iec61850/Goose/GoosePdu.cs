using AR.Iec61850.Mms;

namespace AR.Iec61850.Goose;

public sealed class GoosePdu
{
    public string GoCbRef { get; init; } = string.Empty;
    public uint TimeAllowedToLiveMilliseconds { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string GoId { get; init; } = string.Empty;
    public Iec61850UtcTime Timestamp { get; init; } = new(DateTimeOffset.UnixEpoch, 0);
    public uint StateNumber { get; init; } = 1;
    public uint SequenceNumber { get; init; }
    public bool Test { get; init; }
    public uint ConfigurationRevision { get; init; } = 1;
    public bool NeedsCommissioning { get; init; }
    public IReadOnlyList<MmsDataValue> Values { get; init; } = Array.Empty<MmsDataValue>();
}
