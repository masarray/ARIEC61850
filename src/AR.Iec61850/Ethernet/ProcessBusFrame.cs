namespace AR.Iec61850.Ethernet;

public sealed record ProcessBusFrame(
    EthernetFrame Ethernet,
    ushort AppId,
    ushort DeclaredLength,
    ushort Reserved1,
    ushort Reserved2,
    ReadOnlyMemory<byte> Apdu);
