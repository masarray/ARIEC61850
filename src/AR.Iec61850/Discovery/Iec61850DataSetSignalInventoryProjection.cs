namespace AR.Iec61850.Discovery;

/// <summary>
/// Builds the engine-owned signal inventory used by application signal selectors.
///
/// A DataSet member is protocol evidence even when the IED exposes it only as an FCD
/// (DataObject-level member) and attribute-level type information is not available yet.
/// The inventory is deliberately member-centric: one static DataSet member produces one
/// mandatory selector descriptor. A unique resolved primary DataAttribute is attached as
/// runtime binding metadata; it never replaces the original DataSet member identity.
/// </summary>
public static class Iec61850DataSetSignalInventoryProjection
{
    public static IReadOnlyList<Iec61850SignalDescriptor> GetMandatorySignals(
        LiveIedModelDiscoveryDocument design,
        Iec61850DesignLiveReconciliationDocument? reconciliation = null)
    {
        ArgumentNullException.ThrowIfNull(design);

        var semanticBindings = Iec61850DataSetSemanticBindingResolver.Resolve(design);
        var catalog = Iec61850SignalCatalogBuilder.Build(design, reconciliation);
        var result = new List<Iec61850SignalDescriptor>();

        foreach (var dataSet in design.DataSets.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var member in dataSet.Members.OrderBy(x => x.Index))
            {
                var binding = semanticBindings.Find(dataSet.Reference, member.Index);
                var primary = binding?.PrimaryValue;
                if (primary is not null)
                {
                    var source = FindCatalogSignal(catalog, primary);
                    if (source is not null)
                    {
                        result.Add(ProjectResolvedMemberDescriptor(source, dataSet, member, binding!));
                        continue;
                    }
                }

                var reason = binding is null
                    ? $"Static DataSet member {dataSet.Reference}[{member.Index}] has no semantic binding; the member identity is preserved in the signal inventory."
                    : binding.ResolutionStatus == LiveIedDataSetMemberResolutionStatus.Ambiguous
                        ? $"Static DataSet member {dataSet.Reference}[{member.Index}] has ambiguous primary-value semantics; the member identity is preserved without guessing a runtime leaf."
                        : $"Static DataSet member {dataSet.Reference}[{member.Index}] has no unique primary DataAttribute; the member identity is preserved in the signal inventory.";
                result.Add(BuildUnresolvedMemberDescriptor(design, dataSet, member, reason));
            }
        }

        return result.ToArray();
    }

    private static Iec61850SignalDescriptor? FindCatalogSignal(
        Iec61850SignalCatalogDocument catalog,
        LiveIedResolvedDataSetAttributeModel primary)
    {
        if (!string.IsNullOrWhiteSpace(primary.MmsReference))
        {
            var byMms = catalog.FindByCanonicalMmsReference(primary.MmsReference);
            if (byMms is not null)
                return byMms;
        }

        return catalog.Signals.FirstOrDefault(signal =>
            ReferenceEquals(signal.DesignReference, primary.Reference));
    }

    private static Iec61850SignalDescriptor ProjectResolvedMemberDescriptor(
        Iec61850SignalDescriptor source,
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        LiveIedDataSetMemberSemanticBinding binding)
    {
        var memberReference = NormalizeReference(member.Reference);
        var functionalConstraint = (member.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant();
        var membership = new Iec61850SignalDataSetMembership
        {
            DataSetReference = dataSet.Reference,
            MemberIndex = member.Index,
            OriginalMemberReference = member.Reference,
            CanonicalMemberReference = memberReference,
            FunctionalConstraint = FirstNonEmpty(binding.FunctionalConstraint, source.FunctionalConstraint, functionalConstraint),
            Cdc = FirstNonEmpty(binding.Cdc, source.Cdc),
            ResolutionStatus = binding.ResolutionStatus,
            IsPrimaryValueForMember = true
        };
        var reports = source.ReportMemberships
            .Where(report => ReferenceEquals(report.DataSetReference, dataSet.Reference))
            .ToArray();
        if (reports.Length == 0)
        {
            reports = designReports(dataSet, source).ToArray();
        }
        var evidence = source.Evidence
            .Concat(new[]
            {
                new Iec61850SignalEvidence
                {
                    Kind = Iec61850SignalEvidenceKind.DataSetSemanticBinding,
                    SourceReference = memberReference,
                    Message = $"Static DataSet member {dataSet.Reference}[{member.Index}] keeps its original FCD/FCDA identity; runtime primary binding is '{binding.PrimaryValueReference}'. {string.Join(" ", binding.Evidence)}"
                }
            })
            .ToArray();

        return new Iec61850SignalDescriptor
        {
            DesignReference = source.DesignReference,
            ObservedReference = source.ObservedReference,
            CanonicalMmsReference = source.CanonicalMmsReference,
            EffectiveMmsReference = source.EffectiveMmsReference,
            ObservedMmsReference = source.ObservedMmsReference,
            FunctionalConstraint = FirstNonEmpty(source.FunctionalConstraint, functionalConstraint),
            Cdc = FirstNonEmpty(binding.Cdc, source.Cdc),
            SclBType = source.SclBType,
            MmsType = source.MmsType,
            MmsDomain = source.MmsDomain,
            LogicalDevice = source.LogicalDevice,
            LogicalNode = source.LogicalNode,
            LogicalNodeClass = source.LogicalNodeClass,
            DataObject = source.DataObject,
            DataObjectReference = source.DataObjectReference,
            DataAttributePath = source.DataAttributePath,
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            PrimaryValueReference = binding.PrimaryValueReference,
            PrimaryValueMmsReference = binding.PrimaryValueMmsReference,
            QualityReference = source.QualityReference,
            QualityMmsReference = source.QualityMmsReference,
            TimestampReference = source.TimestampReference,
            TimestampMmsReference = source.TimestampMmsReference,
            DataSetMemberships = new[] { membership },
            ReportMemberships = reports,
            IsStaticDataSetMandatory = true,
            IsOperationalCandidate = true,
            IsEngineeringOnly = false,
            ResolutionStatus = source.ResolutionStatus,
            LiveStatus = source.LiveStatus,
            AlternateStrategy = source.AlternateStrategy,
            Evidence = evidence
        };

        static IEnumerable<Iec61850SignalReportMembership> designReports(
            LiveIedDataSetModel dataSet,
            Iec61850SignalDescriptor source)
        {
            // Source catalog normally already carries report membership. Keep the helper
            // deliberately empty when it does not; the unresolved path remains responsible
            // for design-only report projection. This avoids fabricating report authority.
            return source.ReportMemberships.Where(report =>
                string.Equals(report.DataSetReference, dataSet.Reference, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Iec61850SignalDescriptor BuildUnresolvedMemberDescriptor(
        LiveIedModelDiscoveryDocument design,
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        string? evidenceMessage = null)
    {
        var memberReference = NormalizeReference(member.Reference);
        var context = FindDataObjectContext(design, memberReference);
        var functionalConstraint = (member.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant();
        var mmsReference = FirstNonEmpty(
            member.MmsReference,
            BuildMmsReference(memberReference, functionalConstraint));
        var membership = new Iec61850SignalDataSetMembership
        {
            DataSetReference = dataSet.Reference,
            MemberIndex = member.Index,
            OriginalMemberReference = member.Reference,
            CanonicalMemberReference = memberReference,
            FunctionalConstraint = functionalConstraint,
            Cdc = context?.DataObject.InferredCdc ?? string.Empty,
            ResolutionStatus = LiveIedDataSetMemberResolutionStatus.Unresolved,
            IsPrimaryValueForMember = false
        };
        var reports = design.ReportControls
            .Where(report => ReferenceEquals(report.DataSetReference, dataSet.Reference))
            .OrderBy(report => report.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(report => new Iec61850SignalReportMembership
            {
                ReportControlReference = report.Reference,
                DataSetReference = dataSet.Reference,
                Buffered = report.Buffered,
                ReportId = report.ReportId
            })
            .ToArray();
        var dataAttributePath = context is null
            ? string.Empty
            : ResolveRelativeAttributePath(context.DataObject.Reference, memberReference);

        return new Iec61850SignalDescriptor
        {
            DesignReference = memberReference,
            CanonicalMmsReference = mmsReference,
            EffectiveMmsReference = mmsReference,
            FunctionalConstraint = functionalConstraint,
            Cdc = context?.DataObject.InferredCdc ?? string.Empty,
            MmsDomain = context?.LogicalDevice.MmsDomain ?? ExtractDomain(memberReference),
            LogicalDevice = context is null
                ? ExtractDomain(memberReference)
                : FirstNonEmpty(context.LogicalDevice.Inst, context.LogicalDevice.MmsDomain),
            LogicalNode = context?.LogicalNode.Name ?? ExtractLogicalNode(memberReference),
            LogicalNodeClass = context?.LogicalNode.LnClass ?? string.Empty,
            DataObject = context?.DataObject.Name ?? ExtractDataObject(memberReference),
            DataObjectReference = context?.DataObject.Reference ?? ExtractDataObjectReference(memberReference),
            DataAttributePath = dataAttributePath,
            SemanticRole = Iec61850DataAttributeSemanticRole.Other,
            DataSetMemberships = new[] { membership },
            ReportMemberships = reports,
            IsStaticDataSetMandatory = true,
            IsOperationalCandidate = false,
            IsEngineeringOnly = false,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.Unresolved,
            Evidence = new[]
            {
                new Iec61850SignalEvidence
                {
                    Kind = Iec61850SignalEvidenceKind.DataSetSemanticBinding,
                    SourceReference = memberReference,
                    Message = evidenceMessage ?? $"Static DataSet member {dataSet.Reference}[{member.Index}] is preserved in the signal inventory although no unique primary DataAttribute has been resolved yet."
                }
            }
        };
    }

    private static DataObjectContext? FindDataObjectContext(
        LiveIedModelDiscoveryDocument design,
        string memberReference)
    {
        return design.LogicalDevices
            .SelectMany(logicalDevice => logicalDevice.LogicalNodes.SelectMany(logicalNode =>
                logicalNode.DataObjects.Select(dataObject => new DataObjectContext(logicalDevice, logicalNode, dataObject))))
            .Where(context => IsInsideDataObject(memberReference, context.DataObject.Reference))
            .OrderByDescending(context => NormalizeReference(context.DataObject.Reference).Length)
            .FirstOrDefault();
    }

    private static bool IsInsideDataObject(string memberReference, string dataObjectReference)
    {
        var member = NormalizeReference(memberReference);
        var dataObject = NormalizeReference(dataObjectReference);
        return string.Equals(member, dataObject, StringComparison.OrdinalIgnoreCase) ||
               member.StartsWith(dataObject + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRelativeAttributePath(string dataObjectReference, string memberReference)
    {
        var dataObject = NormalizeReference(dataObjectReference);
        var member = NormalizeReference(memberReference);
        if (string.Equals(member, dataObject, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return member.StartsWith(dataObject + ".", StringComparison.OrdinalIgnoreCase)
            ? member[(dataObject.Length + 1)..]
            : string.Empty;
    }

    private static string BuildMmsReference(string reference, string functionalConstraint)
    {
        var slash = reference.IndexOf('/');
        if (slash <= 0 || slash >= reference.Length - 1 || string.IsNullOrWhiteSpace(functionalConstraint))
            return string.Empty;

        var domain = reference[..slash];
        var logicalPath = reference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        if (firstDot <= 0 || firstDot >= logicalPath.Length - 1)
            return string.Empty;

        var logicalNode = logicalPath[..firstDot];
        var objectPath = logicalPath[(firstDot + 1)..].Replace('.', '$');
        return $"{domain}/{logicalNode}${functionalConstraint}${objectPath}";
    }

    private static string ExtractDomain(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash > 0 ? reference[..slash] : string.Empty;
    }

    private static string ExtractLogicalNode(string reference)
    {
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash >= reference.Length - 1)
            return string.Empty;
        var remainder = reference[(slash + 1)..];
        var dot = remainder.IndexOf('.');
        return dot > 0 ? remainder[..dot] : string.Empty;
    }

    private static string ExtractDataObject(string reference)
    {
        var dataObjectReference = ExtractDataObjectReference(reference);
        var dot = dataObjectReference.LastIndexOf('.');
        return dot >= 0 && dot < dataObjectReference.Length - 1
            ? dataObjectReference[(dot + 1)..]
            : string.Empty;
    }

    private static string ExtractDataObjectReference(string reference)
    {
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash >= reference.Length - 1)
            return reference;
        var firstDot = reference.IndexOf('.', slash + 1);
        if (firstDot < 0)
            return reference;
        var secondDot = reference.IndexOf('.', firstDot + 1);
        return secondDot < 0 ? reference : reference[..secondDot];
    }

    private static bool ReferenceEquals(string? left, string? right)
        => string.Equals(NormalizeReference(left), NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record DataObjectContext(
        LiveIedLogicalDeviceModel LogicalDevice,
        LiveIedLogicalNodeModel LogicalNode,
        LiveIedDataObjectModel DataObject);
}
