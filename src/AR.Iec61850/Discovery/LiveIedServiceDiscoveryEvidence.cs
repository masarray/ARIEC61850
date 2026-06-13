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

public sealed class LiveIedOnlineServiceEvidence
{
    public LiveIedFileServiceEvidence FileService { get; init; } = new();
    public IReadOnlyList<LiveIedSettingGroupReadbackEvidence> SettingGroupReadbacks { get; init; } = Array.Empty<LiveIedSettingGroupReadbackEvidence>();
}
