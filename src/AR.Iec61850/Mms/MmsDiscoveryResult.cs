namespace AR.Iec61850.Mms;

public sealed class MmsDiscoveryResult
{
    public MmsDiscoverySnapshot Snapshot { get; init; } = new();
    public MmsReportInventory ReportInventory { get; init; } = new();
    public MmsIedModelDirectory IedDirectory { get; init; } = new(Array.Empty<MmsFcResolvedPoint>());
    public IReadOnlyList<MmsDataSetDirectoryResult> DataSetDirectories { get; init; } = Array.Empty<MmsDataSetDirectoryResult>();
    public string Summary { get; init; } = string.Empty;
}
