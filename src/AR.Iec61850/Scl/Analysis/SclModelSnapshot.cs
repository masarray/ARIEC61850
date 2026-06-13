using System.Text.Json.Serialization;

namespace AR.Iec61850.Scl.Analysis;

public sealed class SclModelSnapshot
{
    public string SourcePath { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string NamespaceUri { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string Release { get; init; } = string.Empty;
    public IReadOnlyList<string> IedNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LogicalDevices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LogicalNodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DataSets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReportControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> GooseControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SampledValueControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SettingControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LogControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LNodeTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DoTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DaTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EnumTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SclDoCdcBinding> DoCdcBindings { get; init; } = Array.Empty<SclDoCdcBinding>();
    public IReadOnlyList<SclTypeSignature> DoTypeSignatures { get; init; } = Array.Empty<SclTypeSignature>();
    public IReadOnlyList<SclTypeSignature> DaTypeSignatures { get; init; } = Array.Empty<SclTypeSignature>();
    public IReadOnlyDictionary<string, string> ServiceCapabilities { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int ControlBlockCount => ReportControls.Count + GooseControls.Count + SampledValueControls.Count + SettingControls.Count + LogControls.Count;
}

public sealed class SclDoCdcBinding
{
    public string LogicalNodeClass { get; init; } = string.Empty;
    public string DataObjectName { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string DoTypeId { get; init; } = string.Empty;
    public string SourceLNodeTypeId { get; init; } = string.Empty;

    [JsonIgnore]
    public string Key => string.IsNullOrWhiteSpace(LogicalNodeClass)
        ? DataObjectName
        : $"{LogicalNodeClass}.{DataObjectName}";
}

public sealed class SclTypeSignature
{
    public string Kind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public int MemberCount { get; init; }
}

public sealed class SclGoldenDiffReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string GoldenPath { get; init; } = string.Empty;
    public string CandidatePath { get; init; } = string.Empty;
    public SclModelSnapshot Golden { get; init; } = new();
    public SclModelSnapshot Candidate { get; init; } = new();
    public SclDiffSet LogicalDevices { get; init; } = new();
    public SclDiffSet LogicalNodes { get; init; } = new();
    public SclDiffSet DataSets { get; init; } = new();
    public SclDiffSet Reports { get; init; } = new();
    public SclDiffSet GooseControls { get; init; } = new();
    public SclDiffSet SampledValueControls { get; init; } = new();
    public SclDiffSet SettingControls { get; init; } = new();
    public SclDiffSet LogControls { get; init; } = new();
    public SclDiffSet LNodeTypes { get; init; } = new();
    public SclDiffSet DoTypes { get; init; } = new();
    public SclDiffSet DaTypes { get; init; } = new();
    public SclDiffSet EnumTypes { get; init; } = new();
    public IReadOnlyList<SclCdcDifference> CdcDifferences { get; init; } = Array.Empty<SclCdcDifference>();
    public IReadOnlyList<SclServiceCapabilityDifference> ServiceCapabilityDifferences { get; init; } = Array.Empty<SclServiceCapabilityDifference>();
    public IReadOnlyList<SclTypeReuseSummary> TypeReuse { get; init; } = Array.Empty<SclTypeReuseSummary>();

    [JsonIgnore]
    public bool HasMaterialDifferences =>
        LogicalDevices.HasDifferences || LogicalNodes.HasDifferences || DataSets.HasDifferences || Reports.HasDifferences ||
        GooseControls.HasDifferences || SampledValueControls.HasDifferences || SettingControls.HasDifferences || LogControls.HasDifferences ||
        CdcDifferences.Count > 0;
}

public sealed class SclDiffSet
{
    public string Kind { get; init; } = string.Empty;
    public int GoldenCount { get; init; }
    public int CandidateCount { get; init; }
    public IReadOnlyList<string> MissingInCandidate { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraInCandidate { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public bool HasDifferences => MissingInCandidate.Count > 0 || ExtraInCandidate.Count > 0;
}

public sealed class SclCdcDifference
{
    public string Key { get; init; } = string.Empty;
    public IReadOnlyList<string> GoldenCdc { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateCdc { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> GoldenDoTypeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateDoTypeIds { get; init; } = Array.Empty<string>();
}

public sealed class SclServiceCapabilityDifference
{
    public string Service { get; init; } = string.Empty;
    public string GoldenValue { get; init; } = string.Empty;
    public string CandidateValue { get; init; } = string.Empty;
}

public sealed class SclTypeReuseSummary
{
    public string Kind { get; init; } = string.Empty;
    public int GoldenTypeCount { get; init; }
    public int GoldenUniqueShapeCount { get; init; }
    public int CandidateTypeCount { get; init; }
    public int CandidateUniqueShapeCount { get; init; }
    public int CandidateDuplicateShapeCount => Math.Max(0, CandidateTypeCount - CandidateUniqueShapeCount);
}
