namespace AR.Iec61850.TimeSync.Health;

public static class PtpSmpSynchPolicy
{
    public static SmpSynchValue Resolve(PtpTimingHealthReport report, bool allowLocalFallback = true)
    {
        if (report.IsHealthy)
            return SmpSynchValue.GlobalSynchronized;

        return allowLocalFallback && report.Snapshot.HasPtp
            ? SmpSynchValue.LocalSynchronized
            : SmpSynchValue.NotSynchronized;
    }
}
