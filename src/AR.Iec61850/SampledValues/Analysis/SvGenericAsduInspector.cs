namespace AR.Iec61850.SampledValues.Analysis;

/// <summary>
/// Generic, vendor-neutral view of one Sampled Values ASDU.
/// It separates fields observed on the wire from semantic dataset interpretation.
/// </summary>
public sealed record SvGenericAsduInspection
{
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public ushort SampleCount { get; init; }
    public uint ConfigurationRevision { get; init; }
    public bool HasReferenceTime { get; init; }
    public byte SampleSynchronization { get; init; }
    public ushort? SampleRate { get; init; }
    public ushort? SampleMode { get; init; }
    public SvGenericPayloadInspection Payload { get; init; } = new();

    public string MappingState => string.IsNullOrWhiteSpace(DataSetReference)
        ? "Dataset reference not present · semantic mapping unresolved"
        : "Dataset reference observed · import or bind SCL to resolve ordered semantics";

    public string OptionalFieldSummary
    {
        get
        {
            var fields = new List<string>();
            if (HasReferenceTime)
                fields.Add("refrTm");
            if (SampleRate.HasValue)
                fields.Add("smpRate");
            if (SampleMode.HasValue)
                fields.Add("smpMod");
            return fields.Count == 0 ? "No optional ASDU fields observed" : string.Join(", ", fields);
        }
    }
}

public static class SvGenericAsduInspector
{
    public static SvGenericAsduInspection Inspect(SampledValueAsdu asdu)
    {
        ArgumentNullException.ThrowIfNull(asdu);

        return new SvGenericAsduInspection
        {
            SvId = asdu.SvId,
            DataSetReference = asdu.DataSetReference,
            SampleCount = asdu.SampleCount,
            ConfigurationRevision = asdu.ConfigurationRevision,
            HasReferenceTime = asdu.ReferenceTime is not null,
            SampleSynchronization = asdu.SampleSynchronization,
            SampleRate = asdu.SampleRate,
            SampleMode = asdu.SampleMode,
            Payload = SvGenericPayloadInspector.Inspect(asdu.SamplePayload)
        };
    }
}
