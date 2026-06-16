namespace AR.Iec61850.TimeSync.Ptp;

public sealed record PtpFrame(
    PtpHeader Header,
    PtpTimestamp? Timestamp,
    PtpAnnounceMessage? Announce,
    ReadOnlyMemory<byte> RawMessage,
    ReadOnlyMemory<byte> RawBody,
    ushort? VlanId = null,
    ushort? OuterVlanId = null,
    bool IsPeerDelayMulticast = false)
{
    public bool IsAnnounce => Header.MessageType == PtpMessageType.Announce && Announce is not null;
}
