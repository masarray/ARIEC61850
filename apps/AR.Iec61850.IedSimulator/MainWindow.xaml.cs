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
    private readonly IedSimulatorViewModel _viewModel = new();
    private readonly IedSimulatorEngine _engine;
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => StepEngine();
        LoadProfileToView();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _engine.Start();
        _timer.Start();
        _viewModel.IsRunning = true;
        _viewModel.Status = "Simulator running. Values are changing locally; no network MMS server is opened yet.";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _engine.Stop();
        _viewModel.IsRunning = false;
        _viewModel.Status = "Simulator stopped.";
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        StepEngine();
        _viewModel.Status = "Manual simulation step executed.";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _engine.Stop();
        _engine.Reset();
        _viewModel.Events.Clear();
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
        _viewModel.ProfileSummary = $"{profile.Name} · LD={profile.LogicalDevices.Count}, LN={profile.LogicalNodeCount}, points={profile.PointCount}, DataSets={profile.DataSets.Count}, RCB={profile.ReportControlBlocks.Count}.";

        _viewModel.Metrics.Clear();
        _viewModel.Metrics.Add(new SimulatorMetricRow("LD", profile.LogicalDevices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("LN", profile.LogicalNodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("Points", profile.PointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new SimulatorMetricRow("RCB", profile.ReportControlBlocks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        _viewModel.Points.Clear();
        foreach (var state in _engine.PointStates)
        {
            _viewModel.Points.Add(new SimulatorPointRow
            {
                Reference = state.Reference,
                FunctionalConstraint = state.FunctionalConstraint,
                Kind = state.Kind,
                Unit = state.Unit,
                Value = state.Value,
                Quality = state.Quality,
                Reason = state.Reason,
                Timestamp = state.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
            });
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
        RefreshPointRows();

        foreach (var item in events.Take(20))
        {
            _viewModel.Events.Insert(0, new SimulatorEventRow(
                item.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                item.Reference,
                $"{item.PreviousValue} → {item.NewValue}",
                item.Reason));
        }

        while (_viewModel.Events.Count > 250)
            _viewModel.Events.RemoveAt(_viewModel.Events.Count - 1);
    }

    private void RefreshPointRows()
    {
        var states = _engine.PointStates.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _viewModel.Points)
        {
            if (!states.TryGetValue(row.Reference, out var state))
                continue;

            row.Value = state.Value;
            row.Quality = state.Quality;
            row.Reason = state.Reason;
            row.Timestamp = state.TimestampUtc.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
