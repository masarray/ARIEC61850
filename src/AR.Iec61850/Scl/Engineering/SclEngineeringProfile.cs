using System.Text;

namespace AR.Iec61850.Scl.Engineering;

public sealed class SclEngineeringProfile
{
    public string SourceName { get; init; } = string.Empty;
    public SclEdition Edition { get; init; }
    public string HeaderId { get; init; } = string.Empty;
    public string HeaderVersion { get; init; } = string.Empty;
    public string HeaderRevision { get; init; } = string.Empty;
    public IReadOnlyList<SclEngineeringIed> Ieds { get; init; } = Array.Empty<SclEngineeringIed>();
    public IReadOnlyList<SclEngineeringAccessPoint> AccessPoints { get; init; } = Array.Empty<SclEngineeringAccessPoint>();
    public IReadOnlyList<SclEngineeringLogicalDevice> LogicalDevices { get; init; } = Array.Empty<SclEngineeringLogicalDevice>();
    public IReadOnlyList<SclEngineeringLogicalNode> LogicalNodes { get; init; } = Array.Empty<SclEngineeringLogicalNode>();
    public IReadOnlyList<SclEngineeringExternalReference> ExternalReferences { get; init; } = Array.Empty<SclEngineeringExternalReference>();
    public SclEngineeringCapabilityMatrix Capabilities { get; init; } = new();
    public SclEngineeringStreamSummary ProcessBus { get; init; } = new();
    public IReadOnlyList<SclEngineeringFinding> Findings { get; init; } = Array.Empty<SclEngineeringFinding>();

    public int DataSetCount => ProcessBus.DataSetCount;
    public int ReportControlCount => ProcessBus.ReportControls.Count;
    public int GooseStreamCount => ProcessBus.GooseStreams.Count;
    public int SampledValuesStreamCount => ProcessBus.SampledValuesStreams.Count;
    public int ExternalReferenceCount => ExternalReferences.Count;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SCL Engineering Profile");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{SourceName}`");
        sb.AppendLine($"- Header: `{Dash(HeaderId)}` version=`{Dash(HeaderVersion)}` revision=`{Dash(HeaderRevision)}`");
        sb.AppendLine($"- Edition: `{Edition}`");
        sb.AppendLine($"- IEDs: {Ieds.Count}");
        sb.AppendLine($"- Access points: {AccessPoints.Count}");
        sb.AppendLine($"- Logical devices: {LogicalDevices.Count}");
        sb.AppendLine($"- Logical nodes: {LogicalNodes.Count}");
        sb.AppendLine($"- DataSets: {DataSetCount}");
        sb.AppendLine($"- Report controls: {ReportControlCount}");
        sb.AppendLine($"- GOOSE streams: {GooseStreamCount}");
        sb.AppendLine($"- SV streams: {SampledValuesStreamCount}");
        sb.AppendLine($"- External references: {ExternalReferenceCount}");
        sb.AppendLine();

        sb.AppendLine("## Capability Matrix");
        sb.AppendLine();
        sb.AppendLine("| Capability | Status |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| MMS server model | {YesNo(Capabilities.HasServerModel)} |");
        sb.AppendLine($"| DataSet engineering | {YesNo(Capabilities.HasDataSets)} |");
        sb.AppendLine($"| Report control engineering | {YesNo(Capabilities.HasReports)} |");
        sb.AppendLine($"| Buffered reports | {YesNo(Capabilities.HasBufferedReports)} |");
        sb.AppendLine($"| Unbuffered reports | {YesNo(Capabilities.HasUnbufferedReports)} |");
        sb.AppendLine($"| GOOSE engineering | {YesNo(Capabilities.HasGoose)} |");
        sb.AppendLine($"| Sampled Values engineering | {YesNo(Capabilities.HasSampledValues)} |");
        sb.AppendLine($"| Subscriber ExtRef mapping | {YesNo(Capabilities.HasExternalReferences)} |");
        sb.AppendLine($"| Control model objects | {YesNo(Capabilities.HasControlObjects)} |");
        sb.AppendLine($"| Setting group objects | {YesNo(Capabilities.HasSettingGroups)} |");
        sb.AppendLine($"| File service declared | {YesNo(Capabilities.FileServiceDeclared)} |");
        sb.AppendLine($"| Log service declared | {YesNo(Capabilities.LogServiceDeclared)} |");
        sb.AppendLine();

        sb.AppendLine("## Expected Process Bus Streams");
        sb.AppendLine();
        sb.AppendLine("### GOOSE");
        sb.AppendLine();
        sb.AppendLine("| Control block | APPID | MAC | VLAN | DataSet | Members |");
        sb.AppendLine("|---|---:|---|---:|---|---:|");
        foreach (var stream in ProcessBus.GooseStreams)
            sb.AppendLine($"| `{stream.ControlBlockReference}` | {AppId(stream.Address.AppId)} | `{Dash(stream.Address.DestinationMacText)}` | {Vlan(stream.Address)} | `{Dash(stream.DataSetReference)}` | {stream.Entries.Count} |");
        if (ProcessBus.GooseStreams.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("### Sampled Values");
        sb.AppendLine();
        sb.AppendLine("| Control block | APPID | MAC | VLAN | svID | Rate | ASDU | Members |");
        sb.AppendLine("|---|---:|---|---:|---|---:|---:|---:|");
        foreach (var stream in ProcessBus.SampledValuesStreams)
            sb.AppendLine($"| `{stream.ControlBlockReference}` | {AppId(stream.Address.AppId)} | `{Dash(stream.Address.DestinationMacText)}` | {Vlan(stream.Address)} | `{Dash(stream.SvId)}` | {stream.SampleRate} | {stream.NoAsdu} | {stream.Entries.Count} |");
        if (ProcessBus.SampledValuesStreams.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Expected Report Sessions");
        sb.AppendLine();
        sb.AppendLine("| Control block | Type | Indexed | DataSet | ConfRev | Members | BufTm | IntgPd |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|");
        foreach (var report in ProcessBus.ReportControls)
            sb.AppendLine($"| `{report.ControlBlockReference}` | {(report.Buffered ? "BRCB" : "URCB")} | {YesNo(report.Indexed)} | `{Dash(report.DataSetReference)}` | {report.ConfigurationRevision} | {report.Entries.Count} | {report.BufferTimeMilliseconds} | {report.IntegrityPeriodMilliseconds} |");
        if (ProcessBus.ReportControls.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Subscriber External References");
        sb.AppendLine();
        sb.AppendLine("| Subscriber | Source signal | Service | Source CB |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var extRef in ExternalReferences)
            sb.AppendLine($"| `{extRef.SubscriberReference}` | `{Dash(extRef.SourceSignalReference)}` | `{Dash(extRef.ServiceType)}` | `{Dash(extRef.SourceControlBlockName)}` |");
        if (ExternalReferences.Count == 0)
            sb.AppendLine("| - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Message |");
        sb.AppendLine("|---|---|---|");
        foreach (var finding in Findings)
            sb.AppendLine($"| {finding.Severity} | `{finding.Code}` | {EscapeMarkdown(finding.Message)} |");
        if (Findings.Count == 0)
            sb.AppendLine("| Info | `SCL_PROFILE_OK` | No engineering blocking issue detected by static profile checks. |");

        return sb.ToString();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
    private static string Dash(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    private static string AppId(ushort? appId) => appId.HasValue ? $"0x{appId.Value:X4}" : "-";
    private static string Vlan(global::AR.Iec61850.Scl.SclStreamAddress address)
        => address.VlanId.HasValue ? $"{address.VlanId.Value}/{address.VlanPriority.GetValueOrDefault()}" : "-";
    private static string EscapeMarkdown(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}

public sealed class SclEngineeringIed
{
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string ConfigVersion { get; init; } = string.Empty;
    public int AccessPointCount { get; init; }
    public int LogicalDeviceCount { get; init; }
}

public sealed class SclEngineeringAccessPoint
{
    public string IedName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool HasServer { get; init; }
    public string Router { get; init; } = string.Empty;
    public int LogicalDeviceCount { get; init; }
}

public sealed class SclEngineeringLogicalDevice
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string Inst { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int LogicalNodeCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlCount { get; init; }
    public int GooseControlCount { get; init; }
    public int SampledValueControlCount { get; init; }
}

public sealed class SclEngineeringLogicalNode
{
    public string IedName { get; init; } = string.Empty;
    public string LogicalDeviceInst { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string LnClass { get; init; } = string.Empty;
    public string LnInst { get; init; } = string.Empty;
    public string LnType { get; init; } = string.Empty;
    public int DataObjectCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlCount { get; init; }
    public int GooseControlCount { get; init; }
    public int SampledValueControlCount { get; init; }
    public int InputReferenceCount { get; init; }
}

public sealed class SclEngineeringExternalReference
{
    public string SubscriberIedName { get; init; } = string.Empty;
    public string SubscriberLdInst { get; init; } = string.Empty;
    public string SubscriberLogicalNode { get; init; } = string.Empty;
    public string SubscriberReference => string.IsNullOrWhiteSpace(SubscriberLdInst)
        ? $"{SubscriberIedName}/{SubscriberLogicalNode}"
        : $"{SubscriberIedName}/{SubscriberLdInst}/{SubscriberLogicalNode}";
    public string SourceIedName { get; init; } = string.Empty;
    public string SourceLdInst { get; init; } = string.Empty;
    public string SourcePrefix { get; init; } = string.Empty;
    public string SourceLnClass { get; init; } = string.Empty;
    public string SourceLnInst { get; init; } = string.Empty;
    public string DoName { get; init; } = string.Empty;
    public string DaName { get; init; } = string.Empty;
    public string ServiceType { get; init; } = string.Empty;
    public string SourceControlBlockName { get; init; } = string.Empty;
    public string SourceSignalReference { get; init; } = string.Empty;
}

public sealed class SclEngineeringCapabilityMatrix
{
    public bool HasServerModel { get; init; }
    public bool HasDataSets { get; init; }
    public bool HasReports { get; init; }
    public bool HasBufferedReports { get; init; }
    public bool HasUnbufferedReports { get; init; }
    public bool HasGoose { get; init; }
    public bool HasSampledValues { get; init; }
    public bool HasExternalReferences { get; init; }
    public bool HasControlObjects { get; init; }
    public bool HasSettingGroups { get; init; }
    public bool FileServiceDeclared { get; init; }
    public bool LogServiceDeclared { get; init; }
    public bool GooseServiceDeclared { get; init; }
    public bool SampledValuesServiceDeclared { get; init; }
    public bool ReportServiceDeclared { get; init; }
}

public sealed class SclEngineeringStreamSummary
{
    public int DataSetCount { get; init; }
    public IReadOnlyList<SclGooseStream> GooseStreams { get; init; } = Array.Empty<SclGooseStream>();
    public IReadOnlyList<SclSampledValuesStream> SampledValuesStreams { get; init; } = Array.Empty<SclSampledValuesStream>();
    public IReadOnlyList<SclReportControl> ReportControls { get; init; } = Array.Empty<SclReportControl>();
}

public sealed class SclEngineeringFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
}
