using System.Collections.ObjectModel;

namespace AR.Iec61850.IedSimulator.ViewModels;

public sealed class IedSimulatorViewModel : ObservableObject
{
    private bool _isRunning;
    private string _status = "Open an SCL file or run the demo profile, then Start to open the read-only MMS server.";
    private string _profileSummary = string.Empty;
    private string _serverStatus = "MMS server: stopped.";
    private string _serverEndpoint = "127.0.0.1:102";

    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string ProfileSummary { get => _profileSummary; set => SetProperty(ref _profileSummary, value); }
    public string ServerStatus { get => _serverStatus; set => SetProperty(ref _serverStatus, value); }
    public string ServerEndpoint { get => _serverEndpoint; set => SetProperty(ref _serverEndpoint, value); }

    public ObservableCollection<SimulatorMetricRow> Metrics { get; } = new();
    public ObservableCollection<SimulatorPointRow> Points { get; } = new();
    public ObservableCollection<SimulatorDataSetRow> DataSets { get; } = new();
    public ObservableCollection<SimulatorReportRow> Reports { get; } = new();
    public ObservableCollection<SimulatorEventRow> Events { get; } = new();
    public ObservableCollection<SimulatorActivityRow> Activities { get; } = new();
}
