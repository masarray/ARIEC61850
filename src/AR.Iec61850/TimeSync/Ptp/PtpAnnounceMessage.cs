namespace AR.Iec61850.TimeSync.Ptp;

public sealed record PtpAnnounceMessage(
    PtpTimestamp OriginTimestamp,
    short CurrentUtcOffset,
    byte GrandmasterPriority1,
    byte GrandmasterClockClass,
    PtpClockAccuracy GrandmasterClockAccuracy,
    ushort GrandmasterOffsetScaledLogVariance,
    byte GrandmasterPriority2,
    ClockIdentity GrandmasterIdentity,
    ushort StepsRemoved,
    PtpTimeSource TimeSource);
