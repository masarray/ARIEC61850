using AR.Iec61850.Ethernet;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesFrame
{
    public MacAddress Destination { get; init; }
    public MacAddress Source { get; init; }
    public VlanTag? Vlan { get; init; }
    public ushort AppId { get; init; }
    public ushort Reserved1 { get; init; }
    public ushort Reserved2 { get; init; }
    public SampledValuesPdu Pdu { get; init; } = new();
}
