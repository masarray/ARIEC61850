using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Field-stability wrapper for the P1.7 native per-IED capability runtime.
///
/// A native dchg + cleanup witness proves that dynamic reporting may be generalized to
/// fresh exact-resolved members, but it does not prove that a larger NamedVariableList than
/// the G2.3 accepted envelope is safe for the IED association. Runtime therefore keeps the
/// general member-capability semantics while capping each individual Dynamic DataSet to the
/// physically proven G2.3 member-count envelope.
///
/// This planner does not alter identity/witness authorization, RCB availability, deterministic
/// AR_HYB identity, ProductionEligible state, or polling fallback. Those remain owned by the
/// existing stable P1.7 planner and its downstream hybrid planner.
/// </summary>
public static class MmsGuardedDynamicReportNativeFieldCapabilityEnvelopeBoundRuntimePlanner
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
        MmsDynamicReportNativeFieldCapabilityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);
        options ??= new MmsHybridReportAcquisitionOptions();

        var provenEnvelopeMembers = sourceContext.Profile.ProvenSafeMemberCount;
        var boundedMembers = provenEnvelopeMembers > 1
            ? Math.Min(options.MaxDynamicMembersPerReport, provenEnvelopeMembers)
            : options.MaxDynamicMembersPerReport;

        var boundedOptions = new MmsHybridReportAcquisitionOptions
        {
            MaxStaticReportPlans = options.MaxStaticReportPlans,
            MaxDynamicReportPlans = options.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = boundedMembers,
            RequireExactAvailabilityEvidence = options.RequireExactAvailabilityEvidence,
            AllowCallerOwnedReports = options.AllowCallerOwnedReports,
            AllowStaticBrcb = options.AllowStaticBrcb,
            AllowStaticUrcb = options.AllowStaticUrcb,
            AllowDynamicBrcb = options.AllowDynamicBrcb,
            AllowDynamicUrcb = options.AllowDynamicUrcb,
            AllowPollingFallback = options.AllowPollingFallback
        };

        return MmsGuardedDynamicReportNativeFieldCapabilityStableRuntimePlanner.Build(
            catalog,
            requestedSignals,
            inventory,
            availability,
            liveDirectory,
            negotiatedCapabilities,
            boundedOptions,
            sourceContext,
            evidence);
    }
}
