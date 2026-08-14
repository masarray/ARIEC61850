using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Builds an application-facing signal catalog strictly from existing engine authorities:
/// the design model, typed DataSet semantic bindings, report-control membership and an
/// optional design/live reconciliation document. No new vendor or protocol heuristics live here.
/// </summary>
public static class Iec61850SignalCatalogBuilder
{
    public static Iec61850SignalCatalogDocument Build(
        LiveIedModelDiscoveryDocument design,
        Iec61850DesignLiveReconciliationDocument? reconciliation = null)
    {
        ArgumentNullException.ThrowIfNull(design);

        var bindings = Iec61850DataSetSemanticBindingResolver.Resolve(design);
        var bindingIndex = BuildBindingIndex(bindings);
        var reconciliationIndex = BuildReconciliationIndex(reconciliation);
        var signals = new List<Iec61850SignalDescriptor>();
        var seenMmsReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var logicalDevice in design.LogicalDevices)
        {
            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                foreach (var dataObject in logicalNode.DataObjects)
                {
                    foreach (var attribute in dataObject.Attributes)
                    {
                        var canonicalMmsReference = ResolveMmsReference(logicalDevice, attribute);
                        var key = NormalizeMmsReference(canonicalMmsReference);
                        var associations = key.Length > 0 && bindingIndex.TryGetValue(key, out var matches)
                            ? matches
                            : Array.Empty<BindingAssociation>();
                        var point = key.Length > 0 && reconciliationIndex.TryGetValue(key, out var reconciled)
                            ? reconciled
                            : null;

                        signals.Add(BuildDesignDescriptor(
                            design,
                            logicalDevice,
                            logicalNode,
                            dataObject,
                            attribute,
                            canonicalMmsReference,
                            associations,
                            point));

                        if (key.Length > 0)
                            seenMmsReferences.Add(key);
                    }
                }
            }
        }

        foreach (var group in bindingIndex.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0 || seenMmsReferences.Contains(group.Key))
                continue;

            var associations = group.Value;
            var representative = associations[0].Attribute;
            var point = reconciliationIndex.TryGetValue(group.Key, out var reconciled) ? reconciled : null;
            signals.Add(BuildDataSetResolvedDescriptor(design, representative, associations, point));
            seenMmsReferences.Add(group.Key);
        }

        if (reconciliation is not null)
        {
            foreach (var point in reconciliation.Points.Where(x => x.Status == Iec61850DesignLiveStatus.LiveOnly))
            {
                var reference = FirstNonEmpty(point.ObservedMmsReference, point.MmsReference);
                var key = NormalizeMmsReference(reference);
                if (key.Length > 0 && seenMmsReferences.Contains(key))
                    continue;

                signals.Add(BuildLiveOnlyDescriptor(point));
                if (key.Length > 0)
                    seenMmsReferences.Add(key);
            }
        }

        return new Iec61850SignalCatalogDocument
        {
            IedName = design.IedName,
            Source = design.Source,
            Signals = signals
                .OrderBy(x => x.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly ? 1 : 0)
                .ThenBy(x => FirstNonEmpty(x.CanonicalMmsReference, x.DesignReference), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static Iec61850SignalDescriptor BuildDesignDescriptor(
        LiveIedModelDiscoveryDocument design,
        LiveIedLogicalDeviceModel logicalDevice,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        LiveIedDataAttributeModel attribute,
        string canonicalMmsReference,
        IReadOnlyList<BindingAssociation> associations,
        Iec61850DesignLivePointReconciliation? reconciliation)
    {
        var semanticRole = ResolveSemanticRole(associations);
        var operational = IsOperationalCandidate(
            attribute.ObjectReference,
            dataObject.InferredCdc,
            semanticRole,
            associations);
        if (semanticRole == Iec61850DataAttributeSemanticRole.Other && operational)
            semanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue;

        var memberships = BuildDataSetMemberships(associations);
        var reportMemberships = BuildReportMemberships(design, memberships);
        var companions = ResolveCompanions(associations);
        var evidence = new List<Iec61850SignalEvidence>
        {
            new()
            {
                Kind = Iec61850SignalEvidenceKind.DesignModel,
                SourceReference = attribute.ObjectReference,
                Message = $"Signal originates from design model source '{design.Source}'."
            }
        };
        AppendBindingEvidence(evidence, associations);
        AppendReportEvidence(evidence, reportMemberships);
        AppendReconciliationEvidence(evidence, reconciliation);

        var effectiveMmsReference = reconciliation is null
            ? canonicalMmsReference
            : reconciliation.EffectiveMmsReference;

        return new Iec61850SignalDescriptor
        {
            DesignReference = attribute.ObjectReference,
            ObservedReference = reconciliation?.ObservedReference ?? string.Empty,
            CanonicalMmsReference = FirstNonEmpty(reconciliation?.CanonicalMmsReference, canonicalMmsReference),
            EffectiveMmsReference = effectiveMmsReference,
            ObservedMmsReference = reconciliation?.ObservedMmsReference ?? string.Empty,
            FunctionalConstraint = NormalizeFc(FirstNonEmpty(reconciliation?.FunctionalConstraint, attribute.FunctionalConstraint)),
            Cdc = dataObject.InferredCdc,
            SclBType = FirstNonEmpty(attribute.SclBType, reconciliation?.SclBType),
            MmsType = FirstNonEmpty(attribute.MmsType, reconciliation?.MmsType),
            MmsDomain = logicalDevice.MmsDomain,
            LogicalDevice = FirstNonEmpty(logicalDevice.Inst, logicalDevice.MmsDomain),
            LogicalNode = logicalNode.Name,
            LogicalNodeClass = FirstNonEmpty(logicalNode.LnClass, Iec61850ReferenceParts.ParseLogicalNodeName(logicalNode.Name).LnClass),
            DataObject = dataObject.Name,
            DataObjectReference = dataObject.Reference,
            DataAttributePath = ResolveAttributePath(dataObject, attribute),
            SemanticRole = semanticRole,
            PrimaryValueReference = companions.PrimaryReference,
            PrimaryValueMmsReference = companions.PrimaryMmsReference,
            QualityReference = companions.QualityReference,
            QualityMmsReference = companions.QualityMmsReference,
            TimestampReference = companions.TimestampReference,
            TimestampMmsReference = companions.TimestampMmsReference,
            DataSetMemberships = memberships,
            ReportMemberships = reportMemberships,
            IsStaticDataSetMandatory = memberships.Count > 0,
            IsOperationalCandidate = operational,
            IsEngineeringOnly = memberships.Count == 0 && !operational,
            ResolutionStatus = string.IsNullOrWhiteSpace(canonicalMmsReference)
                ? Iec61850SignalCatalogResolutionStatus.Unresolved
                : Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = reconciliation?.Status,
            AlternateStrategy = reconciliation?.AlternateStrategy,
            Evidence = DistinctEvidence(evidence)
        };
    }

    private static Iec61850SignalDescriptor BuildDataSetResolvedDescriptor(
        LiveIedModelDiscoveryDocument design,
        LiveIedResolvedDataSetAttributeModel representative,
        IReadOnlyList<BindingAssociation> associations,
        Iec61850DesignLivePointReconciliation? reconciliation)
    {
        var context = FindDataObjectContext(design, representative.Reference);
        var semanticRole = ResolveSemanticRole(associations);
        var operational = associations.Any(x => Iec61850ProbeValuePolicy.IsPrimaryValueBearing(x.Attribute));
        if (semanticRole == Iec61850DataAttributeSemanticRole.Other && operational)
            semanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue;

        var memberships = BuildDataSetMemberships(associations);
        var reportMemberships = BuildReportMemberships(design, memberships);
        var companions = ResolveCompanions(associations);
        var evidence = new List<Iec61850SignalEvidence>();
        AppendBindingEvidence(evidence, associations);
        AppendReportEvidence(evidence, reportMemberships);
        AppendReconciliationEvidence(evidence, reconciliation);

        var parsed = ParseReferenceContext(representative.Reference);
        var canonicalMmsReference = FirstNonEmpty(reconciliation?.CanonicalMmsReference, representative.MmsReference);
        var isSynthetic = associations.Any(x => x.Attribute.IsSyntheticFallback);

        return new Iec61850SignalDescriptor
        {
            DesignReference = representative.Reference,
            ObservedReference = reconciliation?.ObservedReference ?? string.Empty,
            CanonicalMmsReference = canonicalMmsReference,
            EffectiveMmsReference = reconciliation is null ? canonicalMmsReference : reconciliation.EffectiveMmsReference,
            ObservedMmsReference = reconciliation?.ObservedMmsReference ?? string.Empty,
            FunctionalConstraint = NormalizeFc(FirstNonEmpty(reconciliation?.FunctionalConstraint, representative.FunctionalConstraint)),
            Cdc = FirstNonEmpty(representative.Cdc, context?.DataObject.InferredCdc),
            SclBType = FirstNonEmpty(representative.SclBType, reconciliation?.SclBType),
            MmsType = FirstNonEmpty(representative.MmsType, reconciliation?.MmsType),
            MmsDomain = FirstNonEmpty(context?.LogicalDevice.MmsDomain, parsed.MmsDomain),
            LogicalDevice = context is null
                ? parsed.MmsDomain
                : FirstNonEmpty(context.LogicalDevice.Inst, context.LogicalDevice.MmsDomain),
            LogicalNode = FirstNonEmpty(context?.LogicalNode.Name, parsed.LogicalNode),
            LogicalNodeClass = context is null
                ? Iec61850ReferenceParts.ParseLogicalNodeName(parsed.LogicalNode).LnClass
                : FirstNonEmpty(context.LogicalNode.LnClass, Iec61850ReferenceParts.ParseLogicalNodeName(context.LogicalNode.Name).LnClass),
            DataObject = FirstNonEmpty(context?.DataObject.Name, parsed.DataObject),
            DataObjectReference = FirstNonEmpty(context?.DataObject.Reference, parsed.DataObjectReference),
            DataAttributePath = context is null
                ? parsed.DataAttributePath
                : ResolveAttributePath(context.DataObject, representative.Reference),
            SemanticRole = semanticRole,
            PrimaryValueReference = companions.PrimaryReference,
            PrimaryValueMmsReference = companions.PrimaryMmsReference,
            QualityReference = companions.QualityReference,
            QualityMmsReference = companions.QualityMmsReference,
            TimestampReference = companions.TimestampReference,
            TimestampMmsReference = companions.TimestampMmsReference,
            DataSetMemberships = memberships,
            ReportMemberships = reportMemberships,
            IsStaticDataSetMandatory = true,
            IsOperationalCandidate = operational,
            IsEngineeringOnly = false,
            ResolutionStatus = isSynthetic
                ? Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback
                : Iec61850SignalCatalogResolutionStatus.DataSetResolvedAttribute,
            LiveStatus = reconciliation?.Status,
            AlternateStrategy = reconciliation?.AlternateStrategy,
            Evidence = DistinctEvidence(evidence)
        };
    }

    private static Iec61850SignalDescriptor BuildLiveOnlyDescriptor(Iec61850DesignLivePointReconciliation point)
    {
        var observedReference = FirstNonEmpty(point.ObservedReference, point.Reference);
        var parsed = ParseReferenceContext(observedReference);
        var canonicalMmsReference = FirstNonEmpty(point.CanonicalMmsReference, point.ObservedMmsReference, point.MmsReference);
        var effectiveMmsReference = FirstNonEmpty(point.EffectiveMmsReference, point.ObservedMmsReference, point.MmsReference);
        var evidence = new List<Iec61850SignalEvidence>
        {
            new()
            {
                Kind = Iec61850SignalEvidenceKind.LiveDiscovery,
                SourceReference = effectiveMmsReference,
                Message = "Native live discovery contains this signal without a matching design signal."
            }
        };
        AppendReconciliationEvidence(evidence, point);

        return new Iec61850SignalDescriptor
        {
            ObservedReference = observedReference,
            CanonicalMmsReference = canonicalMmsReference,
            EffectiveMmsReference = effectiveMmsReference,
            ObservedMmsReference = FirstNonEmpty(point.ObservedMmsReference, point.MmsReference),
            FunctionalConstraint = NormalizeFc(FirstNonEmpty(point.ObservedFunctionalConstraint, point.FunctionalConstraint)),
            SclBType = point.SclBType,
            MmsType = point.MmsType,
            MmsDomain = parsed.MmsDomain,
            LogicalDevice = parsed.MmsDomain,
            LogicalNode = parsed.LogicalNode,
            LogicalNodeClass = Iec61850ReferenceParts.ParseLogicalNodeName(parsed.LogicalNode).LnClass,
            DataObject = parsed.DataObject,
            DataObjectReference = parsed.DataObjectReference,
            DataAttributePath = parsed.DataAttributePath,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.LiveOnly,
            LiveStatus = Iec61850DesignLiveStatus.LiveOnly,
            AlternateStrategy = point.AlternateStrategy,
            IsEngineeringOnly = false,
            Evidence = DistinctEvidence(evidence)
        };
    }

    private static Dictionary<string, BindingAssociation[]> BuildBindingIndex(LiveIedDataSetSemanticBindingDocument bindings)
        => bindings.Members
            .SelectMany(member => member.ResolvedAttributes.Select(attribute => new BindingAssociation(member, attribute)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Attribute.MmsReference))
            .GroupBy(x => NormalizeMmsReference(x.Attribute.MmsReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(x => x.Member.DataSetReference, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Member.Index)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, Iec61850DesignLivePointReconciliation> BuildReconciliationIndex(
        Iec61850DesignLiveReconciliationDocument? reconciliation)
    {
        if (reconciliation is null)
            return new Dictionary<string, Iec61850DesignLivePointReconciliation>(StringComparer.OrdinalIgnoreCase);

        return reconciliation.Points
            .Where(x => x.Status != Iec61850DesignLiveStatus.LiveOnly)
            .Select(x => new
            {
                Point = x,
                Key = NormalizeMmsReference(FirstNonEmpty(x.CanonicalMmsReference, x.MmsReference))
            })
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Point, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Iec61850SignalDataSetMembership> BuildDataSetMemberships(
        IReadOnlyList<BindingAssociation> associations)
        => associations
            .GroupBy(x => $"{x.Member.DataSetReference}\u001f{x.Member.Index}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new Iec61850SignalDataSetMembership
                {
                    DataSetReference = first.Member.DataSetReference,
                    MemberIndex = first.Member.Index,
                    OriginalMemberReference = first.Member.OriginalReference,
                    CanonicalMemberReference = first.Member.CanonicalReference,
                    FunctionalConstraint = first.Member.FunctionalConstraint,
                    Cdc = first.Member.Cdc,
                    ResolutionStatus = first.Member.ResolutionStatus,
                    IsPrimaryValueForMember = group.Any(x => Iec61850ProbeValuePolicy.IsPrimaryValueBearing(x.Attribute))
                };
            })
            .OrderBy(x => x.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.MemberIndex)
            .ToArray();

    private static IReadOnlyList<Iec61850SignalReportMembership> BuildReportMemberships(
        LiveIedModelDiscoveryDocument design,
        IReadOnlyList<Iec61850SignalDataSetMembership> memberships)
    {
        var result = new List<Iec61850SignalReportMembership>();
        foreach (var dataSetReference in memberships
                     .Select(x => x.DataSetReference)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var report in design.ReportControls.Where(x => ReferenceEquals(x.DataSetReference, dataSetReference)))
            {
                result.Add(new Iec61850SignalReportMembership
                {
                    ReportControlReference = report.Reference,
                    DataSetReference = dataSetReference,
                    Buffered = report.Buffered,
                    ReportId = report.ReportId
                });
            }

            var dataSet = design.DataSets.FirstOrDefault(x => ReferenceEquals(x.Reference, dataSetReference));
            if (dataSet is null)
                continue;

            foreach (var usedBy in dataSet.UsedByReportControls.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var existing = result.Any(x =>
                    ReferenceEquals(x.DataSetReference, dataSetReference) &&
                    ReferenceEquals(x.ReportControlReference, usedBy));
                if (existing)
                    continue;

                var report = design.ReportControls.FirstOrDefault(x => ReferenceEquals(x.Reference, usedBy));
                result.Add(new Iec61850SignalReportMembership
                {
                    ReportControlReference = usedBy,
                    DataSetReference = dataSetReference,
                    Buffered = report is null ? null : report.Buffered,
                    ReportId = report?.ReportId ?? string.Empty
                });
            }
        }

        return result
            .GroupBy(x => $"{x.DataSetReference}\u001f{x.ReportControlReference}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReportControlReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CompanionReferences ResolveCompanions(IReadOnlyList<BindingAssociation> associations)
    {
        var members = associations
            .Select(x => x.Member)
            .GroupBy(x => $"{x.DataSetReference}\u001f{x.Index}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

        var primary = members
            .SelectMany(x => x.ResolvedAttributes.Where(Iec61850ProbeValuePolicy.IsPrimaryValueBearing))
            .ToArray();
        var quality = members
            .SelectMany(x => x.ResolvedAttributes.Where(y => y.SemanticRole == Iec61850DataAttributeSemanticRole.Quality))
            .ToArray();
        var timestamp = members
            .SelectMany(x => x.ResolvedAttributes.Where(y => y.SemanticRole == Iec61850DataAttributeSemanticRole.Timestamp))
            .ToArray();

        return new CompanionReferences(
            UniqueValue(primary.Select(x => x.Reference)),
            UniqueValue(primary.Select(x => x.MmsReference)),
            UniqueValue(quality.Select(x => x.Reference)),
            UniqueValue(quality.Select(x => x.MmsReference)),
            UniqueValue(timestamp.Select(x => x.Reference)),
            UniqueValue(timestamp.Select(x => x.MmsReference)));
    }

    private static Iec61850DataAttributeSemanticRole ResolveSemanticRole(IReadOnlyList<BindingAssociation> associations)
    {
        var roles = associations
            .Select(x => x.Attribute.SemanticRole)
            .Where(x => x != Iec61850DataAttributeSemanticRole.Other)
            .Distinct()
            .ToArray();
        return roles.Length == 1 ? roles[0] : Iec61850DataAttributeSemanticRole.Other;
    }

    private static bool IsOperationalCandidate(
        string reference,
        string cdc,
        Iec61850DataAttributeSemanticRole semanticRole,
        IReadOnlyList<BindingAssociation> associations)
    {
        if (associations.Any(x => Iec61850ProbeValuePolicy.IsPrimaryValueBearing(x.Attribute)))
            return true;

        return Iec61850ProbeValuePolicy.IsPrimaryValueBearing(new LiveIedResolvedDataSetAttributeModel
        {
            Reference = reference,
            Cdc = cdc,
            SemanticRole = semanticRole
        });
    }

    private static void AppendBindingEvidence(
        ICollection<Iec61850SignalEvidence> evidence,
        IReadOnlyList<BindingAssociation> associations)
    {
        foreach (var association in associations)
        {
            evidence.Add(new Iec61850SignalEvidence
            {
                Kind = Iec61850SignalEvidenceKind.DataSetSemanticBinding,
                SourceReference = $"{association.Member.DataSetReference}[{association.Member.Index}]",
                Message = $"{association.Member.ResolutionStatus}: {string.Join(" ", association.Member.Evidence)}"
            });
        }
    }

    private static void AppendReportEvidence(
        ICollection<Iec61850SignalEvidence> evidence,
        IReadOnlyList<Iec61850SignalReportMembership> reports)
    {
        foreach (var report in reports)
        {
            evidence.Add(new Iec61850SignalEvidence
            {
                Kind = Iec61850SignalEvidenceKind.ReportControlMembership,
                SourceReference = report.ReportControlReference,
                Message = $"Report control references DataSet '{report.DataSetReference}'."
            });
        }
    }

    private static void AppendReconciliationEvidence(
        ICollection<Iec61850SignalEvidence> evidence,
        Iec61850DesignLivePointReconciliation? point)
    {
        if (point is null)
            return;

        var kind = point.Status switch
        {
            Iec61850DesignLiveStatus.Exact or Iec61850DesignLiveStatus.Compatible or Iec61850DesignLiveStatus.LiveOnly
                => Iec61850SignalEvidenceKind.LiveDiscovery,
            Iec61850DesignLiveStatus.RecoveredByProbe => Iec61850SignalEvidenceKind.ExactProbe,
            Iec61850DesignLiveStatus.RecoveredByAlternateProbe => Iec61850SignalEvidenceKind.AlternateProbe,
            Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery => Iec61850SignalEvidenceKind.AlternateDiscovery,
            _ => Iec61850SignalEvidenceKind.ReconciliationDiagnostic
        };

        foreach (var message in point.Evidence.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            evidence.Add(new Iec61850SignalEvidence
            {
                Kind = kind,
                SourceReference = FirstNonEmpty(point.EffectiveMmsReference, point.CanonicalMmsReference, point.MmsReference),
                Message = message
            });
        }

        foreach (var attempt in point.ProbeAttempts)
        {
            evidence.Add(new Iec61850SignalEvidence
            {
                Kind = attempt.IsCanonical ? Iec61850SignalEvidenceKind.ExactProbe : Iec61850SignalEvidenceKind.AlternateProbe,
                SourceReference = attempt.Probe.MmsReference,
                Message = $"{attempt.Probe.Status}: {attempt.Probe.Message}".Trim()
            });
        }
    }

    private static IReadOnlyList<Iec61850SignalEvidence> DistinctEvidence(IEnumerable<Iec61850SignalEvidence> evidence)
        => evidence
            .GroupBy(x => $"{x.Kind}\u001f{x.SourceReference}\u001f{x.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

    private static DataObjectContext? FindDataObjectContext(LiveIedModelDiscoveryDocument design, string reference)
    {
        var normalized = NormalizeDesignReference(reference);
        return design.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes.SelectMany(ln => ln.DataObjects.Select(dataObject => new DataObjectContext(ld, ln, dataObject))))
            .Select(context => new { Context = context, Reference = NormalizeDesignReference(context.DataObject.Reference) })
            .Where(x => x.Reference.Length > 0 &&
                        (string.Equals(normalized, x.Reference, StringComparison.OrdinalIgnoreCase) ||
                         normalized.StartsWith(x.Reference + ".", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Reference.Length)
            .Select(x => x.Context)
            .FirstOrDefault();
    }

    private static ReferenceContext ParseReferenceContext(string reference)
    {
        var normalized = NormalizeDesignReference(reference);
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash >= normalized.Length - 1)
            return new ReferenceContext(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        var domain = normalized[..slash];
        var logicalPath = normalized[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        if (firstDot <= 0)
            return new ReferenceContext(domain, logicalPath, string.Empty, string.Empty, string.Empty);

        var logicalNode = logicalPath[..firstDot];
        var objectAndAttribute = logicalPath[(firstDot + 1)..];
        var dataObject = Iec61850ReferenceParts.TopDataObjectName(objectAndAttribute);
        var attributePath = Iec61850ReferenceParts.DataAttributePath(objectAndAttribute);
        var dataObjectReference = dataObject.Length == 0 ? string.Empty : $"{domain}/{logicalNode}.{dataObject}";
        return new ReferenceContext(domain, logicalNode, dataObject, dataObjectReference, attributePath);
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

    private static string ResolveAttributePath(LiveIedDataObjectModel dataObject, LiveIedDataAttributeModel attribute)
        => !string.IsNullOrWhiteSpace(attribute.AttributePath)
            ? attribute.AttributePath.Trim().Replace('$', '.')
            : ResolveAttributePath(dataObject, attribute.ObjectReference);

    private static string ResolveAttributePath(LiveIedDataObjectModel dataObject, string attributeReference)
    {
        var objectReference = NormalizeDesignReference(dataObject.Reference);
        var attribute = NormalizeDesignReference(attributeReference);
        return attribute.StartsWith(objectReference + ".", StringComparison.OrdinalIgnoreCase)
            ? attribute[(objectReference.Length + 1)..]
            : ParseReferenceContext(attribute).DataAttributePath;
    }

    private static string NormalizeMmsReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').ToUpperInvariant();

    private static string NormalizeDesignReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.').Trim('.');

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool ReferenceEquals(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static string UniqueValue(IEnumerable<string> values)
    {
        var distinct = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 1 ? distinct[0] : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private sealed record BindingAssociation(
        LiveIedDataSetMemberSemanticBinding Member,
        LiveIedResolvedDataSetAttributeModel Attribute);

    private sealed record DataObjectContext(
        LiveIedLogicalDeviceModel LogicalDevice,
        LiveIedLogicalNodeModel LogicalNode,
        LiveIedDataObjectModel DataObject);

    private readonly record struct ReferenceContext(
        string MmsDomain,
        string LogicalNode,
        string DataObject,
        string DataObjectReference,
        string DataAttributePath);

    private readonly record struct CompanionReferences(
        string PrimaryReference,
        string PrimaryMmsReference,
        string QualityReference,
        string QualityMmsReference,
        string TimestampReference,
        string TimestampMmsReference);
}
