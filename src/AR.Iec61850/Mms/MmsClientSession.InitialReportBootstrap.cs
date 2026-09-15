namespace AR.Iec61850.Mms;

public enum MmsInitialReportBootstrapState
{
    StartFailed,
    MonitoringWithoutGiCapability,
    GiWriteFailedMonitoring,
    GiRequestedAwaitingInitialReport,
    InitialValuesReceived
}

public sealed class MmsInitialReportBootstrapResult
{
    public MmsInitialReportBootstrapState State { get; init; }
    public MmsPersistentReportMonitorStartResult Start { get; init; } = new();
    public MmsPersistentReportMonitorReceiveResult InitialReceive { get; init; } = new();
    public MmsPersistentReportMonitorSession? Session => Start.Session;
    public IReadOnlyList<MmsReportFrame> InitialReports => InitialReceive.Reports;
    public bool HasInitialValues => InitialReports.Any(report => report.Values.Count > 0);
    public bool GiCapabilityObserved { get; init; }
    public bool GiAttempted => InitialReceive.WriteSteps.Any(step =>
        step.Attribute.Equals("GI", StringComparison.OrdinalIgnoreCase) && step.Attempted);
    public bool GiSucceeded => InitialReceive.WriteSteps.Any(step =>
        step.Attribute.Equals("GI", StringComparison.OrdinalIgnoreCase) && step.Attempted && step.IsSuccess);
    public string Message { get; init; } = string.Empty;
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Starts a persistent report monitor and performs a one-shot initial-value
    /// bootstrap with GI only after the monitor has been registered for report
    /// routing. This avoids losing a fast GI response between RptEna=true and
    /// monitor registration.
    ///
    /// GI is requested only when the live RCB attribute inventory explicitly
    /// exposes GI. If GI is not proven, the monitor still starts and the method
    /// listens for spontaneous/integrity reports without attempting a blind GI
    /// write.
    /// </summary>
    public async Task<MmsInitialReportBootstrapResult> StartPersistentReportMonitorWithInitialGiAsync(
        MmsReportSubscriptionPlan plan,
        TimeSpan initialReportTimeout,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (initialReportTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialReportTimeout), "Initial report timeout must be positive.");

        // Deliberately suppress GI in StartPersistentReportMonitorAsync. That
        // method registers the monitor after its optional GI write; a fast IED
        // can therefore answer before the persistent routing target exists.
        var start = await StartPersistentReportMonitorAsync(
            plan,
            triggerGeneralInterrogation: false,
            deleteDynamicDataSetOnStop: deleteDynamicDataSetOnStop,
            directory: directory,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!start.IsSuccess || start.Session == null)
        {
            return new MmsInitialReportBootstrapResult
            {
                State = MmsInitialReportBootstrapState.StartFailed,
                Start = start,
                Message = string.IsNullOrWhiteSpace(start.Message)
                    ? "Persistent report monitor could not be started; initial GI was not attempted."
                    : start.Message
            };
        }

        var session = start.Session;
        var giCapabilityObserved = session.ReportControl.Attributes.Contains("GI", StringComparer.OrdinalIgnoreCase);

        var initialReceive = await ReceivePersistentReportMonitorSliceAsync(
            session,
            initialReportTimeout,
            pollDirectory: null,
            pollReferences: null,
            pollInterval: null,
            triggerGeneralInterrogation: giCapabilityObserved,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var hasInitialValues = initialReceive.Reports.Any(report => report.Values.Count > 0);
        var giSucceeded = initialReceive.WriteSteps.Any(step =>
            step.Attribute.Equals("GI", StringComparison.OrdinalIgnoreCase) && step.Attempted && step.IsSuccess);

        var state = hasInitialValues
            ? MmsInitialReportBootstrapState.InitialValuesReceived
            : !giCapabilityObserved
                ? MmsInitialReportBootstrapState.MonitoringWithoutGiCapability
                : !giSucceeded
                    ? MmsInitialReportBootstrapState.GiWriteFailedMonitoring
                    : MmsInitialReportBootstrapState.GiRequestedAwaitingInitialReport;

        var message = state switch
        {
            MmsInitialReportBootstrapState.InitialValuesReceived when giSucceeded =>
                $"Persistent RCB monitor is active and {initialReceive.Reports.Count} report(s) with initial values were received after the one-shot GI request.",
            MmsInitialReportBootstrapState.InitialValuesReceived =>
                $"Persistent RCB monitor is active and {initialReceive.Reports.Count} report(s) with initial values were received during bootstrap.",
            MmsInitialReportBootstrapState.MonitoringWithoutGiCapability =>
                "Persistent RCB monitor is active, but the live RCB directory did not prove a GI attribute. No blind GI write was attempted; waiting for spontaneous/integrity reporting.",
            MmsInitialReportBootstrapState.GiWriteFailedMonitoring =>
                "Persistent RCB monitor is active, but the initial GI write did not succeed. Waiting for spontaneous/integrity reporting.",
            _ =>
                "Persistent RCB monitor is active and GI was requested, but no mapped initial-value report arrived within the bootstrap window."
        };

        return new MmsInitialReportBootstrapResult
        {
            State = state,
            Start = start,
            InitialReceive = initialReceive,
            GiCapabilityObserved = giCapabilityObserved,
            Message = message
        };
    }
}
