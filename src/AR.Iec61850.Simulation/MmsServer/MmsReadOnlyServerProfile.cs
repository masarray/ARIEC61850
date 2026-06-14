using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace AR.Iec61850.Simulation;

public sealed record MmsReadOnlyServerProfileOptions
{
    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";
    public int Port { get; init; } = 102;
    public bool IncludeSelfTest { get; init; } = true;
}

public sealed record MmsReadOnlyServerProfile
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ServerName { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public bool ReadOnly { get; init; } = true;
    public IReadOnlyList<MmsReadOnlyLogicalDevice> LogicalDevices { get; init; } = Array.Empty<MmsReadOnlyLogicalDevice>();
    public IReadOnlyList<MmsReadOnlyLogicalNode> LogicalNodes { get; init; } = Array.Empty<MmsReadOnlyLogicalNode>();
    public IReadOnlyList<MmsReadOnlyPoint> Points { get; init; } = Array.Empty<MmsReadOnlyPoint>();
    public IReadOnlyList<MmsReadOnlyDataSet> DataSets { get; init; } = Array.Empty<MmsReadOnlyDataSet>();
    public IReadOnlyList<MmsReadOnlyReportControlBlock> ReportControlBlocks { get; init; } = Array.Empty<MmsReadOnlyReportControlBlock>();
    public IReadOnlyList<MmsReadOnlyDiagnostic> Diagnostics { get; init; } = Array.Empty<MmsReadOnlyDiagnostic>();
    public IReadOnlyList<MmsReadOnlySelfTestStep> SelfTestSteps { get; init; } = Array.Empty<MmsReadOnlySelfTestStep>();

    [JsonIgnore]
    public int LogicalDeviceCount => LogicalDevices.Count;

    [JsonIgnore]
    public int LogicalNodeCount => LogicalNodes.Count;

    [JsonIgnore]
    public int PointCount => Points.Count;

    [JsonIgnore]
    public int DataSetCount => DataSets.Count;

    [JsonIgnore]
    public int ReportControlBlockCount => ReportControlBlocks.Count;

    [JsonIgnore]
    public bool IsReady => Diagnostics.All(x => !string.Equals(x.Severity, "High", StringComparison.OrdinalIgnoreCase)) &&
                           SelfTestSteps.All(x => x.IsSuccess);

    [JsonIgnore]
    public string Summary => $"MMS read-only server profile: LD={LogicalDeviceCount} LN={LogicalNodeCount} points={PointCount} DataSets={DataSetCount} RCB={ReportControlBlockCount} ready={IsReady}";

    public string ToMarkdown()
    {
        var lines = new List<string>
        {
            "# MMS Read-Only Server Profile",
            string.Empty,
            $"- Generated UTC: {GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff zzz}",
            $"- Server: {Escape(ServerName)}",
            $"- Port: {Port.ToString(CultureInfo.InvariantCulture)}",
            $"- Mode: read-only virtual IED model",
            $"- Logical devices: {LogicalDeviceCount.ToString(CultureInfo.InvariantCulture)}",
            $"- Logical nodes: {LogicalNodeCount.ToString(CultureInfo.InvariantCulture)}",
            $"- Points: {PointCount.ToString(CultureInfo.InvariantCulture)}",
            $"- DataSets: {DataSetCount.ToString(CultureInfo.InvariantCulture)}",
            $"- Report control blocks: {ReportControlBlockCount.ToString(CultureInfo.InvariantCulture)}",
            $"- Ready: {IsReady.ToString().ToLowerInvariant()}",
            string.Empty,
            "## Logical Devices",
            string.Empty,
            "| Logical Device | Logical Nodes | Points |",
            "| --- | ---: | ---: |"
        };

        foreach (var device in LogicalDevices)
            lines.Add($"| {Escape(device.Name)} | {device.LogicalNodeCount} | {device.PointCount} |");

        lines.Add(string.Empty);
        lines.Add("## Logical Nodes");
        lines.Add(string.Empty);
        lines.Add("| Logical Device | Logical Node | Class | Points |");
        lines.Add("| --- | --- | --- | ---: |");
        foreach (var node in LogicalNodes)
            lines.Add($"| {Escape(node.LogicalDevice)} | {Escape(node.Name)} | {Escape(node.LnClass)} | {node.PointCount} |");

        lines.Add(string.Empty);
        lines.Add("## DataSets");
        lines.Add(string.Empty);
        lines.Add("| Reference | Members | Status | ");
        lines.Add("| --- | ---: | --- |");
        foreach (var dataSet in DataSets)
            lines.Add($"| {Escape(dataSet.Reference)} | {dataSet.Members.Count} | {Escape(dataSet.Status)} |");

        lines.Add(string.Empty);
        lines.Add("## Report Control Blocks");
        lines.Add(string.Empty);
        lines.Add("| Reference | Mode | DataSet | ConfRev | TrgOps | OptFlds | Status | ");
        lines.Add("| --- | --- | --- | ---: | --- | --- | --- |");
        foreach (var rcb in ReportControlBlocks)
            lines.Add($"| {Escape(rcb.Reference)} | {Escape(rcb.Mode)} | {Escape(rcb.DataSetReference)} | {rcb.ConfRev} | {Escape(rcb.TriggerOptions)} | {Escape(rcb.OptionalFields)} | {Escape(rcb.Status)} |");

        lines.Add(string.Empty);
        lines.Add("## Sample Points");
        lines.Add(string.Empty);
        lines.Add("| Reference | FC | Kind | Value | Quality | Timestamp UTC | ");
        lines.Add("| --- | --- | --- | --- | --- | --- |");
        foreach (var point in Points.Take(20))
            lines.Add($"| {Escape(point.Reference)} | {Escape(point.FunctionalConstraint)} | {Escape(point.Kind)} | {Escape(point.Value)} | {Escape(point.Quality)} | {point.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff} |");
        if (Points.Count > 20)
            lines.Add($"| ... | ... | ... | ... | ... | {Points.Count - 20} more point(s) | ");

        lines.Add(string.Empty);
        lines.Add("## Diagnostics");
        lines.Add(string.Empty);
        if (Diagnostics.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            foreach (var diagnostic in Diagnostics)
                lines.Add($"- {Escape(diagnostic.Severity)} {Escape(diagnostic.Code)}: {Escape(diagnostic.Message)}");
        }

        lines.Add(string.Empty);
        lines.Add("## Self Test");
        lines.Add(string.Empty);
        lines.Add("| Status | Operation | Target | Message | ");
        lines.Add("| --- | --- | --- | --- |");
        foreach (var step in SelfTestSteps)
            lines.Add($"| {(step.IsSuccess ? "OK" : "FAIL")} | {Escape(step.Operation)} | {Escape(step.Target)} | {Escape(step.Message)} |");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|");
}

public sealed record MmsReadOnlyLogicalDevice
{
    public string Name { get; init; } = string.Empty;
    public int LogicalNodeCount { get; init; }
    public int PointCount { get; init; }
}

public sealed record MmsReadOnlyLogicalNode
{
    public string LogicalDevice { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LnClass { get; init; } = string.Empty;
    public int PointCount { get; init; }
}

public sealed record MmsReadOnlyPoint
{
    public string Reference { get; init; } = string.Empty;
    public string LogicalDevice { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Quality { get; init; } = "valid";
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record MmsReadOnlyDataSet
{
    public string Reference { get; init; } = string.Empty;
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();
    public string Status { get; init; } = "OK";
}

public sealed record MmsReadOnlyReportControlBlock
{
    public string Reference { get; init; } = string.Empty;
    public string Mode { get; init; } = "URCB";
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public int ConfRev { get; init; }
    public int BufferTimeMs { get; init; }
    public int IntegrityPeriodMs { get; init; }
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string Status { get; init; } = "OK";
}

public sealed record MmsReadOnlyDiagnostic
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record MmsReadOnlySelfTestStep
{
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}
