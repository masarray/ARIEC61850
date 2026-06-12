namespace AR.Iec61850.Monitoring;

public sealed class ProcessBusStreamSummary
{
    public ProcessBusEventKind Kind { get; init; }
    public ushort AppId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string StreamId { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public int PacketCount { get; private set; }
    public ushort? FirstSampleCount { get; private set; }
    public ushort? LastSampleCount { get; private set; }
    public uint? LastStateNumber { get; private set; }
    public uint? LastSequenceNumber { get; private set; }

    public void Record(ushort? sampleCount, uint? stateNumber, uint? sequenceNumber)
    {
        PacketCount++;

        if (sampleCount.HasValue)
        {
            FirstSampleCount ??= sampleCount;
            LastSampleCount = sampleCount;
        }

        if (stateNumber.HasValue)
            LastStateNumber = stateNumber;

        if (sequenceNumber.HasValue)
            LastSequenceNumber = sequenceNumber;
    }
}
