using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// P1.6 execution-stable wrapper around the field-capability runtime planner.
///
/// The generic hybrid planner numbers temporary DataSets by plan order. That is sufficient
/// for one planning pass, but ARSAS deliberately revalidates one RCB immediately before each
/// write. Isolating the second dynamic group would otherwise renumber it back to AR_HYB_01,
/// which can collide with an already-active first group in the same logical device.
///
/// This wrapper changes only temporary dynamic DataSet identity: every dynamic RCB receives
/// a deterministic association-independent AR_HYB_<hash> name derived from its exact RCB
/// reference. Member scope, RCB choice, capability gates, limits and ProductionEligible
/// semantics remain owned by MmsGuardedDynamicReportFieldCapabilityRuntimePlanner.
/// </summary>
public static class MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner
{
    public static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities,
        MmsHybridReportAcquisitionOptions? options,
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence evidence)
    {
        var plan = MmsGuardedDynamicReportFieldCapabilityRuntimePlanner.Build(
            catalog,
            requestedSignals,
            inventory,
            availability,
            liveDirectory,
            negotiatedCapabilities,
            options,
            sourceContext,
            evidence);

        return WithStableDynamicDataSetIdentities(plan);
    }

    internal static MmsCapabilityAwareHybridReportAcquisitionPlan WithStableDynamicDataSetIdentities(
        MmsCapabilityAwareHybridReportAcquisitionPlan source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var segments = source.AcquisitionPlan.Segments
            .Select(segment => StabilizeSegment(segment, replacements))
            .ToArray();

        if (replacements.Count == 0)
            return source;

        var assignments = source.AcquisitionPlan.Assignments
            .Select(assignment => StabilizeAssignment(assignment, replacements))
            .ToArray();

        var acquisition = new MmsHybridReportAcquisitionPlan
        {
            SchemaVersion = source.AcquisitionPlan.SchemaVersion,
            Status = source.AcquisitionPlan.Status,
            Capability = source.AcquisitionPlan.Capability,
            Segments = segments,
            Assignments = assignments,
            Warnings = source.AcquisitionPlan.Warnings,
            Blockers = source.AcquisitionPlan.Blockers
        };

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = acquisition,
            AssociationCapability = source.AssociationCapability,
            AutomaticDynamicActivationQuarantined = source.AutomaticDynamicActivationQuarantined,
            ProductionDynamicActivationAuthorized = source.ProductionDynamicActivationAuthorized,
            ProductionDynamicAuthorizationReason = source.ProductionDynamicAuthorizationReason,
            ProductionQualifiedDynamicMemberCount = source.ProductionQualifiedDynamicMemberCount,
            ProductionQualifiedRcbReference = source.ProductionQualifiedRcbReference,
            PolicyWarnings = source.PolicyWarnings
        };
    }

    private static MmsHybridAcquisitionSegment StabilizeSegment(
        MmsHybridAcquisitionSegment segment,
        IDictionary<string, string> replacements)
    {
        if (segment.Kind is not (MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb) ||
            segment.ReportPlan is null ||
            string.IsNullOrWhiteSpace(segment.ReportControlReference) ||
            string.IsNullOrWhiteSpace(segment.ReportPlan.DataSetReference))
        {
            return segment;
        }

        var oldReference = segment.ReportPlan.DataSetReference;
        var newReference = BuildStableDataSetReference(oldReference, segment.ReportControlReference);
        if (oldReference.Equals(newReference, StringComparison.OrdinalIgnoreCase))
            return segment;

        replacements[ReplacementKey(segment.ReportControlReference, oldReference)] = newReference;
        var reportPlan = CloneReportPlan(segment.ReportPlan, oldReference, newReference);

        return new MmsHybridAcquisitionSegment
        {
            Kind = segment.Kind,
            Activation = segment.Activation,
            ReportPlan = reportPlan,
            Availability = segment.Availability,
            Signals = segment.Signals,
            RequiresWrite = segment.RequiresWrite,
            IsAlreadyActiveByCaller = segment.IsAlreadyActiveByCaller,
            Reason = segment.Reason
        };
    }

    private static MmsReportSubscriptionPlan CloneReportPlan(
        MmsReportSubscriptionPlan source,
        string oldDataSetReference,
        string newDataSetReference)
        => new()
        {
            Mode = source.Mode,
            Status = source.Status,
            ReportControl = source.ReportControl,
            DataSetReference = newDataSetReference,
            Members = source.Members,
            DynamicPoints = source.DynamicPoints,
            Steps = source.Steps
                .Select(step => ReplaceOrdinalIgnoreCase(step, oldDataSetReference, newDataSetReference))
                .ToArray(),
            Warnings = source.Warnings,
            Blockers = source.Blockers,
            RcbSelection = source.RcbSelection
        };

    private static MmsHybridSignalAssignment StabilizeAssignment(
        MmsHybridSignalAssignment assignment,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (assignment.Kind is not (MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb) ||
            string.IsNullOrWhiteSpace(assignment.ReportControlReference) ||
            string.IsNullOrWhiteSpace(assignment.DataSetReference))
        {
            return assignment;
        }

        if (!replacements.TryGetValue(
                ReplacementKey(assignment.ReportControlReference, assignment.DataSetReference),
                out var replacement))
        {
            return assignment;
        }

        return new MmsHybridSignalAssignment
        {
            SignalReference = assignment.SignalReference,
            Kind = assignment.Kind,
            ReportControlReference = assignment.ReportControlReference,
            DataSetReference = replacement,
            IsReportBacked = assignment.IsReportBacked,
            Reason = assignment.Reason
        };
    }

    private static string BuildStableDataSetReference(string currentDataSetReference, string rcbReference)
    {
        var current = (currentDataSetReference ?? string.Empty).Trim().Replace('\\', '/');
        var separator = current.LastIndexOf('.');
        if (separator < 0)
            return current;

        return current[..(separator + 1)] + BuildStableDataSetName(rcbReference);
    }

    internal static string BuildStableDataSetName(string rcbReference)
    {
        var normalized = MmsRcbAvailabilityEvaluator.NormalizeReference(rcbReference)
            .Trim()
            .Replace('\\', '/')
            .ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "AR_HYB_" + Convert.ToHexString(digest)[..12];
    }

    private static string ReplacementKey(string rcbReference, string dataSetReference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(rcbReference).Replace('\\', '/') + "\n" +
           (dataSetReference ?? string.Empty).Trim().Replace('\\', '/');

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
            return source;

        var index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return source;

        return source[..index] + newValue + source[(index + oldValue.Length)..];
    }
}
