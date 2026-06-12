namespace AR.Iec61850.Ethernet;

public sealed record EthernetFrame(
    MacAddress Destination,
    MacAddress Source,
    ushort EtherType,
    VlanTag? Vlan,
    ReadOnlyMemory<byte> Payload);
