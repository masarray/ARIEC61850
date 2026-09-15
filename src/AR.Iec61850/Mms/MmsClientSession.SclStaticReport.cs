namespace AR.Iec61850.Mms;

public enum SclStaticReportActivationStepKind
{
    ReadRcb,
    ReserveUrcb,
    EnableReport,
    VerifyRcb,
    FallbackReserveBrcb
}

public static class SclStaticReportActivationPolicy
{
    public static IReadOnlyList<SclStaticReportActivationStepKind> BuildPrimary(
        bool buffered,
        IReadOnlyCollection<string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var steps = new List<SclStaticReportActivationStepKind>
        {
            SclStaticReportActivationStepKind.ReadRcb
        };

        if (!buffered && attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            steps.Add(SclStaticReportActivationStepKind.ReserveUrcb);

        steps.Add(SclStaticReportActivationStepKind.EnableReport);
        steps.Add(SclStaticReportActivationStepKind.VerifyRcb);
        steps.Add(SclStaticReportActivationStepKind.VerifyRcb);
        return steps;
    }

    public static IReadOnlyList<SclStaticReportActivationStepKind> BuildBrcbEnableFallback(
        bool buffered,
        IReadOnlyCollection<string> attributes)
        => buffered && attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase)
            ? new[]
            {
                SclStaticReportActivationStepKind.FallbackReserveBrcb,
                SclStaticReportActivationStepKind.EnableReport
            }
            : Array.Empty<SclStaticReportActivationStepKind>();
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Starts a persistent monitor for a static DataSet already trusted from SCL.
    /// The primary wire sequence is deliberately minimal: whole-RCB Read, optional
    /// URCB Resv=true, RptEna=true, then two whole-RCB verification Reads. BRCB
    /// ResvTms is a retry-only compatibility fallback when direct RptEna is rejected.
    /// No DataSet browsing/creation and no GI are performed unless explicitly requested.
    /// </summary>
    public async Task<MmsPersistentReportMonitorStartResult> StartStaticSclReportMonitorAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = false,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsReady || plan.ReportControl is null)
        {
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                Message = "Trusted-SCL static report activation requires a ready plan with selected RCB."
            };
        }

        if (plan.Mode == MmsReportSubscriptionPlanMode.DynamicDataSet)
        {
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                Message = "Trusted-SCL static report activation never creates or mutates a dynamic DataSet."
            };
        }

        var rcb = plan.ReportControl;
        var writes = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();
        var snapshots = new List<MmsReportRcbSnapshot>();
        var reservationTouched = false;
        var enabled = false;
        MmsPersistentReportMonitorSession? monitor = null;

        try
        {
            // Capture the whole RCB before touching ownership/enabling fields. This is the
            // only capability/readback evidence needed for the trusted static path.
            snapshots.Add(await CaptureReportControlSnapshotAsync(
                rcb,
                "scl-static-before-enable",
                cancellationToken).ConfigureAwait(false));

            // Register the receiver before any write so a very fast spontaneous report
            // cannot race ahead of the application-level monitor registration.
            monitor = new MmsPersistentReportMonitorSession(
                plan,
                rcb,
                rcb.DataSetReference,
                isDynamic: false,
                deleteDynamicDataSetOnStop: false,
                dataSetCreated: false,
                reservationTouched: false,
                enabledByThisClient: false);
            RegisterPersistentReportMonitor(monitor);

            if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(
                    rcb,
                    "Resv",
                    MmsDataValue.Boolean(true),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = reserve.IsSuccess;
                monitor.ReservationTouched = reservationTouched;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv=true was not accepted; direct RptEna will still be attempted once.");
            }

            var enable = await WriteReportAttributeAsync(
                rcb,
                "RptEna",
                MmsDataValue.Boolean(true),
                cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            enabled = enable.IsSuccess;

            // Some BRCBs require explicit reservation even though many accept ownership
            // implicitly through RptEna=true. Keep explicit ResvTms out of the primary wire
            // path and use it only after a real direct-enable rejection.
            if (!enabled &&
                rcb.Buffered &&
                rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                var reserveBrcb = await WriteReportAttributeAsync(
                    rcb,
                    "ResvTms",
                    MmsDataValue.Unsigned(60),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(reserveBrcb);
                reservationTouched = reserveBrcb.IsSuccess;
                monitor.ReservationTouched = reservationTouched;

                if (reserveBrcb.IsSuccess)
                {
                    var retryEnable = await WriteReportAttributeAsync(
                        rcb,
                        "RptEna",
                        MmsDataValue.Boolean(true),
                        cancellationToken).ConfigureAwait(false);
                    writes.Add(retryEnable);
                    enabled = retryEnable.IsSuccess;
                    if (enabled)
                        warnings.Add("BRCB required explicit ResvTms reservation after direct RptEna was rejected.");
                }
            }

            if (!enabled)
            {
                UnregisterPersistentReportMonitor(monitor);
                if (reservationTouched)
                {
                    var release = rcb.Buffered
                        ? await TryWriteReportAttributeForCleanupAsync(
                            rcb,
                            "ResvTms",
                            MmsDataValue.Unsigned(0),
                            CancellationToken.None).ConfigureAwait(false)
                        : await TryWriteReportAttributeForCleanupAsync(
                            rcb,
                            "Resv",
                            MmsDataValue.Boolean(false),
                            CancellationToken.None).ConfigureAwait(false);
                    writes.Add(release);
                }

                return new MmsPersistentReportMonitorStartResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    RcbSnapshots = snapshots,
                    Message = "RptEna=true was not accepted; trusted-SCL static report monitor was not started."
                };
            }

            monitor.EnabledByThisClient = true;

            // Two readbacks mirror the proven static activation flow and provide stable
            // ownership/enabled evidence without performing any directory discovery.
            snapshots.Add(await CaptureReportControlSnapshotAsync(
                rcb,
                "scl-static-after-enable-1",
                cancellationToken).ConfigureAwait(false));
            snapshots.Add(await CaptureReportControlSnapshotAsync(
                rcb,
                "scl-static-after-enable-2",
                cancellationToken).ConfigureAwait(false));

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportAttributeAsync(
                    rcb,
                    "GI",
                    MmsDataValue.Boolean(true),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("Explicit GI=true request was not accepted; spontaneous/integrity reporting remains armed.");
            }

            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = true,
                Session = monitor,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = snapshots,
                Message = $"Trusted-SCL static report monitor armed for {rcb.Reference}; no DataSet discovery/mutation or implicit GI was performed."
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            if (monitor is not null)
                UnregisterPersistentReportMonitor(monitor);

            if (enabled)
            {
                writes.Add(await TryWriteReportAttributeForCleanupAsync(
                    rcb,
                    "RptEna",
                    MmsDataValue.Boolean(false),
                    CancellationToken.None).ConfigureAwait(false));
            }

            if (reservationTouched)
            {
                writes.Add(rcb.Buffered
                    ? await TryWriteReportAttributeForCleanupAsync(
                        rcb,
                        "ResvTms",
                        MmsDataValue.Unsigned(0),
                        CancellationToken.None).ConfigureAwait(false)
                    : await TryWriteReportAttributeForCleanupAsync(
                        rcb,
                        "Resv",
                        MmsDataValue.Boolean(false),
                        CancellationToken.None).ConfigureAwait(false));
            }

            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = snapshots,
                Message = $"Trusted-SCL static report activation failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }
}
