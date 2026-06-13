namespace AR.Iec61850.Transports;

public sealed class ProcessBusCapturedFrame
{
    public DateTimeOffset Timestamp { get; init; }
    public ReadOnlyMemory<byte> Frame { get; init; } = ReadOnlyMemory<byte>.Empty;
    public string Source { get; init; } = string.Empty;
}
