using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using AR.Iec61850.IedDiscovery.ViewModels;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Export;
using Microsoft.Win32;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow : Window
{
    private readonly IedDiscoveryViewModel _viewModel = new();
    private CancellationTokenSource? _cancellation;
    private MmsClientSession? _activeSession;
    private AR.Iec61850.Mms.MmsDiscoveryResult? _lastDiscovery;
    private Iec61850DiscoveredIdentity? _identity;
    private IReadOnlyList<MmsDataSetDirectoryResult> _lastDataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
    private readonly DispatcherTimer _monitorTimer;
    private bool _monitorPollInProgress;
    private bool _reportReceiveInProgress;
    private MmsPersistentReportMonitorSession? _activeReportMonitor;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += MonitorTimer_Tick;
        _monitorTimer.Start();
        _viewModel.AddStatus("Info", "READY", "IED Discovery Workbench is ready. Use Discover IED to connect or Open SCL for offline inspection.");
    }

    protected override async void OnClosed(EventArgs e)
    {
        _monitorTimer.Stop();
        _cancellation?.Cancel();
        await CloseSessionAsync().ConfigureAwait(true);
        base.OnClosed(e);
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        var dialogVm = new DiscoverIedDialogViewModel
        {
            Host = _viewModel.Host,
            Port = _viewModel.Port,
            TimeoutMs = _viewModel.TimeoutMs,
            Name = _viewModel.LastDocument?.IedName ?? "IED",
            ReadDataSetDirectories = _viewModel.ReadDataSetDirectories,
            ProbeReportAttributes = _viewModel.ProbeReportAttributes
        };
        foreach (var profile in ConnectionProfileStore.Load())
            dialogVm.PreviousConnections.Add(profile);

        var dialog = new DiscoverIedWindow(dialogVm) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _viewModel.Host = dialogVm.Host.Trim();
        _viewModel.Port = dialogVm.Port <= 0 ? 102 : dialogVm.Port;
        _viewModel.TimeoutMs = Math.Max(1000, dialogVm.TimeoutMs);
        _viewModel.ReadDataSetDirectories = dialogVm.ReadDataSetDirectories;
        _viewModel.ProbeReportAttributes = dialogVm.ProbeReportAttributes;

        await RunDiscoveryAsync(dialogVm.Name).ConfigureAwait(true);
    }

    private async Task RunDiscoveryAsync(string iedName)
    {
        await CloseSessionAsync().ConfigureAwait(true);
        _viewModel.ClearResults();
        _viewModel.IsBusy = true;
        _viewModel.IsConnected = false;
        _viewModel.IsOnline = false;
        _viewModel.AddStatus("Info", "DISCOVERY_START", $"Starting MMS discovery for {_viewModel.Host}:{_viewModel.Port}.");
        _cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, _viewModel.TimeoutMs + 30000)));

        try
        {
            _activeSession = new MmsClientSession();
            _viewModel.AddStatus("Info", "CONNECTING", "Opening TCP/COTP/ACSE/MMS association...");
            await _activeSession.ConnectAsync(
                _viewModel.Host,
                _viewModel.Port,
                TimeSpan.FromMilliseconds(Math.Max(1000, _viewModel.TimeoutMs)),
                _cancellation.Token).ConfigureAwait(true);

            _viewModel.IsConnected = true;
            _viewModel.IsOnline = true;
            _viewModel.AddStatus("Info", "ASSOCIATED", "MMS association is established. Reading model directory...");

            _lastDiscovery = await _activeSession.DiscoverAsync(
                _viewModel.ProbeReportAttributes,
                Math.Max(0, _viewModel.MaxReportProbes),
                _cancellation.Token).ConfigureAwait(true);
            _identity = Iec61850IdentityResolver.ResolveFromDomains(_lastDiscovery.IedDirectory.LogicalDevices.Keys, _viewModel.Host, iedName);
            _viewModel.AddStatus("Info", "IED_IDENTITY", $"Resolved IED identity: {_identity.DisplayName} ({_identity.Source}, {_identity.Confidence}).");
            _viewModel.AddStatus("Info", "MODEL_DISCOVERED", _lastDiscovery.Summary);

            IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
            if (_viewModel.ReadDataSetDirectories)
            {
                _viewModel.AddStatus("Info", "DATASET_DIRECTORY", "Reading DataSet directories with capped probes...");
                dataSetDirectories = await _activeSession.GetDataSetDirectoriesAsync(
                    _lastDiscovery.ReportInventory.DataSets.Select(x => x.Reference).Take(Math.Max(0, _viewModel.MaxDataSetDirectoryReads)),
                    _lastDiscovery.IedDirectory,
                    _cancellation.Token).ConfigureAwait(true);
            }

            IReadOnlyList<MmsVariableAccessAttributesResult> typeAttributes = Array.Empty<MmsVariableAccessAttributesResult>();
            if (_viewModel.ReadVariableTypes)
            {
                _viewModel.AddStatus("Info", "TYPE_SIGNATURES", "Sampling variable type signatures...");
                typeAttributes = await _activeSession.GetVariableAccessAttributesBatchAsync(
                    _lastDiscovery.IedDirectory.Points.Select(x => x.ToObjectReference()),
                    Math.Max(0, _viewModel.MaxTypeReads),
                    _cancellation.Token).ConfigureAwait(true);
            }

            _lastDataSetDirectories = dataSetDirectories;
            _viewModel.AddStatus("Info", "BUILD_SNAPSHOT", "Building batched discovery snapshot for UI rendering...");
            var document = await Task.Run(() => LiveIedModelDiscoveryBuilder.Build(
                _lastDiscovery,
                new LiveIedModelDiscoveryBuildOptions
                {
                    Host = _viewModel.Host,
                    Port = _viewModel.Port,
                    IedName = _identity?.DisplayName ?? iedName,
                    AccessPointName = "AP1"
                },
                dataSetDirectories,
                typeAttributes), _cancellation.Token).ConfigureAwait(true);

            Populate(document);
            _identity = Iec61850IdentityResolver.Resolve(document);
            ConnectionProfileStore.Save(new ConnectionProfileRow(_viewModel.Host, _viewModel.Port, _identity.DisplayName, _viewModel.TimeoutMs));
            _viewModel.LastDocument = document;
            _viewModel.LastReportProfile = TryCreateFirstStaticReportProfile(_lastDiscovery.ReportInventory, dataSetDirectories);
            _viewModel.ReportProfileSummary = _viewModel.LastReportProfile?.Summary ?? "No safe static report session profile could be planned from this snapshot.";
            _viewModel.AddStatus("Info", "DISCOVERY_READY", "Discovery complete. Select a DO for DA details, an RCB for report readiness, or pin signals to the monitor.");
        }
        catch (OperationCanceledException)
        {
            _viewModel.AddStatus("Warning", "DISCOVERY_CANCELLED", "Discovery cancelled by user or timeout.");
            await CloseSessionAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            _viewModel.AddStatus("Error", "DISCOVERY_FAILED", $"{ex.GetType().Name}: {ex.Message}");
            await CloseSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void OpenScl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open SCL file",
            Filter = "SCL files (*.scd;*.cid;*.icd;*.iid;*.sed;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.sed;*.ssd|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var projected = SclLiveModelProjectionBuilder.Load(dialog.FileName);
            Populate(projected);
            _lastDiscovery = null;
            _lastDataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
            _viewModel.IsConnected = false;
            _viewModel.IsOnline = false;
            _viewModel.AddStatus("Info", "SCL_OPENED", $"Loaded offline SCL model projection: {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "SCL_OPEN_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SaveScl_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastDocument == null)
        {
            MessageBox.Show(this, "Run live discovery first. SCL export uses the discovered live IED snapshot.", "No live model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save discovered IED model as IID",
            Filter = "IID capability/update file (*.iid)|*.iid|CID configured IED file (*.cid)|*.cid|SCL document (*.scd)|*.scd|All files (*.*)|*.*",
            FileName = $"{SafeFile(_viewModel.LastDocument.IedName)}-discovered.iid"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var result = LiveIedSclExporter.WriteFiles(
                _viewModel.LastDocument,
                dialog.FileName,
                new LiveIedSclExportOptions
                {
                    Profile = "safe-connection",
                    IpAddress = _viewModel.Host
                });
            _viewModel.AddStatus("Info", "SCL_EXPORTED", $"Saved IID/SCL model with engine export evidence: {result.SclPath}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "SCL_EXPORT_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void CloseIed_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        await CloseSessionAsync().ConfigureAwait(true);
        _viewModel.ClearResults();
        _lastDiscovery = null;
        _viewModel.AddStatus("Info", "IED_CLOSED", "IED session closed and explorer/panels were cleared.");
    }

    private void Online_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsConnected || _activeSession == null)
        {
            _viewModel.IsOnline = false;
            _viewModel.AddStatus("Warning", "ONLINE_NOT_READY", "Online mode requires a successful live MMS discovery session.");
            return;
        }

        _viewModel.IsOnline = !_viewModel.IsOnline;
        _viewModel.AddStatus("Info", "ONLINE_TOGGLED", _viewModel.IsOnline ? "IED session is online-ready. Read, report and monitor actions are enabled." : "Online monitor paused. Live read/report actions are gated.");
        _viewModel.RaiseCommandState();
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession == null || !_viewModel.IsConnected || !_viewModel.IsOnline)
        {
            _viewModel.AddStatus("Warning", "READ_NOT_ONLINE", "Manual read requires an active online MMS session. Use Discover IED and keep Online enabled.");
            return;
        }

        var selectedRows = BuildSmartReadableRowsForSelection().Take(128).ToArray();
        if (selectedRows.Length == 0)
        {
            _viewModel.AddStatus("Warning", "READ_NO_TARGET", "Select a DO or DA row before reading.");
            return;
        }

        _viewModel.IsBusy = true;
        _cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(5000, _viewModel.TimeoutMs)));
        try
        {
            foreach (var target in selectedRows)
            {
                if (string.IsNullOrWhiteSpace(target.Reference) || string.Equals(target.Reference, "-", StringComparison.Ordinal))
                    continue;

                target.Status = "reading";
                var reference = MmsObjectReference.Parse(target.Reference, target.Fc);
                var result = await _activeSession.ReadSingleVariableAsync(reference, _cancellation.Token).ConfigureAwait(true);
                target.Status = result.IsSuccess ? "read" : "failed";
                if (result.IsSuccess)
                {
                    MmsValueDetailTreeBuilder.ApplyReadValue(target, result.Value, target.Reference);
                }
                else
                {
                    target.Status = "failed";
                    target.Value = result.Message;
                    target.Timestamp = DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss");
                }
            }
            MmsValueDetailTreeBuilder.ApplySmartSummaries(_viewModel.DetailRootRows);
            _viewModel.RefreshDetailRows();
            _viewModel.AddStatus("Info", "READ_COMPLETE", $"Manual read completed for {selectedRows.Length} target(s).");
        }
        catch (OperationCanceledException)
        {
            _viewModel.AddStatus("Warning", "READ_CANCELLED", "Manual read cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "READ_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void ReadAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddStatus("Info", "READ_ALL_STAGED", "Read all is intentionally guarded. Select one LN/DO/DataSet and use Read for the current alpha shell.");
    }

    private async void EnableRcb_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsReportMonitorActive)
        {
            await StopActiveReportMonitorAsync("RCB_MONITOR_STOP_REQUEST").ConfigureAwait(true);
            return;
        }

        var selectedReference = ResolveSelectedReportReference();
        if (string.IsNullOrWhiteSpace(selectedReference))
        {
            _viewModel.AddStatus("Warning", "RCB_ENABLE_NO_SELECTION", "Select or double-click an RCB before enabling reports.");
            return;
        }

        if (_activeSession == null || _lastDiscovery == null || !_viewModel.IsConnected || !_viewModel.IsOnline)
        {
            _viewModel.AddStatus("Warning", "RCB_ENABLE_NOT_CONNECTED", "No active online MMS session is available for report enable.");
            return;
        }

        var rcb = await RefreshReportControlRuntimeAsync(selectedReference, updateUi: true).ConfigureAwait(true);
        if (rcb == null)
        {
            _viewModel.AddStatus("Error", "RCB_ENABLE_NO_CANDIDATE", $"Selected report control {selectedReference} was not found in the live report inventory.");
            return;
        }

        var dataSets = _viewModel.LastDocument?.DataSets.Select(x => x.Reference) ?? Array.Empty<string>();
        var dialogVm = new EnableReportDialogViewModel(rcb, dataSets);
        var dialog = new EnableReportWindow(dialogVm) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            _viewModel.AddStatus("Info", "RCB_ENABLE_CANCELLED", $"Enable dialog cancelled for {rcb.Reference}.");
            return;
        }

        // Re-probe immediately before planning. RCB ownership can change while the dialog is open.
        rcb = await RefreshReportControlRuntimeAsync(rcb.Reference, updateUi: true).ConfigureAwait(true) ?? rcb;
        var isDynamicSlot = string.IsNullOrWhiteSpace(rcb.DataSetReference);
        var plan = BuildSmartReportPlan(rcb, dialogVm.SelectedDataSet, isDynamicSlot);

        if (!plan.IsReady)
        {
            var blockers = plan.Blockers.Count == 0 ? "Report plan is not ready." : string.Join(Environment.NewLine, plan.Blockers);
            MessageBox.Show(this, blockers, "Report plan blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.AddStatus("Warning", "RCB_ENABLE_BLOCKED", blockers.Replace(Environment.NewLine, " "));
            return;
        }

        var actualRcb = plan.ReportControl ?? rcb;
        var actualDynamic = plan.Mode == MmsReportSubscriptionPlanMode.DynamicDataSet;
        var confirm = MessageBox.Show(this,
            actualDynamic
                ? $"This will create a temporary dynamic DataSet ({plan.DataSetReference}), bind {actualRcb.Reference}, enable reports, and keep the monitor running until Stop RCB or Close IED. Continue?"
                : $"This will enable reports on {actualRcb.Reference} and keep RptEna=true until Stop RCB or Close IED. Continue?",
            actualDynamic ? "Start Dynamic Report Monitor" : "Start Report Monitor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _viewModel.IsBusy = true;
        try
        {
            _viewModel.AddStatus("Info", "RCB_MONITOR_START", $"Starting persistent {plan.Mode} report monitor for {actualRcb.Reference} using {plan.DataSetReference}. Selected by Smart RCB policy: {plan.RcbSelection.SelectedRcbReference}.");
            foreach (var warning in plan.Warnings.Take(5))
                _viewModel.AddStatus("Warning", "RCB_PLAN_WARNING", warning);

            var start = await _activeSession.StartPersistentReportMonitorAsync(
                plan,
                triggerGeneralInterrogation: dialogVm.PerformGeneralInterrogation,
                deleteDynamicDataSetOnStop: true,
                directory: _lastDiscovery.IedDirectory,
                cancellationToken: CancellationToken.None).ConfigureAwait(true);

            foreach (var write in start.WriteSteps.Take(16))
                _viewModel.AddStatus(write.IsSuccess ? "Info" : "Warning", "RCB_WRITE", $"{write.Attribute}: {write.Message}");
            foreach (var warning in start.Warnings.Take(8))
                _viewModel.AddStatus("Warning", "RCB_MONITOR_WARNING", warning);

            if (!start.IsSuccess || start.Session == null)
            {
                _viewModel.AddStatus("Error", "RCB_MONITOR_START_FAILED", start.Message);
                MessageBox.Show(this, start.Message, "Report monitor failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _activeReportMonitor = start.Session;
            _viewModel.IsReportMonitorActive = true;
            _viewModel.AddStatus("Info", "RCB_MONITOR_RUNNING", start.Message);
            await RefreshReportControlRuntimeAsync(actualRcb.Reference, updateUi: true).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "RCB_MONITOR_START_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private string ResolveSelectedReportReference()
    {
        if (_viewModel.SelectedNode?.Kind == ExplorerNodeKind.ReportControl && !string.IsNullOrWhiteSpace(_viewModel.SelectedNode.Reference))
            return _viewModel.SelectedNode.Reference;

        if (_viewModel.SelectedDetailRow?.Source == "ReportList" && !string.IsNullOrWhiteSpace(_viewModel.SelectedDetailRow.Reference))
            return _viewModel.SelectedDetailRow.Reference;

        return string.Empty;
    }

    private async Task StopActiveReportMonitorAsync(string code)
    {
        if (_activeSession == null || _activeReportMonitor == null)
        {
            _viewModel.IsReportMonitorActive = false;
            _activeReportMonitor = null;
            _viewModel.AddStatus("Info", code, "No active report monitor is running.");
            return;
        }

        var rcbReference = _activeReportMonitor.ReportControl.Reference;
        _viewModel.IsBusy = true;
        try
        {
            _viewModel.AddStatus("Info", code, $"Stopping persistent report monitor for {rcbReference}.");
            var stop = await _activeSession.StopPersistentReportMonitorAsync(_activeReportMonitor, CancellationToken.None).ConfigureAwait(true);
            foreach (var write in stop.WriteSteps.Take(16))
                _viewModel.AddStatus(write.IsSuccess ? "Info" : "Warning", "RCB_STOP_WRITE", $"{write.Attribute}: {write.Message}");
            _viewModel.AddStatus(stop.IsSuccess ? "Info" : "Warning", stop.IsSuccess ? "RCB_MONITOR_STOPPED" : "RCB_MONITOR_STOPPED_WITH_WARNINGS", stop.Message);
            await RefreshReportControlRuntimeAsync(rcbReference, updateUi: true).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "RCB_MONITOR_STOP_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _activeReportMonitor = null;
            _viewModel.IsReportMonitorActive = false;
            _viewModel.IsBusy = false;
        }
    }

    private MmsReportSubscriptionPlan BuildSmartReportPlan(MmsReportControlCandidate selectedRcb, string selectedDataSet, bool preferDynamic)
    {
        if (_lastDiscovery == null)
            return new MmsReportSubscriptionPlan
            {
                Status = MmsReportSubscriptionPlanStatus.Blocked,
                Blockers = new[] { "No live discovery inventory is available." }
            };

        if (preferDynamic)
        {
            var points = BuildDynamicReportPoints(selectedRcb);
            var strictDynamic = MmsReportSubscriptionPlanner.BuildDynamicPlan(
                _lastDiscovery.ReportInventory,
                _lastDiscovery.IedDirectory,
                points,
                preferredLogicalDevice: selectedRcb.Domain,
                preferredRcbReference: selectedRcb.Reference,
                dataSetName: null,
                strictRcb: true,
                allowUrCbFallback: true,
                allowPollingFallback: false);
            if (strictDynamic.IsReady)
                return strictDynamic;

            var sameLdDynamic = MmsReportSubscriptionPlanner.BuildDynamicPlan(
                _lastDiscovery.ReportInventory,
                _lastDiscovery.IedDirectory,
                points,
                preferredLogicalDevice: selectedRcb.Domain,
                preferredRcbReference: null,
                dataSetName: null,
                strictRcb: false,
                allowUrCbFallback: true,
                allowPollingFallback: false);
            if (sameLdDynamic.IsReady)
                return sameLdDynamic;

            return MmsReportSubscriptionPlanner.BuildDynamicPlan(
                _lastDiscovery.ReportInventory,
                _lastDiscovery.IedDirectory,
                points,
                preferredLogicalDevice: null,
                preferredRcbReference: null,
                dataSetName: null,
                strictRcb: false,
                allowUrCbFallback: true,
                allowPollingFallback: false);
        }

        var strictStatic = MmsReportSubscriptionPlanner.BuildStaticPlan(
            _lastDiscovery.ReportInventory,
            _lastDataSetDirectories,
            preferredRcbReference: selectedRcb.Reference,
            preferredDataSetReference: selectedDataSet,
            strictRcb: true,
            allowUrCbFallback: true,
            allowPollingFallback: false);
        if (strictStatic.IsReady)
            return strictStatic;

        var sameDataSetStatic = MmsReportSubscriptionPlanner.BuildStaticPlan(
            _lastDiscovery.ReportInventory,
            _lastDataSetDirectories,
            preferredRcbReference: null,
            preferredDataSetReference: selectedDataSet,
            strictRcb: false,
            allowUrCbFallback: true,
            allowPollingFallback: false);
        if (sameDataSetStatic.IsReady)
            return sameDataSetStatic;

        return MmsReportSubscriptionPlanner.BuildStaticPlan(
            _lastDiscovery.ReportInventory,
            _lastDataSetDirectories,
            preferredRcbReference: null,
            preferredDataSetReference: null,
            strictRcb: false,
            allowUrCbFallback: true,
            allowPollingFallback: false);
    }

    private IReadOnlyList<string> BuildDynamicReportPoints(MmsReportControlCandidate rcb)
    {
        var pinned = _viewModel.MonitorSignals
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference))
            .Select(x => x.Reference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();
        if (pinned.Length > 0)
            return pinned;

        if (_lastDiscovery == null)
            return Array.Empty<string>();

        return _lastDiscovery.IedDirectory.Points
            .Where(x => string.Equals(x.Domain, rcb.Domain, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) || x.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.DataObjectPath.EndsWith("stVal", StringComparison.OrdinalIgnoreCase) ||
                        x.DataObjectPath.EndsWith("general", StringComparison.OrdinalIgnoreCase) ||
                        x.DataObjectPath.EndsWith("mag.f", StringComparison.OrdinalIgnoreCase) ||
                        x.DataObjectPath.EndsWith("cVal.mag.f", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => DynamicPointPriority(x.DataObjectPath))
            .ThenBy(x => x.UserReference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.UserReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static int DynamicPointPriority(string path)
    {
        var text = path.ToUpperInvariant();
        if (text.Contains("POS.STVAL", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (text.Contains("STR.GENERAL", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (text.Contains("OP.GENERAL", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (text.Contains("A.", StringComparison.OrdinalIgnoreCase) || text.Contains("PHV.", StringComparison.OrdinalIgnoreCase))
            return 10;
        return 100;
    }

    private async Task<MmsReportControlCandidate?> RefreshReportControlRuntimeAsync(string reference, bool updateUi)
    {
        if (_activeSession == null || _lastDiscovery == null || string.IsNullOrWhiteSpace(reference))
            return FindReportControlCandidate(reference);

        var candidate = FindReportControlCandidate(reference);
        if (candidate == null)
            return null;

        try
        {
            await _activeSession.ProbeReportControlAttributesAsync(candidate, CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(candidate.DataSetReference) &&
                !_lastDataSetDirectories.Any(x => string.Equals(NormalizeReportReference(x.DataSetReference), NormalizeReportReference(candidate.DataSetReference), StringComparison.OrdinalIgnoreCase)))
            {
                var directory = await _activeSession.GetDataSetDirectoriesAsync(
                    new[] { candidate.DataSetReference },
                    _lastDiscovery.IedDirectory,
                    CancellationToken.None).ConfigureAwait(true);
                _lastDataSetDirectories = _lastDataSetDirectories
                    .Concat(directory)
                    .GroupBy(x => NormalizeReportReference(x.DataSetReference), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToArray();
            }

            _viewModel.AddStatus("Info", "RCB_RUNTIME_READ", $"RCB runtime snapshot: {candidate.Reference} RptEna={TextOrDash(candidate.EnabledState)} DatSet={TextOrDash(candidate.DataSetReference)} Resv={TextOrDash(candidate.ReservationState)} ResvTms={TextOrDash(candidate.ReservationTimeSeconds)}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Warning", "RCB_RUNTIME_READ_FAILED", $"{candidate.Reference}: {ex.GetType().Name}: {ex.Message}");
        }

        if (updateUi && _viewModel.SelectedNode?.Kind == ExplorerNodeKind.ReportControl &&
            string.Equals(_viewModel.SelectedNode.Reference, reference, StringComparison.OrdinalIgnoreCase))
        {
            PopulateDetails(_viewModel.SelectedNode);
        }

        return candidate;
    }

    private MmsReportControlCandidate? FindReportControlCandidate(string reference)
    {
        if (_lastDiscovery == null)
            return null;

        var normalized = NormalizeReportReference(reference);
        return _lastDiscovery.ReportInventory.ReportControls
            .FirstOrDefault(x => NormalizeReportReference(x.Reference).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static MmsReportControlCandidate ToCandidate(LiveIedReportControlModel rcb)
        => new()
        {
            Domain = rcb.Domain,
            LogicalNode = rcb.LogicalNode,
            FunctionalConstraint = rcb.Buffered ? "BR" : "RP",
            Name = rcb.Name,
            Reference = rcb.Reference,
            Buffered = rcb.Buffered,
            DataSetReference = rcb.DataSetReference,
            ReportId = rcb.ReportId,
            ConfRev = rcb.ConfRev,
            TriggerOptions = rcb.TriggerOptions,
            OptionalFields = rcb.OptionalFields,
            BufferTimeMs = rcb.BufferTimeMs,
            IntegrityPeriodMs = rcb.IntegrityPeriodMs,
            EnabledState = rcb.EnabledState,
            ReservationState = rcb.ReservationState,
            ReservationTimeSeconds = rcb.ReservationTimeSeconds,
            Status = rcb.Status
        };

    private static string NormalizeReportReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private void ApplyPollReadToMonitor(MmsReportPollRead poll)
    {
        if (!poll.IsSuccess || string.IsNullOrWhiteSpace(poll.SelectedReference))
            return;

        var signal = _viewModel.MonitorSignals.FirstOrDefault(x => string.Equals(x.Reference, poll.SelectedReference, StringComparison.OrdinalIgnoreCase));
        if (signal == null)
        {
            signal = new MonitorSignalRow(poll.SelectedReference, poll.FunctionalConstraint, "poll", ShortReference(poll.SelectedReference));
            _viewModel.MonitorSignals.Add(signal);
        }

        signal.Source = "poll";
        signal.Value = poll.DisplayValue;
        signal.Quality = "-";
        signal.Status = poll.Message;
        signal.MarkUpdated(poll.ReadAt);
    }

    private void ApplyReportFrameToMonitor(MmsReportFrame frame)
    {
        var projection = MmsReportValueProjector.Project(frame);
        foreach (var warning in projection.Warnings.Take(4))
            _viewModel.AddStatus("Warning", "REPORT_PROJECTOR", warning);

        foreach (var update in projection.Updates)
        {
            if (string.IsNullOrWhiteSpace(update.Reference))
                continue;

            var signal = _viewModel.MonitorSignals.FirstOrDefault(x => string.Equals(x.Reference, update.Reference, StringComparison.OrdinalIgnoreCase));
            if (signal == null)
            {
                signal = new MonitorSignalRow(update.Reference, update.FunctionalConstraint, "report", update.DisplayName);
                _viewModel.MonitorSignals.Add(signal);
            }

            signal.Source = "report";
            signal.Value = update.Value;
            signal.Quality = update.Quality;
            signal.Status = string.IsNullOrWhiteSpace(update.Reason) ? update.ProjectionStatus : update.Reason;
            signal.MarkUpdated(update.UpdatedAt);
            if (!string.IsNullOrWhiteSpace(update.Timestamp) && update.Timestamp != "-")
                signal.Timestamp = update.Timestamp;
        }
    }

    private static IEnumerable<string> ReportCoverageReferences(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            yield break;

        yield return reference;
        foreach (var suffix in new[] { ".stVal", ".general", ".dirGeneral", ".phsA", ".dirPhsA", ".phsB", ".dirPhsB", ".phsC", ".dirPhsC", ".q", ".t" })
            yield return reference + suffix;
    }

    private void Control_Click(object sender, RoutedEventArgs e)
    {
        var reference = _viewModel.SelectedNode?.Reference ?? "-";
        MessageBox.Show(this,
            $"Control object: {reference}\n\nThe shell correctly enables Control only for controllable candidates. Live operate remains disabled until the safe control-model engine milestone.",
            "Control dry-run preview",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        _viewModel.AddStatus("Warning", "CONTROL_DRY_RUN", $"Control dry-run preview opened for {reference}.");
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var row = ResolvePinRow();
        if (row == null)
        {
            _viewModel.AddStatus("Warning", "PIN_NO_SIGNAL", "Select a readable DA row, DO summary row, or DataSet member before pinning.");
            return;
        }

        var reference = row.Reference;
        if (_viewModel.MonitorSignals.Any(x => string.Equals(x.Reference, reference, StringComparison.OrdinalIgnoreCase) && string.Equals(x.FunctionalConstraint, row.Fc, StringComparison.OrdinalIgnoreCase)))
            return;

        var displayName = string.IsNullOrWhiteSpace(row.Name) ? reference : $"{ShortReference(reference)}";
        var signal = new MonitorSignalRow(reference, row.Fc, _viewModel.IsOnline ? "polling" : "manual", displayName)
        {
            Value = row.Value,
            Quality = row.Quality,
            Status = "pinned"
        };
        _viewModel.MonitorSignals.Add(signal);
        _viewModel.AddStatus("Info", "SIGNAL_PINNED", $"Pinned signal to Activity Monitor: {reference} [{row.Fc}]");
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedMonitorSignal;
        if (selected == null)
        {
            var row = ResolvePinRow();
            if (row != null)
                selected = _viewModel.MonitorSignals.FirstOrDefault(x => string.Equals(x.Reference, row.Reference, StringComparison.OrdinalIgnoreCase) && string.Equals(x.FunctionalConstraint, row.Fc, StringComparison.OrdinalIgnoreCase));
        }

        if (selected == null)
        {
            _viewModel.AddStatus("Warning", "UNPIN_NO_SIGNAL", "Select a pinned monitor signal before unpinning.");
            return;
        }

        _viewModel.MonitorSignals.Remove(selected);
        _viewModel.SelectedMonitorSignal = null;
        _viewModel.AddStatus("Info", "SIGNAL_UNPINNED", $"Removed pinned signal from Activity Monitor: {selected.Reference} [{selected.FunctionalConstraint}]");
    }

    private DataAttributeDetailRow? ResolvePinRow()
    {
        if (_viewModel.SelectedDetailRow != null)
        {
            if (IsReadableRow(_viewModel.SelectedDetailRow))
                return _viewModel.SelectedDetailRow;

            var child = ExpandReadableRows(new[] { _viewModel.SelectedDetailRow })
                .OrderBy(PinPriority)
                .FirstOrDefault();
            if (child != null)
                return child;
        }

        return _viewModel.DetailRootRows
            .SelectMany(row => ExpandReadableRows(new[] { row }))
            .OrderBy(PinPriority)
            .FirstOrDefault();
    }

    private static int PinPriority(DataAttributeDetailRow row)
    {
        var name = row.Name.ToUpperInvariant();
        return name switch
        {
            "STVAL" => 0,
            "GENERAL" => 1,
            "CVAL" => 2,
            "INSTCVAL" => 3,
            "MAG" => 4,
            "F" => 5,
            "Q" => 30,
            "T" => 31,
            _ => row.Fc.Equals("ST", StringComparison.OrdinalIgnoreCase) ? 10 : row.Fc.Equals("MX", StringComparison.OrdinalIgnoreCase) ? 20 : 100
        };
    }

    private async void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        foreach (var signal in _viewModel.MonitorSignals)
            signal.RefreshAge(now);

        if (!_viewModel.IsConnected || !_viewModel.IsOnline || _activeSession == null)
            return;

        if (_activeReportMonitor != null && !_reportReceiveInProgress)
        {
            var activeMonitor = _activeReportMonitor;
            _reportReceiveInProgress = true;
            try
            {
                var reportCoveredReferences = activeMonitor.Plan.Members
                    .Select(x => x.UserReference)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .SelectMany(ReportCoverageReferences)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var fallbackPollReferences = _viewModel.MonitorSignals
                    .Where(x => !string.IsNullOrWhiteSpace(x.Reference) && !reportCoveredReferences.Contains(x.Reference))
                    .Select(x => x.Reference)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToArray();

                var monitor = await _activeSession.ReceivePersistentReportMonitorSliceAsync(
                    activeMonitor,
                    TimeSpan.FromMilliseconds(300),
                    pollDirectory: _lastDiscovery?.IedDirectory,
                    pollReferences: fallbackPollReferences,
                    pollInterval: fallbackPollReferences.Length == 0 ? null : TimeSpan.FromSeconds(1),
                    triggerGeneralInterrogation: false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(true);

                foreach (var report in monitor.Reports)
                    ApplyReportFrameToMonitor(report);
                foreach (var poll in monitor.PollReads)
                    ApplyPollReadToMonitor(poll);
                if (monitor.Reports.Count > 0)
                    _viewModel.AddStatus("Info", "REPORT_RECEIVED", $"Received {monitor.Reports.Count} report frame(s). Monitor total={activeMonitor.ReportCount}.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                _viewModel.AddStatus("Warning", "REPORT_RECEIVE_FAILED", $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _reportReceiveInProgress = false;
            }
        }

        if (_viewModel.MonitorSignals.Count == 0 || _monitorPollInProgress || _activeReportMonitor != null)
            return;

        _monitorPollInProgress = true;
        try
        {
            var signals = _viewModel.MonitorSignals.Take(16).ToArray();
            foreach (var signal in signals)
            {
                if (string.IsNullOrWhiteSpace(signal.Reference) || string.IsNullOrWhiteSpace(signal.FunctionalConstraint))
                    continue;

                try
                {
                    var result = await _activeSession.ReadSingleVariableAsync(MmsObjectReference.Parse(signal.Reference, signal.FunctionalConstraint), CancellationToken.None).ConfigureAwait(true);
                    if (result.IsSuccess)
                    {
                        signal.Value = MmsDataValueRenderer.ToCompactString(result.Value, signal.Reference);
                        signal.Quality = MonitorQualityFromValue(signal.Reference, result.Value);
                        signal.Source = "polling";
                        signal.Status = "live";
                        signal.MarkUpdated(DateTimeOffset.Now);
                    }
                    else
                    {
                        signal.Value = result.Message;
                        signal.Quality = "-";
                        signal.Status = "failed";
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
                {
                    signal.Value = ex.Message;
                    signal.Quality = "-";
                    signal.Status = "failed";
                }
            }
        }
        finally
        {
            _monitorPollInProgress = false;
        }
    }

    private static string MonitorQualityFromValue(string reference, MmsDataValue? value)
    {
        if (value == null)
            return "-";
        if (reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
        {
            var quality = Iec61850QualityDecoder.Decode(value);
            return quality.IsDecoded ? quality.Validity : "-";
        }
        return "-";
    }

    private static string ShortReference(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastDocument == null)
        {
            MessageBox.Show(this, "Run live discovery before exporting a discovery JSON document.", "No discovery document", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export live IED discovery JSON",
            Filter = "JSON document (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"ied-discovery-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_viewModel.LastDocument, options));
        _viewModel.AddStatus("Info", "DISCOVERY_EXPORTED", $"Exported discovery document: {dialog.FileName}");
    }

    private void ClearStatus_Click(object sender, RoutedEventArgs e)
        => _viewModel.StatusHistory.Clear();

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not IedExplorerNode node)
            return;

        _viewModel.SelectedNode = node;
        PopulateDetails(node);
        _viewModel.RaiseCommandState();

        if (node.Kind == ExplorerNodeKind.ReportControl && _viewModel.IsConnected && _viewModel.IsOnline)
            _ = RefreshReportControlRuntimeAsync(node.Reference, updateUi: true);
    }

    private void DetailRowExpander_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DataAttributeDetailRow row || !row.HasChildren)
            return;

        row.IsExpanded = !row.IsExpanded;
        _viewModel.SelectedDetailRow = row;
        _viewModel.RefreshDetailRows();
        e.Handled = true;
    }

    private async void DetailGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedDetailRow?.Source != "ReportList")
            return;

        var reference = _viewModel.SelectedDetailRow.Reference;
        var candidate = FindReportControlCandidate(reference);
        if (candidate == null)
            return;

        var syntheticNode = new IedExplorerNode(candidate.Name, ExplorerNodeKind.ReportControl, candidate.Reference, candidate.Mode)
        {
            Model = candidate
        };
        _viewModel.SelectedNode = syntheticNode;
        PopulateDetails(syntheticNode);
        _viewModel.AddStatus("Info", "RCB_DETAIL_OPENED", $"Opened RCB detail from report list: {candidate.Reference}.");

        if (_viewModel.IsConnected && _viewModel.IsOnline)
            await RefreshReportControlRuntimeAsync(candidate.Reference, updateUi: true).ConfigureAwait(true);

        e.Handled = true;
    }

    private IReadOnlyList<DataAttributeDetailRow> BuildSmartReadableRowsForSelection()
    {
        if (_viewModel.SelectedDetailRow != null)
            return ExpandReadableRows(new[] { _viewModel.SelectedDetailRow })
                .OrderBy(PinPriority)
                .ToArray();

        var allRows = _viewModel.DetailRootRows.ToArray();
        if (_viewModel.SelectedNode?.Model is LiveIedLogicalNodeModel logicalNode)
        {
            var targets = Iec61850SmartReadPlanBuilder.BuildForLogicalNode(logicalNode, maxDataObjects: 24);
            var rows = targets
                .Select(target => FindDetailRowByReference(allRows, target.Reference, target.FunctionalConstraint))
                .Where(row => row != null)
                .Cast<DataAttributeDetailRow>()
                .DistinctBy(row => row.Reference + "|" + row.Fc, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (rows.Length > 0)
                return rows;
        }

        if (_viewModel.SelectedNode?.Model is LiveIedDataObjectModel dataObject)
        {
            var targets = Iec61850SmartReadPlanBuilder.BuildForDataObject(dataObject);
            var rows = targets
                .Select(target => FindDetailRowByReference(allRows, target.Reference, target.FunctionalConstraint))
                .Where(row => row != null)
                .Cast<DataAttributeDetailRow>()
                .DistinctBy(row => row.Reference + "|" + row.Fc, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (rows.Length > 0)
                return rows;
        }

        return ExpandReadableRows(allRows)
            .OrderBy(PinPriority)
            .ToArray();
    }

    private static DataAttributeDetailRow? FindDetailRowByReference(IEnumerable<DataAttributeDetailRow> rows, string reference, string fc)
    {
        foreach (var row in rows)
        {
            if (string.Equals(row.Reference, reference, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(fc) || string.Equals(row.Fc, fc, StringComparison.OrdinalIgnoreCase)))
            {
                return row;
            }

            var child = FindDetailRowByReference(row.Children, reference, fc);
            if (child != null)
                return child;
        }

        return null;
    }

    private static IEnumerable<DataAttributeDetailRow> ExpandReadableRows(IEnumerable<DataAttributeDetailRow> roots)
    {
        foreach (var row in roots)
        {
            if (IsReadableRow(row))
            {
                yield return row;
                continue;
            }

            if (!row.HasChildren)
                continue;

            foreach (var child in ExpandReadableRows(row.Children))
                yield return child;
        }
    }

    private static bool IsReadableRow(DataAttributeDetailRow row)
        => !string.IsNullOrWhiteSpace(row.Reference)
           && !row.Reference.Contains('[', StringComparison.Ordinal)
           && !string.Equals(row.Reference, "-", StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(row.Fc)
           && !string.Equals(row.Fc, "-", StringComparison.Ordinal)
           && !row.Source.Equals("QualityTemplate", StringComparison.OrdinalIgnoreCase)
           && !row.Source.Equals("TimestampTemplate", StringComparison.OrdinalIgnoreCase)
           && !row.Source.Equals("CdcControlTemplate", StringComparison.OrdinalIgnoreCase)
           && !row.Source.Equals("OriginTemplate", StringComparison.OrdinalIgnoreCase)
           && !row.Source.Equals("SchemaGroup", StringComparison.OrdinalIgnoreCase)
           && !row.Source.Equals("DataObjectSchema", StringComparison.OrdinalIgnoreCase);

    private async Task CloseSessionAsync()
    {
        if (_activeSession != null && _activeReportMonitor != null)
        {
            try
            {
                var stop = await _activeSession.StopPersistentReportMonitorAsync(_activeReportMonitor, CancellationToken.None).ConfigureAwait(true);
                _viewModel.AddStatus(stop.IsSuccess ? "Info" : "Warning", "RCB_MONITOR_CLOSE", stop.Message);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                _viewModel.AddStatus("Warning", "RCB_MONITOR_CLOSE_FAILED", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        _activeReportMonitor = null;
        _viewModel.IsReportMonitorActive = false;

        if (_activeSession != null)
        {
            await _activeSession.DisposeAsync().ConfigureAwait(true);
            _activeSession = null;
        }
        _viewModel.IsConnected = false;
        _viewModel.IsOnline = false;
        _viewModel.IsBusy = false;
        _lastDataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
        _identity = null;
    }

    private MmsReportSessionProfile? TryCreateFirstStaticReportProfile(MmsReportInventory inventory, IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories)
    {
        if (dataSetDirectories.Count == 0)
            return null;

        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(inventory, dataSetDirectories, allowPollingFallback: false);
        return plan.IsReady
            ? MmsReportSessionProfile.FromPlan(plan, _viewModel.Host, _viewModel.Port, triggerGeneralInterrogation: true, listenDurationSeconds: 60)
            : null;
    }

    private void Populate(LiveIedModelDiscoveryDocument document)
    {
        _viewModel.ClearResults();
        _viewModel.Summary = document.Summary;
        _viewModel.LastDocument = document;
        AddMetrics(document);
        BuildTree(document);
        PopulateLegacyTables(document);
        foreach (var warning in document.Warnings)
            _viewModel.AddStatus("Warning", warning.Code, string.IsNullOrWhiteSpace(warning.Reference) ? warning.Message : $"{warning.Reference}: {warning.Message}");
    }

    private void AddMetrics(LiveIedModelDiscoveryDocument document)
    {
        _viewModel.Metrics.Add(new MetricRow("LD", document.Coverage.LogicalDeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("LN", document.Coverage.LogicalNodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("DO", document.Coverage.DataObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("DA", document.Coverage.DataAttributeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("RCB", document.Coverage.ReportControlCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void BuildTree(LiveIedModelDiscoveryDocument document)
    {
        var identity = Iec61850IdentityResolver.Resolve(document);
        _identity = identity;
        var root = new IedExplorerNode(identity.DisplayName, ExplorerNodeKind.Ied, document.Host, $"{document.Host}:{document.Port}")
        {
            Model = document,
            IsExpanded = true,
            Status = document.Host
        };

        var goose = new IedExplorerNode("GOOSE", ExplorerNodeKind.Section) { IsExpanded = document.GooseControlBlocks.Count > 0 };
        foreach (var block in document.GooseControlBlocks.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
            goose.Children.Add(new IedExplorerNode(block.Name, ExplorerNodeKind.GooseControl, block.Reference, block.DataSetReference) { Model = block });
        root.Children.Add(goose);

        var reports = new IedExplorerNode("Reports", ExplorerNodeKind.Section) { IsExpanded = document.ReportControls.Count > 0 };
        foreach (var byDomain in document.ReportControls.GroupBy(x => x.Domain).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ldTitle = Iec61850IdentityResolver.DisplayLogicalDevice(identity, byDomain.Key);
            var ld = new IedExplorerNode(ldTitle, ExplorerNodeKind.LogicalDevice, byDomain.Key, byDomain.Key) { IsExpanded = true };
            var knownDataSetReferences = document.DataSets.Select(x => x.Reference).ToArray();
            foreach (var rcb in byDomain.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
            {
                var presentation = MmsReportPresentationBuilder.Build(rcb, knownDataSetReferences);
                ld.Children.Add(new IedExplorerNode(rcb.Name, ExplorerNodeKind.ReportControl, rcb.Reference, presentation.ModeLabel)
                {
                    Model = rcb,
                    Status = presentation.TreeStatus
                });
            }
            reports.Children.Add(ld);
        }
        root.Children.Add(reports);

        var settingGroups = new IedExplorerNode("Setting Groups", ExplorerNodeKind.Section);
        foreach (var sg in document.SettingGroupControls.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
            settingGroups.Children.Add(new IedExplorerNode(sg.Name, ExplorerNodeKind.SettingGroup, sg.Reference) { Model = sg });
        root.Children.Add(settingGroups);

        root.Children.Add(new IedExplorerNode("Files", ExplorerNodeKind.Section, string.Empty, "File service browser milestone"));

        var dataSets = new IedExplorerNode("DataSets", ExplorerNodeKind.Section) { IsExpanded = document.DataSets.Count > 0 };
        foreach (var byDomain in document.DataSets.GroupBy(x => x.Domain).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ldTitle = Iec61850IdentityResolver.DisplayLogicalDevice(identity, byDomain.Key);
            var ld = new IedExplorerNode(ldTitle, ExplorerNodeKind.LogicalDevice, byDomain.Key, byDomain.Key) { IsExpanded = true };
            foreach (var ds in byDomain.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
                ld.Children.Add(new IedExplorerNode(ds.Name, ExplorerNodeKind.DataSet, ds.Reference, $"{ds.MemberCount} member(s)") { Model = ds });
            dataSets.Children.Add(ld);
        }
        root.Children.Add(dataSets);

        var model = new IedExplorerNode("Data Model", ExplorerNodeKind.Section) { IsExpanded = true };
        foreach (var logicalDevice in document.LogicalDevices.OrderBy(x => x.MmsDomain, StringComparer.OrdinalIgnoreCase))
        {
            var ldTitle = Iec61850IdentityResolver.DisplayLogicalDevice(identity, logicalDevice.MmsDomain);
            var ld = new IedExplorerNode(ldTitle, ExplorerNodeKind.LogicalDevice, logicalDevice.MmsDomain, logicalDevice.MmsDomain) { Model = logicalDevice, IsExpanded = true };
            foreach (var logicalNode in OrderLogicalNodes(logicalDevice.LogicalNodes))
            {
                var ln = new IedExplorerNode(logicalNode.Name, ExplorerNodeKind.LogicalNode, $"{logicalDevice.MmsDomain}/{logicalNode.Name}", logicalNode.LnClass) { Model = logicalNode };
                foreach (var dataObject in OrderDataObjects(logicalNode.DataObjects))
                    ln.Children.Add(new IedExplorerNode(dataObject.Name, ExplorerNodeKind.DataObject, dataObject.Reference, dataObject.InferredCdc) { Model = dataObject, Status = dataObject.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Unknown ? "!" : string.Empty });
                ld.Children.Add(ln);
            }
            model.Children.Add(ld);
        }
        root.Children.Add(model);

        _viewModel.ExplorerNodes.Add(root);
    }

    private void PopulateSclProfile(SclEngineeringProfile profile)
    {
        _viewModel.ClearResults();
        _viewModel.Summary = $"SCL profile: IED={profile.Ieds.Count}, LD={profile.LogicalDevices.Count}, LN={profile.LogicalNodes.Count}, reports={profile.ReportControlCount}, GOOSE={profile.GooseStreamCount}, SV={profile.SampledValuesStreamCount}.";
        var root = new IedExplorerNode(string.IsNullOrWhiteSpace(profile.HeaderId) ? Path.GetFileName(profile.SourceName) : profile.HeaderId, ExplorerNodeKind.Ied, profile.SourceName)
        {
            IsExpanded = true,
            Status = "offline"
        };
        var model = new IedExplorerNode("Data Model", ExplorerNodeKind.Section) { IsExpanded = true };
        foreach (var ldGroup in profile.LogicalNodes.GroupBy(x => x.LogicalDeviceInst).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ld = new IedExplorerNode(ldGroup.Key, ExplorerNodeKind.LogicalDevice, ldGroup.Key) { IsExpanded = true };
            foreach (var ln in ldGroup.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
                ld.Children.Add(new IedExplorerNode(ln.Reference.Split('/').LastOrDefault() ?? ln.Reference, ExplorerNodeKind.LogicalNode, ln.Reference, $"{ln.DataObjectCount} DO") { Model = ln });
            model.Children.Add(ld);
        }
        root.Children.Add(model);
        var reports = new IedExplorerNode("Reports", ExplorerNodeKind.Section);
        foreach (var report in profile.ProcessBus.ReportControls)
            reports.Children.Add(new IedExplorerNode(report.ControlBlockReference.Split('.').LastOrDefault() ?? report.ControlBlockReference, ExplorerNodeKind.ReportControl, report.ControlBlockReference, report.Buffered ? "BRCB" : "URCB") { Model = report });
        root.Children.Add(reports);
        var goose = new IedExplorerNode("GOOSE", ExplorerNodeKind.Section);
        foreach (var stream in profile.ProcessBus.GooseStreams)
            goose.Children.Add(new IedExplorerNode(stream.ControlBlockReference.Split('.').LastOrDefault() ?? stream.ControlBlockReference, ExplorerNodeKind.GooseControl, stream.ControlBlockReference, stream.DataSetReference) { Model = stream });
        root.Children.Add(goose);
        var sv = new IedExplorerNode("Sampled Values", ExplorerNodeKind.Section);
        foreach (var stream in profile.ProcessBus.SampledValuesStreams)
            sv.Children.Add(new IedExplorerNode(stream.ControlBlockReference.Split('.').LastOrDefault() ?? stream.ControlBlockReference, ExplorerNodeKind.SampledValueControl, stream.ControlBlockReference, stream.DataSetReference) { Model = stream });
        root.Children.Add(sv);
        _viewModel.ExplorerNodes.Add(root);
        foreach (var finding in profile.Findings)
            _viewModel.AddStatus(finding.Severity, finding.Code, finding.Message);
    }

    private void PopulateLegacyTables(LiveIedModelDiscoveryDocument document)
    {
        foreach (var ld in document.LogicalDevices)
            _viewModel.LogicalDevices.Add(new LogicalDeviceRow(ld.MmsDomain, ld.LogicalNodes.Count, ld.LogicalNodes.Sum(x => x.DataObjects.Sum(d => d.Attributes.Count))));

        foreach (var ds in document.DataSets)
            _viewModel.DataSets.Add(new DataSetRow(ds.Reference, ds.MemberCount, string.Join(", ", ds.UsedByReportControls.Take(3)), string.Join(", ", ds.UsedByGooseControls.Take(3)), string.Join(", ", ds.UsedBySampledValueControls.Take(3))));

        foreach (var rcb in document.ReportControls)
            _viewModel.ReportControls.Add(new ReportControlRow(rcb.Reference, rcb.Buffered ? "BRCB" : "URCB", rcb.DataSetReference, rcb.EnabledState, rcb.ReservationState, rcb.ConfRev, rcb.Status));

        foreach (var warning in document.Warnings)
            _viewModel.Warnings.Add(new WarningRow(warning.Code, warning.Message));
    }

    private void PopulateDetails(IedExplorerNode node)
    {
        var rows = new List<DataAttributeDetailRow>();
        _viewModel.SelectedHeader = string.IsNullOrWhiteSpace(node.Reference) ? node.Title : $"{node.Title} • {node.Reference}";
        _viewModel.SelectedSubHeader = node.Kind switch
        {
            ExplorerNodeKind.DataObject => "Data Object selected. Detail table shows expandable DA rows. Select a row and use Read, Control, or Pin.",
            ExplorerNodeKind.ReportControl => "Report Control Block selected. Review the snapshot, then use Enable RCB to open the guarded report dialog.",
            ExplorerNodeKind.DataSet => "DataSet selected. Members are shown in order when the directory has been discovered.",
            ExplorerNodeKind.LogicalNode => "Logical Node selected. Child Data Objects are listed below.",
            _ => node.Subtitle
        };

        if (TryPopulateReportGroupRows(node, rows))
        {
            _viewModel.ReplaceDetailRows(rows);
            return;
        }

        switch (node.Model)
        {
            case LiveIedDataObjectModel dataObject:
            {
                var dataObjectSchema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(dataObject);
                var rootRow = MmsValueDetailTreeBuilder.FromSchema(dataObjectSchema.ToRootNode());
                rootRow.Status = $"{dataObjectSchema.Cdc} schema • {dataObjectSchema.Confidence}";
                rows.Add(rootRow);
                break;
            }
            case LiveIedLogicalNodeModel logicalNode:
            {
                foreach (var dataObject in OrderDataObjects(logicalNode.DataObjects))
                {
                    var childSchema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(dataObject);
                    var row = MmsValueDetailTreeBuilder.FromSchema(childSchema.ToRootNode(), expandRoot: false);
                    row.Status = $"{childSchema.Cdc} schema • {dataObject.Attributes.Count} DA";
                    rows.Add(row);
                }

                break;
            }
            case LiveIedLogicalDeviceModel logicalDevice:
                foreach (var logicalNode in logicalDevice.LogicalNodes)
                    rows.Add(new DataAttributeDetailRow(logicalNode.Name, "-", logicalNode.LnClass, $"{logicalDevice.MmsDomain}/{logicalNode.Name}", "LD directory") { Status = $"{logicalNode.DataObjects.Count} DO" });
                break;
            case LiveIedDataSetModel dataSet:
                foreach (var member in dataSet.Members)
                    rows.Add(new DataAttributeDetailRow(member.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), member.FunctionalConstraint, "DataSet member", member.Reference, member.Confidence.ToString()) { Status = member.MmsReference });
                if (dataSet.Members.Count == 0)
                    rows.Add(new DataAttributeDetailRow(dataSet.Name, "-", "DataSet", dataSet.Reference, "directory not read") { Status = $"{dataSet.MemberCount} member(s)" });
                break;
            case LiveIedReportControlModel rcb:
            {
                var candidate = FindReportControlCandidate(rcb.Reference) ?? ToCandidate(rcb);
                AddReportControlDetails(rows, candidate);
                break;
            }
            case MmsReportControlCandidate rcb:
                AddReportControlDetails(rows, rcb);
                break;
            case LiveIedControlBlockModel cb:
                AddDetail(rows, "Kind", "-", cb.Kind, cb.Reference, cb.Kind);
                AddDetail(rows, "DataSet", "-", "ObjectReference", cb.Reference, cb.DataSetReference);
                AddDetail(rows, "Control ID", "-", "VisibleString", cb.Reference, string.IsNullOrWhiteSpace(cb.ControlId) ? cb.SmvId : cb.ControlId);
                AddDetail(rows, "APPID", "-", "Unsigned", cb.Reference, cb.AppId);
                AddDetail(rows, "ConfRev", "-", "Unsigned", cb.Reference, cb.ConfRev);
                AddDetail(rows, "Status", "-", "Status", cb.Reference, cb.Message);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(node.Reference))
                    rows.Add(new DataAttributeDetailRow(node.Title, "-", node.Kind.ToString(), node.Reference, node.Subtitle) { Status = node.Status });
                break;
        }

        _viewModel.ReplaceDetailRows(rows);
    }

    private bool TryPopulateReportGroupRows(IedExplorerNode node, List<DataAttributeDetailRow> rows)
    {
        if (_lastDiscovery == null)
            return false;

        if (node.Kind == ExplorerNodeKind.Section && node.Title.Equals("Reports", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var rcb in _lastDiscovery.ReportInventory.ReportControls.OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                AddReportListRow(rows, rcb);
            _viewModel.SelectedHeader = $"{_identity?.DisplayName ?? _viewModel.Host} • Reports";
            _viewModel.SelectedSubHeader = "Report Control Blocks. Lock means enabled/reserved by another client; check means static DataSet is mapped; diamond means dynamic slot.";
            return true;
        }

        if (node.Kind == ExplorerNodeKind.LogicalDevice &&
            _lastDiscovery.ReportInventory.ReportControls.Any(x => x.Domain.Equals(node.Reference, StringComparison.OrdinalIgnoreCase)) &&
            node.Model == null)
        {
            foreach (var rcb in _lastDiscovery.ReportInventory.ReportControls.Where(x => x.Domain.Equals(node.Reference, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                AddReportListRow(rows, rcb);
            _viewModel.SelectedHeader = $"{_identity?.DisplayName ?? _viewModel.Host} • Reports • {node.Title}";
            _viewModel.SelectedSubHeader = "Report Control Blocks in this Logical Device. Select an RCB row in the explorer to read full runtime status.";
            return true;
        }

        return false;
    }

    private void AddReportListRow(ICollection<DataAttributeDetailRow> rows, MmsReportControlCandidate rcb)
    {
        var knownDataSets = _lastDataSetDirectories.Select(x => x.DataSetReference).Concat(_viewModel.LastDocument?.DataSets.Select(x => x.Reference) ?? Array.Empty<string>()).ToArray();
        var presentation = MmsReportPresentationBuilder.Build(rcb, knownDataSets);
        var row = new DataAttributeDetailRow(rcb.Name, rcb.FunctionalConstraint, rcb.Mode, rcb.Reference, "ReportList")
        {
            Value = $"{presentation.DetailStatus}   {presentation.StatusIcon}",
            Status = presentation.StatusIcon
        };
        row.ReplaceChildren(new[]
        {
            new DataAttributeDetailRow("RptEna", "RP", "Boolean", rcb.Reference + ".RptEna", "ReportList", row.Level + 1) { Value = TextOrDash(rcb.EnabledState) },
            new DataAttributeDetailRow(rcb.Buffered ? "ResvTms" : "Resv", "RP", rcb.Buffered ? "Integer" : "Boolean", rcb.Reference + (rcb.Buffered ? ".ResvTms" : ".Resv"), "ReportList", row.Level + 1) { Value = rcb.Buffered ? TextOrDash(rcb.ReservationTimeSeconds) : TextOrDash(rcb.ReservationState) },
            new DataAttributeDetailRow("DatSet", "RP", "ObjectReference", rcb.Reference + ".DatSet", "ReportList", row.Level + 1) { Value = string.IsNullOrWhiteSpace(rcb.DataSetReference) ? "dynamic slot" : rcb.DataSetReference },
            new DataAttributeDetailRow("ConfRev", "RP", "Unsigned", rcb.Reference + ".ConfRev", "ReportList", row.Level + 1) { Value = TextOrDash(rcb.ConfRev) }
        }, expand: false);
        rows.Add(row);
    }

    private void AddReportControlDetails(ICollection<DataAttributeDetailRow> rows, MmsReportControlCandidate rcb)
    {
        var knownDataSets = _lastDataSetDirectories.Select(x => x.DataSetReference).Concat(_viewModel.LastDocument?.DataSets.Select(x => x.Reference) ?? Array.Empty<string>()).ToArray();
        var presentation = MmsReportPresentationBuilder.Build(rcb, knownDataSets);
        AddDetail(rows, "Mode", rcb.FunctionalConstraint, "Report", rcb.Reference, presentation.ModeLabel);
        AddDetail(rows, "Availability", rcb.FunctionalConstraint, "Status", rcb.Reference, presentation.DetailStatus);
        AddDetail(rows, "Enabled", rcb.FunctionalConstraint, "Boolean", rcb.Reference + ".RptEna", rcb.EnabledState);
        AddDetail(rows, rcb.Buffered ? "Reservation time" : "Reserved", rcb.FunctionalConstraint, rcb.Buffered ? "Integer" : "Boolean", rcb.Reference + (rcb.Buffered ? ".ResvTms" : ".Resv"), rcb.Buffered ? rcb.ReservationTimeSeconds : rcb.ReservationState);
        AddDetail(rows, "Owner", rcb.FunctionalConstraint, "VisibleString", rcb.Reference + ".Owner", "not present");
        AddDetail(rows, "Report ID", rcb.FunctionalConstraint, "VisibleString", rcb.Reference + ".RptID", rcb.ReportId);
        AddDetail(rows, "DataSet", rcb.FunctionalConstraint, "ObjectReference", rcb.Reference + ".DatSet", string.IsNullOrWhiteSpace(rcb.DataSetReference) ? "dynamic slot" : rcb.DataSetReference);
        AddDetail(rows, "Trigger options", rcb.FunctionalConstraint, "BitString", rcb.Reference + ".TrgOps", FormatBitStringSummary(rcb.TriggerOptions, ReportBitStringKind.TriggerOptions));
        AddDetail(rows, "Optional fields", rcb.FunctionalConstraint, "BitString", rcb.Reference + ".OptFlds", FormatBitStringSummary(rcb.OptionalFields, ReportBitStringKind.OptionalFields));
        AddDetail(rows, "Configuration revision", rcb.FunctionalConstraint, "Unsigned", rcb.Reference + ".ConfRev", rcb.ConfRev);
        AddDetail(rows, "Buffer time (ms)", rcb.FunctionalConstraint, "Unsigned", rcb.Reference + ".BufTm", rcb.BufferTimeMs);
        AddDetail(rows, "Integrity period (ms)", rcb.FunctionalConstraint, "Unsigned", rcb.Reference + ".IntgPd", rcb.IntegrityPeriodMs);
        foreach (var warning in presentation.Warnings.Take(4))
            AddDetail(rows, "Readiness note", rcb.FunctionalConstraint, "Finding", rcb.Reference, warning);
        foreach (var diagnostic in rcb.ProbeDiagnostics.TakeLast(4))
            AddDetail(rows, "Probe note", rcb.FunctionalConstraint, "Finding", rcb.Reference, diagnostic);
    }

    private static IEnumerable<LiveIedLogicalNodeModel> OrderLogicalNodes(IEnumerable<LiveIedLogicalNodeModel> logicalNodes)
        => logicalNodes
            .Select((node, index) => new { node, index })
            .OrderBy(x => LogicalNodePriority(x.node), Comparer<int>.Default)
            .ThenBy(x => x.index, Comparer<int>.Default)
            .ThenBy(x => x.node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.node);

    private static IEnumerable<LiveIedDataObjectModel> OrderDataObjects(IEnumerable<LiveIedDataObjectModel> dataObjects)
        => dataObjects
            .Select((dataObject, index) => new { dataObject, index })
            .OrderBy(x => DataObjectPriority(x.dataObject.Name), Comparer<int>.Default)
            .ThenBy(x => x.index, Comparer<int>.Default)
            .ThenBy(x => x.dataObject.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.dataObject);

    private static int LogicalNodePriority(LiveIedLogicalNodeModel logicalNode)
    {
        var lnClass = string.IsNullOrWhiteSpace(logicalNode.LnClass)
            ? Iec61850ReferenceParts.ParseLogicalNodeName(logicalNode.Name).SclLnClass
            : logicalNode.LnClass;

        return lnClass.ToUpperInvariant() switch
        {
            "LLN0" => 0,
            "LPHD" => 1,

            // High-value SAS / SCADA operation points first.
            "CSWI" => 10,
            "XCBR" => 11,
            "XSWI" => 12,
            "CILO" => 13,
            "PTRC" => 14,

            // Protection logical nodes next.
            "PTOC" => 20,
            "PDIS" => 21,
            "PDIF" => 22,
            "PTOV" => 23,
            "PTUV" => 24,
            "PTOF" => 25,
            "PTUF" => 26,
            "PTEF" => 27,
            "PTTR" => 28,
            "PVOC" => 29,

            // Measurements used frequently by gateway/SCADA/HMI.
            "MMXU" => 40,
            "MMXN" => 41,
            "MMTR" => 42,
            "MSQI" => 43,
            "MHAI" => 44,
            "MSTA" => 45,
            "TCTR" => 46,
            "TVTR" => 47,

            // Generic IO and auxiliary groups after core bay/protection/measurement signals.
            "GGIO" => 60,
            "GAPC" => 61,
            _ => 100
        };
    }

    private static int DataObjectPriority(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        return normalized.ToUpperInvariant() switch
        {
            "MOD" => 0,
            "BEH" => 1,
            "HEALTH" => 2,
            "LOC" => 3,
            "POS" => 4,
            "OP" => 5,
            "STR" => 6,
            "GENERAL" => 7,
            "DIRGENERAL" => 8,
            "PHA" => 9,
            "DIRPHA" => 10,
            "PHSA" => 11,
            "DIRPHSA" => 12,
            "PHSB" => 13,
            "DIRPHSB" => 14,
            "PHSC" => 15,
            "DIRPHSC" => 16,
            "HZ" => 30,
            "TOTW" => 31,
            "TOTVAR" => 32,
            "TOTVA" => 33,
            "TOTPF" => 34,
            "PPV" => 35,
            "PHV" => 36,
            "A" => 37,
            "W" => 38,
            "VAR" => 39,
            "VA" => 40,
            "PF" => 41,
            "NAMPLT" => 900,
            _ => 100
        };
    }

    private static void AddDetail(ICollection<DataAttributeDetailRow> rows, string name, string fc, string type, string reference, string value)
        => rows.Add(new DataAttributeDetailRow(name, fc, type, reference, "RCB attribute") { Value = string.IsNullOrWhiteSpace(value) ? "-" : value, Status = "snapshot" });

    private static string FormatType(LiveIedDataAttributeModel attribute)
        => !string.IsNullOrWhiteSpace(attribute.SclBType)
            ? attribute.SclBType
            : !string.IsNullOrWhiteSpace(attribute.MmsType)
                ? attribute.MmsType
                : attribute.TypeDiscoveryStatus;

    private static string StatusMarker(string status)
        => status.Contains("enabled", StringComparison.OrdinalIgnoreCase) || status.Contains("reserved", StringComparison.OrdinalIgnoreCase) ? "!" : string.Empty;

    private enum ReportBitStringKind
    {
        TriggerOptions,
        OptionalFields
    }

    private static string FormatBitStringSummary(string value, ReportBitStringKind kind)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
            return "-";

        if (!value.StartsWith("bits(", StringComparison.OrdinalIgnoreCase))
            return value;

        var labels = kind == ReportBitStringKind.TriggerOptions
            ? new[] { "DataChange", "QualityChange", "DataUpdate", "Integrity", "GeneralInterrogation" }
            : new[] { "SequenceNumber", "ReportTimestamp", "ReasonForInclusion", "DataSetName", "DataReference", "BufferOverflow", "EntryID", "ConfRev", "Segmentation" };

        var hexStart = value.IndexOf('(');
        var comma = value.IndexOf(',', hexStart + 1);
        if (hexStart < 0 || comma < 0)
            return value;

        var hex = value[(hexStart + 1)..comma].Trim();
        if (hex.Length == 0)
            return value;

        try
        {
            var data = Convert.FromHexString(hex);
            var enabled = new List<string>();
            for (var i = 0; i < labels.Length; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;
                if (byteIndex < data.Length && (data[byteIndex] & (0x80 >> bitIndex)) != 0)
                    enabled.Add(labels[i]);
            }

            return enabled.Count == 0 ? value : string.Join(", ", enabled);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string SafeFile(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "ied" : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            text = text.Replace(c, '_');
        return text;
    }
}
