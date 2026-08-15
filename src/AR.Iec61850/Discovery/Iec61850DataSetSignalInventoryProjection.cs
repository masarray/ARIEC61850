namespace AR.Iec61850.Discovery;

/// <summary>
/// Builds the engine-owned signal inventory used by application signal selectors.
///
/// A DataSet member is protocol evidence even when the IED exposes it only as an FCD
/// (DataObject-level member) and attribute-level type information is not available yet.
/// Resolved primary-value descriptors are preferred. Any remaining DataSet member is
/// preserved as an unresolved mandatory descriptor instead of being silently dropped.
/// </summary>
public static class Iec61850DataSetSignalInventoryProjection
{
    public static IReadOnlyList<Iec61850SignalDescriptor> GetMandatorySignals(
        LiveIedModelDiscoveryDocument design,
        Iec61850DesignLiveReconciliationDocument? reconciliation = null)
    {
        ArgumentNullException.ThrowIfNull(design);

        var catalog = Iec61850SignalCatalogBuilder.Build(design, reconciliation);
        var result = catalog.GetMandatoryPrimarySignals().ToList();
        var coveredMembers = result
            .SelectMany(signal => signal.DataSetMemberships)
            .Select(MembershipKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dataSet in design.DataSets
                     .OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var member in dataSet.Members.OrderBy(x => x.Index))
            {
                var key = MembershipKey(dataSet.Reference, member.Index);
                if (coveredMembers.Contains(key))
                    continue;

                result.Add(BuildUnresolvedMemberDescriptor(design, dataSet, member));
                coveredMembers.Add(key);
            }
        }

        return result
            .OrderBy(signal => signal.DataSetMemberships.FirstOrDefault()?.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.DataSetMemberships.FirstOrDefault()?.MemberIndex ?? int.MaxValue)
            .ThenBy(SortReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Iec61850SignalDescriptor BuildUnresolvedMemberDescriptor(
        LiveIedModelDiscoveryDocument design,
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member)
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
            OriginalMemberReference = memberReference,
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
                    Message = $"Static DataSet member {dataSet.Reference}[{member.Index}] is preserved in the signal inventory although no unique primary DataAttribute has been resolved yet."
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

    private static string SortReference(Iec61850SignalDescriptor signal)
        => FirstNonEmpty(
            signal.DesignReference,
            signal.CanonicalMmsReference,
            signal.EffectiveMmsReference,
            signal.ObservedReference);

    private static string MembershipKey(Iec61850SignalDataSetMembership membership)
        => MembershipKey(membership.DataSetReference, membership.MemberIndex);

    private static string MembershipKey(string dataSetReference, int memberIndex)
        => $"{NormalizeReference(dataSetReference)}\u001f{memberIndex}";

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
