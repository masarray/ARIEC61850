namespace AR.Iec61850.Discovery;

public enum Iec61850SignalCatalogResolutionStatus
{
    Unresolved,
    DesignAttribute,
    DataSetResolvedAttribute,
    DataSetSyntheticFallback,
    LiveOnly
}

public enum Iec61850SignalEvidenceKind
{
    DesignModel,
    DataSetSemanticBinding,
    ReportControlMembership,
    LiveDiscovery,
    ExactProbe,
    AlternateProbe,
    AlternateDiscovery,
    ReconciliationDiagnostic
}

public sealed class Iec61850SignalDataSetMembership
{
    public string DataSetReference { get; init; } = string.Empty;
    public int MemberIndex { get; init; }
    public string OriginalMemberReference { get; init; } = string.Empty;
    public string CanonicalMemberReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public LiveIedDataSetMemberResolutionStatus ResolutionStatus { get; init; }
    public bool IsPrimaryValueForMember { get; init; }
}

public sealed class Iec61850SignalReportMembership
{
    public string ReportControlReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public bool? Buffered { get; init; }
    public string ReportId { get; init; } = string.Empty;
}

public sealed class Iec61850SignalEvidence
{
    public Iec61850SignalEvidenceKind Kind { get; init; }
    public string SourceReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Read-only signal projection that combines existing SCL, DataSet semantic binding and
/// design/live reconciliation evidence. The catalog does not replace those authorities;
/// it provides one stable descriptor for application consumers while migration is staged.
/// </summary>
public sealed class Iec61850SignalDescriptor
{
    public string DesignReference { get; init; } = string.Empty;
    public string ObservedReference { get; init; } = string.Empty;
    public string CanonicalMmsReference { get; init; } = string.Empty;
    public string EffectiveMmsReference { get; init; } = string.Empty;
    public string ObservedMmsReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public string MmsDomain { get; init; } = string.Empty;
    public string LogicalDevice { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string LogicalNodeClass { get; init; } = string.Empty;
    public string DataObject { get; init; } = string.Empty;
    public string DataObjectReference { get; init; } = string.Empty;
    public string DataAttributePath { get; init; } = string.Empty;
    public Iec61850DataAttributeSemanticRole SemanticRole { get; init; } = Iec61850DataAttributeSemanticRole.Other;
    public string PrimaryValueReference { get; init; } = string.Empty;
    public string PrimaryValueMmsReference { get; init; } = string.Empty;
    public string QualityReference { get; init; } = string.Empty;
    public string QualityMmsReference { get; init; } = string.Empty;
    public string TimestampReference { get; init; } = string.Empty;
    public string TimestampMmsReference { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850SignalDataSetMembership> DataSetMemberships { get; init; }
        = Array.Empty<Iec61850SignalDataSetMembership>();
    public IReadOnlyList<Iec61850SignalReportMembership> ReportMemberships { get; init; }
        = Array.Empty<Iec61850SignalReportMembership>();
    public bool IsStaticDataSetMandatory { get; init; }
    public bool IsOperationalCandidate { get; init; }

    /// <summary>
    /// True only when the current engine evidence identifies neither a static-DataSet
    /// obligation nor an operational value-bearing role. This is a catalog classification,
    /// not permission to discard the signal.
    /// </summary>
    public bool IsEngineeringOnly { get; init; }

    public Iec61850SignalCatalogResolutionStatus ResolutionStatus { get; init; }
    public Iec61850DesignLiveStatus? LiveStatus { get; init; }
    public Iec61850AlternateReferenceStrategyKind? AlternateStrategy { get; init; }
    public IReadOnlyList<Iec61850SignalEvidence> Evidence { get; init; }
        = Array.Empty<Iec61850SignalEvidence>();

    public bool IsVerifiedPresent => LiveStatus is
        Iec61850DesignLiveStatus.Exact or
        Iec61850DesignLiveStatus.Compatible or
        Iec61850DesignLiveStatus.RecoveredByProbe or
        Iec61850DesignLiveStatus.RecoveredByAlternateProbe or
        Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery;

    public bool IsConfirmedAbsent => LiveStatus == Iec61850DesignLiveStatus.Absent;
}

public sealed class Iec61850SignalCatalogDocument
{
    public string SchemaVersion { get; init; } = "iec61850-signal-catalog-v1";
    public string IedName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850SignalDescriptor> Signals { get; init; }
        = Array.Empty<Iec61850SignalDescriptor>();

    public int SignalCount => Signals.Count;
    public int DesignSignalCount => Signals.Count(x => x.ResolutionStatus != Iec61850SignalCatalogResolutionStatus.LiveOnly);
    public int LiveOnlyCount => Signals.Count(x => x.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly);
    public int StaticDataSetMandatoryCount => Signals.Count(x => x.IsStaticDataSetMandatory);
    public int OperationalCandidateCount => Signals.Count(x => x.IsOperationalCandidate);
    public int VerifiedPresentCount => Signals.Count(x => x.IsVerifiedPresent);
    public int ConfirmedAbsentCount => Signals.Count(x => x.IsConfirmedAbsent);

    public Iec61850SignalDescriptor? FindByCanonicalMmsReference(string? mmsReference)
        => Signals.FirstOrDefault(x => string.Equals(
            NormalizeMmsReference(x.CanonicalMmsReference),
            NormalizeMmsReference(mmsReference),
            StringComparison.OrdinalIgnoreCase));

    public Iec61850SignalDescriptor? FindByEffectiveMmsReference(string? mmsReference)
        => Signals.FirstOrDefault(x => string.Equals(
            NormalizeMmsReference(x.EffectiveMmsReference),
            NormalizeMmsReference(mmsReference),
            StringComparison.OrdinalIgnoreCase));

    private static string NormalizeMmsReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/');
}
