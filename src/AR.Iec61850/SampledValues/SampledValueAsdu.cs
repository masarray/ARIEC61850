using AR.Iec61850.Mms;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValueAsdu
{
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public ushort SampleCount { get; init; }
    public uint ConfigurationRevision { get; init; } = 1;
    public Iec61850UtcTime? ReferenceTime { get; init; }
    public byte SampleSynchronization { get; init; } = 2;
    public ushort? SampleRate { get; init; }
    public ushort? SampleMode { get; init; }
    public byte[] SamplePayload { get; init; } = [];
}
