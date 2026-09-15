using System.Globalization;

namespace AR.Iec61850.Mms;

public enum MmsReportControlKind
{
    Urcb,
    Brcb
}

public enum MmsReportLifecyclePhase
{
    Discovered,
    PreparingDataSet,
    DataSetReady,
    Reserved,
    Configured,
    Enabled,
    Monitoring,
    Stopping,
    Disabled,
    Released,
    Cleaned,
    Faulted
}

public enum MmsReportDataSetOwnership
{
    External,
    CreatedBySession
}

public enum MmsReportLifecycleEventKind
{
    Observation,
    DataSetCreate,
    DataSetVerify,
    Reservation,
    Configuration,
    Enable,
    GeneralInterrogation,
    Report,
    ReplayDuplicate,
    SequenceGap,
    SequenceReset,
    BufferOverflow,
    TimestampRegression,
    Disable,
    ReservationRelease,
    DataSetRestore,
    DataSetDelete,
    CleanupResidue,
    Failure
}

public sealed class MmsReportLifecycleEvent
{
    public long Sequence { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; }
    public MmsReportLifecyclePhase Phase { get; init; }
    public MmsReportLifecycleEventKind Kind { get; init; }
    public string Target { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsBufferedReportReplayDiagnostics
{
    public int AcceptedReportCount { get; internal set; }
    public int DuplicateReportCount { get; internal set; }
    public int SequenceGapCount { get; internal set; }
    public int SequenceResetCount { get; internal set; }
    public int BufferOverflowCount { get; internal set; }
    public int TimestampRegressionCount { get; internal set; }
    public int GeneralInterrogationReportCount { get; internal set; }
    public int IntegrityReportCount { get; internal set; }
    public string LastEntryIdHex { get; internal set; } = string.Empty;
    public string LastTimeOfEntry { get; internal set; } = string.Empty;
    public ulong? LastSequenceNumber { get; internal set; }

    public string Summary =>
        $"accepted={AcceptedReportCount}, duplicate={DuplicateReportCount}, gaps={SequenceGapCount}, resets={SequenceResetCount}, overflow={BufferOverflowCount}, timestampRegression={TimestampRegressionCount}, gi={GeneralInterrogationReportCount}, integrity={IntegrityReportCount}, lastEntryID={TextOrDash(LastEntryIdHex)}, lastSqNum={LastSequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    private static string TextOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}

public sealed class MmsReportLifecycleSnapshot
{
    public MmsReportControlKind Kind { get; init; }
    public MmsReportLifecyclePhase Phase { get; init; }
    public MmsReportDataSetOwnership DataSetOwnership { get; init; }
    public string ReportControlReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public bool IsReserved { get; init; }
    public bool IsEnabled { get; init; }
    public bool GeneralInterrogationRequested { get; init; }
    public bool CleanupHasResidue { get; init; }
    public int FailureCount { get; init; }
    public MmsBufferedReportReplayDiagnostics Replay { get; init; } = new();
    public IReadOnlyList<MmsReportLifecycleEvent> Events { get; init; } = Array.Empty<MmsReportLifecycleEvent>();

    public string Summary =>
        $"report lifecycle: kind={Kind}, phase={Phase}, rcb={ReportControlReference}, dataset={DataSetReference}, ownership={DataSetOwnership}, reserved={IsReserved}, enabled={IsEnabled}, residue={CleanupHasResidue}, failures={FailureCount}; replay[{Replay.Summary}]";
}

/// <summary>
/// Deterministic, vendor-neutral lifecycle evidence for one IEC 61850 report-control
/// session. It does not send MMS traffic; callers feed it observed write/readback/report
/// evidence so protocol execution and lifecycle reasoning remain separate.
/// </summary>
public sealed class MmsReportLifecycleStateMachine
{
    private const int MaximumRememberedReplayKeys = 4096;
    private readonly object _sync = new();
    private readonly List<MmsReportLifecycleEvent> _events = new();
    private readonly HashSet<string> _replayKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _replayKeyOrder = new();
    private readonly Dictionary<string, ulong> _lastSequenceByStream = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastTimeOfEntryByStream = new(StringComparer.OrdinalIgnoreCase);
    private long _eventSequence;
    private int _failureCount;
    private bool _cleanupHasResidue;

    public MmsReportLifecycleStateMachine(MmsReportSubscriptionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.ReportControl);

        ReportControlReference = plan.ReportControl.Reference;
        DataSetReference = plan.DataSetReference;
        Kind = plan.ReportControl.Buffered ? MmsReportControlKind.Brcb : MmsReportControlKind.Urcb;
        Phase = MmsReportLifecyclePhase.Discovered;
        DataSetOwnership = MmsReportDataSetOwnership.External;
        Replay = new MmsBufferedReportReplayDiagnostics();
        AddEvent(MmsReportLifecycleEventKind.Observation, ReportControlReference, true,
            $"Lifecycle initialized from {plan.Mode} subscription plan; RCB kind={Kind}.");
    }

    public MmsReportControlKind Kind { get; }
    public string ReportControlReference { get; }
    public string DataSetReference { get; private set; }
    public MmsReportLifecyclePhase Phase { get; private set; }
    public MmsReportDataSetOwnership DataSetOwnership { get; private set; }
    public bool IsReserved { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool GeneralInterrogationRequested { get; private set; }
    public MmsBufferedReportReplayDiagnostics Replay { get; }

    public void ObserveStartResult(MmsPersistentReportMonitorStartResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync)
        {
            foreach (var step in result.WriteSteps)
                ObserveStartWriteCore(step);
            foreach (var snapshot in result.DataSetSnapshots)
                ObserveDataSetSnapshotCore(snapshot);
            foreach (var snapshot in result.RcbSnapshots)
                ObserveRcbSnapshotCore(snapshot);

            if (result.IsSuccess)
            {
                if (IsEnabled)
                    Transition(MmsReportLifecyclePhase.Monitoring);
                else
                    RecordFailure("start", "Start result reported success but no successful RptEna=true evidence was recorded.");
            }
            else
            {
                RecordFailure("start", string.IsNullOrWhiteSpace(result.Message) ? "Report monitor start failed." : result.Message);
            }
        }
    }

    public void ObserveReceiveResult(MmsPersistentReportMonitorReceiveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync)
        {
            foreach (var step in result.WriteSteps)
            {
                if (step.Attribute.Equals("GI", StringComparison.OrdinalIgnoreCase))
                {
                    GeneralInterrogationRequested |= step.IsSuccess;
                    AddEvent(MmsReportLifecycleEventKind.GeneralInterrogation, step.Reference, step.IsSuccess, step.Message);
                    if (!step.IsSuccess)
                        _failureCount++;
                }
            }

            foreach (var report in result.Reports)
                ObserveReportCore(report);
        }
    }

    public void BeginStop()
    {
        lock (_sync)
        {
            if (Phase == MmsReportLifecyclePhase.Cleaned)
                return;
            Transition(MmsReportLifecyclePhase.Stopping);
            AddEvent(MmsReportLifecycleEventKind.Observation, ReportControlReference, true, "Cleanup started; RptEna disable must precede reservation release and owned DataSet deletion.");
        }
    }

    public void ObserveStopResult(MmsPersistentReportMonitorStopResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync)
        {
            foreach (var step in result.WriteSteps)
                ObserveStopWriteCore(step);

            if (IsEnabled)
                RecordResidue("RptEna", "Cleanup completed while lifecycle evidence still indicates RptEna enabled.");
            if (Kind == MmsReportControlKind.Urcb && IsReserved)
                RecordResidue("Resv", "Cleanup completed while lifecycle evidence still indicates URCB reservation held.");
            if (DataSetOwnership == MmsReportDataSetOwnership.CreatedBySession &&
                result.WriteSteps.Any(x => x.Attribute.Equals("DeleteNamedVariableList", StringComparison.OrdinalIgnoreCase)) &&
                !result.WriteSteps.Any(x => x.Attribute.Equals("DeleteNamedVariableList", StringComparison.OrdinalIgnoreCase) && x.IsSuccess))
            {
                RecordResidue(DataSetReference, "Session-owned dynamic DataSet was not deleted successfully.");
            }

            if (result.IsSuccess && !_cleanupHasResidue)
                Transition(MmsReportLifecyclePhase.Cleaned);
            else if (_cleanupHasResidue || !result.IsSuccess)
                Transition(MmsReportLifecyclePhase.Faulted);
        }
    }

    public MmsReportLifecycleSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new MmsReportLifecycleSnapshot
            {
                Kind = Kind,
                Phase = Phase,
                DataSetOwnership = DataSetOwnership,
                ReportControlReference = ReportControlReference,
                DataSetReference = DataSetReference,
                IsReserved = IsReserved,
                IsEnabled = IsEnabled,
                GeneralInterrogationRequested = GeneralInterrogationRequested,
                CleanupHasResidue = _cleanupHasResidue,
                FailureCount = _failureCount,
                Replay = CopyReplay(),
                Events = _events.ToArray()
            };
        }
    }

    private void ObserveStartWriteCore(MmsReportAttributeWriteStep step)
    {
        var attribute = step.Attribute?.Trim() ?? string.Empty;
        if (attribute.Equals("DefineNamedVariableList", StringComparison.OrdinalIgnoreCase))
        {
            Transition(MmsReportLifecyclePhase.PreparingDataSet);
            if (step.IsSuccess)
            {
                DataSetOwnership = MmsReportDataSetOwnership.CreatedBySession;
                if (!string.IsNullOrWhiteSpace(step.Reference))
                    DataSetReference = step.Reference;
                Transition(MmsReportLifecyclePhase.DataSetReady);
            }
            AddEvent(MmsReportLifecycleEventKind.DataSetCreate, step.Reference, step.IsSuccess, step.Message);
        }
        else if (attribute.Equals("Resv", StringComparison.OrdinalIgnoreCase) || attribute.Equals("ResvTms", StringComparison.OrdinalIgnoreCase))
        {
            if (step.IsSuccess)
            {
                IsReserved = true;
                Transition(MmsReportLifecyclePhase.Reserved);
            }
            AddEvent(MmsReportLifecycleEventKind.Reservation, step.Reference, step.IsSuccess, step.Message);
        }
        else if (attribute.Equals("RptEna", StringComparison.OrdinalIgnoreCase))
        {
            if (step.IsSuccess)
            {
                IsEnabled = true;
                Transition(MmsReportLifecyclePhase.Enabled);
            }
            AddEvent(MmsReportLifecycleEventKind.Enable, step.Reference, step.IsSuccess, step.Message);
        }
        else if (attribute.Equals("GI", StringComparison.OrdinalIgnoreCase))
        {
            GeneralInterrogationRequested |= step.IsSuccess;
            AddEvent(MmsReportLifecycleEventKind.GeneralInterrogation, step.Reference, step.IsSuccess, step.Message);
        }
        else if (attribute.Equals("DatSet", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Equals("TrgOps", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Equals("OptFlds", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Equals("BufTm", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Equals("IntgPd", StringComparison.OrdinalIgnoreCase))
        {
            if (step.IsSuccess)
                Transition(MmsReportLifecyclePhase.Configured);
            AddEvent(MmsReportLifecycleEventKind.Configuration, step.Reference, step.IsSuccess, step.Message);
        }
        else
        {
            AddEvent(MmsReportLifecycleEventKind.Observation, step.Reference, step.IsSuccess, step.Message);
        }

        if (step.Attempted && !step.IsSuccess)
            _failureCount++;
    }

    private void ObserveStopWriteCore(MmsReportAttributeWriteStep step)
    {
        var attribute = step.Attribute?.Trim() ?? string.Empty;
        var kind = MmsReportLifecycleEventKind.Observation;
        if (attribute.Equals("RptEna", StringComparison.OrdinalIgnoreCase))
        {
            kind = MmsReportLifecycleEventKind.Disable;
            if (step.IsSuccess)
            {
                IsEnabled = false;
                Transition(MmsReportLifecyclePhase.Disabled);
            }
        }
        else if (attribute.Equals("Resv", StringComparison.OrdinalIgnoreCase) || attribute.Equals("ResvTms", StringComparison.OrdinalIgnoreCase))
        {
            kind = MmsReportLifecycleEventKind.ReservationRelease;
            if (step.IsSuccess)
            {
                IsReserved = false;
                Transition(MmsReportLifecyclePhase.Released);
            }
        }
        else if (attribute.Equals("DatSet", StringComparison.OrdinalIgnoreCase))
        {
            kind = MmsReportLifecycleEventKind.DataSetRestore;
        }
        else if (attribute.Equals("DeleteNamedVariableList", StringComparison.OrdinalIgnoreCase))
        {
            kind = MmsReportLifecycleEventKind.DataSetDelete;
            if (step.IsSuccess)
                DataSetOwnership = MmsReportDataSetOwnership.External;
        }

        AddEvent(kind, step.Reference, step.IsSuccess, step.Message);
        if (step.Attempted && !step.IsSuccess)
        {
            _failureCount++;
            if (kind is MmsReportLifecycleEventKind.Disable or MmsReportLifecycleEventKind.ReservationRelease or MmsReportLifecycleEventKind.DataSetDelete)
                RecordResidue(step.Reference, $"Cleanup step {attribute} failed: {step.Message}");
        }
    }

    private void ObserveDataSetSnapshotCore(MmsReportDataSetSnapshot snapshot)
    {
        if (snapshot.IsSuccess && snapshot.Exists)
            Transition(MmsReportLifecyclePhase.DataSetReady);
        AddEvent(MmsReportLifecycleEventKind.DataSetVerify, snapshot.DataSetReference, snapshot.IsSuccess,
            string.IsNullOrWhiteSpace(snapshot.Message) ? snapshot.Summary : snapshot.Message);
    }

    private void ObserveRcbSnapshotCore(MmsReportRcbSnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            AddEvent(MmsReportLifecycleEventKind.Observation, snapshot.Reference, false, snapshot.Message);
            return;
        }

        var enabled = ParseBool(snapshot.EnabledState);
        if (enabled.HasValue)
            IsEnabled = enabled.Value;
        var reserved = ParseBool(snapshot.ReservationState);
        if (reserved.HasValue)
            IsReserved = reserved.Value;
        if (!string.IsNullOrWhiteSpace(snapshot.DataSetReference))
            DataSetReference = snapshot.DataSetReference;

        AddEvent(MmsReportLifecycleEventKind.Observation, snapshot.Reference, true, snapshot.Summary);
    }

    private void ObserveReportCore(MmsReportFrame report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var replayKey = BuildReplayKey(report);
        if (!string.IsNullOrWhiteSpace(replayKey) && !_replayKeys.Add(replayKey))
        {
            Replay.DuplicateReportCount++;
            AddEvent(MmsReportLifecycleEventKind.ReplayDuplicate, report.StreamKey, true,
                $"Duplicate report tolerated and identified by {replayKey}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(replayKey))
        {
            _replayKeyOrder.Enqueue(replayKey);
            while (_replayKeyOrder.Count > MaximumRememberedReplayKeys)
                _replayKeys.Remove(_replayKeyOrder.Dequeue());
        }

        Replay.AcceptedReportCount++;
        Replay.LastEntryIdHex = report.Header.EntryIdHex ?? string.Empty;
        Replay.LastTimeOfEntry = report.Header.TimeOfEntry ?? string.Empty;
        Replay.LastSequenceNumber = report.Header.SequenceNumber;

        if (report.Header.BufferOverflow == true)
        {
            Replay.BufferOverflowCount++;
            AddEvent(MmsReportLifecycleEventKind.BufferOverflow, report.StreamKey, true, "Report advertised buffer overflow evidence.");
        }

        if (report.Header.SequenceNumber is { } sequence)
        {
            if (_lastSequenceByStream.TryGetValue(report.StreamKey, out var previous))
            {
                if (sequence > previous + 1)
                {
                    Replay.SequenceGapCount++;
                    AddEvent(MmsReportLifecycleEventKind.SequenceGap, report.StreamKey, true,
                        $"Sequence advanced from {previous} to {sequence}; this is loss/replay-boundary evidence, not proof of network loss.");
                }
                else if (sequence < previous)
                {
                    Replay.SequenceResetCount++;
                    AddEvent(MmsReportLifecycleEventKind.SequenceReset, report.StreamKey, true,
                        $"Sequence moved backward from {previous} to {sequence}; SqNum reset/wrap is tolerated and EntryID remains the preferred BRCB replay identity when present.");
                }
            }
            _lastSequenceByStream[report.StreamKey] = sequence;
        }

        if (TryParseTimeOfEntry(report.Header.TimeOfEntry, out var timeOfEntry))
        {
            if (_lastTimeOfEntryByStream.TryGetValue(report.StreamKey, out var previousTime) && timeOfEntry < previousTime)
            {
                Replay.TimestampRegressionCount++;
                AddEvent(MmsReportLifecycleEventKind.TimestampRegression, report.StreamKey, true,
                    $"TimeOfEntry regressed from {previousTime:O} to {timeOfEntry:O}.");
            }
            _lastTimeOfEntryByStream[report.StreamKey] = timeOfEntry;
        }

        if (report.Values.Any(value => value.ReasonForInclusion.Contains("general-interrogation", StringComparer.OrdinalIgnoreCase)))
            Replay.GeneralInterrogationReportCount++;
        if (report.Values.Any(value => value.ReasonForInclusion.Contains("integrity", StringComparer.OrdinalIgnoreCase)))
            Replay.IntegrityReportCount++;

        if (Phase == MmsReportLifecyclePhase.Enabled)
            Transition(MmsReportLifecyclePhase.Monitoring);
        AddEvent(MmsReportLifecycleEventKind.Report, report.StreamKey, true, report.Header.Summary);
    }

    private string BuildReplayKey(MmsReportFrame report)
    {
        if (Kind == MmsReportControlKind.Brcb && !string.IsNullOrWhiteSpace(report.Header.EntryIdHex))
            return $"entry:{report.Header.EntryIdHex.Trim()}";
        if (!string.IsNullOrWhiteSpace(report.Header.TimeOfEntry) && report.Header.SequenceNumber.HasValue)
            return $"stream:{report.StreamKey}|toe:{report.Header.TimeOfEntry.Trim()}|sq:{report.Header.SequenceNumber.Value}";
        return string.Empty;
    }

    private void RecordFailure(string target, string message)
    {
        _failureCount++;
        AddEvent(MmsReportLifecycleEventKind.Failure, target, false, message);
        Transition(MmsReportLifecyclePhase.Faulted);
    }

    private void RecordResidue(string target, string message)
    {
        _cleanupHasResidue = true;
        AddEvent(MmsReportLifecycleEventKind.CleanupResidue, target, false, message);
    }

    private void AddEvent(MmsReportLifecycleEventKind kind, string target, bool success, string message)
    {
        _events.Add(new MmsReportLifecycleEvent
        {
            Sequence = ++_eventSequence,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Phase = Phase,
            Kind = kind,
            Target = target ?? string.Empty,
            IsSuccess = success,
            Message = message ?? string.Empty
        });
    }

    private void Transition(MmsReportLifecyclePhase next)
    {
        if (Phase == MmsReportLifecyclePhase.Faulted && next != MmsReportLifecyclePhase.Cleaned)
            return;
        Phase = next;
    }

    private MmsBufferedReportReplayDiagnostics CopyReplay()
        => new()
        {
            AcceptedReportCount = Replay.AcceptedReportCount,
            DuplicateReportCount = Replay.DuplicateReportCount,
            SequenceGapCount = Replay.SequenceGapCount,
            SequenceResetCount = Replay.SequenceResetCount,
            BufferOverflowCount = Replay.BufferOverflowCount,
            TimestampRegressionCount = Replay.TimestampRegressionCount,
            GeneralInterrogationReportCount = Replay.GeneralInterrogationReportCount,
            IntegrityReportCount = Replay.IntegrityReportCount,
            LastEntryIdHex = Replay.LastEntryIdHex,
            LastTimeOfEntry = Replay.LastTimeOfEntry,
            LastSequenceNumber = Replay.LastSequenceNumber
        };

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text is "1")
            return true;
        if (text is "0")
            return false;
        return null;
    }

    private static bool TryParseTimeOfEntry(string? value, out DateTimeOffset parsed)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);
}
