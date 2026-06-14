using System.Collections.ObjectModel;

namespace AR.Iec61850.IedSimulator.ViewModels;

public sealed class IedSimulatorViewModel : ObservableObject
{
    private bool _isRunning;
    private string _status = "Offline simulator profile loaded. Network MMS server is a future phase.";
    private string _profileSummary = string.Empty;

    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string ProfileSummary { get => _profileSummary; set => SetProperty(ref _profileSummary, value); }

    public ObservableCollection<SimulatorMetricRow> Metrics { get; } = new();
    public ObservableCollection<SimulatorPointRow> Points { get; } = new();
    public ObservableCollection<SimulatorDataSetRow> DataSets { get; } = new();
    public ObservableCollection<SimulatorReportRow> Reports { get; } = new();
    public ObservableCollection<SimulatorEventRow> Events { get; } = new();
}
