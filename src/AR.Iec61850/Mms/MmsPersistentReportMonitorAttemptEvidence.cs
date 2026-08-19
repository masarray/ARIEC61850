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
    DynamicDataSetProbeDefineFailed,
    DynamicDataSetProbeVerificationFailed,
    DynamicDataSetProbeDeleteFailed,
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
/// P6.2 adds a single-member Define/GetAttributes/Delete probation before the full dynamic
/// DataSet is created so vendor/service failures are isolated before any RCB is mutated.
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

        MmsDynamicDataSetProbeResult? probe = null;
        if (isDynamic && MmsDynamicDataSetProbePolicy.ShouldProbe(plan))
        {
            probe = await ProbeDynamicDataSetServiceAsync(
                plan.DataSetReference,
                plan.DynamicPoints[0].ToObjectReference(),
                directory,
                cancellationToken).ConfigureAwait(false);

            if (!probe.IsSuccess)
            {
                var failedStart = new MmsPersistentReportMonitorStartResult
                {
                    IsSuccess = false,
                    Message = $"P6.2 dynamic DataSet service probation failed before RCB mutation. {probe.Summary}",
                    WriteSteps = probe.WriteSteps,
                    Warnings = probe.EvidenceLines
                };

                return new MmsPersistentReportMonitorAttemptResult
                {
                    StartResult = failedStart,
                    DynamicAttemptState = probe.DynamicMutationAttempted
                        ? MmsDynamicReportAttemptState.AttemptedFailed
                        : MmsDynamicReportAttemptState.NotAttempted,
                    FailureReason = MmsDynamicDataSetProbePolicy.FailureReason(probe.FailureStage),
                    CleanupAttempted = probe.CleanupAttempted,
                    CleanupSucceeded = probe.CleanupSucceeded,
                    CleanupSteps = probe.WriteSteps
                        .Where(step => step.Attribute.Equals("Probe.DeleteNamedVariableList", StringComparison.OrdinalIgnoreCase))
                        .ToArray(),
                    CleanupWarnings = probe.CleanupSucceeded
                        ? Array.Empty<string>()
                        : ["P6.2 single-member DataSet probe cleanup failed. Fresh association/NamedVariableList evidence is required before another automatic dynamic attempt."]
                };
            }
        }

        var start = await StartPersistentReportMonitorAsync(
            plan,
            triggerGeneralInterrogation,
            deleteDynamicDataSetOnStop,
            directory,
            cancellationToken).ConfigureAwait(false);

        if (probe is not null)
            start = MergeProbeEvidence(start, probe);

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
            (step.Attribute.Contains("DefineNamedVariableList", StringComparison.OrdinalIgnoreCase) ||
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
            // Probe.DefineNamedVariableList is deliberately excluded here: a successful
            // probe is deleted before full activation. Only the full DataSet definition
            // may need failed-start cleanup below.
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

    private static MmsPersistentReportMonitorStartResult MergeProbeEvidence(
        MmsPersistentReportMonitorStartResult start,
        MmsDynamicDataSetProbeResult probe)
        => new()
        {
            IsSuccess = start.IsSuccess,
            Message = start.IsSuccess
                ? start.Message
                : $"{start.Message} P6.2 probation had already succeeded: {probe.Summary}",
            Session = start.Session,
            WriteSteps = probe.WriteSteps.Concat(start.WriteSteps).ToArray(),
            Warnings = start.IsSuccess
                ? start.Warnings
                : probe.EvidenceLines.Concat(start.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RcbSnapshots = start.RcbSnapshots,
            DataSetSnapshots = start.DataSetSnapshots
        };

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
            if (failed.Attribute.Equals("Probe.DefineNamedVariableList", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.DynamicDataSetProbeDefineFailed;
            if (failed.Attribute.Equals("Probe.GetNamedVariableListAttributes", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.DynamicDataSetProbeVerificationFailed;
            if (failed.Attribute.Equals("Probe.DeleteNamedVariableList", StringComparison.OrdinalIgnoreCase))
                return MmsReportActivationFailureReason.DynamicDataSetProbeDeleteFailed;
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
