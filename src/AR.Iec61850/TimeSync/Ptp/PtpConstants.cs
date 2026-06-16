namespace AR.Iec61850.TimeSync.Ptp;

/// <summary>
/// Well-known constants for IEEE 1588 / IEC 61588 PTPv2 over Ethernet.
/// </summary>
public static class PtpConstants
{
    public const ushort EtherType = 0x88F7;
    public const ushort VlanEtherType = 0x8100;
    public const ushort QinQEtherType = 0x88A8;
    public const byte Version2 = 2;
    public const int HeaderLength = 34;
    public const int ClockIdentityLength = 8;
    public const int PortIdentityLength = 10;

    public static ReadOnlySpan<byte> GeneralMulticastMac => new byte[] { 0x01, 0x1B, 0x19, 0x00, 0x00, 0x00 };
    public static ReadOnlySpan<byte> PeerDelayMulticastMac => new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x0E };
}
