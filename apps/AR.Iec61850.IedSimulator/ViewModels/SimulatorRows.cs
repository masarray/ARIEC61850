namespace AR.Iec61850.IedSimulator.ViewModels;

public sealed class SimulatorPointRow : ObservableObject
{
    private string _value = string.Empty;
    private string _quality = "valid";
    private string _timestamp = string.Empty;
    private string _reason = string.Empty;

    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
    public string Timestamp { get => _timestamp; set => SetProperty(ref _timestamp, value); }
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
}

public sealed record SimulatorDataSetRow(string Reference, int MemberCount);
public sealed record SimulatorReportRow(string Reference, string Mode, string DataSet, int ConfRev, string TriggerOptions);
public sealed record SimulatorEventRow(string Time, string Reference, string Change, string Reason);
public sealed record SimulatorMetricRow(string Label, string Value);
public sealed record SimulatorActivityRow(string Time, string Kind, string Remote, string Operation, string Target, string Status, string Detail);
