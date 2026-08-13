using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Reconciles authoritative design attributes with native live MMS discovery and,
/// when requested, exact targeted reads. A missing GetNameList entry is DesignOnly,
/// never Absent, until the canonical target and every known semantic alternate have
/// been verified absent by MMS.
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

                var typeCompatibility = Iec61850TypeCompatibility.Compare(
                    expected.SclBType,
                    expected.MmsType,
                    live.SclBType,
                    live.MmsType);
                if (typeCompatibility == Iec61850TypeCompatibilityKind.Conflict)
                {
                    reconciled.Add(ToResult(expected, Iec61850DesignLiveStatus.TypeMismatch, live, null,
                        $"Exact MMS target was found, but authoritative type families conflict: design={DisplayType(expected)}, live={DisplayType(live)}."));
                    continue;
                }

                var status = typeCompatibility == Iec61850TypeCompatibilityKind.Exact
                    ? Iec61850DesignLiveStatus.Exact
                    : Iec61850DesignLiveStatus.Compatible;
                reconciled.Add(ToResult(expected, status, live, null,
                    typeCompatibility == Iec61850TypeCompatibilityKind.Compatible
                        ? "Exact MMS target is present in native live discovery; type evidence is protocol-family compatible."
                        : "Exact MMS target is present in native live discovery."));
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

            reconciled.Add(await ProbeExpectedPointAsync(expected, probe!, options, cancellationToken).ConfigureAwait(false));
        }

        foreach (var live in observedPoints.Where(x => !consumedObserved.Contains(x)))
        {
            reconciled.Add(new Iec61850DesignLivePointReconciliation
            {
                Reference = live.Reference,
                MmsReference = live.MmsReference,
                EffectiveMmsReference = live.MmsReference,
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

    private static async Task<Iec61850DesignLivePointReconciliation> ProbeExpectedPointAsync(
        PointDescriptor expected,
        IIec61850ExactReadProbe probe,
        Iec61850DesignLiveReconciliationOptions options,
        CancellationToken cancellationToken)
    {
        var attempts = new List<Iec61850ProbeAttemptEvidence>();
        var canonicalProbe = await probe.ProbeAsync(
            expected.MmsReference,
            expected.FunctionalConstraint,
            cancellationToken).ConfigureAwait(false);
        attempts.Add(new Iec61850ProbeAttemptEvidence
        {
            IsCanonical = true,
            Explanation = "Canonical MMS target.",
            Probe = canonicalProbe
        });

        if (canonicalProbe.Status == Iec61850ExactProbeStatus.Readable)
        {
            return ToResult(
                expected,
                Iec61850DesignLiveStatus.RecoveredByProbe,
                null,
                canonicalProbe,
                "Native discovery omitted the target, but exact MMS Confirmed-Read proved the canonical target is readable.",
                expected.MmsReference,
                attempts);
        }

        if (canonicalProbe.Status == Iec61850ExactProbeStatus.TransportFailure)
        {
            return ToResult(
                expected,
                Iec61850DesignLiveStatus.TransportFailure,
                null,
                canonicalProbe,
                canonicalProbe.Message,
                string.Empty,
                attempts);
        }

        var alternates = options.ProbeKnownAlternateReferences
            ? Iec61850AlternateReferencePolicy.GetCandidates(expected.MmsReference)
            : Array.Empty<Iec61850AlternateReferenceCandidate>();

        foreach (var alternate in alternates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var alternateProbe = await probe.ProbeAsync(
                alternate.MmsReference,
                expected.FunctionalConstraint,
                cancellationToken).ConfigureAwait(false);
            attempts.Add(new Iec61850ProbeAttemptEvidence
            {
                IsCanonical = false,
                AlternateStrategy = alternate.Strategy,
                Explanation = alternate.Explanation,
                Probe = alternateProbe
            });

            if (alternateProbe.Status == Iec61850ExactProbeStatus.Readable)
            {
                return ToResult(
                    expected,
                    Iec61850DesignLiveStatus.RecoveredByAlternateProbe,
                    null,
                    alternateProbe,
                    $"Canonical target was not readable, but engine-owned alternate strategy {alternate.Strategy} resolved a readable MMS sibling.",
                    alternate.MmsReference,
                    attempts);
            }

            if (alternateProbe.Status == Iec61850ExactProbeStatus.TransportFailure)
            {
                return ToResult(
                    expected,
                    Iec61850DesignLiveStatus.TransportFailure,
                    null,
                    alternateProbe,
                    alternateProbe.Message,
                    string.Empty,
                    attempts);
            }
        }

        var finalStatus = ClassifyFailedProbeAttempts(attempts);
        var finalProbe = attempts[^1].Probe;
        var evidence = finalStatus == Iec61850DesignLiveStatus.Absent && attempts.Count > 1
            ? "Canonical target and every known semantic alternate returned protocol-level absence."
            : finalProbe.Message;

        return ToResult(expected, finalStatus, null, finalProbe, evidence, string.Empty, attempts);
    }

    private static Iec61850DesignLiveStatus ClassifyFailedProbeAttempts(
        IReadOnlyCollection<Iec61850ProbeAttemptEvidence> attempts)
    {
        if (attempts.Any(x => x.Probe.Status == Iec61850ExactProbeStatus.TransportFailure))
            return Iec61850DesignLiveStatus.TransportFailure;
        if (attempts.Any(x => x.Probe.Status == Iec61850ExactProbeStatus.Unreadable))
            return Iec61850DesignLiveStatus.Unreadable;
        if (attempts.Any(x => x.Probe.Status == Iec61850ExactProbeStatus.InvalidTarget))
            return Iec61850DesignLiveStatus.InvalidTarget;
        if (attempts.Count > 0 && attempts.All(x => x.Probe.Status == Iec61850ExactProbeStatus.Absent))
            return Iec61850DesignLiveStatus.Absent;

        return Iec61850DesignLiveStatus.Unreadable;
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
                var isPrimaryValue = Iec61850ProbeValuePolicy.IsPrimaryValueBearing(attribute);
                var semanticRole = isPrimaryValue && attribute.SemanticRole == Iec61850DataAttributeSemanticRole.Other
                    ? Iec61850DataAttributeSemanticRole.PrimaryValue
                    : attribute.SemanticRole;
                if (!index.TryGetValue(key, out var descriptor))
                {
                    descriptor = new MandatoryDescriptor
                    {
                        Reference = attribute.Reference,
                        MmsReference = attribute.MmsReference,
                        FunctionalConstraint = attribute.FunctionalConstraint,
                        SclBType = attribute.SclBType,
                        MmsType = attribute.MmsType,
                        SemanticRole = semanticRole,
                        IsPrimaryValue = isPrimaryValue
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
        string evidence,
        string effectiveMmsReference = "",
        IReadOnlyList<Iec61850ProbeAttemptEvidence>? probeAttempts = null)
        => new()
        {
            Reference = expected.Reference,
            MmsReference = expected.MmsReference,
            CanonicalMmsReference = expected.MmsReference,
            EffectiveMmsReference = ResolveEffectiveReference(status, expected, observed, effectiveMmsReference),
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
            ProbeAttempts = probeAttempts ?? Array.Empty<Iec61850ProbeAttemptEvidence>(),
            Evidence = new[] { evidence }
        };

    private static string ResolveEffectiveReference(
        Iec61850DesignLiveStatus status,
        PointDescriptor expected,
        PointDescriptor? observed,
        string explicitEffective)
    {
        if (!string.IsNullOrWhiteSpace(explicitEffective))
            return explicitEffective;

        return status switch
        {
            Iec61850DesignLiveStatus.Exact or Iec61850DesignLiveStatus.Compatible
                => observed?.MmsReference ?? expected.MmsReference,
            Iec61850DesignLiveStatus.RecoveredByProbe => expected.MmsReference,
            _ => string.Empty
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
