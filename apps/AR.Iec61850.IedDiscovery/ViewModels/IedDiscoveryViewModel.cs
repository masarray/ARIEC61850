using System.Collections.ObjectModel;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.IedDiscovery.ViewModels;

public sealed class IedDiscoveryViewModel : ObservableObject
{
    private string _host = "192.168.1.10";
    private int _port = 102;
    private int _timeoutMs = 30000;
    private int _maxReportProbes = 96;
    private int _maxDataSetDirectoryReads = 64;
    private int _maxTypeReads = 64;
    private bool _probeReportAttributes = true;
    private bool _readDataSetDirectories = true;
    private bool _readVariableTypes;
    private bool _isBusy;
    private bool _isConnected;
    private bool _isOnline;
    private string _status = "Ready. Discover an IED or open an SCL file.";
    private string _summary = "No IED model loaded.";
    private string _reportProfileSummary = "No report session profile planned yet.";
    private string _selectedHeader = "No object selected";
    private string _selectedSubHeader = "Select an LD/LN/DO/DataSet/RCB from the left explorer.";
    private IedExplorerNode? _selectedNode;
    private DataAttributeDetailRow? _selectedDetailRow;
    private LiveIedModelDiscoveryDocument? _lastDocument;
    private MmsReportSessionProfile? _lastReportProfile;

    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public int TimeoutMs { get => _timeoutMs; set => SetProperty(ref _timeoutMs, value); }
    public int MaxReportProbes { get => _maxReportProbes; set => SetProperty(ref _maxReportProbes, value); }
    public int MaxDataSetDirectoryReads { get => _maxDataSetDirectoryReads; set => SetProperty(ref _maxDataSetDirectoryReads, value); }
    public int MaxTypeReads { get => _maxTypeReads; set => SetProperty(ref _maxTypeReads, value); }
    public bool ProbeReportAttributes { get => _probeReportAttributes; set => SetProperty(ref _probeReportAttributes, value); }
    public bool ReadDataSetDirectories { get => _readDataSetDirectories; set => SetProperty(ref _readDataSetDirectories, value); }
    public bool ReadVariableTypes { get => _readVariableTypes; set => SetProperty(ref _readVariableTypes, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) RaiseCommandState(); } }
    public bool IsConnected { get => _isConnected; set { if (SetProperty(ref _isConnected, value)) RaiseCommandState(); } }
    public bool IsOnline { get => _isOnline; set { if (SetProperty(ref _isOnline, value)) { RaiseCommandState(); OnPropertyChanged(nameof(OnlineLabel)); OnPropertyChanged(nameof(OnlineIcon)); OnPropertyChanged(nameof(OnlineBrush)); } } }
    public string OnlineLabel => IsOnline ? "Online" : "Offline";
    public string OnlineIcon => IsOnline ? "●" : "●";
    public string OnlineBrush => IsOnline ? "#16A34A" : "#DC2626";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
    public string ReportProfileSummary { get => _reportProfileSummary; set => SetProperty(ref _reportProfileSummary, value); }
    public string SelectedHeader { get => _selectedHeader; set => SetProperty(ref _selectedHeader, value); }
    public string SelectedSubHeader { get => _selectedSubHeader; set => SetProperty(ref _selectedSubHeader, value); }
    public IedExplorerNode? SelectedNode { get => _selectedNode; set { if (SetProperty(ref _selectedNode, value)) RaiseCommandState(); } }
    public DataAttributeDetailRow? SelectedDetailRow { get => _selectedDetailRow; set { if (SetProperty(ref _selectedDetailRow, value)) RaiseCommandState(); } }
    public LiveIedModelDiscoveryDocument? LastDocument { get => _lastDocument; set { if (SetProperty(ref _lastDocument, value)) RaiseCommandState(); } }
    public MmsReportSessionProfile? LastReportProfile { get => _lastReportProfile; set => SetProperty(ref _lastReportProfile, value); }

    public bool HasModel => LastDocument != null || ExplorerNodes.Count > 0;
    public bool CanClose => IsBusy || IsConnected;
    public bool CanRead => !IsBusy && IsConnected && IsOnline && SelectedNode != null && SelectedNode.Kind is (ExplorerNodeKind.DataObject or ExplorerNodeKind.DataSet or ExplorerNodeKind.ReportControl or ExplorerNodeKind.LogicalNode);
    public bool CanReadAll => !IsBusy && IsConnected && IsOnline && LastDocument != null;
    public bool CanExport => LastDocument != null;
    public bool CanPin => !IsBusy && SelectedNode != null && SelectedNode.Kind is (ExplorerNodeKind.DataObject or ExplorerNodeKind.DataSet or ExplorerNodeKind.ReportControl or ExplorerNodeKind.LogicalNode);
    public bool CanEnableReport => !IsBusy && IsConnected && IsOnline && SelectedNode?.Kind == ExplorerNodeKind.ReportControl;
    public bool CanControl => !IsBusy && IsConnected && IsOnline && SelectedNode?.Kind == ExplorerNodeKind.DataObject && IsControlCandidate(SelectedNode);

    public ObservableCollection<MetricRow> Metrics { get; } = new();
    public ObservableCollection<IedExplorerNode> ExplorerNodes { get; } = new();
    public ObservableCollection<DataAttributeDetailRow> DetailRows { get; } = new();
    public List<DataAttributeDetailRow> DetailRootRows { get; } = new();
    public ObservableCollection<MonitorSignalRow> MonitorSignals { get; } = new();
    public ObservableCollection<StatusHistoryRow> StatusHistory { get; } = new();
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
        ExplorerNodes.Clear();
        DetailRows.Clear();
        DetailRootRows.Clear();
        MonitorSignals.Clear();
        Summary = "Discovery cleared.";
        ReportProfileSummary = "No report session profile planned yet.";
        SelectedHeader = "No object selected";
        SelectedSubHeader = "Select an LD/LN/DO/DataSet/RCB from the left explorer.";
        SelectedNode = null;
        SelectedDetailRow = null;
        LastDocument = null;
        LastReportProfile = null;
        RaiseCommandState();
    }


    public void ReplaceDetailRows(IEnumerable<DataAttributeDetailRow> roots)
    {
        SelectedDetailRow = null;
        DetailRootRows.Clear();
        DetailRootRows.AddRange(roots);
        RefreshDetailRows();
    }

    public void RefreshDetailRows()
    {
        var selected = DetailRows.FirstOrDefault(x => ReferenceEquals(x, SelectedDetailRow));
        DetailRows.Clear();
        foreach (var row in DetailRowFlattener.Flatten(DetailRootRows))
            DetailRows.Add(row);
        if (selected != null && DetailRows.Contains(selected))
            SelectedDetailRow = selected;
    }

    public void AddStatus(string severity, string code, string description)
    {
        if (StatusHistory.Count >= 500)
            StatusHistory.RemoveAt(0);

        StatusHistory.Add(new StatusHistoryRow(DateTimeOffset.Now, severity, code, description));
        Status = description;
    }

    public void RaiseCommandState()
    {
        OnPropertyChanged(nameof(HasModel));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanReadAll));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(CanEnableReport));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(OnlineLabel));
        OnPropertyChanged(nameof(OnlineIcon));
        OnPropertyChanged(nameof(OnlineBrush));
    }

    private static bool IsControlCandidate(IedExplorerNode node)
    {
        if (node.Model is not LiveIedDataObjectModel dataObject)
            return false;

        if (string.Equals(dataObject.Name, "Pos", StringComparison.OrdinalIgnoreCase))
            return true;

        return dataObject.Attributes.Any(x => string.Equals(x.FunctionalConstraint, "CO", StringComparison.OrdinalIgnoreCase)
            || x.AttributePath.Contains("ctl", StringComparison.OrdinalIgnoreCase)
            || x.AttributePath.Contains("sbo", StringComparison.OrdinalIgnoreCase));
    }
}
