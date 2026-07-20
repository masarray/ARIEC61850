using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

public sealed class LiveIedSclExportOptions
{
    public string Profile { get; init; } = "safe-connection";
    public SclSchemaProfile SchemaProfile { get; init; } = SclSchemaProfile.Edition2V31;
    public string SubNetworkName { get; init; } = "StationBus";
    public string IpAddress { get; init; } = string.Empty;
    /// <summary>
    /// Authoritative SCL IED identity supplied by the caller. MMS exposes Logical Device
    /// domain names, which are not a reliable source for recovering the enclosing IED name
    /// when an implementation uses explicit communication-level LD names.
    /// </summary>
    public string IedNameOverride { get; init; } = string.Empty;
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

    public LiveIedSclExportProfile ResolvedProfile => LiveIedSclExportProfileParser.Parse(Profile);
    public SclSchemaProfileDescriptor ResolvedSchemaProfile => SclSchemaProfiles.Get(SchemaProfile);
}

public enum LiveIedSclExportProfile
{
    SafeConnection,
    FullModel,
    SimulatorSeed
}

public static class LiveIedSclExportProfileParser
{
    public static LiveIedSclExportProfile Parse(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "connection" or "safe" or "safe-connection" or "client" => LiveIedSclExportProfile.SafeConnection,
            "full" or "full-model" or "model" or "audit" or "standard-discovery" or "discovery" => LiveIedSclExportProfile.FullModel,
            "sim" or "simulator" or "simulator-seed" or "server" => LiveIedSclExportProfile.SimulatorSeed,
            _ => throw new ArgumentException("SCL export profile must be safe-connection, full-model/standard-discovery, or simulator-seed.")
        };

    public static string ToProfileName(LiveIedSclExportProfile profile)
        => profile switch
        {
            LiveIedSclExportProfile.SafeConnection => "safe-connection",
            LiveIedSclExportProfile.FullModel => "full-model",
            LiveIedSclExportProfile.SimulatorSeed => "simulator-seed",
            _ => "safe-connection"
        };
}

public enum LiveIedSclLogicalDeviceNameMode
{
    Auto,
    Keep
}

public sealed class LiveIedSclExportResult
{
    public string SchemaVersion { get; init; } = "live-to-scl-export-v2";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Profile { get; init; } = string.Empty;
    public string SclSchema { get; init; } = string.Empty;
    public string SclPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public string ExcludedAttributesPath { get; init; } = string.Empty;
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
    public IReadOnlyList<LiveIedSclExcludedAttribute> ExcludedAttributes { get; init; } = Array.Empty<LiveIedSclExcludedAttribute>();
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

public sealed class LiveIedSclExcludedAttribute
{
    public string Profile { get; init; } = string.Empty;
    public string DataObjectReference { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
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
    public Dictionary<string, string> EnumTypeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LiveIedSclExportWarning> Warnings { get; } = [];
    public List<LiveIedSclExcludedAttribute> ExcludedAttributes { get; } = [];
    public List<LiveIedSclExportMapping> DataSetMappings { get; } = [];
    public List<LiveIedSclExportMapping> ReportMappings { get; } = [];
    public List<LiveIedSclExportMapping> ControlBlockMappings { get; } = [];
    public HashSet<string> UsedIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}
