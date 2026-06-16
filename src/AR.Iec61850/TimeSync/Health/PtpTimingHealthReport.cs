using AR.Iec61850.TimeSync.Monitoring;

namespace AR.Iec61850.TimeSync.Health;

public sealed record PtpTimingHealthReport(
    DateTimeOffset EvaluatedAt,
    PtpHealthSeverity Severity,
    PtpMonitorSnapshot Snapshot,
    IReadOnlyList<PtpHealthCheckResult> Checks)
{
    public bool IsHealthy => Severity == PtpHealthSeverity.Ok;
}
