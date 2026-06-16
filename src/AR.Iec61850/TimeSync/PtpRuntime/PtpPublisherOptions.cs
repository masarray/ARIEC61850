
using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.PtpRuntime;

public sealed record PtpPublisherOptions
{
    public byte DomainNumber { get; init; }
    public byte[] SourceMac { get; init; } = new byte[] { 0x02, 0x00, 0x00, 0xFF, 0xFE, 0x00 };
    public ushort? VlanId { get; init; }
    public byte VlanPriority { get; init; } = 4;
    public ClockIdentity ClockIdentity { get; init; } = ClockIdentity.Parse("02:00:00:FF:FE:00:00:01");
    public ushort PortNumber { get; init; } = 1;
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FollowUpDelay { get; init; } = TimeSpan.FromMilliseconds(2);
    public bool TwoStepClock { get; init; } = true;
    public bool RespondToPeerDelay { get; init; } = true;
    public byte Priority1 { get; init; } = 128;
    public byte Priority2 { get; init; } = 128;
    public byte ClockClass { get; init; } = 248;
    public PtpClockAccuracy ClockAccuracy { get; init; } = PtpClockAccuracy.Unknown;
    public ushort OffsetScaledLogVariance { get; init; } = 0xFFFF;
    public PtpTimeSource TimeSource { get; init; } = PtpTimeSource.InternalOscillator;
    public short CurrentUtcOffset { get; init; } = 37;

    public PtpPortIdentity SourcePortIdentity => new(ClockIdentity, PortNumber);
}
