namespace AR.Iec61850.Mms;

/// <summary>
/// Client-compatibility activation wrapper for persistent reporting.
///
/// Mature IEC 61850 clients normally reserve a BRCB when ResvTms is exposed,
/// enable reporting, install/retain the report receiver, and only then request GI.
/// Some servers also support implicit BRCB reservation through RptEna=true, so an
/// explicit ResvTms rejection is non-fatal and the baseline activation is still tried.
///
/// This wrapper does not create dynamic DataSets and does not schedule cyclic process
/// reads. It only hardens the RCB control-plane sequence used by report acquisition.
/// </summary>
public sealed partial class MmsClientSession
{
    public async Task<MmsPersistentReportMonitorAttemptResult> StartPersistentReportMonitorClientCompatibleAsync(
        MmsReportSubscriptionPlan plan,
        bool triggerGeneralInterrogation = true,
        bool deleteDynamicDataSetOnStop = true,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var rcb = plan.ReportControl;
        MmsReportAttributeWriteStep? reservationStep = null;
        var compatibilityWarnings = new List<string>();

        if (rcb is { Buffered: true } &&
            rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase) &&
            !MmsReportSubscriptionPlanner.IsExplicitlyEnabled(rcb) &&
            !MmsReportSubscriptionPlanner.IsReservedByOtherClient(rcb))
        {
            reservationStep = await WriteReportAttributeAsync(
                rcb,
                "ResvTms",
                MmsDataValue.Unsigned(60),
                cancellationToken).ConfigureAwait(false);

            if (!reservationStep.IsSuccess)
            {
                compatibilityWarnings.Add(
                    $"BRCB ResvTms=60 explicit reservation was not accepted ({reservationStep.Message}). Continuing with standards-compatible implicit reservation through RptEna=true.");
            }
        }

        // Deliberately suppress GI inside the baseline start. The baseline method registers
        // the persistent monitor only after RptEna succeeds. Requesting GI below guarantees
        // that the report receiver/session is already registered when the server emits the
        // initial InformationReport, while the receive router still preserves any earlier
        // unconfirmed traffic that arrived during confirmed writes.
        var attempt = await StartPersistentReportMonitorWithAttemptEvidenceAsync(
            plan,
            triggerGeneralInterrogation: false,
            deleteDynamicDataSetOnStop,
            directory,
            cancellationToken).ConfigureAwait(false);

        var start = attempt.StartResult;
        var writes = new List<MmsReportAttributeWriteStep>();
        if (reservationStep is not null)
            writes.Add(reservationStep);
        writes.AddRange(start.WriteSteps);

        var warnings = start.Warnings
            .Where(warning => !warning.Contains("ResvTms pre-reserve was skipped", StringComparison.OrdinalIgnoreCase))
            .Concat(compatibilityWarnings)
            .ToList();

        if (!attempt.IsSuccess || start.Session is null)
        {
            var cleanupSteps = attempt.CleanupSteps.ToList();
            var cleanupWarnings = attempt.CleanupWarnings.ToList();
            var cleanupAttempted = attempt.CleanupAttempted;
            var cleanupSucceeded = attempt.CleanupSucceeded;

            if (reservationStep?.IsSuccess == true && rcb is not null)
            {
                var release = await TryWriteReportAttributeForCleanupAsync(
                    rcb,
                    "ResvTms",
                    MmsDataValue.Unsigned(0),
                    CancellationToken.None).ConfigureAwait(false);
                cleanupSteps.Add(release);
                cleanupAttempted = true;
                cleanupSucceeded &= release.IsSuccess;
                if (!release.IsSuccess)
                    cleanupWarnings.Add($"BRCB ResvTms cleanup after failed activation was not accepted: {release.Message}");
            }

            return new MmsPersistentReportMonitorAttemptResult
            {
                StartResult = CopyStartResult(start, writes, warnings),
                DynamicAttemptState = attempt.DynamicAttemptState,
                FailureReason = attempt.FailureReason,
                CleanupAttempted = cleanupAttempted,
                CleanupSucceeded = cleanupSucceeded,
                CleanupSteps = cleanupSteps,
                CleanupWarnings = cleanupWarnings
            };
        }

        if (reservationStep?.IsSuccess == true)
            start.Session.ReservationTouched = true;

        if (triggerGeneralInterrogation)
        {
            var gi = await WriteReportAttributeAsync(
                start.Session.ReportControl,
                "GI",
                MmsDataValue.Boolean(true),
                cancellationToken).ConfigureAwait(false);
            writes.Add(gi);
            if (!gi.IsSuccess)
                warnings.Add("GI=true write failed or is not supported by this RCB. Waiting for spontaneous/integrity reports only.");
        }

        var compatibilityMessage = reservationStep?.IsSuccess == true
            ? "BRCB explicitly reserved with ResvTms=60 before RptEna; GI was requested only after the persistent receiver was registered."
            : "GI was requested only after the persistent receiver was registered.";

        return new MmsPersistentReportMonitorAttemptResult
        {
            StartResult = CopyStartResult(
                start,
                writes,
                warnings,
                $"{start.Message} {compatibilityMessage}"),
            DynamicAttemptState = attempt.DynamicAttemptState,
            FailureReason = attempt.FailureReason,
            CleanupAttempted = attempt.CleanupAttempted,
            CleanupSucceeded = attempt.CleanupSucceeded,
            CleanupSteps = attempt.CleanupSteps,
            CleanupWarnings = attempt.CleanupWarnings
        };
    }

    private static MmsPersistentReportMonitorStartResult CopyStartResult(
        MmsPersistentReportMonitorStartResult source,
        IReadOnlyList<MmsReportAttributeWriteStep> writes,
        IReadOnlyList<string> warnings,
        string? message = null)
        => new()
        {
            IsSuccess = source.IsSuccess,
            Message = message ?? source.Message,
            Session = source.Session,
            WriteSteps = writes.ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RcbSnapshots = source.RcbSnapshots,
            DataSetSnapshots = source.DataSetSnapshots
        };
}
