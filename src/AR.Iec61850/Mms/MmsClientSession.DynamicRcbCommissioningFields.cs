namespace AR.Iec61850.Mms;

/// <summary>
/// Exact temporary lease over mutable URCB proof fields used only by explicit
/// commissioning. The original MMS values are retained byte-for-byte so they
/// can be restored after the proof attempt.
/// </summary>
public sealed class MmsDynamicRcbCommissioningFieldLease
{
    internal MmsDynamicRcbCommissioningFieldLease(
        MmsReportControlCandidate reportControl,
        MmsDataValue originalTriggerOptions,
        MmsDataValue originalOptionalFields,
        string requestedTriggerOptions,
        string requestedOptionalFields)
    {
        ReportControl = reportControl;
        OriginalTriggerOptions = originalTriggerOptions;
        OriginalOptionalFields = originalOptionalFields;
        OriginalTriggerOptionsText = MmsDataCodec.ToDisplayString(originalTriggerOptions);
        OriginalOptionalFieldsText = MmsDataCodec.ToDisplayString(originalOptionalFields);
        RequestedTriggerOptions = requestedTriggerOptions;
        RequestedOptionalFields = requestedOptionalFields;
    }

    public MmsReportControlCandidate ReportControl { get; }
    public MmsDataValue OriginalTriggerOptions { get; }
    public MmsDataValue OriginalOptionalFields { get; }
    public string OriginalTriggerOptionsText { get; }
    public string OriginalOptionalFieldsText { get; }
    public string RequestedTriggerOptions { get; }
    public string RequestedOptionalFields { get; }
    public bool TriggerOptionsTouched { get; internal set; }
    public bool OptionalFieldsTouched { get; internal set; }
    public bool IsRestored { get; internal set; }
}

public sealed class MmsDynamicRcbCommissioningFieldPrepareResult
{
    public bool IsSuccess { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public MmsDynamicRcbCommissioningFieldLease? Lease { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed class MmsDynamicRcbCommissioningFieldRestoreResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Temporarily configures the two report-control bit-string fields needed to
    /// make an actual InformationReport self-identifying during explicit G2.4
    /// commissioning. This API does not bind DatSet, reserve the RCB, or write
    /// RptEna/GI. Any partial write is rolled back immediately before failure is
    /// returned.
    /// </summary>
    public async Task<MmsDynamicRcbCommissioningFieldPrepareResult> PrepareDynamicRcbCommissioningFieldsAsync(
        MmsReportControlCandidate reportControl,
        string triggerOptions,
        string optionalFields,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionalFields);

        var writes = new List<MmsReportAttributeWriteStep>();
        var evidence = new List<string>();

        if (reportControl.Buffered)
        {
            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Temporary G2.4 report-field lease is restricted to URCBs."
            };
        }

        if (!reportControl.Attributes.Contains("TrgOps", StringComparer.OrdinalIgnoreCase) ||
            !reportControl.Attributes.Contains("OptFlds", StringComparer.OrdinalIgnoreCase))
        {
            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Selected URCB does not expose both TrgOps and OptFlds; no commissioning field was changed."
            };
        }

        if (!MmsReportControlFieldCodec.TryEncodeTriggerOptions(triggerOptions, out var desiredTriggerOptions) ||
            !MmsReportControlFieldCodec.TryEncodeOptionalFields(optionalFields, out var desiredOptionalFields))
        {
            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Requested G2.4 TrgOps/OptFlds configuration could not be encoded; no commissioning field was changed."
            };
        }

        var originalTriggerRead = await ReadReportControlFieldValueAsync(reportControl, "TrgOps", cancellationToken).ConfigureAwait(false);
        var originalOptionalRead = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", cancellationToken).ConfigureAwait(false);
        if (!originalTriggerRead.IsSuccess || originalTriggerRead.Value?.Kind != MmsDataKind.BitString ||
            !originalOptionalRead.IsSuccess || originalOptionalRead.Value?.Kind != MmsDataKind.BitString)
        {
            evidence.Add($"original TrgOps read: success={originalTriggerRead.IsSuccess}; value={RenderValue(originalTriggerRead.Value)}; result={originalTriggerRead.Message}");
            evidence.Add($"original OptFlds read: success={originalOptionalRead.IsSuccess}; value={RenderValue(originalOptionalRead.Value)}; result={originalOptionalRead.Message}");
            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Original TrgOps/OptFlds could not be captured as exact MMS BitString values. No commissioning field was changed.",
                Evidence = evidence
            };
        }

        var lease = new MmsDynamicRcbCommissioningFieldLease(
            reportControl,
            originalTriggerRead.Value,
            originalOptionalRead.Value,
            triggerOptions,
            optionalFields);

        evidence.Add($"captured original TrgOps={lease.OriginalTriggerOptionsText}; OptFlds={lease.OriginalOptionalFieldsText}");
        evidence.Add($"requested temporary TrgOps={MmsDataCodec.ToDisplayString(desiredTriggerOptions)} ({triggerOptions}); OptFlds={MmsDataCodec.ToDisplayString(desiredOptionalFields)} ({optionalFields})");

        try
        {
            var triggerWrite = await WriteReportAttributeAsync(reportControl, "TrgOps", desiredTriggerOptions, cancellationToken).ConfigureAwait(false);
            lease.TriggerOptionsTouched = true;
            writes.Add(triggerWrite);
            if (!triggerWrite.IsSuccess)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary TrgOps write failed.").ConfigureAwait(false);

            var triggerReadback = await ReadReportControlFieldValueAsync(reportControl, "TrgOps", cancellationToken).ConfigureAwait(false);
            var triggerExact = triggerReadback.IsSuccess && triggerReadback.Value is not null && ExactMmsValueEquals(triggerReadback.Value, desiredTriggerOptions);
            evidence.Add($"temporary TrgOps readback: success={triggerReadback.IsSuccess}; exact={triggerExact}; value={RenderValue(triggerReadback.Value)}; result={triggerReadback.Message}");
            if (!triggerExact)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary TrgOps readback was not exact.").ConfigureAwait(false);

            var optionalWrite = await WriteReportAttributeAsync(reportControl, "OptFlds", desiredOptionalFields, cancellationToken).ConfigureAwait(false);
            lease.OptionalFieldsTouched = true;
            writes.Add(optionalWrite);
            if (!optionalWrite.IsSuccess)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary OptFlds write failed.").ConfigureAwait(false);

            var optionalReadback = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", cancellationToken).ConfigureAwait(false);
            var optionalExact = optionalReadback.IsSuccess && optionalReadback.Value is not null && ExactMmsValueEquals(optionalReadback.Value, desiredOptionalFields);
            evidence.Add($"temporary OptFlds readback: success={optionalReadback.IsSuccess}; exact={optionalExact}; value={RenderValue(optionalReadback.Value)}; result={optionalReadback.Message}");
            if (!optionalExact)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary OptFlds readback was not exact.").ConfigureAwait(false);

            reportControl.TriggerOptions = triggerOptions;
            reportControl.OptionalFields = optionalFields;

            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = true,
                CleanupSucceeded = false,
                Lease = lease,
                WriteSteps = writes,
                Evidence = evidence,
                Message = "Temporary G2.4 TrgOps/OptFlds lease established with exact readback. Caller must restore the lease after the report proof attempt."
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            evidence.Add($"temporary report-field exception: {ex.GetType().Name}: {ex.Message}");
            return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary report-field preparation failed with a protocol/transport exception.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Restores exact original TrgOps/OptFlds values captured by
    /// PrepareDynamicRcbCommissioningFieldsAsync. The caller should stop/disable
    /// the RCB first. Success requires write success and exact readback of every
    /// field that was touched.
    /// </summary>
    public async Task<MmsDynamicRcbCommissioningFieldRestoreResult> RestoreDynamicRcbCommissioningFieldsAsync(
        MmsDynamicRcbCommissioningFieldLease lease,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(lease);

        if (lease.IsRestored)
        {
            return new MmsDynamicRcbCommissioningFieldRestoreResult
            {
                IsSuccess = true,
                Message = "Temporary G2.4 report-field lease was already restored."
            };
        }

        var writes = new List<MmsReportAttributeWriteStep>();
        var evidence = new List<string>();
        var success = true;

        // Restore OptFlds first, then TrgOps. RptEna is expected to be false before
        // this method is called, so no report can be emitted with a half-restored
        // proof configuration.
        if (lease.OptionalFieldsTouched)
        {
            var restoreOptional = await TryWriteReportAttributeForCleanupAsync(
                lease.ReportControl,
                "OptFlds",
                lease.OriginalOptionalFields,
                CancellationToken.None).ConfigureAwait(false);
            writes.Add(restoreOptional);
            success &= restoreOptional.IsSuccess;

            var readback = await ReadReportControlFieldValueAsync(lease.ReportControl, "OptFlds", CancellationToken.None).ConfigureAwait(false);
            var exact = readback.IsSuccess && readback.Value is not null && ExactMmsValueEquals(readback.Value, lease.OriginalOptionalFields);
            evidence.Add($"restore OptFlds readback: write={restoreOptional.IsSuccess}; read={readback.IsSuccess}; exact={exact}; value={RenderValue(readback.Value)}; expected={lease.OriginalOptionalFieldsText}");
            success &= exact;
        }

        if (lease.TriggerOptionsTouched)
        {
            var restoreTrigger = await TryWriteReportAttributeForCleanupAsync(
                lease.ReportControl,
                "TrgOps",
                lease.OriginalTriggerOptions,
                CancellationToken.None).ConfigureAwait(false);
            writes.Add(restoreTrigger);
            success &= restoreTrigger.IsSuccess;

            var readback = await ReadReportControlFieldValueAsync(lease.ReportControl, "TrgOps", CancellationToken.None).ConfigureAwait(false);
            var exact = readback.IsSuccess && readback.Value is not null && ExactMmsValueEquals(readback.Value, lease.OriginalTriggerOptions);
            evidence.Add($"restore TrgOps readback: write={restoreTrigger.IsSuccess}; read={readback.IsSuccess}; exact={exact}; value={RenderValue(readback.Value)}; expected={lease.OriginalTriggerOptionsText}");
            success &= exact;
        }

        if (success)
        {
            lease.ReportControl.TriggerOptions = lease.OriginalTriggerOptionsText;
            lease.ReportControl.OptionalFields = lease.OriginalOptionalFieldsText;
            lease.IsRestored = true;
        }

        return new MmsDynamicRcbCommissioningFieldRestoreResult
        {
            IsSuccess = success,
            WriteSteps = writes,
            Evidence = evidence,
            Message = success
                ? "Temporary G2.4 TrgOps/OptFlds lease restored with exact MMS readback."
                : "Temporary G2.4 TrgOps/OptFlds restore was not fully proven; treat the RCB as requiring fresh manual/read-only inspection before retry."
        };
    }

    private async Task<MmsDynamicRcbCommissioningFieldPrepareResult> FailPrepareAndRollbackAsync(
        MmsDynamicRcbCommissioningFieldLease lease,
        List<MmsReportAttributeWriteStep> writes,
        List<string> evidence,
        string failure)
    {
        MmsDynamicRcbCommissioningFieldRestoreResult restore;
        try
        {
            restore = await RestoreDynamicRcbCommissioningFieldsAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            evidence.Add($"automatic lease rollback exception: {ex.GetType().Name}: {ex.Message}");
            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Lease = lease,
                WriteSteps = writes,
                Evidence = evidence,
                Message = failure + " Automatic TrgOps/OptFlds rollback could not be proven."
            };
        }

        writes.AddRange(restore.WriteSteps);
        evidence.AddRange(restore.Evidence.Select(line => "rollback " + line));
        return new MmsDynamicRcbCommissioningFieldPrepareResult
        {
            IsSuccess = false,
            CleanupSucceeded = restore.IsSuccess,
            Lease = lease,
            WriteSteps = writes,
            Evidence = evidence,
            Message = restore.IsSuccess
                ? failure + " Automatic TrgOps/OptFlds rollback passed."
                : failure + " Automatic TrgOps/OptFlds rollback was not fully proven."
        };
    }

    private async Task<MmsReadResult> ReadReportControlFieldValueAsync(
        MmsReportControlCandidate reportControl,
        string attribute,
        CancellationToken cancellationToken)
    {
        var reference = MmsObjectReference.Parse($"{reportControl.Reference}.{attribute}", reportControl.FunctionalConstraint);
        return await ReadSingleVariableAsync(reference, cancellationToken).ConfigureAwait(false);
    }

    private static bool ExactMmsValueEquals(MmsDataValue left, MmsDataValue right)
        => left.Kind == right.Kind && MmsDataCodec.Encode(left).AsSpan().SequenceEqual(MmsDataCodec.Encode(right));

    private static string RenderValue(MmsDataValue? value)
        => value is null ? "-" : MmsDataCodec.ToDisplayString(value);
}
