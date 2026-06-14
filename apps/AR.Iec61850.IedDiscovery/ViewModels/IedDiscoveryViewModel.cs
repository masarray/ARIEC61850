using System.Collections.ObjectModel;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.IedDiscovery.ViewModels;

public sealed class IedDiscoveryViewModel : ObservableObject
{
    private string _host = "192.168.1.10";
    private int _port = 102;
    private int _timeoutMs = 30000;
    private int _maxReportProbes = 64;
    private int _maxDataSetDirectoryReads = 32;
    private int _maxTypeReads = 48;
    private bool _probeReportAttributes = true;
    private bool _readDataSetDirectories = true;
    private bool _readVariableTypes;
    private bool _isBusy;
    private string _status = "Ready. Use an isolated lab network for live IED discovery.";
    private string _summary = "No discovery has been run yet.";
    private string _reportProfileSummary = "No report session profile planned yet.";
    private LiveIedModelDiscoveryDocument? _lastDocument;
    private AR.Iec61850.Mms.MmsReportSessionProfile? _lastReportProfile;

    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public int TimeoutMs { get => _timeoutMs; set => SetProperty(ref _timeoutMs, value); }
    public int MaxReportProbes { get => _maxReportProbes; set => SetProperty(ref _maxReportProbes, value); }
    public int MaxDataSetDirectoryReads { get => _maxDataSetDirectoryReads; set => SetProperty(ref _maxDataSetDirectoryReads, value); }
    public int MaxTypeReads { get => _maxTypeReads; set => SetProperty(ref _maxTypeReads, value); }
    public bool ProbeReportAttributes { get => _probeReportAttributes; set => SetProperty(ref _probeReportAttributes, value); }
    public bool ReadDataSetDirectories { get => _readDataSetDirectories; set => SetProperty(ref _readDataSetDirectories, value); }
    public bool ReadVariableTypes { get => _readVariableTypes; set => SetProperty(ref _readVariableTypes, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
    public string ReportProfileSummary { get => _reportProfileSummary; set => SetProperty(ref _reportProfileSummary, value); }
    public LiveIedModelDiscoveryDocument? LastDocument { get => _lastDocument; set => SetProperty(ref _lastDocument, value); }
    public AR.Iec61850.Mms.MmsReportSessionProfile? LastReportProfile { get => _lastReportProfile; set => SetProperty(ref _lastReportProfile, value); }

    public ObservableCollection<MetricRow> Metrics { get; } = new();
    public ObservableCollection<LogicalDeviceRow> LogicalDevices { get; } = new();
    public ObservableCollection<DataSetRow> DataSets { get; } = new();
    public ObservableCollection<ReportControlRow> ReportControls { get; } = new();
    public ObservableCollection<WarningRow> Warnings { get; } = new();

    public void ClearResults()
    {
        Metrics.Clear();
        LogicalDevices.Clear();
        DataSets.Clear();
        ReportControls.Clear();
        Warnings.Clear();
        Summary = "Discovery cleared.";
        ReportProfileSummary = "No report session profile planned yet.";
        LastDocument = null;
        LastReportProfile = null;
    }
}
