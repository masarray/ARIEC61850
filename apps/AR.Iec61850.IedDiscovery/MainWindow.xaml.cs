using System.IO;
using System.Text.Json;
using System.Windows;
using AR.Iec61850.Discovery;
using AR.Iec61850.IedDiscovery.ViewModels;
using AR.Iec61850.Mms;
using Microsoft.Win32;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow : Window
{
    private readonly IedDiscoveryViewModel _viewModel = new();
    private CancellationTokenSource? _cancellation;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        _viewModel.ClearResults();
        _viewModel.IsBusy = true;
        _viewModel.Status = "Connecting to MMS endpoint...";
        _cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, _viewModel.TimeoutMs + 15000)));

        try
        {
            await using var session = new MmsClientSession();
            await session.ConnectAsync(
                _viewModel.Host,
                _viewModel.Port,
                TimeSpan.FromMilliseconds(Math.Max(1000, _viewModel.TimeoutMs)),
                _cancellation.Token).ConfigureAwait(true);

            _viewModel.Status = "Running native MMS GetNameList discovery...";
            var discovery = await session.DiscoverAsync(
                _viewModel.ProbeReportAttributes,
                Math.Max(0, _viewModel.MaxReportProbes),
                _cancellation.Token).ConfigureAwait(true);

            IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
            if (_viewModel.ReadDataSetDirectories)
            {
                _viewModel.Status = "Reading DataSet directories...";
                dataSetDirectories = await session.GetDataSetDirectoriesAsync(
                    discovery.ReportInventory.DataSets.Select(x => x.Reference).Take(Math.Max(0, _viewModel.MaxDataSetDirectoryReads)),
                    discovery.IedDirectory,
                    _cancellation.Token).ConfigureAwait(true);
            }

            IReadOnlyList<MmsVariableAccessAttributesResult> typeAttributes = Array.Empty<MmsVariableAccessAttributesResult>();
            if (_viewModel.ReadVariableTypes)
            {
                _viewModel.Status = "Sampling variable type signatures...";
                typeAttributes = await session.GetVariableAccessAttributesBatchAsync(
                    discovery.IedDirectory.Points.Select(x => x.ToObjectReference()),
                    Math.Max(0, _viewModel.MaxTypeReads),
                    _cancellation.Token).ConfigureAwait(true);
            }

            var document = LiveIedModelDiscoveryBuilder.Build(
                discovery,
                new LiveIedModelDiscoveryBuildOptions
                {
                    Host = _viewModel.Host,
                    Port = _viewModel.Port,
                    AccessPointName = "AP1"
                },
                dataSetDirectories,
                typeAttributes);

            Populate(document);
            _viewModel.LastDocument = document;
            _viewModel.LastReportProfile = TryCreateFirstStaticReportProfile(discovery.ReportInventory, dataSetDirectories);
            _viewModel.ReportProfileSummary = _viewModel.LastReportProfile?.Summary ?? "No ready static report profile could be planned from the discovered RCB/DataSet state.";
            _viewModel.Status = "Discovery complete. Review RCB/DataSet readiness before enabling reports.";
        }
        catch (OperationCanceledException)
        {
            _viewModel.Status = "Discovery cancelled.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            _viewModel.Status = $"Discovery failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _cancellation?.Cancel();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastDocument == null)
        {
            MessageBox.Show(this, "Run discovery first before exporting.", "No discovery document", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var json = JsonSerializer.Serialize(_viewModel.LastDocument, options);
        File.WriteAllText(dialog.FileName, json);
        _viewModel.Status = $"Exported discovery document: {dialog.FileName}";
    }


    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastReportProfile == null)
        {
            MessageBox.Show(this, "Run discovery with DataSet directory reads first. A report profile can only be exported when a usable RCB/DataSet plan is available.", "No report profile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export report session profile JSON",
            Filter = "JSON document (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"report-session-profile-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_viewModel.LastReportProfile, options);
        File.WriteAllText(dialog.FileName, json);
        _viewModel.Status = $"Exported report session profile: {dialog.FileName}";
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
        _viewModel.Summary = document.Summary;
        _viewModel.Metrics.Clear();
        _viewModel.Metrics.Add(new MetricRow("LD", document.Coverage.LogicalDeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("LN", document.Coverage.LogicalNodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("Points", document.Coverage.DataAttributeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("RCB", document.Coverage.ReportControlCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("DataSets", document.Coverage.DataSetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        foreach (var ld in document.LogicalDevices)
        {
            _viewModel.LogicalDevices.Add(new LogicalDeviceRow(
                ld.MmsDomain,
                ld.LogicalNodes.Count,
                ld.LogicalNodes.Sum(x => x.DataObjects.Sum(d => d.Attributes.Count))));
        }

        foreach (var ds in document.DataSets)
        {
            _viewModel.DataSets.Add(new DataSetRow(
                ds.Reference,
                ds.MemberCount,
                string.Join(", ", ds.UsedByReportControls.Take(3)),
                string.Join(", ", ds.UsedByGooseControls.Take(3)),
                string.Join(", ", ds.UsedBySampledValueControls.Take(3))));
        }

        foreach (var rcb in document.ReportControls)
        {
            _viewModel.ReportControls.Add(new ReportControlRow(
                rcb.Reference,
                rcb.Buffered ? "BRCB" : "URCB",
                rcb.DataSetReference,
                rcb.EnabledState,
                rcb.ReservationState,
                rcb.ConfRev,
                rcb.Status));
        }

        foreach (var warning in document.Warnings)
        {
            _viewModel.Warnings.Add(new WarningRow(warning.Code, warning.Message));
        }
    }
}
