namespace AR.Iec61850.TimeSync.Ptp;

public sealed record PtpBuildOptions
{
    public byte TransportSpecific { get; init; }
    public byte DomainNumber { get; init; }
    public PtpPortIdentity SourcePortIdentity { get; init; } = new(ClockIdentity.Empty, 1);
    public ushort SequenceId { get; init; }
    public long CorrectionField { get; init; }
    public sbyte LogMessageInterval { get; init; } = 0;
    public bool TwoStepFlag { get; init; } = true;
    public byte Priority1 { get; init; } = 128;
    public byte Priority2 { get; init; } = 128;
    public byte ClockClass { get; init; } = 248;
    public PtpClockAccuracy ClockAccuracy { get; init; } = PtpClockAccuracy.Unknown;
    public ushort OffsetScaledLogVariance { get; init; } = 0xFFFF;
    public ClockIdentity GrandmasterIdentity { get; init; } = ClockIdentity.Empty;
    public ushort StepsRemoved { get; init; }
    public PtpTimeSource TimeSource { get; init; } = PtpTimeSource.InternalOscillator;
    public PtpTimestamp Timestamp { get; init; } = PtpTimestamp.Zero;

    public PtpBuildOptions WithTimestampNow()
        => this with { Timestamp = PtpTimestamp.Now() };
}
