using AR.Iec61850.Ethernet;

namespace AR.Iec61850.Goose;

public sealed class GooseFrame
{
    public MacAddress Destination { get; init; }
    public MacAddress Source { get; init; }
    public VlanTag? Vlan { get; init; }
    public ushort AppId { get; init; }
    public ushort Reserved1 { get; init; }
    public ushort Reserved2 { get; init; }
    public GoosePdu Pdu { get; init; } = new();
}
