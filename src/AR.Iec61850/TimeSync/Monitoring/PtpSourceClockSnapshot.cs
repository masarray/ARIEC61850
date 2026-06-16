using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.Monitoring;

public sealed record PtpSourceClockSnapshot(
    byte DomainNumber,
    PtpPortIdentity SourcePortIdentity,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyDictionary<PtpMessageType, int> MessageCounts,
    IReadOnlyDictionary<PtpMessageType, ushort> LastSequenceIds,
    int SequenceAnomalyCount,
    ushort? VlanId,
    ushort? OuterVlanId)
{
    public int Count(PtpMessageType messageType)
        => MessageCounts.TryGetValue(messageType, out var count) ? count : 0;
}
