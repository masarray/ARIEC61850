namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesPdu
{
    public IReadOnlyList<SampledValueAsdu> Asdus { get; init; } = Array.Empty<SampledValueAsdu>();
}
