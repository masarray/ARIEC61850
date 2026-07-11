using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AR.Iec61850.IedSimulator.ViewModels;
using AR.Iec61850.Simulation;
using Microsoft.Win32;

namespace AR.Iec61850.IedSimulator;

public partial class MainWindow : Window
{
    private const int ServerPort = 102;

    private readonly IedSimulatorViewModel _viewModel = new();
    private readonly DispatcherTimer _timer;
    private IedSimulatorEngine _engine;
    private IedSimulatorMmsServer? _server;
    private Task? _stopServerTask;
    private bool _isClosing;
    private string _profileName = "Demo feeder";
    private readonly Dictionary<string, SimulatorPointRow> _pointRows = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => StepEngine();
        LoadProfileToView();
    }

    private async void OpenScl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open SCL / SCD / CID / ICD / IID",
            Filter = "SCL files (*.scd;*.cid;*.icd;*.iid;*.xml)|*.scd;*.cid;*.icd;*.iid;*.xml|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await StopServerAsync();
            _timer.Stop();

            var result = new IedSimulatorProfileBuilder().FromScl(dialog.FileName);
            _engine = new IedSimulatorEngine(result.Profile);
            _profileName = string.IsNullOrWhiteSpace(result.SelectedIedName) ? Path.GetFileName(dialog.FileName) : result.SelectedIedName;

            _viewModel.IsRunning = false;
            _viewModel.Events.Clear();
            _viewModel.Activities.Clear();
            _viewModel.DataSets.Clear();
            _viewModel.Reports.Clear();
            LoadProfileToView();

            var note = result.Findings.Count > 0 ? $" ({result.Findings.Count} note(s))" : string.Empty;
            _viewModel.Status = $"Loaded {Path.GetFileName(dialog.FileName)} as IED {_profileName}{note}. Press Start to open the MMS server.";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Failed to open SCL: {ex.Message}";
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_stopServerTask is not null)
        {
            _viewModel.Status = "MMS server is still stopping. Please wait before starting it again.";
            return;
        }

        _engine.Start();
        _timer.Start();
        _viewModel.IsRunning = true;
        StartServer();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _engine.Stop();
        _viewModel.IsRunning = false;
        await StopServerAsync();
        _viewModel.Status = "Simulator stopped.";
    }

    private void StartServer()
    {
        if (_server is not null)
            return;

        try
        {
            var server = IedSimulatorMmsServer.Create(_engine, new IedSimulatorMmsServerOptions
            {
                Host = "127.0.0.1",
                Port = ServerPort,
                ServerName = _profileName
            });

            server.Activity += (_, activity) => OnUi(() => AppendActivity(activity, server));

            server.Start();
            _server = server;
            _viewModel.ServerEndpoint = $"127.0.0.1:{server.BoundPort}";
            _viewModel.ServerStatus = $"MMS server: listening on 127.0.0.1:{server.BoundPort} (read-only).";
            _viewModel.Status = $"Simulator running as IED {_profileName}; MMS server open on port {server.BoundPort}.";
        }
        catch (Exception ex)
        {
            _viewModel.ServerStatus = $"MMS server: failed to start on port {ServerPort}: {ex.Message}. Run the app as Administrator.";
        }
    }

    private Task StopServerAsync()
    {
        if (_stopServerTask is not null)
            return _stopServerTask;

        var server = _server;
        _server = null;
        if (server is null)
            return Task.CompletedTask;

        _stopServerTask = StopServerCoreAsync(server);
        return _stopServerTask;
    }

    private async Task StopServerCoreAsync(IedSimulatorMmsServer server)
    {
        _viewModel.ServerStatus = "MMS server: stopping...";

        try
        {
            await server.StopAsync();
        }
        catch
        {
            // ignore shutdown errors
        }
        finally
        {
            _stopServerTask = null;
            _viewModel.ServerStatus = "MMS server: stopped.";
        }
    }

    private void AppendActivity(IedSimulatorServerActivity activity, IedSimulatorMmsServer server)
    {
        var target = string.IsNullOrWhiteSpace(activity.Target) ? "-" : activity.Target;
        _viewModel.Activities.Insert(0, new SimulatorActivityRow(
            activity.TimeUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            activity.Kind.ToString(),
            string.IsNullOrWhiteSpace(activity.RemoteEndPoint) ? "-" : activity.RemoteEndPoint,
            string.IsNullOrWhiteSpace(activity.Operation) ? "-" : activity.Operation,
            target,
            activity.Success ? "PASS" : "FAIL",
            string.IsNullOrWhiteSpace(activity.Message) ? "-" : activity.Message));

        while (_viewModel.Activities.Count > 300)
            _viewModel.Activities.RemoveAt(_viewModel.Activities.Count - 1);

        _viewModel.ServerStatus = activity.Kind switch
        {
            IedSimulatorServerActivityKind.ServerStarted => $"MMS server: listening on 127.0.0.1:{server.BoundPort} (active {server.ActiveConnectionCount}).",
            IedSimulatorServerActivityKind.ServerStopped => "MMS server: stopped.",
            IedSimulatorServerActivityKind.ClientConnected => $"MMS server: client connected from {activity.RemoteEndPoint} (active {server.ActiveConnectionCount}).",
            IedSimulatorServerActivityKind.ClientDisconnected => $"MMS server: client disconnected from {activity.RemoteEndPoint} (active {server.ActiveConnectionCount}).",
            IedSimulatorServerActivityKind.HandshakeReceived => $"MMS server: received {activity.Operation} from {activity.RemoteEndPoint}.",
            IedSimulatorServerActivityKind.HandshakeSent => $"MMS server: sent {activity.Operation} to {activity.RemoteEndPoint}.",
            IedSimulatorServerActivityKind.ClientClosed => $"MMS server: client closed during {activity.Operation}. {activity.Message}",
            IedSimulatorServerActivityKind.RequestServed => $"MMS server: {activity.Operation} {target} -> {(activity.Success ? "PASS" : "FAIL")}.",
            IedSimulatorServerActivityKind.AssociationRejected => $"MMS server: association rejected from {activity.RemoteEndPoint}.",
            IedSimulatorServerActivityKind.Error => $"MMS server: {activity.Message}",
            _ => _viewModel.ServerStatus
        };
    }

    private void OnUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher has begun shutting down.
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing && (_server is not null || _stopServerTask is not null))
        {
            e.Cancel = true;
            _isClosing = true;
            _timer.Stop();
            _engine.Stop();
            _viewModel.IsRunning = false;
            await StopServerAsync();
            Close();
            return;
        }

        base.OnClosing(e);
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        StepEngine();
        _viewModel.Status = "Manual simulation step executed.";
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _engine.Stop();
        _engine.Reset();
        await StopServerAsync();
        _viewModel.Events.Clear();
        _viewModel.Activities.Clear();
        _viewModel.IsRunning = false;
        RefreshPointRows();
        _viewModel.Status = "Simulator reset to initial profile values.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export simulator profile JSON",
            Filter = "JSON document (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"ied-simulator-profile-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var json = JsonSerializer.Serialize(_engine.Profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        _viewModel.Status = $"Exported simulator profile: {dialog.FileName}";
    }

    private void LoadProfileToView()
    {
        var profile = _engine.Profile;
        _viewModel.DataSets.Clear();
        _viewModel.Reports.Clear();
        _viewModel.ProfileSummary = $"{profile.Name} - LD={profile.LogicalDevices.Count}, LN={profile.LogicalNodeCount}, points={profile.PointCount}, DataSets={profile.DataSets.Count}, RCB={profile.ReportControlBlocks.Count}.";
        _viewModel.ServerEndpoint = $"127.0.0.1:{ServerPort}";

        _viewModel.Metrics.Clear();
        _viewModel.Metrics.Add(new SimulatorMetricRow("LD", profile.LogicalDevices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("LN", profile.LogicalNodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("Points", profile.PointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("RCB", profile.ReportControlBlocks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        _viewModel.Points.Clear();
        _pointRows.Clear();
        foreach (var state in _engine.PointStates)
        {
            var row = new SimulatorPointRow
            {
                Reference = state.Reference,
                FunctionalConstraint = state.FunctionalConstraint,
                Kind = state.Kind,
                Unit = state.Unit,
                Value = state.Value,
                Quality = state.Quality,
                Reason = state.Reason,
                Timestamp = state.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
            };
            _viewModel.Points.Add(row);
            _pointRows[row.Reference] = row;
        }

        foreach (var dataSet in profile.DataSets)
            _viewModel.DataSets.Add(new SimulatorDataSetRow(dataSet.Reference, dataSet.Members.Count));

        foreach (var report in profile.ReportControlBlocks)
            _viewModel.Reports.Add(new SimulatorReportRow(report.Reference, report.Mode, report.DataSetReference, report.ConfRev, report.TriggerOptions));
    }
    private void StepEngine()
    {
        var now = DateTimeOffset.UtcNow;
        var events = _engine.Step(now);
        RefreshPointRows(events);

        foreach (var item in events.Take(20))
        {
            _viewModel.Events.Insert(0, new SimulatorEventRow(
                item.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                item.Reference,
                $"{item.PreviousValue} -> {item.NewValue}",
                item.Reason));
        }

        while (_viewModel.Events.Count > 250)
            _viewModel.Events.RemoveAt(_viewModel.Events.Count - 1);
    }

    private void RefreshPointRows(IReadOnlyList<IedSimulatorEvent>? changedEvents = null)
    {
        if (changedEvents is null)
        {
            foreach (var row in _viewModel.Points)
                RefreshPointRow(row.Reference, row);
            return;
        }

        foreach (var change in changedEvents)
            if (_pointRows.TryGetValue(change.Reference, out var row))
                RefreshPointRow(change.Reference, row);
    }

    private void RefreshPointRow(string reference, SimulatorPointRow row)
    {
        if (!_engine.TryGetPointState(reference, out var state))
            return;

        row.Value = state.Value;
        row.Quality = state.Quality;
        row.Reason = state.Reason;
        row.Timestamp = state.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
    }
}
