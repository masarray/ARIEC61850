using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.Monitoring;

public sealed record PtpObservedMessage(
    DateTimeOffset ObservedAt,
    PtpMessageType MessageType,
    byte DomainNumber,
    PtpPortIdentity SourcePortIdentity,
    ushort SequenceId,
    bool IsTwoStep,
    ushort? VlanId,
    ushort? OuterVlanId,
    bool IsPeerDelayMulticast);
