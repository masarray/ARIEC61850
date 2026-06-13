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

public sealed class LiveIedOnlineServiceEvidence
{
    public LiveIedFileServiceEvidence FileService { get; init; } = new();
    public IReadOnlyList<LiveIedSettingGroupReadbackEvidence> SettingGroupReadbacks { get; init; } = Array.Empty<LiveIedSettingGroupReadbackEvidence>();
    public LiveIedSettingGroupMapDocument SettingGroupMap { get; init; } = new();
}
