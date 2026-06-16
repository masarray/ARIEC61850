using AR.Iec61850.TimeSync.Monitoring;
using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.Health;

public sealed class PtpTimingHealthValidator
{
    public PtpTimingHealthReport Evaluate(PtpMonitorSnapshot snapshot, PtpTimingHealthOptions? options = null)
    {
        options ??= new PtpTimingHealthOptions();
        var now = snapshot.CapturedAt;
        var checks = new List<PtpHealthCheckResult>();

        if (!snapshot.HasPtp)
        {
            checks.Add(new PtpHealthCheckResult("ptp.visibility", PtpHealthSeverity.Fail, "No valid PTP frame has been observed on the selected adapter."));
            return BuildReport(now, snapshot, checks);
        }

        checks.Add(new PtpHealthCheckResult("ptp.visibility", PtpHealthSeverity.Ok, $"Observed {snapshot.ValidPtpFrames} valid PTP frame(s)."));

        var activeSources = snapshot.Sources.Where(s => now - s.LastSeenAt <= options.SourceTimeout).ToArray();
        if (activeSources.Length == 0)
            checks.Add(new PtpHealthCheckResult("ptp.liveness", PtpHealthSeverity.Fail, $"No PTP source has been seen within {options.SourceTimeout.TotalSeconds:0.#} s."));
        else
            checks.Add(new PtpHealthCheckResult("ptp.liveness", PtpHealthSeverity.Ok, $"{activeSources.Length} active PTP source(s) are visible."));

        if (options.ExpectedDomainNumber is { } expectedDomain)
        {
            var matching = activeSources.Where(s => s.DomainNumber == expectedDomain).ToArray();
            if (matching.Length == 0)
                checks.Add(new PtpHealthCheckResult("ptp.domain", PtpHealthSeverity.Fail, $"No active source is using expected PTP domain {expectedDomain}."));
            else
                checks.Add(new PtpHealthCheckResult("ptp.domain", PtpHealthSeverity.Ok, $"PTP domain {expectedDomain} is visible."));
        }

        var selected = SelectBestSource(activeSources, options.ExpectedDomainNumber);
        if (selected is null)
            return BuildReport(now, snapshot, checks);

        if (options.RequireAnnounce)
            AddMessagePresenceCheck(checks, selected, PtpMessageType.Announce, "ptp.announce", "Announce");
        if (options.RequireSync)
            AddMessagePresenceCheck(checks, selected, PtpMessageType.Sync, "ptp.sync", "Sync");
        if (options.RequireFollowUpForTwoStep)
            AddMessagePresenceCheck(checks, selected, PtpMessageType.FollowUp, "ptp.followup", "Follow_Up");
        if (options.RequirePeerDelayActivity)
        {
            var pdelayCount = selected.Count(PtpMessageType.PdelayReq) + selected.Count(PtpMessageType.PdelayResp) + selected.Count(PtpMessageType.PdelayRespFollowUp);
            checks.Add(pdelayCount > 0
                ? new PtpHealthCheckResult("ptp.pdelay", PtpHealthSeverity.Ok, "Peer-delay activity is visible.")
                : new PtpHealthCheckResult("ptp.pdelay", PtpHealthSeverity.Warning, "No Pdelay activity is visible. Some power-utility profiles expect peer-delay behavior."));
        }

        if (selected.SequenceAnomalyCount > options.MaximumSequenceAnomalies)
        {
            checks.Add(new PtpHealthCheckResult(
                "ptp.sequence",
                PtpHealthSeverity.Warning,
                $"Detected {selected.SequenceAnomalyCount} PTP sequence anomaly/anomalies for {selected.SourcePortIdentity}."));
        }
        else
        {
            checks.Add(new PtpHealthCheckResult("ptp.sequence", PtpHealthSeverity.Ok, "No PTP sequence anomaly above threshold."));
        }

        return BuildReport(now, snapshot, checks);
    }

    private static PtpSourceClockSnapshot? SelectBestSource(IReadOnlyList<PtpSourceClockSnapshot> sources, byte? expectedDomain)
    {
        var candidates = expectedDomain.HasValue ? sources.Where(s => s.DomainNumber == expectedDomain.Value) : sources;
        return candidates
            .OrderByDescending(s => s.Count(PtpMessageType.Announce))
            .ThenByDescending(s => s.Count(PtpMessageType.Sync))
            .ThenByDescending(s => s.LastSeenAt)
            .FirstOrDefault();
    }

    private static void AddMessagePresenceCheck(List<PtpHealthCheckResult> checks, PtpSourceClockSnapshot source, PtpMessageType type, string id, string label)
    {
        var count = source.Count(type);
        checks.Add(count > 0
            ? new PtpHealthCheckResult(id, PtpHealthSeverity.Ok, $"{label} messages are visible from {source.SourcePortIdentity}.")
            : new PtpHealthCheckResult(id, PtpHealthSeverity.Fail, $"No {label} message is visible from selected PTP source {source.SourcePortIdentity}."));
    }

    private static PtpTimingHealthReport BuildReport(DateTimeOffset evaluatedAt, PtpMonitorSnapshot snapshot, IReadOnlyList<PtpHealthCheckResult> checks)
    {
        var severity = checks.Any(c => c.Severity == PtpHealthSeverity.Fail)
            ? PtpHealthSeverity.Fail
            : checks.Any(c => c.Severity == PtpHealthSeverity.Warning)
                ? PtpHealthSeverity.Warning
                : PtpHealthSeverity.Ok;

        return new PtpTimingHealthReport(evaluatedAt, severity, snapshot, checks.ToArray());
    }
}
