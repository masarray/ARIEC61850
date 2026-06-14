using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.AddStatus("Info", "READY", "IED Discovery Workbench is ready. Use Discover IED to connect or Open SCL for offline inspection.");
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

            _viewModel.AddStatus("Info", "BUILD_SNAPSHOT", "Building batched discovery snapshot for UI rendering...");
            var document = await Task.Run(() => LiveIedModelDiscoveryBuilder.Build(
                _lastDiscovery,
                new LiveIedModelDiscoveryBuildOptions
                {
                    Host = _viewModel.Host,
                    Port = _viewModel.Port,
                    IedName = iedName,
                    AccessPointName = "AP1"
                },
                dataSetDirectories,
                typeAttributes), _cancellation.Token).ConfigureAwait(true);

            Populate(document);
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
            var profile = new SclEngineeringProfileBuilder().Load(dialog.FileName);
            PopulateSclProfile(profile);
            _viewModel.IsConnected = false;
            _viewModel.IsOnline = false;
            _viewModel.AddStatus("Info", "SCL_OPENED", $"Loaded SCL engineering profile: {Path.GetFileName(dialog.FileName)}.");
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
            Title = "Save discovered IED as SCL",
            Filter = "SCL document (*.scd)|*.scd|All files (*.*)|*.*",
            FileName = $"{SafeFile(_viewModel.LastDocument.IedName)}-discovered.scd"
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
                    Profile = "full-model",
                    IpAddress = _viewModel.Host
                });
            _viewModel.AddStatus("Info", "SCL_EXPORTED", $"Saved SCL plus export evidence: {result.SclPath}");
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
        _viewModel.AddStatus("Info", "IED_CLOSED", "IED session closed. Offline tree remains available for inspection.");
    }

    private void Online_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsOnline = !_viewModel.IsOnline;
        _viewModel.AddStatus("Info", "ONLINE_TOGGLED", _viewModel.IsOnline ? "Online monitor enabled for future polling/report actions." : "Online monitor paused.");
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession == null || !_viewModel.IsConnected)
        {
            _viewModel.AddStatus("Warning", "READ_NOT_CONNECTED", "No active MMS session is available for manual read.");
            return;
        }

        var selectedRows = DetailGrid.SelectedItem is DataAttributeDetailRow row
            ? new[] { row }
            : _viewModel.DetailRows.Take(32).ToArray();
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
                target.Value = result.IsSuccess ? MmsDataValueRenderer.ToCompactString(result.Value, target.Reference) : result.Message;
                target.Timestamp = DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss");
                if (string.Equals(target.Name, "q", StringComparison.OrdinalIgnoreCase))
                    target.Quality = target.Value;
            }
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

    private void EnableRcb_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode?.Model is not LiveIedReportControlModel rcb)
            return;

        var message = $"Report: {rcb.Reference}\nDataSet: {rcb.DataSetReference}\nConfRev: {rcb.ConfRev}\nEnabled: {rcb.EnabledState}\nReservation: {rcb.ReservationState}\n\nN5.40 exposes the safe RCB context. Guarded enable and GI are scheduled for the report dialog milestone.";
        MessageBox.Show(this, message, "Enable RCB preview", MessageBoxButton.OK, MessageBoxImage.Information);
        _viewModel.AddStatus("Info", "RCB_CONTEXT", $"RCB context opened for {rcb.Reference}.");
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
        var reference = DetailGrid.SelectedItem is DataAttributeDetailRow row && !string.IsNullOrWhiteSpace(row.Reference)
            ? row.Reference
            : _viewModel.SelectedNode?.Reference ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return;

        if (_viewModel.MonitorSignals.Any(x => string.Equals(x.Reference, reference, StringComparison.OrdinalIgnoreCase)))
            return;

        _viewModel.MonitorSignals.Add(new MonitorSignalRow(reference, _viewModel.IsOnline ? "polling" : "manual"));
        _viewModel.AddStatus("Info", "SIGNAL_PINNED", $"Pinned signal to Activity Monitor: {reference}");
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
    }

    private async Task CloseSessionAsync()
    {
        if (_activeSession != null)
        {
            await _activeSession.DisposeAsync().ConfigureAwait(true);
            _activeSession = null;
        }
        _viewModel.IsConnected = false;
        _viewModel.IsOnline = false;
        _viewModel.IsBusy = false;
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
        var root = new IedExplorerNode(string.IsNullOrWhiteSpace(document.IedName) ? document.Host : document.IedName, ExplorerNodeKind.Ied, document.Host, $"{document.Host}:{document.Port}")
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
            var ld = new IedExplorerNode(byDomain.Key, ExplorerNodeKind.LogicalDevice, byDomain.Key) { IsExpanded = true };
            foreach (var rcb in byDomain.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
                ld.Children.Add(new IedExplorerNode(rcb.Name, ExplorerNodeKind.ReportControl, rcb.Reference, rcb.Buffered ? "BRCB" : "URCB") { Model = rcb, Status = StatusMarker(rcb.Status) });
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
            var ld = new IedExplorerNode(byDomain.Key, ExplorerNodeKind.LogicalDevice, byDomain.Key) { IsExpanded = true };
            foreach (var ds in byDomain.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
                ld.Children.Add(new IedExplorerNode(ds.Name, ExplorerNodeKind.DataSet, ds.Reference, $"{ds.MemberCount} member(s)") { Model = ds });
            dataSets.Children.Add(ld);
        }
        root.Children.Add(dataSets);

        var model = new IedExplorerNode("Data Model", ExplorerNodeKind.Section) { IsExpanded = true };
        foreach (var logicalDevice in document.LogicalDevices.OrderBy(x => x.MmsDomain, StringComparer.OrdinalIgnoreCase))
        {
            var ld = new IedExplorerNode(logicalDevice.MmsDomain, ExplorerNodeKind.LogicalDevice, logicalDevice.MmsDomain) { Model = logicalDevice, IsExpanded = true };
            foreach (var logicalNode in logicalDevice.LogicalNodes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var ln = new IedExplorerNode(logicalNode.Name, ExplorerNodeKind.LogicalNode, $"{logicalDevice.MmsDomain}/{logicalNode.Name}", logicalNode.LnClass) { Model = logicalNode };
                foreach (var dataObject in logicalNode.DataObjects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
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
        _viewModel.DetailRows.Clear();
        _viewModel.SelectedHeader = string.IsNullOrWhiteSpace(node.Reference) ? node.Title : $"{node.Title} • {node.Reference}";
        _viewModel.SelectedSubHeader = node.Kind switch
        {
            ExplorerNodeKind.DataObject => "Data Object selected. Detail table shows DA rows. Select a row and use Read or Pin.",
            ExplorerNodeKind.ReportControl => "Report Control Block selected. Review status before opening the guarded enable dialog.",
            ExplorerNodeKind.DataSet => "DataSet selected. Members are shown in order when the directory has been discovered.",
            ExplorerNodeKind.LogicalNode => "Logical Node selected. Child Data Objects are listed below.",
            _ => node.Subtitle
        };

        switch (node.Model)
        {
            case LiveIedDataObjectModel dataObject:
                foreach (var attribute in dataObject.Attributes)
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(
                        string.IsNullOrWhiteSpace(attribute.AttributePath) ? dataObject.Name : attribute.AttributePath,
                        attribute.FunctionalConstraint,
                        FormatType(attribute),
                        attribute.ObjectReference,
                        attribute.Source));
                break;
            case LiveIedLogicalNodeModel logicalNode:
                foreach (var dataObject in logicalNode.DataObjects)
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(dataObject.Name, "-", dataObject.InferredCdc, dataObject.Reference, dataObject.ConfidenceLevel.ToString()) { Status = $"{dataObject.Attributes.Count} DA" });
                break;
            case LiveIedLogicalDeviceModel logicalDevice:
                foreach (var logicalNode in logicalDevice.LogicalNodes)
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(logicalNode.Name, "-", logicalNode.LnClass, $"{logicalDevice.MmsDomain}/{logicalNode.Name}", "LD directory") { Status = $"{logicalNode.DataObjects.Count} DO" });
                break;
            case LiveIedDataSetModel dataSet:
                foreach (var member in dataSet.Members)
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(member.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), member.FunctionalConstraint, "DataSet member", member.Reference, member.Confidence.ToString()) { Status = member.MmsReference });
                if (dataSet.Members.Count == 0)
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(dataSet.Name, "-", "DataSet", dataSet.Reference, "directory not read") { Status = $"{dataSet.MemberCount} member(s)" });
                break;
            case LiveIedReportControlModel rcb:
                AddDetail("Enabled", "RP", "Boolean", rcb.Reference + ".RptEna", rcb.EnabledState);
                AddDetail("Reserved", "RP", "Boolean", rcb.Reference + ".Resv", rcb.ReservationState);
                AddDetail("Owner", "RP", "VisibleString", rcb.Reference + ".Owner", "-");
                AddDetail("Report ID", "RP", "VisibleString", rcb.Reference + ".RptID", rcb.ReportId);
                AddDetail("DataSet", "RP", "ObjectReference", rcb.Reference + ".DatSet", rcb.DataSetReference);
                AddDetail("Trigger options", "RP", "BitString", rcb.Reference + ".TrgOps", rcb.TriggerOptions);
                AddDetail("Optional fields", "RP", "BitString", rcb.Reference + ".OptFlds", rcb.OptionalFields);
                AddDetail("Configuration revision", "RP", "Unsigned", rcb.Reference + ".ConfRev", rcb.ConfRev);
                AddDetail("Buffer time (ms)", "RP", "Unsigned", rcb.Reference + ".BufTm", rcb.BufferTimeMs);
                AddDetail("Integrity period (ms)", "RP", "Unsigned", rcb.Reference + ".IntgPd", rcb.IntegrityPeriodMs);
                break;
            case LiveIedControlBlockModel cb:
                AddDetail("Kind", "-", cb.Kind, cb.Reference, cb.Kind);
                AddDetail("DataSet", "-", "ObjectReference", cb.Reference, cb.DataSetReference);
                AddDetail("Control ID", "-", "VisibleString", cb.Reference, string.IsNullOrWhiteSpace(cb.ControlId) ? cb.SmvId : cb.ControlId);
                AddDetail("APPID", "-", "Unsigned", cb.Reference, cb.AppId);
                AddDetail("ConfRev", "-", "Unsigned", cb.Reference, cb.ConfRev);
                AddDetail("Status", "-", "Status", cb.Reference, cb.Message);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(node.Reference))
                    _viewModel.DetailRows.Add(new DataAttributeDetailRow(node.Title, "-", node.Kind.ToString(), node.Reference, node.Subtitle) { Status = node.Status });
                break;
        }
    }

    private void AddDetail(string name, string fc, string type, string reference, string value)
        => _viewModel.DetailRows.Add(new DataAttributeDetailRow(name, fc, type, reference, "RCB attribute") { Value = string.IsNullOrWhiteSpace(value) ? "-" : value, Status = "snapshot" });

    private static string FormatType(LiveIedDataAttributeModel attribute)
        => !string.IsNullOrWhiteSpace(attribute.SclBType)
            ? attribute.SclBType
            : !string.IsNullOrWhiteSpace(attribute.MmsType)
                ? attribute.MmsType
                : attribute.TypeDiscoveryStatus;

    private static string StatusMarker(string status)
        => status.Contains("enabled", StringComparison.OrdinalIgnoreCase) || status.Contains("reserved", StringComparison.OrdinalIgnoreCase) ? "!" : string.Empty;

    private static string SafeFile(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "ied" : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            text = text.Replace(c, '_');
        return text;
    }
}
