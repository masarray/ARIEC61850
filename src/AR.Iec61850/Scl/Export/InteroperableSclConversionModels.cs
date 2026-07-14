using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace AR.Iec61850.Scl.Export;

public sealed class InteroperableSclConversionOptions
{
    public string IedName { get; init; } = string.Empty;
    public bool PreserveAllIeds { get; init; }
    public bool RemoveExternalInputs { get; init; } = true;
    public bool RemoveUnusedTypeTemplates { get; init; } = true;
    public bool RemoveSubstationSection { get; init; } = true;
    public string ToolId { get; init; } = "ARIEC61850";
}

public sealed record class InteroperableSclConversionResult
{
    [JsonIgnore]
    public XDocument Document { get; init; } = new();

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public string SelectedIedName { get; init; } = string.Empty;
    public IReadOnlyList<string> OutputIedNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedIedNames { get; init; } = Array.Empty<string>();
    public int RemovedPrivateElementCount { get; init; }
    public int RemovedVendorElementCount { get; init; }
    public int RemovedVendorAttributeCount { get; init; }
    public int RemovedCompatibilityElementCount { get; init; }
    public int RemovedExternalInputCount { get; init; }
    public int RemovedUnusedTypeTemplateCount { get; init; }
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlCount { get; init; }
    public int GooseControlBlockCount { get; init; }
    public int SampledValueControlBlockCount { get; init; }
    public int LNodeTypeCount { get; init; }
    public int DoTypeCount { get; init; }
    public int DaTypeCount { get; init; }
    public int EnumTypeCount { get; init; }
    public IReadOnlyList<InteroperableSclFinding> Findings { get; init; } = Array.Empty<InteroperableSclFinding>();
}

public sealed class InteroperableSclFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
