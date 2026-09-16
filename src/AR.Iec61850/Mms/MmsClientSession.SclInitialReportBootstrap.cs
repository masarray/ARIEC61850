using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public enum MmsSclInitialReportBootstrapState
{
    PlanningBlocked,
    MonitorStartFailed,
    Monitoring
}

public sealed class MmsSclInitialReportBootstrapResult
{
    public MmsSclInitialReportBootstrapState State { get; init; }
    public MmsSclReportSubscriptionPlanResult Planning { get; init; } = new();
    public MmsInitialReportBootstrapResult? Bootstrap { get; init; }

    public bool IsMonitoring => State == MmsSclInitialReportBootstrapState.Monitoring && Bootstrap?.Session is { IsStopped: false };
    public bool HasInitialValues => Bootstrap?.HasInitialValues == true;
    public string Message { get; init; } = string.Empty;
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Application-facing SCL-assisted reporting bootstrap for an already
    /// associated MMS session. The live inventory is authoritative for the
    /// concrete RCB instance; SCL supplies the declarative ReportControl family
    /// and expected DataSet identity.
    ///
    /// No write is attempted when planning cannot reconcile the SCL declaration
    /// to a safe live RCB. When planning succeeds, persistent routing is
    /// registered before the one-shot initial GI request.
    /// </summary>
    public async Task<MmsSclInitialReportBootstrapResult> StartSclPersistentReportMonitorWithInitialGiAsync(
        SclReportControl reportControl,
        MmsReportInventory liveInventory,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        TimeSpan initialReportTimeout,
        bool allowUrCbFallback = false,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentNullException.ThrowIfNull(liveInventory);
        ArgumentNullException.ThrowIfNull(dataSetDirectories);

        var planning = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            liveInventory,
            dataSetDirectories,
            reportControl,
            allowUrCbFallback,
            allowPollingFallback,
            excludedRcbReferences);

        if (!planning.IsReady)
        {
            return new MmsSclInitialReportBootstrapResult
            {
                State = MmsSclInitialReportBootstrapState.PlanningBlocked,
                Planning = planning,
                Message = $"SCL-assisted report bootstrap blocked before any report-control write. {planning.Summary}"
            };
        }

        var bootstrap = await StartPersistentReportMonitorWithInitialGiAsync(
            planning.Plan,
            initialReportTimeout,
            deleteDynamicDataSetOnStop: deleteDynamicDataSetOnStop,
            directory: directory,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var state = bootstrap.Session == null
            ? MmsSclInitialReportBootstrapState.MonitorStartFailed
            : MmsSclInitialReportBootstrapState.Monitoring;

        return new MmsSclInitialReportBootstrapResult
        {
            State = state,
            Planning = planning,
            Bootstrap = bootstrap,
            Message = $"{planning.RcbResolution.Message} {bootstrap.Message}"
        };
    }
}
