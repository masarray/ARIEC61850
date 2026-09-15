using System.Runtime.CompilerServices;

namespace AR.Iec61850.Mms;

public sealed class MmsHardenedReportMonitorStartResult
{
    public MmsPersistentReportMonitorStartResult Operation { get; init; } = new();
    public MmsReportLifecycleSnapshot? Lifecycle { get; init; }
    public bool IsSuccess => Operation.IsSuccess;
    public MmsPersistentReportMonitorSession? Session => Operation.Session;
}

public sealed class MmsHardenedReportMonitorReceiveResult
{
    public MmsPersistentReportMonitorReceiveResult Operation { get; init; } = new();
    public MmsReportLifecycleSnapshot Lifecycle { get; init; } = new();
    public IReadOnlyList<MmsReportDataSetOrderValidationResult> DataSetOrder { get; init; } = Array.Empty<MmsReportDataSetOrderValidationResult>();
    public bool HasDataSetOrderViolations => DataSetOrder.Any(x => !x.IsValid);
    public bool IsSemanticallyValid => !HasDataSetOrderViolations;
}

public sealed class MmsHardenedReportMonitorStopResult
{
    public MmsPersistentReportMonitorStopResult Operation { get; init; } = new();
    public MmsReportLifecycleSnapshot Lifecycle { get; init; } = new();
    public bool IsSuccess => Operation.IsSuccess && !Lifecycle.CleanupHasResidue;
}

/// <summary>
/// Reporting-hardening facade layered over the established persistent report monitor.
/// Existing MMS wire behavior remains authoritative; this facade adds deterministic
/// lifecycle/replay evidence and keeps it associated with the exact monitor session.
/// </summary>
public sealed partial class MmsClientSession
{
    private readonly ConditionalWeakTable<MmsPersistentReportMonitorSession, MmsReportLifecycleStateMachine> _reportLifecycleStates = new();

    public async Task<MmsHardenedReportMonitorStartResult> StartHardenedPersistentReportMonitorAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = true,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var operation = await StartPersistentReportMonitorAsync(
            plan,
            triggerGeneralInterrogation,
            deleteDynamicDataSetOnStop,
            directory,
            cancellationToken).ConfigureAwait(false);

        MmsReportLifecycleSnapshot? lifecycle = null;
        if (plan.ReportControl is not null)
        {
            var state = new MmsReportLifecycleStateMachine(plan);
            state.ObserveStartResult(operation);
            lifecycle = state.Snapshot();
            if (operation.Session is not null)
            {
                _reportLifecycleStates.Remove(operation.Session);
                _reportLifecycleStates.Add(operation.Session, state);
            }
        }

        return new MmsHardenedReportMonitorStartResult
        {
            Operation = operation,
            Lifecycle = lifecycle
        };
    }

    public async Task<MmsHardenedReportMonitorReceiveResult> ReceiveHardenedPersistentReportMonitorSliceAsync(
        MmsPersistentReportMonitorSession session,
        TimeSpan duration,
        MmsIedModelDirectory? pollDirectory = null,
        IReadOnlyList<string>? pollReferences = null,
        TimeSpan? pollInterval = null,
        bool triggerGeneralInterrogation = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = GetOrCreateReportLifecycleState(session);
        var operation = await ReceivePersistentReportMonitorSliceAsync(
            session,
            duration,
            pollDirectory,
            pollReferences,
            pollInterval,
            triggerGeneralInterrogation,
            cancellationToken).ConfigureAwait(false);
        var dataSetOrder = MmsReportDataSetOrderValidator.ValidateAll(operation.Reports, session.Plan.Members);
        state.ObserveReceiveResult(operation);

        return new MmsHardenedReportMonitorReceiveResult
        {
            Operation = operation,
            Lifecycle = state.Snapshot(),
            DataSetOrder = dataSetOrder
        };
    }

    public async Task<MmsHardenedReportMonitorStopResult> StopHardenedPersistentReportMonitorAsync(
        MmsPersistentReportMonitorSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = GetOrCreateReportLifecycleState(session);
        state.BeginStop();
        var operation = await StopPersistentReportMonitorAsync(session, cancellationToken).ConfigureAwait(false);
        state.ObserveStopResult(operation);
        var lifecycle = state.Snapshot();
        _reportLifecycleStates.Remove(session);

        return new MmsHardenedReportMonitorStopResult
        {
            Operation = operation,
            Lifecycle = lifecycle
        };
    }

    public MmsReportLifecycleSnapshot GetReportLifecycleSnapshot(MmsPersistentReportMonitorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return GetOrCreateReportLifecycleState(session).Snapshot();
    }

    private MmsReportLifecycleStateMachine GetOrCreateReportLifecycleState(MmsPersistentReportMonitorSession session)
        => _reportLifecycleStates.GetValue(session, static key => new MmsReportLifecycleStateMachine(key.Plan));
}
