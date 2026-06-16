namespace AR.Iec61850.TimeSync.Ptp;

public readonly record struct PtpPortIdentity(ClockIdentity ClockIdentity, ushort PortNumber)
{
    public override string ToString()
        => $"{ClockIdentity}/{PortNumber}";
}
