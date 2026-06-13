namespace AR.Iec61850.Discovery;

public sealed class LiveIedFileServiceEvidence
{
    public string DirectoryName { get; init; } = string.Empty;
    public bool Attempted { get; init; }
    public bool IsSuccess { get; init; }
    public int PageCount { get; init; }
    public bool MoreFollows { get; init; }
    public IReadOnlyList<LiveIedFileEntryEvidence> Entries { get; init; } = Array.Empty<LiveIedFileEntryEvidence>();
    public string Message { get; init; } = string.Empty;
}

public sealed class LiveIedFileEntryEvidence
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public uint? SizeBytes { get; init; }
    public string LastModifiedRaw { get; init; } = string.Empty;
    public bool IsLikelyDirectory { get; init; }
}

public sealed class LiveIedSettingGroupReadbackEvidence
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedSettingGroupAttributeReadback> Attributes { get; init; } = Array.Empty<LiveIedSettingGroupAttributeReadback>();
    public bool HasAnySuccess => Attributes.Any(x => x.IsSuccess);
}

public sealed class LiveIedSettingGroupAttributeReadback
{
    public string Name { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class LiveIedSettingGroupMapDocument
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Summary { get; init; } = string.Empty;
    public int SettingGroupControlCount { get; init; }
    public int CoreReadbackCompleteCount { get; init; }
    public int NumberOfSettingGroups { get; init; }
    public int ActiveSettingGroup { get; init; }
    public int EditSettingGroup { get; init; }
    public bool? ConfirmEdit { get; init; }
    public int EntryCount { get; init; }
    public int ReadAttemptCount { get; init; }
    public int ReadSuccessCount { get; init; }
    public int ReadFailureCount { get; init; }
    public IReadOnlyList<LiveIedSettingGroupMapEntry> Entries { get; init; } = Array.Empty<LiveIedSettingGroupMapEntry>();
}

public sealed class LiveIedSettingGroupMapEntry
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string LogicalNodeClass { get; init; } = string.Empty;
    public string DataObject { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string InferredCdc { get; init; } = string.Empty;
    public double CdcConfidence { get; init; }
    public string SclBType { get; init; } = string.Empty;
    public string TypeSource { get; init; } = string.Empty;
    public bool ReadAttempted { get; init; }
    public bool IsReadSuccess { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class LiveIedVariableTypeProbeEvidence
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool Attempted { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
    public int MaxReads { get; init; }
    public int DelayMs { get; init; }
    public int RawCandidateCount { get; init; }
    public int SelectedCandidateCount { get; init; }
    public int SkippedCandidateCount { get; init; }
    public int AttemptCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int ExactScalarTypeCount { get; init; }
    public int ExactStructureTypeCount { get; init; }
    public bool StoppedBeforeCandidateExhausted { get; init; }
    public bool ProtocolFaultSuspected { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedVariableTypeProbeSkipSummary> SkippedByReason { get; init; } = Array.Empty<LiveIedVariableTypeProbeSkipSummary>();
    public IReadOnlyList<LiveIedVariableTypeProbeCandidateEvidence> SelectedCandidates { get; init; } = Array.Empty<LiveIedVariableTypeProbeCandidateEvidence>();
    public IReadOnlyList<LiveIedVariableTypeProbeResultEvidence> Results { get; init; } = Array.Empty<LiveIedVariableTypeProbeResultEvidence>();
}

public sealed class LiveIedVariableTypeProbeSkipSummary
{
    public string Reason { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class LiveIedVariableTypeProbeCandidateEvidence
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class LiveIedVariableTypeProbeResultEvidence
{
    public string Reference { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string MmsType { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string TypeSignature { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}


public sealed class LiveIedVariableSpecQuarantineEvidence
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsEnabled { get; init; } = true;
    public bool IsQuarantined { get; init; }
    public string Scope { get; init; } = "Session";
    public string TargetKey { get; init; } = string.Empty;
    public string TriggerReference { get; init; } = string.Empty;
    public string TriggerMessage { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool CoreDiscoveryPreserved { get; init; } = true;
    public string Recommendation { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed class LiveIedGoldenSclTypeLearningEvidence
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool Attempted { get; init; }
    public string GoldenSclPath { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public int GoldenBindingCount { get; init; }
    public int LiveDataObjectCount { get; init; }
    public int LiveUnknownOrMediumCount { get; init; }
    public int ExactKeyMatchCount { get; init; }
    public int CandidateImprovementCount { get; init; }
    public int CdcConflictCount { get; init; }
    public IReadOnlyList<LiveIedGoldenSclTypeLearningEntry> Candidates { get; init; } = Array.Empty<LiveIedGoldenSclTypeLearningEntry>();
    public IReadOnlyList<LiveIedGoldenSclTypeLearningConflict> Conflicts { get; init; } = Array.Empty<LiveIedGoldenSclTypeLearningConflict>();
    public string Summary { get; init; } = string.Empty;
}

public sealed class LiveIedGoldenSclTypeLearningEntry
{
    public string Reference { get; init; } = string.Empty;
    public string LogicalNodeClass { get; init; } = string.Empty;
    public string DataObjectName { get; init; } = string.Empty;
    public string CurrentCdc { get; init; } = string.Empty;
    public string GoldenCdc { get; init; } = string.Empty;
    public string GoldenDoTypeId { get; init; } = string.Empty;
    public string CurrentConfidence { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
}

public sealed class LiveIedGoldenSclTypeLearningConflict
{
    public string Key { get; init; } = string.Empty;
    public string LiveCdc { get; init; } = string.Empty;
    public string GoldenCdc { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}


public sealed class LiveIedOnlineServiceEvidence
{
    public LiveIedFileServiceEvidence FileService { get; init; } = new();
    public IReadOnlyList<LiveIedSettingGroupReadbackEvidence> SettingGroupReadbacks { get; init; } = Array.Empty<LiveIedSettingGroupReadbackEvidence>();
    public LiveIedSettingGroupMapDocument SettingGroupMap { get; init; } = new();
    public LiveIedVariableTypeProbeEvidence VariableTypeProbe { get; init; } = new();
    public LiveIedVariableSpecQuarantineEvidence VariableSpecQuarantine { get; init; } = new();
    public LiveIedGoldenSclTypeLearningEvidence GoldenSclTypeLearning { get; init; } = new();
}
