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
    Unreadable,
    Absent,
    TransportFailure,
    UnresolvedDesign
}

public enum Iec61850ExactProbeStatus
{
    Readable,
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
                Status = Iec61850ExactProbeStatus.Unreadable,
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

        var status = !_session.IsMmsInitiated && result.Message.Contains("transport fault", StringComparison.OrdinalIgnoreCase)
            ? Iec61850ExactProbeStatus.TransportFailure
            : result.FailureCode is 4 or 5 or 10
                ? Iec61850ExactProbeStatus.Absent
                : Iec61850ExactProbeStatus.Unreadable;

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
}

public sealed class Iec61850DesignLivePointReconciliation
{
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
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
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class Iec61850DesignLiveReconciliationDocument
{
    public IReadOnlyList<Iec61850DesignLivePointReconciliation> Points { get; init; }
        = Array.Empty<Iec61850DesignLivePointReconciliation>();

    public int DesignPointCount => Points.Count(x => x.Status != Iec61850DesignLiveStatus.LiveOnly);
    public int LiveOnlyCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.LiveOnly);
    public int DirectlyMatchedCount => Points.Count(x => x.Status is Iec61850DesignLiveStatus.Exact or Iec61850DesignLiveStatus.Compatible);
    public int RecoveredByProbeCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.RecoveredByProbe);
    public int UnreadableCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.Unreadable);
    public int AbsentCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.Absent);
    public int TransportFailureCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.TransportFailure);
    public int DesignOnlyCount => Points.Count(x => x.Status == Iec61850DesignLiveStatus.DesignOnly);

    public bool HasConfirmedAbsence => AbsentCount > 0;
}

/// <summary>
/// Reconciles authoritative design attributes with native live MMS discovery and,
/// when requested, exact targeted reads. A missing GetNameList entry is DesignOnly,
/// never Absent, until the exact target has been verified by MMS.
/// </summary>
public static class Iec61850DesignLiveReconciler
{
    private static readonly HashSet<string> FunctionalConstraints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ST", "MX", "CO", "SP", "CF", "DC", "SG", "SE", "SV", "EX", "SR", "OR", "BL",
        "RP", "BR", "LG", "GO", "GS", "MS", "US"
    };

    public static async Task<Iec61850DesignLiveReconciliationDocument> ReconcileAsync(
        LiveIedModelDiscoveryDocument design,
        LiveIedModelDiscoveryDocument observed,
        IIec61850ExactReadProbe? probe = null,
        Iec61850DesignLiveReconciliationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(observed);
        options ??= new Iec61850DesignLiveReconciliationOptions();

        var mandatory = BuildMandatoryIndex(design);
        var designPoints = Flatten(design, mandatory, includeSyntheticMandatory: true);
        var observedPoints = Flatten(observed, new Dictionary<string, MandatoryDescriptor>(StringComparer.OrdinalIgnoreCase), includeSyntheticMandatory: false);
        var observedByExact = observedPoints
            .Where(x => !string.IsNullOrWhiteSpace(x.MmsReference))
            .GroupBy(x => NormalizeMmsReference(x.MmsReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var observedByIdentity = observedPoints
            .Where(x => !string.IsNullOrWhiteSpace(x.MmsReference))
            .GroupBy(x => IdentityWithoutFunctionalConstraint(x.MmsReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var consumedObserved = new HashSet<PointDescriptor>();
        var reconciled = new List<Iec61850DesignLivePointReconciliation>();

        foreach (var expected in designPoints.OrderBy(x => x.MmsReference, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exactKey = NormalizeMmsReference(expected.MmsReference);
            if (string.IsNullOrWhiteSpace(exactKey))
            {
                reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.UnresolvedDesign, null, null,
                    "Design attribute has no exact MMS target."));
                continue;
            }

            if (observedByExact.TryGetValue(exactKey, out var exactMatches))
            {
                if (exactMatches.Length > 1)
                {
                    foreach (var match in exactMatches)
                        consumedObserved.Add(match);
                    reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.Ambiguous, exactMatches[0], null,
                        $"More than one live attribute matched exact MMS target '{expected.MmsReference}'."));
                    continue;
                }

                var live = exactMatches[0];
                consumedObserved.Add(live);
                if (!SameFc(expected.FunctionalConstraint, live.FunctionalConstraint))
                {
                    reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.FunctionalConstraintMismatch, live, null,
                        $"Exact object identity was found with FC={live.FunctionalConstraint}, expected FC={expected.FunctionalConstraint}."));
                    continue;
                }

                if (HasTypeConflict(expected, live))
                {
                    reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.TypeMismatch, live, null,
                        $"Exact MMS target was found, but type evidence conflicts: design={DisplayType(expected)}, live={DisplayType(live)}."));
                    continue;
                }

                var status = HasComparableType(expected) && HasComparableType(live)
                    ? Iec61850DesignLiveStatus.Exact
                    : Iec61850DesignLiveStatus.Compatible;
                reconciled.Add(ToResult(expected, status, live, null,
                    "Exact MMS target is present in native live discovery."));
                continue;
            }

            var identityKey = IdentityWithoutFunctionalConstraint(expected.MmsReference);
            if (observedByIdentity.TryGetValue(identityKey, out var identityMatches) && identityMatches.Length > 0)
            {
                foreach (var match in identityMatches)
                    consumedObserved.Add(match);
                var status = identityMatches.Length == 1
                    ? Iec61850DesignLiveStatus.FunctionalConstraintMismatch
                    : Iec61850DesignLiveStatus.Ambiguous;
                reconciled.Add(ToResult(expected, status, identityMatches[0], null,
                    identityMatches.Length == 1
                        ? $"Live object exists under FC={identityMatches[0].FunctionalConstraint}, expected FC={expected.FunctionalConstraint}."
                        : "Multiple live FC variants match the same object identity."));
                continue;
            }

            var shouldProbe = probe is not null &&
                (options.ProbeAllMissingDesignAttributes ||
                 (options.ProbeMissingDataSetPrimaryValues && expected.IsDataSetMandatory && expected.IsPrimaryValue));
            if (!shouldProbe)
            {
                reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.DesignOnly, null, null,
                    "Target is expected by design but was not enumerated by native live discovery; exact read verification has not been performed."));
                continue;
            }

            var probeEvidence = await probe!.ProbeAsync(expected.MmsReference, expected.FunctionalConstraint, cancellationToken).ConfigureAwait(false);
            var probeStatus = probeEvidence.Status switch
            {
                Iec61850ExactProbeStatus.Readable => Iec61850DesignLiveStatus.RecoveredByProbe,
                Iec61850ExactProbeStatus.Absent => Iec61850DesignLiveStatus.Absent,
                Iec61850ExactProbeStatus.TransportFailure => Iec61850DesignLiveStatus.TransportFailure,
                _ => Iec61850DesignLiveStatus.Unreadable
            };
            reconciled.Add(ToResult(expected, probeStatus, null, probeEvidence,
                probeStatus == Iec61850DesignLiveStatus.RecoveredByProbe
                    ? "Native discovery omitted the target, but exact MMS Confirmed-Read proved it is readable."
                    : probeEvidence.Message));
        }

        foreach (var live in observedPoints.Where(x => !consumedObserved.Contains(x)))
        {
            reconciled.Add(new Iec61850DesignLivePointReconciliation
            {
                Reference = live.Reference,
                MmsReference = live.MmsReference,
                FunctionalConstraint = live.FunctionalConstraint,
                SclBType = live.SclBType,
                MmsType = live.MmsType,
                Status = Iec61850DesignLiveStatus.LiveOnly,
                ObservedReference = live.Reference,
                ObservedMmsReference = live.MmsReference,
                ObservedFunctionalConstraint = live.FunctionalConstraint,
                Evidence = new[] { "Native live discovery contains this attribute, but it is not present in the design model." }
            });
        }

        return new Iec61850DesignLiveReconciliationDocument
        {
            Points = reconciled
                .OrderBy(x => x.Status == Iec61850DesignLiveStatus.LiveOnly ? 1 : 0)
                .ThenBy(x => x.MmsReference, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static Dictionary<string, MandatoryDescriptor> BuildMandatoryIndex(LiveIedModelDiscoveryDocument design)
    {
        var index = new Dictionary<string, MandatoryDescriptor>(StringComparer.OrdinalIgnoreCase);
        var bindings = Iec61850DataSetSemanticBindingResolver.Resolve(design);
        foreach (var member in bindings.Members.Where(x => x.IsResolved))
        {
            foreach (var attribute in member.ResolvedAttributes.Where(x => !string.IsNullOrWhiteSpace(x.MmsReference)))
            {
                var key = NormalizeMmsReference(attribute.MmsReference);
                if (!index.TryGetValue(key, out var descriptor))
                {
                    descriptor = new MandatoryDescriptor
                    {
                        Reference = attribute.Reference,
                        MmsReference = attribute.MmsReference,
                        FunctionalConstraint = attribute.FunctionalConstraint,
                        SclBType = attribute.SclBType,
                        MmsType = attribute.MmsType,
                        SemanticRole = attribute.SemanticRole,
                        IsPrimaryValue = attribute.IsPrimaryValue
                    };
                    index[key] = descriptor;
                }
                descriptor.DataSetReferences.Add(member.DataSetReference);
            }
        }
        return index;
    }

    private static IReadOnlyList<PointDescriptor> Flatten(
        LiveIedModelDiscoveryDocument document,
        IReadOnlyDictionary<string, MandatoryDescriptor> mandatory,
        bool includeSyntheticMandatory)
    {
        var points = new Dictionary<string, PointDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var logicalDevice in document.LogicalDevices)
        {
            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                foreach (var dataObject in logicalNode.DataObjects)
                {
                    foreach (var attribute in dataObject.Attributes)
                    {
                        var mmsReference = ResolveMmsReference(logicalDevice, attribute);
                        var key = NormalizeMmsReference(mmsReference);
                        mandatory.TryGetValue(key, out var mandatoryDescriptor);
                        var point = new PointDescriptor
                        {
                            Reference = attribute.ObjectReference,
                            MmsReference = mmsReference,
                            FunctionalConstraint = NormalizeFc(attribute.FunctionalConstraint),
                            SclBType = attribute.SclBType,
                            MmsType = attribute.MmsType,
                            MmsTypeSignature = attribute.MmsTypeSignature,
                            SemanticRole = mandatoryDescriptor?.SemanticRole ?? Iec61850DataAttributeSemanticRole.Other,
                            IsDataSetMandatory = mandatoryDescriptor is not null,
                            IsPrimaryValue = mandatoryDescriptor?.IsPrimaryValue == true,
                            DataSetReferences = mandatoryDescriptor?.DataSetReferences.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>()
                        };
                        if (!string.IsNullOrWhiteSpace(key))
                            points.TryAdd(key, point);
                    }
                }
            }
        }

        if (includeSyntheticMandatory)
        {
            foreach (var pair in mandatory)
            {
                if (points.ContainsKey(pair.Key))
                    continue;
                var item = pair.Value;
                points[pair.Key] = new PointDescriptor
                {
                    Reference = item.Reference,
                    MmsReference = item.MmsReference,
                    FunctionalConstraint = item.FunctionalConstraint,
                    SclBType = item.SclBType,
                    MmsType = item.MmsType,
                    SemanticRole = item.SemanticRole,
                    IsDataSetMandatory = true,
                    IsPrimaryValue = item.IsPrimaryValue,
                    DataSetReferences = item.DataSetReferences.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                };
            }
        }

        return points.Values.ToArray();
    }

    private static string ResolveMmsReference(LiveIedLogicalDeviceModel logicalDevice, LiveIedDataAttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.MmsReference))
            return attribute.MmsReference.Trim();
        if (!string.IsNullOrWhiteSpace(logicalDevice.MmsDomain) && !string.IsNullOrWhiteSpace(attribute.MmsItemName))
            return $"{logicalDevice.MmsDomain.Trim()}/{attribute.MmsItemName.Trim()}";

        var parsed = MmsObjectReference.FromIec61850Reference(attribute.ObjectReference, attribute.FunctionalConstraint);
        return string.IsNullOrWhiteSpace(parsed.Domain) || string.IsNullOrWhiteSpace(parsed.Item)
            ? string.Empty
            : $"{parsed.Domain}/{parsed.Item}";
    }

    private static Iec61850DesignLivePointReconciliation ToResult(
        PointDescriptor expected,
        Iec61850DesignLiveStatus status,
        PointDescriptor? observed,
        Iec61850ExactProbeEvidence? probe,
        string evidence)
        => new()
        {
            Reference = expected.Reference,
            MmsReference = expected.MmsReference,
            FunctionalConstraint = expected.FunctionalConstraint,
            SclBType = expected.SclBType,
            MmsType = expected.MmsType,
            SemanticRole = expected.SemanticRole,
            IsDataSetMandatory = expected.IsDataSetMandatory,
            IsPrimaryValue = expected.IsPrimaryValue,
            DataSetReferences = expected.DataSetReferences,
            Status = status,
            ObservedReference = observed?.Reference ?? string.Empty,
            ObservedMmsReference = observed?.MmsReference ?? string.Empty,
            ObservedFunctionalConstraint = observed?.FunctionalConstraint ?? string.Empty,
            Probe = probe,
            Evidence = new[] { evidence }
        };

    private static bool HasTypeConflict(PointDescriptor expected, PointDescriptor observed)
    {
        var left = ComparableType(expected);
        var right = ComparableType(observed);
        return left.Length > 0 && right.Length > 0 && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasComparableType(PointDescriptor point) => ComparableType(point).Length > 0;

    private static string ComparableType(PointDescriptor point)
    {
        var type = FirstNonEmpty(point.SclBType, point.MmsType);
        if (string.IsNullOrWhiteSpace(type))
            type = point.MmsTypeSignature;
        if (type.Contains(':'))
            type = type[..type.IndexOf(':')];
        return type.Trim().ToUpperInvariant() switch
        {
            "BOOL" => "BOOLEAN",
            "INTEGER" or "INT" => "INT32",
            "UNSIGNED" => "INT32U",
            "FLOATINGPOINT" or "FLOAT" => "FLOAT32",
            "BITSTRING" => "BIT-STRING",
            _ => type.Trim().ToUpperInvariant()
        };
    }

    private static string DisplayType(PointDescriptor point)
        => FirstNonEmpty(point.MmsTypeSignature, FirstNonEmpty(point.SclBType, point.MmsType));

    private static string NormalizeMmsReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').ToUpperInvariant();

    private static string IdentityWithoutFunctionalConstraint(string? mmsReference)
    {
        var normalized = NormalizeMmsReference(mmsReference);
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash >= normalized.Length - 1)
            return normalized;
        var domain = normalized[..slash];
        var parts = normalized[(slash + 1)..]
            .Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (parts.Count > 2 && FunctionalConstraints.Contains(parts[1]))
            parts.RemoveAt(1);
        return $"{domain}/{string.Join('$', parts)}";
    }

    private static bool SameFc(string? left, string? right)
        => string.Equals(NormalizeFc(left), NormalizeFc(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string FirstNonEmpty(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second ?? string.Empty : first;

    private sealed class MandatoryDescriptor
    {
        public string Reference { get; init; } = string.Empty;
        public string MmsReference { get; init; } = string.Empty;
        public string FunctionalConstraint { get; init; } = string.Empty;
        public string SclBType { get; init; } = string.Empty;
        public string MmsType { get; init; } = string.Empty;
        public Iec61850DataAttributeSemanticRole SemanticRole { get; init; }
        public bool IsPrimaryValue { get; init; }
        public HashSet<string> DataSetReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PointDescriptor
    {
        public string Reference { get; init; } = string.Empty;
        public string MmsReference { get; init; } = string.Empty;
        public string FunctionalConstraint { get; init; } = string.Empty;
        public string SclBType { get; init; } = string.Empty;
        public string MmsType { get; init; } = string.Empty;
        public string MmsTypeSignature { get; init; } = string.Empty;
        public Iec61850DataAttributeSemanticRole SemanticRole { get; init; }
        public bool IsDataSetMandatory { get; init; }
        public bool IsPrimaryValue { get; init; }
        public IReadOnlyList<string> DataSetReferences { get; init; } = Array.Empty<string>();
    }
}