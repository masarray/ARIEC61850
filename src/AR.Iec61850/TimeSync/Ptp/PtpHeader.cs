namespace AR.Iec61850.TimeSync.Ptp;

public sealed record PtpHeader(
    byte TransportSpecific,
    PtpMessageType MessageType,
    byte Version,
    ushort MessageLength,
    byte DomainNumber,
    ushort Flags,
    long CorrectionField,
    PtpPortIdentity SourcePortIdentity,
    ushort SequenceId,
    byte ControlField,
    sbyte LogMessageInterval)
{
    public bool IsTwoStep
        => (Flags & 0x0200) != 0;
}
