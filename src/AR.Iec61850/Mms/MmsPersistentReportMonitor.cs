namespace AR.Iec61850.Mms;

public sealed class MmsPersistentReportMonitorSession
{
    internal MmsPersistentReportMonitorSession(
        MmsReportSubscriptionPlan plan,
        MmsReportControlCandidate reportControl,
        string originalDataSetReference,
        bool isDynamic,
        bool deleteDynamicDataSetOnStop,
        bool dataSetCreated,
        bool reservationTouched,
        bool enabledByThisClient)
    {
        Plan = plan;
        ReportControl = reportControl;
        OriginalDataSetReference = originalDataSetReference;
        IsDynamic = isDynamic;
        DeleteDynamicDataSetOnStop = deleteDynamicDataSetOnStop;
        DataSetCreated = dataSetCreated;
        ReservationTouched = reservationTouched;
        EnabledByThisClient = enabledByThisClient;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public MmsReportSubscriptionPlan Plan { get; }
    public MmsReportControlCandidate ReportControl { get; }
    public string OriginalDataSetReference { get; }
    public bool IsDynamic { get; }
    public bool DeleteDynamicDataSetOnStop { get; }
    public bool DataSetCreated { get; internal set; }
    public bool ReservationTouched { get; internal set; }
    public bool EnabledByThisClient { get; internal set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset LastReportAt { get; internal set; }
    public int ReportCount { get; internal set; }
    public int PollReadCount { get; internal set; }
    public bool IsStopped { get; internal set; }

    public string Summary =>
        $"persistent report monitor: rcb={ReportControl.Reference}, dataset={Plan.DataSetReference}, mode={Plan.Mode}, reports={ReportCount}, stopped={IsStopped}";
}

public sealed class MmsPersistentReportMonitorStartResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public MmsPersistentReportMonitorSession? Session { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MmsReportRcbSnapshot> RcbSnapshots { get; init; } = Array.Empty<MmsReportRcbSnapshot>();
    public IReadOnlyList<MmsReportDataSetSnapshot> DataSetSnapshots { get; init; } = Array.Empty<MmsReportDataSetSnapshot>();
}

public sealed class MmsPersistentReportMonitorReceiveResult
{
    public IReadOnlyList<MmsReportFrame> Reports { get; init; } = Array.Empty<MmsReportFrame>();
    public IReadOnlyList<MmsReportPollRead> PollReads { get; init; } = Array.Empty<MmsReportPollRead>();
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsPersistentReportMonitorStopResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
}

public sealed partial class MmsClientSession
{
    public async Task<MmsPersistentReportMonitorStartResult> StartPersistentReportMonitorAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = true,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsReady || plan.ReportControl == null)
        {
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                Message = "Persistent report monitor requires a ready plan with selected RCB."
            };
        }

        var rcb = plan.ReportControl;
        var writes = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();
        var rcbSnapshots = new List<MmsReportRcbSnapshot>();
        var dataSetSnapshots = new List<MmsReportDataSetSnapshot>();
        var originalDataSetReference = rcb.DataSetReference;
        var dataSetCreated = false;
        var reservationTouched = false;
        var enabledByThisClient = false;
        var isDynamic = plan.Mode == MmsReportSubscriptionPlanMode.DynamicDataSet;

        try
        {
            var beforeSnapshot = await CaptureReportControlSnapshotAsync(rcb, "before-start", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(beforeSnapshot);

            if (isDynamic)
            {
                if (plan.DynamicPoints.Count == 0 || string.IsNullOrWhiteSpace(plan.DataSetReference))
                {
                    return new MmsPersistentReportMonitorStartResult
                    {
                        IsSuccess = false,
                        WriteSteps = writes,
                        Warnings = warnings,
                        RcbSnapshots = rcbSnapshots,
                        Message = "Dynamic persistent monitor requires resolved points and a temporary DataSet reference."
                    };
                }

                var define = await DefineNamedVariableListAsync(
                    plan.DataSetReference,
                    plan.DynamicPoints.Select(x => x.ToObjectReference()),
                    cancellationToken).ConfigureAwait(false);
                writes.Add(new MmsReportAttributeWriteStep
                {
                    Attribute = "DefineNamedVariableList",
                    Reference = plan.DataSetReference,
                    Attempted = true,
                    IsSuccess = define.IsSuccess,
                    Message = define.Message
                });
                dataSetCreated = define.IsSuccess;
                if (!define.IsSuccess)
                {
                    return new MmsPersistentReportMonitorStartResult
                    {
                        IsSuccess = false,
                        WriteSteps = writes,
                        Warnings = warnings,
                        RcbSnapshots = rcbSnapshots,
                        Message = "Dynamic DataSet create failed; persistent report monitor was not started."
                    };
                }

                var afterCreateDataSet = await CaptureDataSetSnapshotAsync(plan.DataSetReference, plan.Members, "after-create", directory, cancellationToken).ConfigureAwait(false);
                dataSetSnapshots.Add(afterCreateDataSet);

                var dataSetValue = ToReportDataSetAttributeValue(plan.DataSetReference);
                var dataSetWrite = await WriteReportAttributeAsync(rcb, "DatSet", MmsDataValue.VisibleString(dataSetValue), cancellationToken).ConfigureAwait(false);
                writes.Add(dataSetWrite);
                if (!dataSetWrite.IsSuccess)
                {
                    return new MmsPersistentReportMonitorStartResult
                    {
                        IsSuccess = false,
                        WriteSteps = writes,
                        Warnings = warnings,
                        RcbSnapshots = rcbSnapshots,
                        DataSetSnapshots = dataSetSnapshots,
                        Message = "RCB.DatSet write failed; persistent report monitor was not started."
                    };
                }
            }
            else if (!string.IsNullOrWhiteSpace(plan.DataSetReference))
            {
                var dataSetBefore = await CaptureDataSetSnapshotAsync(plan.DataSetReference, plan.Members, "before-start", directory, cancellationToken).ConfigureAwait(false);
                dataSetSnapshots.Add(dataSetBefore);
            }

            if (rcb.Buffered && rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("BRCB ResvTms pre-reserve was skipped. This keeps the first monitor attach compatible with relays that accept ownership through RptEna=true.");
            }
            else if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(rcb, "Resv", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = reserve.IsSuccess;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv write failed. Continuing only if RptEna=true is accepted by the IED.");
            }

            var enable = await WriteReportAttributeAsync(rcb, "RptEna", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            enabledByThisClient = enable.IsSuccess;
            if (!enable.IsSuccess)
            {
                return new MmsPersistentReportMonitorStartResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    RcbSnapshots = rcbSnapshots,
                    DataSetSnapshots = dataSetSnapshots,
                    Message = "RptEna=true failed; persistent report monitor was not started."
                };
            }

            var afterEnableSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-enable", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterEnableSnapshot);

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed or is not supported by this RCB. Waiting for spontaneous/integrity reports only.");
            }

            var session = new MmsPersistentReportMonitorSession(
                plan,
                rcb,
                originalDataSetReference,
                isDynamic,
                deleteDynamicDataSetOnStop,
                dataSetCreated,
                reservationTouched,
                enabledByThisClient);

            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = true,
                Session = session,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = rcbSnapshots,
                DataSetSnapshots = dataSetSnapshots,
                Message = $"Persistent report monitor started for {rcb.Reference}. RptEna remains true until Stop RCB or Close IED."
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = false,
                WriteSteps = writes,
                Warnings = warnings,
                RcbSnapshots = rcbSnapshots,
                DataSetSnapshots = dataSetSnapshots,
                Message = $"Persistent report monitor start failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    public async Task<MmsPersistentReportMonitorReceiveResult> ReceivePersistentReportMonitorSliceAsync(
        MmsPersistentReportMonitorSession session,
        TimeSpan duration,
        MmsIedModelDirectory? pollDirectory = null,
        IReadOnlyList<string>? pollReferences = null,
        TimeSpan? pollInterval = null,
        bool triggerGeneralInterrogation = false,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsStopped)
            return new MmsPersistentReportMonitorReceiveResult { Message = "Report monitor is stopped." };

        var reports = new List<MmsReportFrame>();
        var pollReads = new List<MmsReportPollRead>();
        var writes = new List<MmsReportAttributeWriteStep>();
        Func<CancellationToken, Task<MmsReportAttributeWriteStep>>? giWriter = null;
        TimeSpan? giInterval = null;
        if (triggerGeneralInterrogation)
        {
            giWriter = token => WriteReportAttributeAsync(session.ReportControl, "GI", MmsDataValue.Boolean(true), token);
            giInterval = TimeSpan.FromMilliseconds(1);
        }

        var received = await ReceiveInformationReportsAsync(
            session.Plan.Members,
            duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : duration,
            pollDirectory,
            pollReferences,
            pollInterval,
            pollReads,
            soakSnapshots: null,
            soakSnapshotInterval: null,
            giWriter,
            giInterval,
            writes,
            cancellationToken).ConfigureAwait(false);
        reports.AddRange(received);

        if (reports.Count > 0)
        {
            session.ReportCount += reports.Count;
            session.LastReportAt = reports[^1].ReceivedAt;
        }
        session.PollReadCount += pollReads.Count;

        return new MmsPersistentReportMonitorReceiveResult
        {
            Reports = reports,
            PollReads = pollReads,
            WriteSteps = writes,
            Message = $"Report monitor slice: reports={reports.Count}, pollReads={pollReads.Count}."
        };
    }

    public async Task<MmsPersistentReportMonitorStopResult> StopPersistentReportMonitorAsync(
        MmsPersistentReportMonitorSession session,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsStopped)
            return new MmsPersistentReportMonitorStopResult { IsSuccess = true, Message = "Report monitor already stopped." };

        var writes = new List<MmsReportAttributeWriteStep>();
        var success = true;

        if (session.EnabledByThisClient)
        {
            var disable = await TryWriteReportAttributeForCleanupAsync(session.ReportControl, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
            writes.Add(disable);
            success &= disable.IsSuccess;
        }

        if (session.IsDynamic && session.DataSetCreated)
        {
            var restoreValue = string.IsNullOrWhiteSpace(session.OriginalDataSetReference)
                ? string.Empty
                : ToReportDataSetAttributeValue(session.OriginalDataSetReference);
            var restore = await TryWriteReportAttributeForCleanupAsync(session.ReportControl, "DatSet", MmsDataValue.VisibleString(restoreValue), CancellationToken.None).ConfigureAwait(false);
            writes.Add(restore);
            success &= restore.IsSuccess;

            if (session.DeleteDynamicDataSetOnStop)
            {
                try
                {
                    var delete = await DeleteNamedVariableListAsync(session.Plan.DataSetReference, CancellationToken.None).ConfigureAwait(false);
                    writes.Add(new MmsReportAttributeWriteStep
                    {
                        Attribute = "DeleteNamedVariableList",
                        Reference = session.Plan.DataSetReference,
                        Attempted = true,
                        IsSuccess = delete.IsSuccess,
                        Message = delete.Message
                    });
                    success &= delete.IsSuccess;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    writes.Add(new MmsReportAttributeWriteStep
                    {
                        Attribute = "DeleteNamedVariableList",
                        Reference = session.Plan.DataSetReference,
                        Attempted = true,
                        IsSuccess = false,
                        Message = $"delete dynamic DataSet failed: {ex.GetType().Name}: {ex.Message}"
                    });
                    success = false;
                }
            }
        }

        if (session.ReservationTouched)
        {
            var release = session.ReportControl.Buffered
                ? await TryWriteReportAttributeForCleanupAsync(session.ReportControl, "ResvTms", MmsDataValue.Unsigned(0), CancellationToken.None).ConfigureAwait(false)
                : await TryWriteReportAttributeForCleanupAsync(session.ReportControl, "Resv", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
            writes.Add(release);
            success &= release.IsSuccess;
        }

        session.IsStopped = true;
        return new MmsPersistentReportMonitorStopResult
        {
            IsSuccess = success,
            WriteSteps = writes,
            Message = success
                ? $"Persistent report monitor stopped for {session.ReportControl.Reference}."
                : $"Persistent report monitor stop completed with cleanup warnings for {session.ReportControl.Reference}."
        };
    }
}
