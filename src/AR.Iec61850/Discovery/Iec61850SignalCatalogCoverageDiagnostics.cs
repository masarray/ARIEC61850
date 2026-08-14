namespace AR.Iec61850.Discovery;

/// <summary>
/// Count-only diagnostics over an already-built typed signal catalog. The diagnostics do
/// not infer presence, absence, protocol meaning or vendor behavior; they summarize the
/// catalog classifications and reconciliation statuses exactly as recorded.
/// </summary>
public sealed class Iec61850SignalCatalogCoverageDiagnostics
{
    public int SignalCount { get; init; }
    public int DesignSignalCount { get; init; }
    public int LiveOnlyCount { get; init; }

    public int DesignAttributeCount { get; init; }
    public int DataSetResolvedAttributeCount { get; init; }
    public int DataSetSyntheticFallbackCount { get; init; }
    public int CatalogUnresolvedCount { get; init; }

    public int StaticDataSetMandatoryCount { get; init; }
    public int MandatoryPrimaryCount { get; init; }
    public int OperationalCandidateCount { get; init; }
    public int EngineeringOnlyCount { get; init; }
    public int ReportBackedSignalCount { get; init; }
    public int MultiDataSetSignalCount { get; init; }
    public int MultiReportSignalCount { get; init; }

    public int ReconciledDesignCount { get; init; }
    public int UnreconciledDesignCount { get; init; }

    public int ExactCount { get; init; }
    public int CompatibleCount { get; init; }
    public int RecoveredByProbeCount { get; init; }
    public int RecoveredByAlternateProbeCount { get; init; }
    public int RecoveredByAlternateDiscoveryCount { get; init; }
    public int VerifiedPresentCount { get; init; }
    public int DesignOnlyCount { get; init; }
    public int FunctionalConstraintMismatchCount { get; init; }
    public int TypeMismatchCount { get; init; }
    public int AmbiguousCount { get; init; }
    public int InvalidTargetCount { get; init; }
    public int UnreadableCount { get; init; }
    public int ConfirmedAbsentCount { get; init; }
    public int TransportFailureCount { get; init; }
    public int UnresolvedDesignCount { get; init; }

    public int MandatoryPrimaryReconciledCount { get; init; }
    public int MandatoryPrimaryUnreconciledCount { get; init; }
    public int MandatoryPrimaryExactCount { get; init; }
    public int MandatoryPrimaryCompatibleCount { get; init; }
    public int MandatoryPrimaryRecoveredByProbeCount { get; init; }
    public int MandatoryPrimaryRecoveredByAlternateProbeCount { get; init; }
    public int MandatoryPrimaryRecoveredByAlternateDiscoveryCount { get; init; }
    public int MandatoryPrimaryVerifiedPresentCount { get; init; }
    public int MandatoryPrimaryDesignOnlyCount { get; init; }
    public int MandatoryPrimaryFunctionalConstraintMismatchCount { get; init; }
    public int MandatoryPrimaryTypeMismatchCount { get; init; }
    public int MandatoryPrimaryAmbiguousCount { get; init; }
    public int MandatoryPrimaryInvalidTargetCount { get; init; }
    public int MandatoryPrimaryUnreadableCount { get; init; }
    public int MandatoryPrimaryConfirmedAbsentCount { get; init; }
    public int MandatoryPrimaryTransportFailureCount { get; init; }
    public int MandatoryPrimaryUnresolvedDesignCount { get; init; }

    public bool HasConfirmedAbsence => ConfirmedAbsentCount > 0;
    public bool HasConfirmedMandatoryPrimaryAbsence => MandatoryPrimaryConfirmedAbsentCount > 0;

    public static Iec61850SignalCatalogCoverageDiagnostics Create(Iec61850SignalCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var signals = catalog.Signals.ToArray();
        var design = signals.Where(IsDesign).ToArray();
        var mandatoryPrimary = design.Where(IsMandatoryPrimary).ToArray();

        static int ResolutionCount(
            IReadOnlyCollection<Iec61850SignalDescriptor> source,
            Iec61850SignalCatalogResolutionStatus status)
            => source.Count(signal => signal.ResolutionStatus == status);

        static int LiveStatusCount(
            IReadOnlyCollection<Iec61850SignalDescriptor> source,
            Iec61850DesignLiveStatus status)
            => source.Count(signal => signal.LiveStatus == status);

        return new Iec61850SignalCatalogCoverageDiagnostics
        {
            SignalCount = signals.Length,
            DesignSignalCount = design.Length,
            LiveOnlyCount = ResolutionCount(signals, Iec61850SignalCatalogResolutionStatus.LiveOnly),

            DesignAttributeCount = ResolutionCount(signals, Iec61850SignalCatalogResolutionStatus.DesignAttribute),
            DataSetResolvedAttributeCount = ResolutionCount(signals, Iec61850SignalCatalogResolutionStatus.DataSetResolvedAttribute),
            DataSetSyntheticFallbackCount = ResolutionCount(signals, Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback),
            CatalogUnresolvedCount = ResolutionCount(signals, Iec61850SignalCatalogResolutionStatus.Unresolved),

            StaticDataSetMandatoryCount = signals.Count(signal => signal.IsStaticDataSetMandatory),
            MandatoryPrimaryCount = mandatoryPrimary.Length,
            OperationalCandidateCount = signals.Count(signal => signal.IsOperationalCandidate),
            EngineeringOnlyCount = signals.Count(signal => signal.IsEngineeringOnly),
            ReportBackedSignalCount = signals.Count(signal => signal.ReportMemberships.Count > 0),
            MultiDataSetSignalCount = signals.Count(signal => signal.DataSetMemberships.Count > 1),
            MultiReportSignalCount = signals.Count(signal => signal.ReportMemberships.Count > 1),

            ReconciledDesignCount = design.Count(signal => signal.LiveStatus.HasValue),
            UnreconciledDesignCount = design.Count(signal => signal.LiveStatus is null),

            ExactCount = LiveStatusCount(design, Iec61850DesignLiveStatus.Exact),
            CompatibleCount = LiveStatusCount(design, Iec61850DesignLiveStatus.Compatible),
            RecoveredByProbeCount = LiveStatusCount(design, Iec61850DesignLiveStatus.RecoveredByProbe),
            RecoveredByAlternateProbeCount = LiveStatusCount(design, Iec61850DesignLiveStatus.RecoveredByAlternateProbe),
            RecoveredByAlternateDiscoveryCount = LiveStatusCount(design, Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery),
            VerifiedPresentCount = design.Count(signal => signal.IsVerifiedPresent),
            DesignOnlyCount = LiveStatusCount(design, Iec61850DesignLiveStatus.DesignOnly),
            FunctionalConstraintMismatchCount = LiveStatusCount(design, Iec61850DesignLiveStatus.FunctionalConstraintMismatch),
            TypeMismatchCount = LiveStatusCount(design, Iec61850DesignLiveStatus.TypeMismatch),
            AmbiguousCount = LiveStatusCount(design, Iec61850DesignLiveStatus.Ambiguous),
            InvalidTargetCount = LiveStatusCount(design, Iec61850DesignLiveStatus.InvalidTarget),
            UnreadableCount = LiveStatusCount(design, Iec61850DesignLiveStatus.Unreadable),
            ConfirmedAbsentCount = LiveStatusCount(design, Iec61850DesignLiveStatus.Absent),
            TransportFailureCount = LiveStatusCount(design, Iec61850DesignLiveStatus.TransportFailure),
            UnresolvedDesignCount = LiveStatusCount(design, Iec61850DesignLiveStatus.UnresolvedDesign),

            MandatoryPrimaryReconciledCount = mandatoryPrimary.Count(signal => signal.LiveStatus.HasValue),
            MandatoryPrimaryUnreconciledCount = mandatoryPrimary.Count(signal => signal.LiveStatus is null),
            MandatoryPrimaryExactCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.Exact),
            MandatoryPrimaryCompatibleCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.Compatible),
            MandatoryPrimaryRecoveredByProbeCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.RecoveredByProbe),
            MandatoryPrimaryRecoveredByAlternateProbeCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.RecoveredByAlternateProbe),
            MandatoryPrimaryRecoveredByAlternateDiscoveryCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery),
            MandatoryPrimaryVerifiedPresentCount = mandatoryPrimary.Count(signal => signal.IsVerifiedPresent),
            MandatoryPrimaryDesignOnlyCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.DesignOnly),
            MandatoryPrimaryFunctionalConstraintMismatchCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.FunctionalConstraintMismatch),
            MandatoryPrimaryTypeMismatchCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.TypeMismatch),
            MandatoryPrimaryAmbiguousCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.Ambiguous),
            MandatoryPrimaryInvalidTargetCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.InvalidTarget),
            MandatoryPrimaryUnreadableCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.Unreadable),
            MandatoryPrimaryConfirmedAbsentCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.Absent),
            MandatoryPrimaryTransportFailureCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.TransportFailure),
            MandatoryPrimaryUnresolvedDesignCount = LiveStatusCount(mandatoryPrimary, Iec61850DesignLiveStatus.UnresolvedDesign)
        };
    }

    private static bool IsDesign(Iec61850SignalDescriptor signal)
        => signal.ResolutionStatus != Iec61850SignalCatalogResolutionStatus.LiveOnly;

    private static bool IsMandatoryPrimary(Iec61850SignalDescriptor signal)
        => signal.IsStaticDataSetMandatory &&
           signal.SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue;
}

public static class Iec61850SignalCatalogCoverageExtensions
{
    public static Iec61850SignalCatalogCoverageDiagnostics GetCoverageDiagnostics(
        this Iec61850SignalCatalogDocument catalog)
        => Iec61850SignalCatalogCoverageDiagnostics.Create(catalog);
}
