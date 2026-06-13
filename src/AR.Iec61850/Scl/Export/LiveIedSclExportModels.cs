using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

public sealed class LiveIedSclExportOptions
{
    public string Profile { get; init; } = "connection";
    public string SubNetworkName { get; init; } = "StationBus";
    public string IpAddress { get; init; } = string.Empty;
    public string IpSubnet { get; init; } = "255.255.255.0";
    public string IpGateway { get; init; } = "0.0.0.0";
    public string OsiApTitle { get; init; } = string.Empty;
    public string OsiAeQualifier { get; init; } = string.Empty;
    public string OsiPsel { get; init; } = "00000001";
    public string OsiSsel { get; init; } = "0001";
    public string OsiTsel { get; init; } = "0001";
    public bool IncludeDefaultOsiParameters { get; init; } = true;
    public bool IncludeRuntimeStateComment { get; init; } = true;
    public bool IncludeLowConfidenceTypes { get; init; } = true;
    public LiveIedSclLogicalDeviceNameMode LogicalDeviceNameMode { get; init; } = LiveIedSclLogicalDeviceNameMode.Auto;
}

public enum LiveIedSclLogicalDeviceNameMode
{
    Auto,
    Keep
}

public sealed class LiveIedSclExportResult
{
    public string SchemaVersion { get; init; } = "live-to-scl-export-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Profile { get; init; } = string.Empty;
    public string SclPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlCount { get; init; }
    public int GooseControlBlockCount { get; init; }
    public int SampledValueControlBlockCount { get; init; }
    public int SettingGroupControlCount { get; init; }
    public int LogControlCount { get; init; }
    public int LNodeTypeCount { get; init; }
    public int DoTypeCount { get; init; }
    public int DaTypeCount { get; init; }
    public int EnumTypeCount { get; init; }
    public IReadOnlyList<LiveIedSclExportWarning> Warnings { get; init; } = Array.Empty<LiveIedSclExportWarning>();
    public IReadOnlyList<LiveIedSclExportMapping> DataSetMappings { get; init; } = Array.Empty<LiveIedSclExportMapping>();
    public IReadOnlyList<LiveIedSclExportMapping> ReportMappings { get; init; } = Array.Empty<LiveIedSclExportMapping>();
    public IReadOnlyList<LiveIedSclExportMapping> ControlBlockMappings { get; init; } = Array.Empty<LiveIedSclExportMapping>();
}

public sealed class LiveIedSclExportWarning
{
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class LiveIedSclExportMapping
{
    public string Kind { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
    public string SclReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

internal sealed class LiveIedSclBuildContext
{
    public Dictionary<string, string> SclLogicalDeviceInstByMmsDomain { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> LogicalNodeTypeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DataObjectTypeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DataAttributeTypeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<XElement> LNodeTypes { get; } = [];
    public List<XElement> DoTypes { get; } = [];
    public List<XElement> DaTypes { get; } = [];
    public List<XElement> EnumTypes { get; } = [];
    public List<LiveIedSclExportWarning> Warnings { get; } = [];
    public List<LiveIedSclExportMapping> DataSetMappings { get; } = [];
    public List<LiveIedSclExportMapping> ReportMappings { get; } = [];
    public List<LiveIedSclExportMapping> ControlBlockMappings { get; } = [];
    public HashSet<string> UsedIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}
