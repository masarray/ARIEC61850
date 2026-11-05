using System.Collections.Concurrent;

namespace AR.Iec61850.Mms;

public enum MmsPersistentReportTrafficState
{
    ArmedAwaitingTraffic,
    TrafficProven,
    Stopped
}

public sealed class MmsPersistentReportMonitorSession
{
    private long _firstRoutedReportUtcTicks;
    private long _lastRoutedReportUtcTicks;
    private int _routedReportCount;
    private int _pendingReportCount;
    private int _pendingDeliveryOverflowCount;
    private int _pendingAdmissionsInFlight;
    private int _stopped;
    private readonly int _pendingReportCapacity;
    internal const int DefaultPendingReportCapacity = 2048;

    internal MmsPersistentReportMonitorSession(
        MmsReportSubscriptionPlan plan,
        MmsReportControlCandidate reportControl,
        string originalDataSetReference,
        bool isDynamic,
        bool deleteDynamicDataSetOnStop,
        bool dataSetCreated,
        bool reservationTouched,
        bool enabledByThisClient,
        int pendingReportCapacity = DefaultPendingReportCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingReportCapacity);
        _pendingReportCapacity = Math.Min(pendingReportCapacity, DefaultPendingReportCapacity);
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
    // Concurrent dispatch observes stops without relying on an unsynchronized
    // auto-property read from an old immutable RCB registry snapshot.
    public bool IsStopped
    {
        get => Volatile.Read(ref _stopped) != 0;
        internal set => Volatile.Write(ref _stopped, value ? 1 : 0);
    }
    public int RoutedReportCount => Volatile.Read(ref _routedReportCount);
    public int PendingReportCount => Volatile.Read(ref _pendingReportCount);
    public int PendingDeliveryOverflowCount => Volatile.Read(ref _pendingDeliveryOverflowCount);
    public bool IsDeliveryContinuityUnverified => PendingDeliveryOverflowCount > 0;
    public DateTimeOffset? FirstRoutedReportAt => ToUtcTimestamp(Volatile.Read(ref _firstRoutedReportUtcTicks));
    public DateTimeOffset? LastRoutedReportAt => ToUtcTimestamp(Volatile.Read(ref _lastRoutedReportUtcTicks));
    public MmsPersistentReportTrafficState TrafficState => IsStopped
        ? MmsPersistentReportTrafficState.Stopped
        : RoutedReportCount > 0
            ? MmsPersistentReportTrafficState.TrafficProven
            : MmsPersistentReportTrafficState.ArmedAwaitingTraffic;
    public TimeSpan? FirstReportLatency => FirstRoutedReportAt.HasValue
        ? FirstRoutedReportAt.Value - StartedAt
        : null;

    private ConcurrentQueue<MmsReportFrame> PendingReports { get; } = new();

    // Capacity must be reserved atomically before enqueue. No blocking, hidden
    // frame replacement or unbounded memory growth when a consumer stalls.
    internal bool TryQueuePendingReport(MmsReportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (IsStopped)
            return false;

        // A stop may begin after the first closed check. Acquire the small
        // in-flight admission lease BEFORE checking again; lifecycle cleanup
        // waits for leased writers outside the registry lock before draining.
        Interlocked.Increment(ref _pendingAdmissionsInFlight);
        try
        {
            if (IsStopped)
                return false;

            while (true)
            {
                if (IsStopped)
                    return false;
                var count = Volatile.Read(ref _pendingReportCount);
                if (count >= _pendingReportCapacity)
                {
                    // A shutdown is not consumer lag; do not inflate overflow.
                    if (IsStopped)
                        return false;
                    Interlocked.Increment(ref _pendingDeliveryOverflowCount);
                    return false;
                }
                if (Interlocked.CompareExchange(ref _pendingReportCount, count + 1, count) == count)
                    break;
            }

            // The admission lease makes this enqueue visible to shutdown
            // before it can drain; no frame can arrive behind the final drain.
            PendingReports.Enqueue(frame);
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingAdmissionsInFlight);
        }
    }

    // Lifecycle-only barrier, never called from MMS receive hot path. All
    // pending writers either completed enqueue or observed IsStopped before
    // shutdown drains the queue. Later writers reject immediately.
    internal void CompletePendingReportAdmissions()
    {
        IsStopped = true;
        var spinner = new SpinWait();
        while (Volatile.Read(ref _pendingAdmissionsInFlight) != 0)
            spinner.SpinOnce();
    }

    internal bool TryDequeuePendingReport(out MmsReportFrame frame)
    {
        if (PendingReports.TryDequeue(out var queued))
        {
            Interlocked.Decrement(ref _pendingReportCount);
            frame = queued;
            return true;
        }
        frame = null!;
        return false;
    }

    internal void ObserveRoutedReport(MmsReportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var ticks = frame.ReceivedAt.UtcDateTime.Ticks;
        Interlocked.CompareExchange(ref _firstRoutedReportUtcTicks, ticks, 0);
        Interlocked.Exchange(ref _lastRoutedReportUtcTicks, ticks);
        Interlocked.Increment(ref _routedReportCount);
    }

    public string Summary =>
        $"persistent report monitor: rcb={ReportControl.Reference}, dataset={Plan.DataSetReference}, mode={Plan.Mode}, " +
        $"traffic={TrafficState}, routedReports={RoutedReportCount}, drainedReports={ReportCount}, " +
        $"pendingReports={PendingReportCount}, pendingDeliveryOverflow={PendingDeliveryOverflowCount}, " +
        $"deliveryContinuityUnverified={IsDeliveryContinuityUnverified}, stopped={IsStopped}";

    private static DateTimeOffset? ToUtcTimestamp(long ticks)
        => ticks <= 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
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
    public int PendingDeliveryOverflowCount { get; init; }
    public bool DeliveryContinuityUnverified { get; init; }
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
    // Immutable registry snapshot. The association receive loop must not lock
    // and allocate a new array for every single InformationReport.
    private MmsPersistentReportMonitorSession[] _activePersistentReportMonitors = Array.Empty<MmsPersistentReportMonitorSession>();
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
        MmsPersistentReportMonitorSession? registeredSession = null;
        var keepRegisteredSession = false;
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
                var reserve = await WriteReportSemanticAttributeAsync(rcb, MmsReportControlSemanticWrite.Reservation(true), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = reserve.IsSuccess;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv write failed. Continuing only if RptEna=true is accepted by the IED.");
            }

            var enable = await WriteReportSemanticAttributeAsync(rcb, MmsReportControlSemanticWrite.ReportEnable(true), cancellationToken).ConfigureAwait(false);
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

            // Other RCB receive slices can drain the shared canonical FIFO during
            // this method. Register this target before readback or GI so its
            // initial image is never orphaned by a concurrent dispatcher.
            registeredSession = new MmsPersistentReportMonitorSession(
                plan, rcb, originalDataSetReference, isDynamic, deleteDynamicDataSetOnStop,
                dataSetCreated, reservationTouched, enabledByThisClient);
            RegisterPersistentReportMonitor(registeredSession);

            var afterEnableSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-enable", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterEnableSnapshot);

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportSemanticAttributeAsync(rcb, MmsReportControlSemanticWrite.GeneralInterrogation(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed or is not supported by this RCB. Waiting for spontaneous/integrity reports only.");
            }

            keepRegisteredSession = true;
            return new MmsPersistentReportMonitorStartResult
            {
                IsSuccess = true,
                Session = registeredSession,
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
        finally
        {
            // Also runs for cancellation and unexpected exceptions: never leave
            // an unowned monitor target registered after failed activation.
            if (!keepRegisteredSession && registeredSession is not null)
                UnregisterPersistentReportMonitor(registeredSession);
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
            PendingDeliveryOverflowCount = session.PendingDeliveryOverflowCount,
            DeliveryContinuityUnverified = session.IsDeliveryContinuityUnverified,
            Message = $"Report monitor slice: reports={reports.Count}, pollReads={pollReads.Count}, " +
                $"pendingDeliveryOverflow={session.PendingDeliveryOverflowCount}, " +
                $"continuityUnverified={session.IsDeliveryContinuityUnverified}."
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
            var disable = await TryWriteReportSemanticAttributeForCleanupAsync(session.ReportControl, MmsReportControlSemanticWrite.ReportEnable(false), CancellationToken.None).ConfigureAwait(false);
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
                ? await TryWriteReportSemanticAttributeForCleanupAsync(session.ReportControl, MmsReportControlSemanticWrite.ReservationTime(0), CancellationToken.None).ConfigureAwait(false)
                : await TryWriteReportSemanticAttributeForCleanupAsync(session.ReportControl, MmsReportControlSemanticWrite.Reservation(false), CancellationToken.None).ConfigureAwait(false);
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

    internal void RegisterPersistentReportMonitor(MmsPersistentReportMonitorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsStopped)
            throw new InvalidOperationException("Cannot register a stopped RCB monitor.");
        lock (_persistentReportMonitorSync)
        {
            if (_persistentReportMonitors.Add(session))
                PublishPersistentReportMonitorSnapshotLocked();
        }
    }

    private void UnregisterPersistentReportMonitor(MmsPersistentReportMonitorSession session)
    {
        lock (_persistentReportMonitorSync)
        {
            // Mark stopped before a stale receiver can admit more frames, then
            // publish the new registry for all future dispatch iterations.
            session.IsStopped = true;
            if (_persistentReportMonitors.Remove(session))
                PublishPersistentReportMonitorSnapshotLocked();
        }

        session.CompletePendingReportAdmissions();
        while (session.TryDequeuePendingReport(out _))
        {
        }
    }

    // Receive/dispatch is read-mostly. Membership changes happen only on
    // activation, stop, and association reset. A volatile read gives every
    // report an immutable, association-owned RCB registry with no monitor lock
    // or per-frame snapshot allocation. Never publish mutable HashSet storage.
    internal MmsPersistentReportMonitorSession[] SnapshotPersistentReportMonitors()
        => Volatile.Read(ref _activePersistentReportMonitors);

    private void PublishPersistentReportMonitorSnapshotLocked()
        => Volatile.Write(ref _activePersistentReportMonitors,
            _persistentReportMonitors.Where(x => !x.IsStopped).ToArray());

    internal void ClearPersistentReportMonitors()
    {
        MmsPersistentReportMonitorSession[] retired;
        lock (_persistentReportMonitorSync)
        {
            retired = _persistentReportMonitors.ToArray();
            foreach (var session in retired)
                session.IsStopped = true;

            _persistentReportMonitors.Clear();
            Volatile.Write(ref _activePersistentReportMonitors,
                Array.Empty<MmsPersistentReportMonitorSession>());
            Interlocked.Exchange(ref _unroutedPersistentReportCount, 0);
        }

        // Do not hold the registry lock while draining an overloaded RCB.
        // In-flight stale snapshots see IsStopped and fail closed at queue admit.
        foreach (var session in retired)
        {
            session.CompletePendingReportAdmissions();
            while (session.TryDequeuePendingReport(out _))
            {
            }
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
                writes.Add(await WriteReportSemanticAttributeAsync(
                    requestedSession.ReportControl, MmsReportControlSemanticWrite.GeneralInterrogation(true),
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
                // The association-owned receive pump already blocks on the transport.
                // Do not add a 25 ms application polling loop on top of it. Wait for the
                // router's binary InformationReport signal, bounded by the next legitimate
                // scheduler deadline (residual polling) or this receive slice deadline.
                var wakeAt = deadline;
                if (pollDirectory != null &&
                    references.Length > 0 &&
                    nextPollAt < wakeAt)
                {
                    wakeAt = nextPollAt;
                }

                var waitFor = wakeAt - DateTimeOffset.UtcNow;
                if (waitFor > TimeSpan.Zero)
                {
                    await _receiveRouter.WaitForInformationReportAsync(
                        waitFor,
                        cancellationToken).ConfigureAwait(false);
                }

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
        while (session.TryDequeuePendingReport(out var frame))
            destination.Add(frame);
    }

    internal void RoutePersistentInformationReport(byte[] payload)
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

        // Control termination / LastApplError use the same MMS service but are
        // not RCB reports. Never feed them to the single-monitor fallback.
        // Their independent control subscribers already received the envelope.
        if (!HasRcbReportHeader(decoded))
            return;

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
        if (!target.TryQueuePendingReport(frame))
        {
            Interlocked.Increment(ref _unroutedPersistentReportCount);
            LastReceiveRoutingSummary = target.IsStopped
                ? $"InformationReport arrived while {target.ReportControl.Reference} was stopping; no post-stop projection."
                : $"Routed InformationReport for {target.ReportControl.Reference} could not enter its bounded delivery queue. " +
                  $"Delivery continuity unverified; pendingOverflow={target.PendingDeliveryOverflowCount}.";
            return;
        }
        target.ObserveRoutedReport(frame);
        LastReceiveRoutingSummary =
            $"Routed InformationReport to {target.ReportControl.Reference} by {evidence}. RptID={RoutingTextOrDash(header.ReportId)}, DatSet={RoutingTextOrDash(header.DataSetReference)}.";
    }

    internal static bool HasRcbReportHeader(MmsInformationReport report)
        => report.IsSuccess && report.Items.Count >= 2 &&
           report.Items[0].Value?.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString &&
           report.Items[1].Value?.Kind == MmsDataKind.BitString;

    internal static MmsPersistentReportMonitorSession? SelectPersistentReportMonitor(
        IReadOnlyList<MmsPersistentReportMonitorSession> active,
        MmsReportHeader header,
        out string evidence)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(header);

        // The common exact-RptID path scans the immutable registry once.
        // Do not allocate LINQ candidate arrays, even with many active RCBs.
        // Keep exact ID > RCB affinity > exact DataSet > DataSet tail >
        // single-active fallback, including the legacy ambiguity policy.
        var reportId = NormalizeRoutingReference(header.ReportId);
        var dataSet = NormalizeRoutingReference(header.DataSetReference);
        var hasReportId = !string.IsNullOrWhiteSpace(reportId);
        var hasDataSet = !string.IsNullOrWhiteSpace(dataSet);
        var liveCount = 0;
        var exactIdCount = 0;
        MmsPersistentReportMonitorSession? sole = null;
        MmsPersistentReportMonitorSession? exactIdCandidate = null;

        for (var i = 0; i < active.Count; i++)
        {
            var candidate = active[i];
            if (candidate.IsStopped)
                continue;
            sole = candidate;
            liveCount++;
            if (hasReportId &&
                NormalizeRoutingReference(candidate.ReportControl.ReportId)
                    .Equals(reportId, StringComparison.OrdinalIgnoreCase))
            {
                exactIdCandidate = candidate;
                exactIdCount++;
            }
        }

        if (liveCount == 0)
        {
            evidence = "no active persistent monitor";
            return null;
        }

        if (exactIdCount == 1)
        {
            if (ConflictsWithAdvertisedDataSet(exactIdCandidate!, dataSet))
            {
                evidence = "RptID/DatSet conflict: report was quarantined before process-value projection";
                return null;
            }
            evidence = "exact RptID";
            return exactIdCandidate;
        }

        if (hasReportId)
        {
            MmsPersistentReportMonitorSession? affinityCandidate = null;
            var affinityCount = 0;
            for (var i = 0; i < active.Count; i++)
            {
                var candidate = active[i];
                if (candidate.IsStopped || !HasRcbNameAffinity(reportId, candidate.ReportControl))
                    continue;
                affinityCandidate = candidate;
                affinityCount++;
            }

            if (affinityCount == 1)
            {
                if (ConflictsWithAdvertisedDataSet(affinityCandidate!, dataSet))
                {
                    evidence = "RptID/DatSet conflict: report was quarantined before process-value projection";
                    return null;
                }
                evidence = "RptID-to-RCB name affinity";
                return affinityCandidate;
            }
        }

        if (hasDataSet)
        {
            var dataSetTail = RoutingTail(dataSet);
            var hasDataSetTail = !string.IsNullOrWhiteSpace(dataSetTail);
            MmsPersistentReportMonitorSession? exactDataSetCandidate = null;
            MmsPersistentReportMonitorSession? tailDataSetCandidate = null;
            var exactDataSetCount = 0;
            var tailDataSetCount = 0;

            for (var i = 0; i < active.Count; i++)
            {
                var candidate = active[i];
                if (candidate.IsStopped)
                    continue;
                var expected = NormalizeRoutingReference(candidate.Plan.DataSetReference);
                if (expected.Equals(dataSet, StringComparison.OrdinalIgnoreCase))
                {
                    exactDataSetCandidate = candidate;
                    exactDataSetCount++;
                }
                if (hasDataSetTail &&
                    RoutingTail(expected).Equals(dataSetTail, StringComparison.OrdinalIgnoreCase))
                {
                    tailDataSetCandidate = candidate;
                    tailDataSetCount++;
                }
            }

            if (exactDataSetCount == 1)
            {
                evidence = "exact DataSet";
                return exactDataSetCandidate;
            }
            if (tailDataSetCount == 1)
            {
                evidence = "DataSet tail";
                return tailDataSetCandidate;
            }
        }

        if (liveCount == 1)
        {
            if (ConflictsWithAdvertisedDataSet(sole!, dataSet))
            {
                evidence = "single-monitor DatSet conflict: report was quarantined before process-value projection";
                return null;
            }
            evidence = "single active monitor fallback";
            return sole;
        }

        evidence = "ambiguous report identity; report was not projected against an arbitrary DataSet";
        return null;
    }

    // Fail closed only when an advertised DataSet contradicts the selected RCB.
    // Partial identity is valid on some relays, but suffixes must match at the
    // appropriate LN/DataSet component boundary.
    private static bool ConflictsWithAdvertisedDataSet(
        MmsPersistentReportMonitorSession candidate, string dataSet)
    {
        if (string.IsNullOrWhiteSpace(dataSet))
            return false;

        var expected = NormalizeRoutingReference(candidate.Plan.DataSetReference);
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        if (dataSet.Contains('/'))
            return !expected.Equals(dataSet, StringComparison.OrdinalIgnoreCase);

        if (dataSet.Contains('.'))
        {
            if (expected.Equals(dataSet, StringComparison.OrdinalIgnoreCase))
                return false;
            var offset = expected.Length - dataSet.Length;
            return offset <= 0 ||
                   !expected.EndsWith(dataSet, StringComparison.OrdinalIgnoreCase) ||
                   expected[offset - 1] != '/';
        }

        return !RoutingTail(expected).Equals(dataSet, StringComparison.OrdinalIgnoreCase);
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
