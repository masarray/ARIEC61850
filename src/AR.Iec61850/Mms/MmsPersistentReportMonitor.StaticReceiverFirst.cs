namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Static-DataSet activation path that closes the receiver-registration race present in the
    /// legacy persistent monitor start sequence. A fast IED is allowed to emit an
    /// InformationReport immediately after RptEna=true or GI=true, so the monitor must already
    /// be registered before either write is issued.
    ///
    /// This path never creates, rebinds or deletes a DataSet. It preserves the configured static
    /// DataSet/RCB authority supplied by the caller and performs only the minimum RCB ownership,
    /// enable and GI writes needed to start reporting.
    /// </summary>
    public async Task<MmsPersistentReportMonitorStartResult> StartStaticPersistentReportMonitorReceiverFirstAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = true,
        bool deleteDynamicDataSetOnStop = false,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Mode != MmsReportSubscriptionPlanMode.StaticDataSet || !plan.IsReady || plan.ReportControl == null)
        {
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                Message = "Receiver-first static monitor requires a ready StaticDataSet plan with selected RCB."
            };
        }

        var rcb = plan.ReportControl;
        var writes = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();
        var rcbSnapshots = new List<MmsReportRcbSnapshot>();
        var dataSetSnapshots = new List<MmsReportDataSetSnapshot>();
        var originalDataSetReference = rcb.DataSetReference;
        var reservationAttempted = false;
        var reservationTouched = false;
        var enableAttempted = false;
        MmsPersistentReportMonitorSession? monitor = null;

        try
        {
            var beforeSnapshot = await CaptureReportControlSnapshotAsync(
                rcb,
                "before-static-receiver-first-start",
                cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(beforeSnapshot);

            if (!string.IsNullOrWhiteSpace(plan.DataSetReference))
            {
                var dataSetBefore = await CaptureDataSetSnapshotAsync(
                    plan.DataSetReference,
                    plan.Members,
                    "before-static-receiver-first-start",
                    directory,
                    cancellationToken).ConfigureAwait(false);
                dataSetSnapshots.Add(dataSetBefore);
            }

            if (rcb.Buffered && rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add(
                    "BRCB ResvTms pre-reserve was skipped. Receiver-first static activation keeps the existing relay-compatible RptEna ownership path.");
            }
            else if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                reservationAttempted = true;
                var reserve = await WriteReportAttributeAsync(
                    rcb,
                    "Resv",
                    MmsDataValue.Boolean(true),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = reserve.IsSuccess;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv write failed. Continuing only if RptEna=true is accepted by the IED.");
            }

            // P0 ordering invariant: register first. From this point onward an unsolicited or GI
            // InformationReport has an unambiguous active monitor to route into.
            monitor = new MmsPersistentReportMonitorSession(
                plan,
                rcb,
                originalDataSetReference,
                isDynamic: false,
                deleteDynamicDataSetOnStop,
                dataSetCreated: false,
                reservationTouched,
                enabledByThisClient: false);
            RegisterPersistentReportMonitor(monitor);
            warnings.Add("InformationReport receiver registered before RptEna/GI (receiver-first static activation).");

            enableAttempted = true;
            var enable = await WriteReportAttributeAsync(
                rcb,
                "RptEna",
                MmsDataValue.Boolean(true),
                cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            monitor.EnabledByThisClient = enable.IsSuccess;

            if (!enable.IsSuccess)
            {
                var cleanup = await CleanupFailedStaticReceiverFirstStartAsync(
                    monitor,
                    enableAttempted,
                    reservationAttempted || reservationTouched).ConfigureAwait(false);
                writes.AddRange(cleanup.Steps);
                warnings.AddRange(cleanup.Warnings);

                return new MmsPersistentReportMonitorStartResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    RcbSnapshots = rcbSnapshots,
                    DataSetSnapshots = dataSetSnapshots,
                    Message = "RptEna=true failed after the static receiver was registered; receiver/RCB state was rolled back best-effort."
                };
            }

            var afterEnableSnapshot = await CaptureReportControlSnapshotAsync(
                rcb,
                "after-static-receiver-first-enable",
                cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterEnableSnapshot);

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportAttributeAsync(
                    rcb,
                    "GI",
                    MmsDataValue.Boolean(true),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed or is not supported by this RCB. Receiver remains active for spontaneous/integrity reports.");
            }

            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = true,
                Session = monitor,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = rcbSnapshots,
                DataSetSnapshots = dataSetSnapshots,
                Message = $"Receiver-first static persistent report monitor started for {rcb.Reference}. Receiver was registered before RptEna/GI."
            };
        }
        catch (OperationCanceledException)
        {
            if (monitor is not null)
                await CleanupFailedStaticReceiverFirstStartAsync(
                    monitor,
                    enableAttempted,
                    reservationAttempted || reservationTouched).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            if (monitor is not null)
            {
                var cleanup = await CleanupFailedStaticReceiverFirstStartAsync(
                    monitor,
                    enableAttempted,
                    reservationAttempted || reservationTouched).ConfigureAwait(false);
                writes.AddRange(cleanup.Steps);
                warnings.AddRange(cleanup.Warnings);
            }

            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = rcbSnapshots,
                DataSetSnapshots = dataSetSnapshots,
                Message = $"Receiver-first static persistent report monitor start failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private async Task<(IReadOnlyList<MmsReportAttributeWriteStep> Steps, IReadOnlyList<string> Warnings)>
        CleanupFailedStaticReceiverFirstStartAsync(
            MmsPersistentReportMonitorSession monitor,
            bool enableAttempted,
            bool reservationMayHaveBeenTouched)
    {
        var steps = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();

        // Keep the receiver registered while disabling the RCB. If the IED emits a final report
        // during disable, it is still routed deterministically and then discarded when the failed
        // monitor is unregistered below.
        if (enableAttempted)
        {
            var disable = await TryWriteReportAttributeForCleanupAsync(
                monitor.ReportControl,
                "RptEna",
                MmsDataValue.Boolean(false),
                CancellationToken.None).ConfigureAwait(false);
            steps.Add(disable);
            if (!disable.IsSuccess)
                warnings.Add("Failed-start cleanup could not prove RptEna=false. Re-read RCB state on a fresh association before another activation attempt.");
        }

        if (reservationMayHaveBeenTouched && !monitor.ReportControl.Buffered)
        {
            var release = await TryWriteReportAttributeForCleanupAsync(
                monitor.ReportControl,
                "Resv",
                MmsDataValue.Boolean(false),
                CancellationToken.None).ConfigureAwait(false);
            steps.Add(release);
            if (!release.IsSuccess)
                warnings.Add("Failed-start cleanup could not prove URCB Resv=false. Re-read ownership before another activation attempt.");
        }

        UnregisterPersistentReportMonitor(monitor);
        monitor.IsStopped = true;
        return (steps, warnings);
    }
}
