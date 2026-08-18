namespace AR.Iec61850.Mms;

public enum MmsDynamicReportAttemptState
{
    NotDynamic,
    NotAttempted,
    AttemptedSucceeded,
    AttemptedFailed
}

public enum MmsReportActivationFailureReason
{
    None,
    InvalidPlan,
    DynamicDataSetDefineFailed,
    DynamicDataSetBindFailed,
    TriggerOptionsUnavailable,
    TriggerOptionsWriteFailed,
    ReportEnableFailed,
    ActivationException,
    OtherActivationFailure
}

/// <summary>
/// P4 runtime evidence around persistent-report activation. For a dynamic plan,
/// DynamicAttempted is true only when the engine actually issued at least one dynamic
/// configuration service/write to the IED. Failed mutated attempts are rolled back best-effort.
/// </summary>
public sealed class MmsPersistentReportMonitorAttemptResult
{
    public MmsPersistentReportMonitorStartResult StartResult { get; init; } = new();
    public MmsDynamicReportAttemptState DynamicAttemptState { get; init; }
    public MmsReportActivationFailureReason FailureReason { get; init; }
    public bool CleanupAttempted { get; init; }
    public bool CleanupSucceeded { get; init; } = true;
    public IReadOnlyList<MmsReportAttributeWriteStep> CleanupSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> CleanupWarnings { get; init; } = Array.Empty<string>();

    public bool IsSuccess => StartResult.IsSuccess;
    public bool DynamicAttempted => DynamicAttemptState is
        MmsDynamicReportAttemptState.AttemptedSucceeded or
        MmsDynamicReportAttemptState.AttemptedFailed;
}

public sealed partial class MmsClientSession
{
    public async Task<MmsPersistentReportMonitorAttemptResult> StartPersistentReportMonitorWithAttemptEvidenceAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = true,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var isDynamic = plan.Mode == MmsReportSubscriptionPlanMode.DynamicDataSet;
        var originalDataSetReference = plan.ReportControl?.DataSetReference ?? string.Empty;

        var start = await StartPersistentReportMonitorAsync(
            plan,
            triggerGeneralInterrogation,
            deleteDynamicDataSetOnStop,
            directory,
            cancellationToken).ConfigureAwait(false);

        if (!isDynamic)
        {
            return new MmsPersistentReportMonitorAttemptResult
            {
                StartResult = start,
                DynamicAttemptState = MmsDynamicReportAttemptState.NotDynamic,
                FailureReason = start.IsSuccess ? MmsReportActivationFailureReason.None : ClassifyFailure(start)
            };
        }

        var dynamicAttempted = start.WriteSteps.Any(step =>
            step.Attempted &&
            (step.Attribute.Equals("DefineNamedVariableList", StringComparison.OrdinalIgnoreCase) ||
             step.Attribute.Equals("DatSet", StringComparison.OrdinalIgnoreCase) ||
             step.Attribute.Equals("TrgOps", StringComparison.OrdinalIgnoreCase) ||
             step.Attribute.Equals("RptEna", StringComparison.OrdinalIgnoreCase)));

        if (start.IsSuccess)
        {
            return new MmsPersistentReportMonitorAttemptResult
            {
                StartResult = start,
                DynamicAttemptState = MmsDynamicReportAttemptState.AttemptedSucceeded,
                FailureReason = MmsReportActivationFailureReason.None
            };
        }

        if (!dynamicAttempted)
        {
            return new MmsPersistentReportMonitorAttemptResult
            {
                StartResult = start,
                DynamicAttemptState = MmsDynamicReportAttemptState.NotAttempted,
                FailureReason = ClassifyFailure(start)
            };
        }

        var cleanupSteps = new List<MmsReportAttributeWriteStep>();
        var cleanupWarnings = new List<string>();
        var cleanupSucceeded = true;
        var rcb = plan.ReportControl;

        if (rcb is not null)
        {
            var enabled = SuccessfulStep(start.WriteSteps, "RptEna");
            var reserved = SuccessfulStep(start.WriteSteps, "Resv") || SuccessfulStep(start.WriteSteps, "ResvTms");
            var dataSetDefined = SuccessfulStep(start.WriteSteps, "DefineNamedVariableList");
            var dataSetBound = SuccessfulStep(start.WriteSteps, "DatSet");

            if (enabled)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(
                    rcb,
                    "RptEna",
                    MmsDataValue.Boolean(false),
                    CancellationToken.None).ConfigureAwait(false);
                cleanupSteps.Add(disable);
                cleanupSucceeded &= disable.IsSuccess;
            }

            if (dataSetBound)
            {
                var restoreValue = string.IsNullOrWhiteSpace(originalDataSetReference)
                    ? string.Empty
                    : ToReportDataSetAttributeValue(originalDataSetReference);
                var restore = await TryWriteReportAttributeForCleanupAsync(
                    rcb,
                    "DatSet",
                    MmsDataValue.VisibleString(restoreValue),
                    CancellationToken.None).ConfigureAwait(false);
                cleanupSteps.Add(restore);
                cleanupSucceeded &= restore.IsSuccess;
            }

            if (dataSetDefined && !string.IsNullOrWhiteSpace(plan.DataSetReference))
            {
                try
                {
                    var delete = await DeleteNamedVariableListAsync(plan.DataSetReference, CancellationToken.None).ConfigureAwait(false);
                    var deleteStep = new MmsReportAttributeWriteStep
                    {
                        Attribute = "DeleteNamedVariableList",
                        Reference = plan.DataSetReference,
                        Attempted = true,
                        IsSuccess = delete.IsSuccess,
                        Message = delete.Message
                    };
                    cleanupSteps.Add(deleteStep);
                    cleanupSucceeded &= delete.IsSuccess;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    cleanupSteps.Add(new MmsReportAttributeWriteStep
                    {
                        Attribute = "DeleteNamedVariableList",
                        Reference = plan.DataSetReference,
                        Attempted = true,
                        IsSuccess = false,
                        Message = $"failed-start cleanup delete failed: {ex.GetType().Name}: {ex.Message}"
                    });
                    cleanupSucceeded = false;
                }
            }

            if (reserved)
            {
                var release = rcb.Buffered
                    ? await TryWriteReportAttributeForCleanupAsync(rcb, "ResvTms", MmsDataValue.Unsigned(0), CancellationToken.None).ConfigureAwait(false)
                    : await TryWriteReportAttributeForCleanupAsync(rcb, "Resv", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                cleanupSteps.Add(release);
                cleanupSucceeded &= release.IsSuccess;
            }
        }

        if (!cleanupSucceeded)
            cleanupWarnings.Add("One or more best-effort rollback steps failed after the dynamic report activation attempt. Fresh RCB/DataSet availability must be re-read before another automatic attempt.");

        return new MmsPersistentReportMonitorAttemptResult
        {
            StartResult = start,
            DynamicAttemptState = MmsDynamicReportAttemptState.AttemptedFailed,
            FailureReason = ClassifyFailure(start),
            CleanupAttempted = cleanupSteps.Count > 0,
            CleanupSucceeded = cleanupSucceeded,
            CleanupSteps = cleanupSteps,
            CleanupWarnings = cleanupWarnings
        };
    }

    private static bool SuccessfulStep(IEnumerable<MmsReportAttributeWriteStep> steps, string attribute)
        => steps.Any(step =>
            step.Attempted &&
            step.IsSuccess &&
            step.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase));

    private static MmsReportActivationFailureReason ClassifyFailure(MmsPersistentReportMonitorStartResult result)
    {
        var failed = result.WriteSteps.LastOrDefault(step => step.Attempted && !step.IsSuccess);
        if (failed is not null)
        {
            if (failed.Attribute.Equals("DefineNamedVariableList", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.DynamicDataSetDefineFailed;
            if (failed.Attribute.Equals("DatSet", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.DynamicDataSetBindFailed;
            if (failed.Attribute.Equals("TrgOps", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.TriggerOptionsWriteFailed;
            if (failed.Attribute.Equals("RptEna", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.ReportEnableFailed;
        }

        if (result.Message.Contains("requires a writable TrgOps", StringComparison.OrdinalIgnoreCase))
            return MmsReportActivationFailureReason.TriggerOptionsUnavailable;
        if (result.Message.Contains("requires resolved points", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("requires a ready plan", StringComparison.OrdinalIgnoreCase))
            return MmsReportActivationFailureReason.InvalidPlan;
        if (result.Message.Contains("start failed:", StringComparison.OrdinalIgnoreCase))
            return MmsReportActivationFailureReason.ActivationException;
        return result.IsSuccess
            ? MmsReportActivationFailureReason.None
            : MmsReportActivationFailureReason.OtherActivationFailure;
    }
}
