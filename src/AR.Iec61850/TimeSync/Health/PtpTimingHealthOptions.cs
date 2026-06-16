namespace AR.Iec61850.TimeSync.Health;

public sealed record PtpTimingHealthOptions
{
    public byte? ExpectedDomainNumber { get; init; }
    public TimeSpan SourceTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public bool RequireAnnounce { get; init; } = true;
    public bool RequireSync { get; init; } = true;
    public bool RequireFollowUpForTwoStep { get; init; } = true;
    public bool RequirePeerDelayActivity { get; init; } = true;
    public int MaximumSequenceAnomalies { get; init; } = 0;
}
