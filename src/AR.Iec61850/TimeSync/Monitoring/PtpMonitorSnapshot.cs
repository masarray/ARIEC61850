namespace AR.Iec61850.TimeSync.Monitoring;

public sealed record PtpMonitorSnapshot(
    DateTimeOffset CapturedAt,
    int TotalFramesObserved,
    int ValidPtpFrames,
    int InvalidPtpFrames,
    IReadOnlyList<PtpObservedMessage> RecentMessages,
    IReadOnlyList<PtpSourceClockSnapshot> Sources)
{
    public bool HasPtp => ValidPtpFrames > 0;
}
