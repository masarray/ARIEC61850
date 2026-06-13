using AR.Iec61850.SampledValues;

namespace AR.Iec61850.Monitoring;

public sealed class ProcessBusStreamEvent
{
    public ProcessBusEventKind Kind { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public ushort? AppId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string StreamId { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public ushort? SampleCount { get; init; }
    public uint? StateNumber { get; init; }
    public uint? SequenceNumber { get; init; }
    public int ValueCount { get; init; }
    public int PayloadBytes { get; init; }
    public ProcessBusSequenceStatus SequenceStatus { get; init; } = ProcessBusSequenceStatus.Unknown;
    public bool IsBoundToScl { get; init; }
    public string ControlBlockReference { get; init; } = string.Empty;
    public int DecodedValueCount { get; init; }
    public IReadOnlyList<SampledValuesDecodedValue> DecodedValues { get; init; } = Array.Empty<SampledValuesDecodedValue>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public string Detail { get; init; } = string.Empty;
}
