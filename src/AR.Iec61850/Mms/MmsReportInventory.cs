namespace AR.Iec61850.Mms;

public sealed class MmsDataSetCandidate
{
    public string Domain { get; set; } = string.Empty;
    public string LogicalNode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RawMmsName { get; set; } = string.Empty;
}

public sealed class MmsReportControlCandidate
{
    public string Domain { get; set; } = string.Empty;
    public string LogicalNode { get; set; } = string.Empty;
    public string FunctionalConstraint { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public bool Buffered { get; set; }
    public string DataSetReference { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public string ConfRev { get; set; } = string.Empty;
    public string IntegrityPeriodMs { get; set; } = string.Empty;
    public string EnabledState { get; set; } = string.Empty;
    public string ReservationState { get; set; } = string.Empty;
    public string ReservationTimeSeconds { get; set; } = string.Empty;
    public string BufferTimeMs { get; set; } = string.Empty;
    public string TriggerOptions { get; set; } = string.Empty;
    public string OptionalFields { get; set; } = string.Empty;
    public string Status { get; set; } = "Discovered";
    public List<string> Attributes { get; set; } = new();
    public List<string> ProbeDiagnostics { get; } = new();

    public string Mode => Buffered ? "BRCB" : "URCB";
    public string Summary => $"{Mode} {Reference}" + (string.IsNullOrWhiteSpace(DataSetReference) ? string.Empty : $" -> {DataSetReference}");
}

public sealed class MmsReportInventory
{
    public List<MmsDataSetCandidate> DataSets { get; } = new();
    public List<MmsReportControlCandidate> ReportControls { get; } = new();

    public int BufferedCount => ReportControls.Count(x => x.Buffered);
    public int UnbufferedCount => ReportControls.Count(x => !x.Buffered);
    public string Summary => $"DataSets={DataSets.Count}, RCB={ReportControls.Count} (BRCB={BufferedCount}, URCB={UnbufferedCount})";
}
