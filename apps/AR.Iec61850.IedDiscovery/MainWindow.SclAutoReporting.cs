using System.IO;
using System.Windows.Threading;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow
{
    private readonly List<MmsPersistentReportMonitorSession> _sclAutoReportMonitors = new();
    private string? _sclConnectionContextPath;
    private DispatcherTimer? _sclAutoReportTimer;
    private bool _sclAutoReceiveInProgress;
    private bool _sclAutoWindowCloseReentry;

    private async Task TryStartSclAutoReportingAsync()
    {
        var sclPath = !string.IsNullOrWhiteSpace(_sclConnectionContextPath)
            ? _sclConnectionContextPath
            : _openedSclPath;

        if (_activeSession == null || _lastDiscovery == null || !_viewModel.IsConnected ||
            string.IsNullOrWhiteSpace(sclPath) || !File.Exists(sclPath) || _sclAutoReportMonitors.Count > 0)
            return;

        SclDocument scl;
        try
        {
            scl = new SclParser().Load(sclPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Warning", "SCL_REPORT_BOOTSTRAP_PARSE_FAILED", $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (scl.ReportControls.Count == 0)
        {
            _viewModel.AddStatus("Info", "SCL_REPORT_BOOTSTRAP_SKIPPED", "Opened SCL contains no ReportControl declarations; no automatic RCB write was attempted.");
            return;
        }

        _viewModel.AddStatus(
            "Info",
            "SCL_REPORT_BOOTSTRAP_START",
            $"Reconciling {scl.ReportControls.Count} SCL ReportControl family/families against the live MMS directory. Runtime RCB state remains authoritative; unmatched families are fail-closed.");

        MmsSclReportGroupBootstrapResult group;
        try
        {
            group = await _activeSession.StartSclPersistentReportMonitorsWithInitialGiAsync(
                scl.ReportControls,
                _lastDiscovery.ReportInventory,
                _lastDataSetDirectories,
                initialReportTimeout: TimeSpan.FromSeconds(5),
                allowUrCbFallback: false,
                allowPollingFallback: false,
                deleteDynamicDataSetOnStop: false,
                directory: _lastDiscovery.IedDirectory,
                cancellationToken: CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Warning", "SCL_REPORT_BOOTSTRAP_FAILED", $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        _lastDataSetDirectories = group.DataSetDirectories;

        foreach (var warning in group.Warnings.Take(12))
            _viewModel.AddStatus("Warning", "SCL_REPORT_BOOTSTRAP_WARNING", warning);

        foreach (var item in group.Items)
        {
            var mode = item.ReportControl.Buffered ? "BRCB" : "URCB";
            var selected = item.Session?.ReportControl.Reference ?? "-";
            var level = item.IsMonitoring ? "Info" : "Warning";
            var code = item.IsMonitoring ? "SCL_REPORT_FAMILY_RUNNING" : "SCL_REPORT_FAMILY_BLOCKED";
            _viewModel.AddStatus(level, code, $"{mode} {item.ReportControl.ControlBlockReference} -> {selected}. {item.Message}");

            var bootstrap = item.Bootstrap?.Bootstrap;
            if (bootstrap != null)
            {
                foreach (var write in bootstrap.Start.WriteSteps.Take(12))
                    _viewModel.AddStatus(write.IsSuccess ? "Info" : "Warning", "SCL_REPORT_START_WRITE", $"{selected} {write.Attribute}: {write.Message}");
                foreach (var write in bootstrap.InitialReceive.WriteSteps.Take(8))
                    _viewModel.AddStatus(write.IsSuccess ? "Info" : "Warning", "SCL_REPORT_BOOT_WRITE", $"{selected} {write.Attribute}: {write.Message}");

                _viewModel.AddStatus(
                    bootstrap.HasInitialValues ? "Info" : "Warning",
                    bootstrap.HasInitialValues ? "SCL_REPORT_INITIAL_VALUES" : "SCL_REPORT_INITIAL_PENDING",
                    $"{selected}: GI-capability={bootstrap.GiCapabilityObserved}, GI-attempted={bootstrap.GiAttempted}, GI-success={bootstrap.GiSucceeded}, initial-reports={bootstrap.InitialReports.Count}.");
            }

            // Project frames in receive order. Buffered history is therefore
            // naturally collapsed by MonitorSignalRow to the latest value per
            // DataSet member rather than exposed as competing initial snapshots.
            foreach (var report in item.InitialReports)
                ApplyReportFrameToMonitor(report);
        }

        foreach (var session in group.Sessions)
        {
            if (_sclAutoReportMonitors.All(existing => !ReferenceEquals(existing, session)))
                _sclAutoReportMonitors.Add(session);
        }

        if (_sclAutoReportMonitors.Count > 0)
        {
            // The legacy activity timer polls when it owns no single RCB session.
            // Automatic SCL mode has its own multi-RCB event receiver below, so
            // keep that legacy polling path gated for the lifetime of the group.
            _monitorPollInProgress = true;
        }

        _viewModel.IsReportMonitorActive = _sclAutoReportMonitors.Count > 0 || _activeReportMonitor is { IsStopped: false };
        _viewModel.AddStatus(
            group.HasAnyMonitoring ? "Info" : "Warning",
            group.HasAnyMonitoring ? "SCL_REPORT_BOOTSTRAP_READY" : "SCL_REPORT_BOOTSTRAP_NO_MONITOR",
            $"{group.Summary} Automatic report mode uses event-driven receive only; no periodic polling is scheduled while these monitors are active.");
    }

    private async Task<MmsPersistentReportMonitorStartResult> StartManualPersistentReportMonitorAsync(
        MmsReportSubscriptionPlan plan,
        bool performGeneralInterrogation)
    {
        if (_activeSession == null || _lastDiscovery == null)
            return new MmsPersistentReportMonitorStartResult { Message = "No active MMS discovery session is available." };

        if (!performGeneralInterrogation)
        {
            return await _activeSession.StartPersistentReportMonitorAsync(
                plan,
                triggerGeneralInterrogation: false,
                deleteDynamicDataSetOnStop: true,
                directory: _lastDiscovery.IedDirectory,
                cancellationToken: CancellationToken.None).ConfigureAwait(true);
        }

        var bootstrap = await _activeSession.StartPersistentReportMonitorWithInitialGiAsync(
            plan,
            initialReportTimeout: TimeSpan.FromSeconds(5),
            deleteDynamicDataSetOnStop: true,
            directory: _lastDiscovery.IedDirectory,
            cancellationToken: CancellationToken.None).ConfigureAwait(true);

        foreach (var report in bootstrap.InitialReports)
            ApplyReportFrameToMonitor(report);

        _viewModel.AddStatus(
            bootstrap.GiSucceeded ? "Info" : "Warning",
            bootstrap.GiSucceeded ? "RCB_MANUAL_INITIAL_GI" : "RCB_MANUAL_INITIAL_GI_PENDING",
            $"Registered-monitor bootstrap: state={bootstrap.State}, GI-capability={bootstrap.GiCapabilityObserved}, GI-attempted={bootstrap.GiAttempted}, GI-success={bootstrap.GiSucceeded}, initial-reports={bootstrap.InitialReports.Count}. {bootstrap.Message}");

        return bootstrap.Start;
    }

    private async Task StopAllReportMonitorsAsync(string code)
    {
        await StopSclAutoReportMonitorsAsync(code).ConfigureAwait(true);
        await StopActiveReportMonitorAsync(code).ConfigureAwait(true);
    }

    private async Task StopSclAutoReportMonitorsAsync(string code)
    {
        if (_activeSession == null || _sclAutoReportMonitors.Count == 0)
        {
            _sclAutoReportMonitors.Clear();
            _monitorPollInProgress = false;
            _viewModel.IsReportMonitorActive = _activeReportMonitor is { IsStopped: false };
            return;
        }

        foreach (var monitor in _sclAutoReportMonitors.ToArray())
        {
            if (monitor.IsStopped)
                continue;

            try
            {
                _viewModel.AddStatus("Info", code, $"Stopping SCL automatic report monitor for {monitor.ReportControl.Reference}.");
                var stop = await _activeSession.StopPersistentReportMonitorAsync(monitor, CancellationToken.None).ConfigureAwait(true);
                foreach (var write in stop.WriteSteps.Take(12))
                    _viewModel.AddStatus(write.IsSuccess ? "Info" : "Warning", "SCL_REPORT_STOP_WRITE", $"{monitor.ReportControl.Reference} {write.Attribute}: {write.Message}");
                _viewModel.AddStatus(stop.IsSuccess ? "Info" : "Warning", "SCL_REPORT_MONITOR_STOPPED", stop.Message);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                _viewModel.AddStatus("Warning", "SCL_REPORT_MONITOR_STOP_FAILED", $"{monitor.ReportControl.Reference}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _sclAutoReportMonitors.Clear();
        _monitorPollInProgress = false;
        _viewModel.IsReportMonitorActive = _activeReportMonitor is { IsStopped: false };
    }

    /// <summary>
    /// Drains all automatically-started SCL report sessions without polling.
    /// Returns true while automatic report mode owns the online receive path.
    /// </summary>
    private async Task<bool> ReceiveSclAutoReportMonitorsAsync()
    {
        if (_activeSession == null || _sclAutoReportMonitors.Count == 0)
            return false;

        _sclAutoReportMonitors.RemoveAll(monitor => monitor.IsStopped);
        if (_sclAutoReportMonitors.Count == 0)
        {
            _monitorPollInProgress = false;
            _viewModel.IsReportMonitorActive = _activeReportMonitor is { IsStopped: false };
            return false;
        }

        if (_sclAutoReceiveInProgress)
            return true;

        _sclAutoReceiveInProgress = true;
        try
        {
            var received = 0;
            foreach (var monitor in _sclAutoReportMonitors.ToArray())
            {
                if (monitor.IsStopped)
                    continue;

                var slice = await _activeSession.ReceivePersistentReportMonitorSliceAsync(
                    monitor,
                    TimeSpan.FromMilliseconds(120),
                    pollDirectory: null,
                    pollReferences: null,
                    pollInterval: null,
                    triggerGeneralInterrogation: false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(true);

                foreach (var report in slice.Reports)
                {
                    ApplyReportFrameToMonitor(report);
                    received++;
                }
            }

            if (received > 0)
                _viewModel.AddStatus("Info", "SCL_REPORT_RECEIVED", $"Received {received} event-driven report frame(s) across {_sclAutoReportMonitors.Count} SCL monitor(s); no polling/GI was issued by the steady-state receive path.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Warning", "SCL_REPORT_RECEIVE_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _sclAutoReceiveInProgress = false;
        }

        _sclAutoReportMonitors.RemoveAll(monitor => monitor.IsStopped);
        if (_sclAutoReportMonitors.Count == 0)
            _monitorPollInProgress = false;
        _viewModel.IsReportMonitorActive = _sclAutoReportMonitors.Count > 0 || _activeReportMonitor is { IsStopped: false };
        return _sclAutoReportMonitors.Count > 0;
    }
}
