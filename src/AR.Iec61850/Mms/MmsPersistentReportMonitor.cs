using System.Collections.Concurrent;

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
    internal ConcurrentQueue<MmsReportFrame> PendingReports { get; } = new();

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
    private readonly object _persistentReportMonitorSync = new();
    private readonly HashSet<MmsPersistentReportMonitorSession> _persistentReportMonitors = new();
    private int _unroutedPersistentReportCount;

    /// <summary>
    /// Number of InformationReport PDUs that could not be assigned unambiguously
    /// to an active persistent monitor. They are deliberately not projected
    /// against an arbitrary DataSet.
    /// </summary>
    public int UnroutedPersistentReportCount => Volatile.Read(ref _unroutedPersistentReportCount);

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

            if (isDynamic)
            {
                if (!rcb.Attributes.Contains("TrgOps", StringComparer.OrdinalIgnoreCase) ||
                    !MmsReportControlFieldCodec.TryEncodeTriggerOptions(rcb.TriggerOptions, out var triggerOptions))
                {
                    return new MmsPersistentReportMonitorStartResult
                    {
                        IsSuccess = false,
                        WriteSteps = writes,
                        Warnings = warnings,
                        RcbSnapshots = rcbSnapshots,
                        DataSetSnapshots = dataSetSnapshots,
                        Message = "Dynamic report monitor requires a writable TrgOps field with explicit dchg trigger configuration."
                    };
                }

                var triggerWrite = await WriteReportAttributeAsync(rcb, "TrgOps", triggerOptions, cancellationToken).ConfigureAwait(false);
                writes.Add(triggerWrite);
                if (!triggerWrite.IsSuccess)
                {
                    return new MmsPersistentReportMonitorStartResult
                    {
                        IsSuccess = false,
                        WriteSteps = writes,
                        Warnings = warnings,
                        RcbSnapshots = rcbSnapshots,
                        DataSetSnapshots = dataSetSnapshots,
                        Message = "RCB.TrgOps write failed; dynamic reporting was not armed because dchg could not be guaranteed."
                    };
                }

                if (rcb.Attributes.Contains("OptFlds", StringComparer.OrdinalIgnoreCase) &&
                    MmsReportControlFieldCodec.TryEncodeOptionalFields(rcb.OptionalFields, out var optionalFields))
                {
                    var optionalWrite = await WriteReportAttributeAsync(rcb, "OptFlds", optionalFields, cancellationToken).ConfigureAwait(false);
                    writes.Add(optionalWrite);
                    if (!optionalWrite.IsSuccess)
                        warnings.Add("RCB.OptFlds write failed. Reporting can continue, but report timestamp/reason diagnostics may be incomplete.");
                }
                else
                {
                    warnings.Add("Dynamic RCB has no writable OptFlds mapping. Reporting can continue, but source timestamp/reason diagnostics may be incomplete.");
                }
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
            RegisterPersistentReportMonitor(session);

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
        await ReceiveAndDispatchPersistentReportsAsync(
            session,
            duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : duration,
            pollDirectory,
            pollReferences,
            pollInterval,
            triggerGeneralInterrogation,
            reports,
            pollReads,
            writes,
            cancellationToken).ConfigureAwait(false);

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
        UnregisterPersistentReportMonitor(session);

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

    private void RegisterPersistentReportMonitor(MmsPersistentReportMonitorSession session)
    {
        lock (_persistentReportMonitorSync)
            _persistentReportMonitors.Add(session);
    }

    private void UnregisterPersistentReportMonitor(MmsPersistentReportMonitorSession session)
    {
        lock (_persistentReportMonitorSync)
            _persistentReportMonitors.Remove(session);

        while (session.PendingReports.TryDequeue(out _))
        {
        }
    }

    private MmsPersistentReportMonitorSession[] SnapshotPersistentReportMonitors()
    {
        lock (_persistentReportMonitorSync)
            return _persistentReportMonitors.Where(x => !x.IsStopped).ToArray();
    }

    private void ClearPersistentReportMonitors()
    {
        lock (_persistentReportMonitorSync)
        {
            foreach (var session in _persistentReportMonitors)
            {
                session.IsStopped = true;
                while (session.PendingReports.TryDequeue(out _))
                {
                }
            }

            _persistentReportMonitors.Clear();
            Interlocked.Exchange(ref _unroutedPersistentReportCount, 0);
        }
    }

    private async Task ReceiveAndDispatchPersistentReportsAsync(
        MmsPersistentReportMonitorSession requestedSession,
        TimeSpan duration,
        MmsIedModelDirectory? pollDirectory,
        IReadOnlyList<string>? pollReferences,
        TimeSpan? pollInterval,
        bool triggerGeneralInterrogation,
        List<MmsReportFrame> reports,
        List<MmsReportPollRead> pollReads,
        List<MmsReportAttributeWriteStep> writes,
        CancellationToken cancellationToken)
    {
        var references = pollReferences?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        var effectiveDuration = duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : duration;
        var deadline = DateTimeOffset.UtcNow + effectiveDuration;
        var effectivePollInterval = pollInterval.GetValueOrDefault(TimeSpan.FromSeconds(1));
        if (effectivePollInterval <= TimeSpan.Zero)
            effectivePollInterval = TimeSpan.FromSeconds(1);

        var nextPollAt = DateTimeOffset.UtcNow;
        var giPending = triggerGeneralInterrogation;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrainPendingReports(requestedSession, reports);

            var routedAny = false;
            while (TryDequeueInformationReport(out var queuedPayload))
            {
                RoutePersistentInformationReport(queuedPayload);
                routedAny = true;
            }

            DrainPendingReports(requestedSession, reports);
            if (routedAny)
                continue;

            if (giPending)
            {
                writes.Add(await WriteReportAttributeAsync(
                    requestedSession.ReportControl,
                    "GI",
                    MmsDataValue.Boolean(true),
                    cancellationToken).ConfigureAwait(false));
                giPending = false;
                continue;
            }

            if (pollDirectory != null && references.Length > 0 && DateTimeOffset.UtcNow >= nextPollAt)
            {
                foreach (var reference in references)
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        break;

                    pollReads.Add(await ReadReportPollReferenceAsync(
                        pollDirectory,
                        reference,
                        cancellationToken).ConfigureAwait(false));
                }

                nextPollAt = DateTimeOffset.UtcNow + effectivePollInterval;
                continue;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            if (IsReceivePumpRunning)
            {
                var delay = remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!_cotp.HasDataAvailable)
            {
                var delay = remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var payload = await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
            var route = _receiveRouter.Route(payload);
            LastReceiveRoutingSummary = route.Message;
        }

        DrainPendingReports(requestedSession, reports);
    }

    private static void DrainPendingReports(MmsPersistentReportMonitorSession session, List<MmsReportFrame> destination)
    {
        while (session.PendingReports.TryDequeue(out var frame))
            destination.Add(frame);
    }

    private void RoutePersistentInformationReport(byte[] payload)
    {
        if (!MmsInformationReportDecoder.IsInformationReport(payload))
            return;

        MmsInformationReport decoded;
        try
        {
            decoded = MmsInformationReportDecoder.Decode(payload);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Interlocked.Increment(ref _unroutedPersistentReportCount);
            LastReceiveRoutingSummary = $"InformationReport decode failed before RCB routing: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var header = MmsReportFrameMapper.DecodeHeader(decoded);
        var active = SnapshotPersistentReportMonitors();
        var target = SelectPersistentReportMonitor(active, header, out var evidence);
        if (target == null)
        {
            Interlocked.Increment(ref _unroutedPersistentReportCount);
            LastReceiveRoutingSummary =
                $"Unrouted InformationReport: RptID={RoutingTextOrDash(header.ReportId)}, DatSet={RoutingTextOrDash(header.DataSetReference)}, activeRCB={active.Length}. {evidence}";
            return;
        }

        var frame = MmsReportFrameMapper.Map(decoded, target.Plan.Members, DateTimeOffset.UtcNow);
        target.PendingReports.Enqueue(frame);
        LastReceiveRoutingSummary =
            $"Routed InformationReport to {target.ReportControl.Reference} by {evidence}. RptID={RoutingTextOrDash(header.ReportId)}, DatSet={RoutingTextOrDash(header.DataSetReference)}.";
    }

    internal static MmsPersistentReportMonitorSession? SelectPersistentReportMonitor(
        IReadOnlyList<MmsPersistentReportMonitorSession> active,
        MmsReportHeader header,
        out string evidence)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(header);
        var candidates = active.Where(x => !x.IsStopped).ToArray();
        if (candidates.Length == 0)
        {
            evidence = "no active persistent monitor";
            return null;
        }

        var reportId = NormalizeRoutingReference(header.ReportId);
        if (!string.IsNullOrWhiteSpace(reportId))
        {
            var exactRptId = candidates
                .Where(x => NormalizeRoutingReference(x.ReportControl.ReportId).Equals(reportId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactRptId.Length == 1)
            {
                evidence = "exact RptID";
                return exactRptId[0];
            }

            var rcbAffinity = candidates.Where(x => HasRcbNameAffinity(reportId, x.ReportControl)).ToArray();
            if (rcbAffinity.Length == 1)
            {
                evidence = "RptID-to-RCB name affinity";
                return rcbAffinity[0];
            }
        }

        var dataSet = NormalizeRoutingReference(header.DataSetReference);
        if (!string.IsNullOrWhiteSpace(dataSet))
        {
            var exactDataSet = candidates
                .Where(x => NormalizeRoutingReference(x.Plan.DataSetReference).Equals(dataSet, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactDataSet.Length == 1)
            {
                evidence = "exact DataSet";
                return exactDataSet[0];
            }

            var dataSetTail = RoutingTail(dataSet);
            var tailMatches = candidates
                .Where(x => RoutingTail(NormalizeRoutingReference(x.Plan.DataSetReference)).Equals(dataSetTail, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (!string.IsNullOrWhiteSpace(dataSetTail) && tailMatches.Length == 1)
            {
                evidence = "DataSet tail";
                return tailMatches[0];
            }
        }

        if (candidates.Length == 1)
        {
            evidence = "single active monitor fallback";
            return candidates[0];
        }

        evidence = "ambiguous report identity; report was not projected against an arbitrary DataSet";
        return null;
    }

    private static bool HasRcbNameAffinity(string reportId, MmsReportControlCandidate reportControl)
    {
        var name = NormalizeRoutingReference(reportControl.Name);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return reportId.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               reportId.EndsWith("." + name, StringComparison.OrdinalIgnoreCase) ||
               reportId.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) ||
               reportId.Contains("." + name + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string RoutingTail(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var slash = reference.LastIndexOf('/');
        var dot = reference.LastIndexOf('.');
        var index = Math.Max(slash, dot);
        return index >= 0 && index + 1 < reference.Length ? reference[(index + 1)..] : reference;
    }

    private static string NormalizeRoutingReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace('\\', '/').TrimEnd('.');

    private static string RoutingTextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
