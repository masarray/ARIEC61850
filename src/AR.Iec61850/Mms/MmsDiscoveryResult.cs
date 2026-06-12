namespace AR.Iec61850.Mms;

public sealed class MmsDiscoveryResult
{
    public MmsDiscoverySnapshot Snapshot { get; init; } = new();
    public MmsReportInventory ReportInventory { get; init; } = new();
    public MmsIedModelDirectory IedDirectory { get; init; } = new(Array.Empty<MmsFcResolvedPoint>());
    public string Summary { get; init; } = string.Empty;
}
