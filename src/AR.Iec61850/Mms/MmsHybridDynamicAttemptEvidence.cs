namespace AR.Iec61850.Mms;

/// <summary>
/// P4 planning disposition for the dynamic-report stage. Planned means runtime must
/// actually attempt the dynamic report before the signal can be classified as final
/// MMS-polling fallback. Skipped is allowed only with an explicit reason.
/// </summary>
public enum MmsHybridDynamicAttemptDisposition
{
    NotRequired,
    Planned,
    Skipped
}

public enum MmsHybridPollingFallbackReason
{
    None,
    DynamicDisabledByPolicy,
    WriteServiceUnsupported,
    DefineNamedVariableListUnsupported,
    DeleteNamedVariableListUnsupported,
    NoCapabilityQualifiedDynamicRcb,
    DynamicPlanUnavailableAfterCapabilityQualification,
    PollingFallbackDisabled
}

public sealed class MmsHybridSignalAttemptEvidence
{
    public string SignalReference { get; init; } = string.Empty;
    public MmsHybridAcquisitionKind PlannedKind { get; init; }
    public MmsHybridDynamicAttemptDisposition DynamicAttemptDisposition { get; init; }
    public MmsHybridPollingFallbackReason PollingFallbackReason { get; init; }
    public string Detail { get; init; } = string.Empty;

    public bool DynamicAttemptRequired => DynamicAttemptDisposition == MmsHybridDynamicAttemptDisposition.Planned;
    public bool IsExplainablePollingFallback =>
        PlannedKind != MmsHybridAcquisitionKind.MmsPollingFallback ||
        (DynamicAttemptDisposition == MmsHybridDynamicAttemptDisposition.Skipped &&
         PollingFallbackReason != MmsHybridPollingFallbackReason.None);
}

/// <summary>
/// Builds authoritative P4 attempt evidence from the capability-qualified acquisition plan.
/// This does not claim that a network write happened; Planned explicitly means that the
/// runtime still owes a real dynamic activation attempt.
/// </summary>
public static class MmsHybridDynamicAttemptEvidenceBuilder
{
    public static IReadOnlyList<MmsHybridSignalAttemptEvidence> Build(
        MmsCapabilityAwareHybridReportAcquisitionPlan plan,
        MmsHybridReportAcquisitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new MmsHybridReportAcquisitionOptions();

        return plan.AcquisitionPlan.Assignments
            .Select(assignment => BuildOne(assignment, plan.AssociationCapability, options))
            .ToArray();
    }

    private static MmsHybridSignalAttemptEvidence BuildOne(
        MmsHybridSignalAssignment assignment,
        MmsReportAssociationCapability capability,
        MmsHybridReportAcquisitionOptions options)
    {
        if (assignment.Kind is MmsHybridAcquisitionKind.StaticBrcb or MmsHybridAcquisitionKind.StaticUrcb)
        {
            return Evidence(
                assignment,
                MmsHybridDynamicAttemptDisposition.NotRequired,
                MmsHybridPollingFallbackReason.None,
                "Static report coverage owns this signal; no dynamic attempt is required while that static plan activates successfully.");
        }

        if (assignment.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb)
        {
            return Evidence(
                assignment,
                MmsHybridDynamicAttemptDisposition.Planned,
                MmsHybridPollingFallbackReason.None,
                "Capability-qualified dynamic report is planned. Runtime must attempt activation before this signal can become final MMS-polling fallback.");
        }

        if (assignment.Kind == MmsHybridAcquisitionKind.Uncovered && !options.AllowPollingFallback)
        {
            return Evidence(
                assignment,
                MmsHybridDynamicAttemptDisposition.Skipped,
                MmsHybridPollingFallbackReason.PollingFallbackDisabled,
                assignment.Reason);
        }

        var reason = ClassifyPollingReason(capability, options);
        return Evidence(
            assignment,
            MmsHybridDynamicAttemptDisposition.Skipped,
            reason,
            string.IsNullOrWhiteSpace(assignment.Reason)
                ? Describe(reason)
                : $"{Describe(reason)} {assignment.Reason}");
    }

    private static MmsHybridPollingFallbackReason ClassifyPollingReason(
        MmsReportAssociationCapability capability,
        MmsHybridReportAcquisitionOptions options)
    {
        if (!options.AllowDynamicBrcb && !options.AllowDynamicUrcb)
            return MmsHybridPollingFallbackReason.DynamicDisabledByPolicy;
        if (capability.WriteService == MmsCapabilityEvidenceState.Unsupported)
            return MmsHybridPollingFallbackReason.WriteServiceUnsupported;
        if (capability.DefineNamedVariableListService == MmsCapabilityEvidenceState.Unsupported)
            return MmsHybridPollingFallbackReason.DefineNamedVariableListUnsupported;
        if (capability.DeleteNamedVariableListService == MmsCapabilityEvidenceState.Unsupported)
            return MmsHybridPollingFallbackReason.DeleteNamedVariableListUnsupported;
        if (capability.DynamicBrcbSlotCount + capability.DynamicUrcbSlotCount == 0)
            return MmsHybridPollingFallbackReason.NoCapabilityQualifiedDynamicRcb;
        return MmsHybridPollingFallbackReason.DynamicPlanUnavailableAfterCapabilityQualification;
    }

    private static string Describe(MmsHybridPollingFallbackReason reason)
        => reason switch
        {
            MmsHybridPollingFallbackReason.DynamicDisabledByPolicy => "Dynamic reporting is disabled by caller policy.",
            MmsHybridPollingFallbackReason.WriteServiceUnsupported => "The MMS association explicitly reports Write service unsupported.",
            MmsHybridPollingFallbackReason.DefineNamedVariableListUnsupported => "The MMS association explicitly reports DefineNamedVariableList unsupported.",
            MmsHybridPollingFallbackReason.DeleteNamedVariableListUnsupported => "The MMS association explicitly reports DeleteNamedVariableList unsupported for safely cleanable temporary DataSets.",
            MmsHybridPollingFallbackReason.NoCapabilityQualifiedDynamicRcb => "No fresh, capability-qualified free dynamic BRCB/URCB slot is available.",
            MmsHybridPollingFallbackReason.DynamicPlanUnavailableAfterCapabilityQualification => "Dynamic capability exists, but no ready dynamic subscription plan could be produced for this exact signal.",
            MmsHybridPollingFallbackReason.PollingFallbackDisabled => "Polling fallback is disabled.",
            _ => "No polling fallback reason was supplied."
        };

    private static MmsHybridSignalAttemptEvidence Evidence(
        MmsHybridSignalAssignment assignment,
        MmsHybridDynamicAttemptDisposition disposition,
        MmsHybridPollingFallbackReason reason,
        string detail)
        => new()
        {
            SignalReference = assignment.SignalReference,
            PlannedKind = assignment.Kind,
            DynamicAttemptDisposition = disposition,
            PollingFallbackReason = reason,
            Detail = detail
        };
}
