namespace AR.Iec61850.Discovery;

public enum LiveIedDiscoveryConfidenceLevel
{
    Exact,
    High,
    Medium,
    Low,
    Unknown
}

public sealed class LiveIedModelDiscoveryDocument
{
    public string SchemaVersion { get; init; } = "live-ied-model-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "LiveMmsDiscovery";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string IedName { get; init; } = string.Empty;
    public LiveIedIdentity IedIdentity { get; init; } = new();
    public string AccessPointName { get; init; } = "AP1";
    public string Summary { get; init; } = string.Empty;
    public LiveIedModelDiscoveryCoverage Coverage { get; init; } = new();
    public IReadOnlyList<LiveIedLogicalDeviceModel> LogicalDevices { get; init; } = Array.Empty<LiveIedLogicalDeviceModel>();
    public LiveIedFileDirectoryModel FileDirectory { get; init; } = new();
    public IReadOnlyList<LiveIedDataSetModel> DataSets { get; init; } = Array.Empty<LiveIedDataSetModel>();
    public IReadOnlyList<LiveIedReportControlModel> ReportControls { get; init; } = Array.Empty<LiveIedReportControlModel>();
    public IReadOnlyList<LiveIedControlBlockModel> GooseControlBlocks { get; init; } = Array.Empty<LiveIedControlBlockModel>();
    public IReadOnlyList<LiveIedControlBlockModel> SampledValueControlBlocks { get; init; } = Array.Empty<LiveIedControlBlockModel>();
    public IReadOnlyList<LiveIedControlBlockModel> SettingGroupControls { get; init; } = Array.Empty<LiveIedControlBlockModel>();
    public IReadOnlyList<LiveIedControlBlockModel> LogControls { get; init; } = Array.Empty<LiveIedControlBlockModel>();
    public IReadOnlyList<LiveIedTypeTemplateCandidate> TypeTemplates { get; init; } = Array.Empty<LiveIedTypeTemplateCandidate>();
    public IReadOnlyList<LiveIedVariableTypeDiscoveryModel> VariableTypeDiscoveries { get; init; } = Array.Empty<LiveIedVariableTypeDiscoveryModel>();
    public IReadOnlyList<LiveIedDiscoveryWarning> Warnings { get; init; } = Array.Empty<LiveIedDiscoveryWarning>();
}

public sealed class LiveIedModelDiscoveryCoverage
{
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int DataObjectCount { get; init; }
    public int DataAttributeCount { get; init; }
    public int ExactFunctionalConstraintCount { get; init; }
    public int HighConfidenceCdcCount { get; init; }
    public int MediumConfidenceCdcCount { get; init; }
    public int LowConfidenceCdcCount { get; init; }
    public int UnknownCdcCount { get; init; }
    public int DataSetCount { get; init; }
    public int FileCount { get; init; }
    public int VariableTypeReadAttemptCount { get; init; }
    public int VariableTypeReadSuccessCount { get; init; }
    public int VariableTypeReadFailureCount { get; init; }
    public int ExactMmsTypeCount { get; init; }
    public int ReportControlCount { get; init; }
    public int BufferedReportControlCount { get; init; }
    public int UnbufferedReportControlCount { get; init; }
    public int GooseControlBlockCount { get; init; }
    public int SampledValueControlBlockCount { get; init; }
    public int SettingGroupControlCount { get; init; }
    public int LogControlCount { get; init; }
}

public sealed class LiveIedFileDirectoryModel
{
    public bool Attempted { get; init; }
    public bool IsSuccess { get; init; }
    public string DirectoryName { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedFileModel> Entries { get; init; } = Array.Empty<LiveIedFileModel>();
}

public sealed class LiveIedFileModel
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public uint? SizeBytes { get; init; }
    public string LastModified { get; init; } = string.Empty;
    public bool IsLikelyDirectory { get; init; }
    public string Source { get; init; } = "MmsFileDirectory";
}

public sealed class LiveIedLogicalDeviceModel
{
    public string MmsDomain { get; init; } = string.Empty;
    public string Inst { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedLogicalNodeModel> LogicalNodes { get; init; } = Array.Empty<LiveIedLogicalNodeModel>();
}

public sealed class LiveIedLogicalNodeModel
{
    public string Name { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string LnClass { get; init; } = string.Empty;
    public string LnInst { get; init; } = string.Empty;
    public string ProposedLnTypeId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> FunctionalConstraintCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<LiveIedDataObjectModel> DataObjects { get; init; } = Array.Empty<LiveIedDataObjectModel>();
}

public sealed class LiveIedDataObjectModel
{
    public string Reference { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ProposedDoTypeId { get; init; } = string.Empty;
    public string InferredCdc { get; init; } = string.Empty;
    public double CdcConfidence { get; init; }
    public LiveIedDiscoveryConfidenceLevel ConfidenceLevel { get; init; } = LiveIedDiscoveryConfidenceLevel.Unknown;
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<LiveIedDataAttributeModel> Attributes { get; init; } = Array.Empty<LiveIedDataAttributeModel>();
}

public sealed class LiveIedDataAttributeModel
{
    public string ObjectReference { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public string MmsTypeSignature { get; init; } = string.Empty;
    public string TypeDiscoveryStatus { get; init; } = "NotRead";
    public string TypeDiscoveryMessage { get; init; } = string.Empty;
    public string TypeSource { get; init; } = "NameListHeuristic";
    public LiveIedDiscoveryConfidenceLevel TypeConfidence { get; init; } = LiveIedDiscoveryConfidenceLevel.Low;
    public LiveIedDiscoveryConfidenceLevel FunctionalConstraintConfidence { get; init; } = LiveIedDiscoveryConfidenceLevel.Exact;
}

public sealed class LiveIedDataSetModel
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool? IsDeletable { get; init; }
    public int MemberCount { get; init; }
    public IReadOnlyList<LiveIedDataSetMemberModel> Members { get; init; } = Array.Empty<LiveIedDataSetMemberModel>();
    public IReadOnlyList<string> UsedByReportControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UsedByGooseControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UsedBySampledValueControls { get; init; } = Array.Empty<string>();
}

public sealed class LiveIedDataSetMemberModel
{
    public int Index { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public LiveIedDiscoveryConfidenceLevel Confidence { get; init; } = LiveIedDiscoveryConfidenceLevel.Exact;
}

public sealed class LiveIedReportControlModel
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string BufferTimeMs { get; init; } = string.Empty;
    public string IntegrityPeriodMs { get; init; } = string.Empty;
    public string EnabledState { get; init; } = string.Empty;
    public string ReservationState { get; init; } = string.Empty;
    public string ReservationTimeSeconds { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class LiveIedControlBlockModel
{
    public string Kind { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public int AttributeCount { get; init; }
    public IReadOnlyList<string> Attributes { get; init; } = Array.Empty<string>();
    public string DataSetReference { get; init; } = string.Empty;
    public string DataSetReferenceStatus { get; init; } = "NotRead";
    public string ControlId { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string SmvId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string MinimumTimeMs { get; init; } = string.Empty;
    public string MaximumTimeMs { get; init; } = string.Empty;
    public string SampleRate { get; init; } = string.Empty;
    public string SampleMode { get; init; } = string.Empty;
    public string NumberOfAsdu { get; init; } = string.Empty;
    public string AddressStatus { get; init; } = "NotDiscovered";
    public string DiscoveryStatus { get; init; } = "AttributeInventoryOnly";
    public string Message { get; init; } = string.Empty;
}

public sealed class LiveIedTypeTemplateCandidate
{
    public string TemplateKind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
    public string InferredType { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();
}

public sealed class LiveIedVariableTypeDiscoveryModel
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string MmsType { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string TypeSignature { get; init; } = string.Empty;
    public bool? IsMmsDeletable { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = "GetVariableAccessAttributes";
}

public sealed class LiveIedDiscoveryWarning
{
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
