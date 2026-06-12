using AR.Iec61850.Ethernet;

namespace AR.Iec61850.Transports.Npcap;

public sealed class NpcapAdapterInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public MacAddress? MacAddress { get; init; }
}
