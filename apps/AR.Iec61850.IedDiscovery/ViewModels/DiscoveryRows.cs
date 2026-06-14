using System.Collections.ObjectModel;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.IedDiscovery.ViewModels;

public enum ExplorerNodeKind
{
    Ied,
    Section,
    LogicalDevice,
    LogicalNode,
    DataObject,
    DataSet,
    ReportControl,
    GooseControl,
    SampledValueControl,
    SettingGroup,
    File
}

public sealed class IedExplorerNode : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private string _status = string.Empty;

    public IedExplorerNode(string title, ExplorerNodeKind kind, string reference = "", string subtitle = "")
    {
        Title = title;
        Kind = kind;
        Reference = reference;
        Subtitle = subtitle;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Reference { get; }
    public ExplorerNodeKind Kind { get; }
    public string Badge => Kind switch
    {
        ExplorerNodeKind.Ied => "IED",
        ExplorerNodeKind.Section => "•",
        ExplorerNodeKind.LogicalDevice => "LD",
        ExplorerNodeKind.LogicalNode => "LN",
        ExplorerNodeKind.DataObject => "DO",
        ExplorerNodeKind.DataSet => "DS",
        ExplorerNodeKind.ReportControl => "RCB",
        ExplorerNodeKind.GooseControl => "GCB",
        ExplorerNodeKind.SampledValueControl => "SVCB",
        ExplorerNodeKind.SettingGroup => "SG",
        ExplorerNodeKind.File => "FILE",
        _ => "•"
    };

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public object? Model { get; init; }
    public ObservableCollection<IedExplorerNode> Children { get; } = new();
}

public sealed class MonitorSignalRow : ObservableObject
{
    private string _value = "-";
    private string _quality = "-";
    private string _timestamp = "-";
    private string _age = "-";
    private string _status = "pinned";

    public MonitorSignalRow(string reference, string source)
    {
        Reference = reference;
        Source = source;
    }

    public string Reference { get; }
    public string Source { get; }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
    public string Timestamp { get => _timestamp; set => SetProperty(ref _timestamp, value); }
    public string Age { get => _age; set => SetProperty(ref _age, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}

public sealed record StatusHistoryRow(DateTimeOffset Time, string Severity, string Code, string Description)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss");
}

public sealed record MetricRow(string Label, string Value, string Accent = "");
public sealed record LogicalDeviceRow(string LogicalDevice, int LogicalNodeCount, int PointCount);
public sealed record DataSetRow(string Reference, int MemberCount, string UsedByReports, string UsedByGoose, string UsedBySv);
public sealed record ReportControlRow(string Reference, string Mode, string DataSet, string EnabledState, string ReservationState, string ConfRev, string Status);
public sealed record WarningRow(string Severity, string Message);
public sealed record ConnectionProfileRow(string Host, int Port, string Name, int TimeoutMs)
{
    public string Endpoint => $"{Host}:{Port}";
}
