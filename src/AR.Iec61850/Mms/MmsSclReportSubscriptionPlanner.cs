using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed class MmsSclReportSubscriptionPlanResult
{
    public MmsSclRcbFamilyResolution RcbResolution { get; init; } = new();
    public MmsReportSubscriptionPlan Plan { get; init; } = new();

    public bool IsReady => RcbResolution.IsSuccess && Plan.IsReady;
    public string Summary => $"{RcbResolution.Message} {Plan.Summary}";
}

/// <summary>
/// SCL-aware front door for static report planning. SCL contributes the
/// declarative ReportControl family identity; the live MMS inventory remains
/// authoritative for concrete instances, runtime ownership, and DataSet state.
/// </summary>
public static class MmsSclReportSubscriptionPlanner
{
    public static MmsSclReportSubscriptionPlanResult BuildStaticPlan(
        MmsReportInventory liveInventory,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        SclReportControl reportControl,
        bool allowUrCbFallback = false,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null)
    {
        ArgumentNullException.ThrowIfNull(liveInventory);
        ArgumentNullException.ThrowIfNull(dataSetDirectories);
        ArgumentNullException.ThrowIfNull(reportControl);

        var resolution = MmsSclRcbFamilyResolver.Resolve(reportControl, liveInventory.ReportControls);
        if (!resolution.IsSuccess)
        {
            return new MmsSclReportSubscriptionPlanResult
            {
                RcbResolution = resolution,
                Plan = new MmsReportSubscriptionPlan
                {
                    Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
                    Status = MmsReportSubscriptionPlanStatus.Blocked,
                    DataSetReference = reportControl.DataSetReference,
                    Blockers =
                    [
                        resolution.Message,
                        "No report-control write is permitted until the SCL declaration is reconciled to at least one concrete live MMS RCB instance."
                    ],
                    Steps =
                    [
                        "Refresh the live MMS RCB directory and reconcile the declared SCL ReportControl family again."
                    ]
                }
            };
        }

        var scopedInventory = new MmsReportInventory();
        scopedInventory.DataSets.AddRange(liveInventory.DataSets);
        scopedInventory.ReportControls.AddRange(resolution.Candidates);

        // The live inventory is deliberately scoped to the reconciled family.
        // We therefore do not pass the declarative family reference into the
        // legacy strict exact-reference filter, which would reject concrete
        // indexed instances such as Buffer01/Buffer02.
        //
        // allowUrCbFallback means "may a BRCB request fall back to URCB" in the
        // legacy selector. An SCL ReportControl that is itself an URCB is not a
        // fallback: it is the explicitly requested family, so its RP candidates
        // must remain eligible even when cross-mode fallback is disabled.
        var effectiveAllowUrCb = !reportControl.Buffered || allowUrCbFallback;
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(
            scopedInventory,
            dataSetDirectories,
            preferredRcbReference: null,
            preferredDataSetReference: EmptyToNull(reportControl.DataSetReference),
            strictRcb: false,
            allowUrCbFallback: effectiveAllowUrCb,
            allowPollingFallback: allowPollingFallback,
            excludedRcbReferences: excludedRcbReferences);

        plan = AddResolutionEvidence(plan, resolution);
        return new MmsSclReportSubscriptionPlanResult
        {
            RcbResolution = resolution,
            Plan = plan
        };
    }

    private static MmsReportSubscriptionPlan AddResolutionEvidence(
        MmsReportSubscriptionPlan plan,
        MmsSclRcbFamilyResolution resolution)
        => new()
        {
            Mode = plan.Mode,
            Status = plan.Status,
            ReportControl = plan.ReportControl,
            DataSetReference = plan.DataSetReference,
            Members = plan.Members,
            DynamicPoints = plan.DynamicPoints,
            Steps = new[] { resolution.Message }.Concat(plan.Steps).ToArray(),
            Warnings = plan.Warnings,
            Blockers = plan.Blockers,
            RcbSelection = plan.RcbSelection
        };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
