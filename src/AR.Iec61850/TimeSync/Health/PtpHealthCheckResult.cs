namespace AR.Iec61850.TimeSync.Health;

public sealed record PtpHealthCheckResult(string CheckId, PtpHealthSeverity Severity, string Message);
