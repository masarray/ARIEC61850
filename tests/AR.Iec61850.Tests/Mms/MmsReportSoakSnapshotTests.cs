using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportSoakSnapshotTests
{
    [Fact]
    public void Soak_snapshot_summary_contains_core_runtime_counters()
    {
        var snapshot = new MmsReportSoakSnapshot
        {
            CapturedAt = new DateTimeOffset(2026, 6, 12, 21, 0, 0, TimeSpan.Zero),
            ElapsedSeconds = 60,
            ReportCount = 4,
            ValueCount = 6,
            PollReadCount = 56,
            PollReadSuccessCount = 56,
            PollReadFailureCount = 0,
            PendingConfirmedOperationCount = 0,
            QueuedInformationReportCount = 0,
            LastReceiveRoutingSummary = "Receive pump completed ConfirmedResponse for invokeID=1."
        };

        Assert.Contains("elapsed=60", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reports=4", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("poll=56/56", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending=0", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
