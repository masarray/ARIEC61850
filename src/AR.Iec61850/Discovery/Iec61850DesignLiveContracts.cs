using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

public enum Iec61850DesignLiveStatus
{
    Exact,
    Compatible,
    RecoveredByProbe,
    DesignOnly,
    LiveOnly,
    FunctionalConstraintMismatch,
    TypeMismatch,
    Ambiguous,
    InvalidTarget,
    Unreadable,
    Absent,
    TransportFailure,
    UnresolvedDesign,
    RecoveredByAlternateProbe
}

public enum Iec61850ExactProbeStatus
{
    Readable,
    InvalidTarget,
    Unreadable,
    Absent,
    TransportFailure
}

public sealed class Iec61850ExactProbeEvidence
{
    public Iec61850ExactProbeStatus Status { get; init; }
    public string MmsReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public int? FailureCode { get; init; }
    public string ValueSummary { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class Iec61850ProbeAttemptEvidence
{
    public bool IsCanonical { get; init; }
    public Iec61850AlternateReferenceStrategyKind? AlternateStrategy { get; init; }
    public string Explanation { get; init; } = string.Empty;
    public Iec61850ExactProbeEvidence Probe { get; init; } = new();
}

public interface IIec61850ExactReadProbe
{
    Task<Iec61850ExactProbeEvidence> ProbeAsync(
        string mmsReference,
        string functionalConstraint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs an exact MMS Confirmed-Read against an already resolved MMS target.
/// Discovery absence is intentionally not treated as protocol absence.
/// </summary>
public sealed class MmsClientSessionExactReadProbe : IIec61850ExactReadProbe
{
    private readonly MmsClientSession _session;

    public MmsClientSessionExactReadProbe(MmsClientSession session)
        => _session = session ?? throw new ArgumentNullException(nameof(session));

    public async Task<Iec61850ExactProbeEvidence> ProbeAsync(
        string mmsReference,
        string functionalConstraint,
        CancellationToken cancellationToken = default)
    {
        var normalized = (mmsReference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash >= normalized.Length - 1)
        {
            return new Iec61850ExactProbeEvidence
            {
                Status = Iec61850ExactProbeStatus.InvalidTarget,
                MmsReference = normalized,
                FunctionalConstraint = NormalizeFc(functionalConstraint),
                Message = "Exact MMS target is invalid; expected domain/item form."
            };
        }

        var fc = NormalizeFc(functionalConstraint);
        var target = new MmsObjectReference(normalized[..slash], normalized[(slash + 1)..], fc);
        var result = await _session.ReadSingleVariableAsync(target, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return new Iec61850ExactProbeEvidence
            {
                Status = Iec61850ExactProbeStatus.Readable,
                MmsReference = normalized,
                FunctionalConstraint = fc,
                ValueSummary = result.Value is null ? string.Empty : MmsDataValueRenderer.ToCompactString(result.Value),
                Message = result.Message
            };
        }

        var status = !_session.IsMmsInitiated
            ? Iec61850ExactProbeStatus.TransportFailure
            : result.FailureCode switch
            {
                4 or 10 => Iec61850ExactProbeStatus.Absent,
                5 => Iec61850ExactProbeStatus.InvalidTarget,
                _ => Iec61850ExactProbeStatus.Unreadable
            };

        return new Iec61850ExactProbeEvidence
        {
            Status = status,
            MmsReference = normalized,
            FunctionalConstraint = fc,
            FailureCode = result.FailureCode,
            Message = result.Message
        };
    }

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class Iec61850DesignLiveReconciliationOptions
{
    /// <summary>
    /// Exact reads are intentionally bounded to DataSet primary values by default.
    /// This prevents a design model with thousands of engineering leaves from causing
    /// an unbounded read storm while still protecting FAT/SCADA mandatory signals.
    /// </summary>
    public bool ProbeMissingDataSetPrimaryValues { get; init; } = true;

    public bool ProbeAllMissingDesignAttributes { get; init; }

    /// <summary>
    /// Try only engine-owned, bounded semantic sibling candidates after a canonical target
    /// is not readable. Vendor/domain aliases are deliberately excluded until evidenced.
    /// </summary>
    public bool ProbeKnownAlternateReferences { get; init; } = true;
}

public sealed class Iec61850DesignLivePointReconciliation
{
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string CanonicalMmsReference { get; init; } = string.Empty;
    public string EffectiveMmsReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public Iec61850DataAttributeSemanticRole SemanticRole { get; init; } = Iec61850DataAttributeSemanticRole.Other;
    public bool IsDataSetMandatory { get; init; }
    public bool IsPrimaryValue { get; init; }
    public IReadOnlyList<string> DataSetReferences { get; init; } = Array.Empty<string>();
    public Iec61850DesignLiveStatus Status { get; init; }
    public string ObservedReference { get; init; } = string.Empty;
    public string ObservedMmsReference { get; init; } = string.Empty;
    public string ObservedFunctionalConstraint { get; init; } = string.Empty;
    public Iec61850ExactProbeEvidence? Probe { get; init; }
    public IReadOnlyList<Iec61850ProbeAttemptEvidence> ProbeAttempts { get; init; } = Array.Empty<Iec61850ProbeAttemptEvidence>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class Iec61850DesignLiveCoverageDiagnostics
{
    public int DesignPointCount { get; init; }
    public int LiveOnlyCount { get; init; }
    public int DataSetMandatoryPointCount { get; init; }
    public int MandatoryPrimaryPointCount { get; init; }
    public int DirectlyMatchedCount { get; init; }
    public int RecoveredByProbeCount { get; init; }
    public int RecoveredByAlternateProbeCount { get; init; }
    public int ReadableCount { get; init; }
    public int DesignOnlyCount { get; init; }
    public int InvalidTargetCount { get; init; }
    public int UnreadableCount { get; init; }
    public int AbsentCount { get; init; }
    public int TransportFailureCount { get; init; }
    public int FunctionalConstraintMismatchCount { get; init; }
    public int TypeMismatchCount { get; init; }
    public int AmbiguousCount { get; init; }
    public int UnresolvedDesignCount { get; init; }
    public int MandatoryPrimaryDirectlyMatchedCount { get; init; }
    public int MandatoryPrimaryRecoveredByProbeCount { get; init; }
    public int MandatoryPrimaryRecoveredByAlternateProbeCount { get; init; }
    public int MandatoryPrimaryReadableCount { get; init; }
    public int MandatoryPrimaryDesignOnlyCount { get; init; }
    public int MandatoryPrimaryInvalidTargetCount { get; init; }
    public int MandatoryPrimaryUnreadableCount { get; init; }
    public int MandatoryPrimaryAbsentCount { get; init; }
    public int MandatoryPrimaryTransportFailureCount { get; init; }

    public bool HasConfirmedMandatoryAbsence => MandatoryPrimaryAbsentCount > 0;

    public static Iec61850DesignLiveCoverageDiagnostics Create(
        IReadOnlyList<Iec61850DesignLivePointReconciliation> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var design = points.Where(x => x.Status != Iec61850DesignLiveStatus.LiveOnly).ToArray();
        var mandatory = design.Where(x => x.IsDataSetMandatory).ToArray();
        var primary = mandatory.Where(x => x.IsPrimaryValue).ToArray();

        static int Direct(IReadOnlyCollection<Iec61850DesignLivePointReconciliation> source)
            => source.Count(x => x.Status is Iec61850DesignLiveStatus.Exact or Iec61850DesignLiveStatus.Compatible);

        static int Count(IReadOnlyCollection<Iec61850DesignLivePointReconciliation> source, Iec61850DesignLiveStatus status)
            => source.Count(x => x.Status == status);

        var directlyMatched = Direct(design);
        var recovered = Count(design, Iec61850DesignLiveStatus.RecoveredByProbe);
        var recoveredAlternate = Count(design, Iec61850DesignLiveStatus.RecoveredByAlternateProbe);
        var mandatoryDirect = Direct(primary);
        var mandatoryRecovered = Count(primary, Iec61850DesignLiveStatus.RecoveredByProbe);
        var mandatoryRecoveredAlternate = Count(primary, Iec61850DesignLiveStatus.RecoveredByAlternateProbe);

        return new Iec61850DesignLiveCoverageDiagnostics
        {
            DesignPointCount = design.Length,
            LiveOnlyCount = Count(points, Iec61850DesignLiveStatus.LiveOnly),
            DataSetMandatoryPointCount = mandatory.Length,
            MandatoryPrimaryPointCount = primary.Length,
            DirectlyMatchedCount = directlyMatched,
            RecoveredByProbeCount = recovered,
            RecoveredByAlternateProbeCount = recoveredAlternate,
            ReadableCount = directlyMatched + recovered + recoveredAlternate,
            DesignOnlyCount = Count(design, Iec61850DesignLiveStatus.DesignOnly),
            InvalidTargetCount = Count(design, Iec61850DesignLiveStatus.InvalidTarget),
            UnreadableCount = Count(design, Iec61850DesignLiveStatus.Unreadable),
            AbsentCount = Count(design, Iec61850DesignLiveStatus.Absent),
            TransportFailureCount = Count(design, Iec61850DesignLiveStatus.TransportFailure),
            FunctionalConstraintMismatchCount = Count(design, Iec61850DesignLiveStatus.FunctionalConstraintMismatch),
            TypeMismatchCount = Count(design, Iec61850DesignLiveStatus.TypeMismatch),
            AmbiguousCount = Count(design, Iec61850DesignLiveStatus.Ambiguous),
            UnresolvedDesignCount = Count(design, Iec61850DesignLiveStatus.UnresolvedDesign),
            MandatoryPrimaryDirectlyMatchedCount = mandatoryDirect,
            MandatoryPrimaryRecoveredByProbeCount = mandatoryRecovered,
            MandatoryPrimaryRecoveredByAlternateProbeCount = mandatoryRecoveredAlternate,
            MandatoryPrimaryReadableCount = mandatoryDirect + mandatoryRecovered + mandatoryRecoveredAlternate,
            MandatoryPrimaryDesignOnlyCount = Count(primary, Iec61850DesignLiveStatus.DesignOnly),
            MandatoryPrimaryInvalidTargetCount = Count(primary, Iec61850DesignLiveStatus.InvalidTarget),
            MandatoryPrimaryUnreadableCount = Count(primary, Iec61850DesignLiveStatus.Unreadable),
            MandatoryPrimaryAbsentCount = Count(primary, Iec61850DesignLiveStatus.Absent),
            MandatoryPrimaryTransportFailureCount = Count(primary, Iec61850DesignLiveStatus.TransportFailure)
        };
    }
}

public sealed class Iec61850DesignLiveReconciliationDocument
{
    public IReadOnlyList<Iec61850DesignLivePointReconciliation> Points { get; init; }
        = Array.Empty<Iec61850DesignLivePointReconciliation>();

    public Iec61850DesignLiveCoverageDiagnostics Coverage => Iec61850DesignLiveCoverageDiagnostics.Create(Points);
    public int DesignPointCount => Coverage.DesignPointCount;
    public int LiveOnlyCount => Coverage.LiveOnlyCount;
    public int DirectlyMatchedCount => Coverage.DirectlyMatchedCount;
    public int RecoveredByProbeCount => Coverage.RecoveredByProbeCount;
    public int RecoveredByAlternateProbeCount => Coverage.RecoveredByAlternateProbeCount;
    public int InvalidTargetCount => Coverage.InvalidTargetCount;
    public int UnreadableCount => Coverage.UnreadableCount;
    public int AbsentCount => Coverage.AbsentCount;
    public int TransportFailureCount => Coverage.TransportFailureCount;
    public int DesignOnlyCount => Coverage.DesignOnlyCount;

    public bool HasConfirmedAbsence => AbsentCount > 0;
}
