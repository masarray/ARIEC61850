namespace AR.Iec61850.Mms;

/// <summary>
/// Transactional temporary lease over mutable URCB proof fields used only by explicit
/// commissioning. Original MMS values are retained byte-for-byte for restore writes,
/// while proof equality follows the IEC significant BIT STRING bits and keeps raw BER
/// differences as evidence instead of treating vendor padding as process semantics.
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

        // Do not trust GetNameList child advertisement as a write gate. Exact direct reads
        // capture the actual original MMS values that will be written back during cleanup.
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
                Message = "Original TrgOps/OptFlds could not be captured as MMS BitString values. No commissioning field was changed.",
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
            var triggerComparison = triggerReadback.IsSuccess && triggerReadback.Value is not null
                ? MmsReportControlFieldCodec.CompareTriggerOptions(desiredTriggerOptions, triggerReadback.Value)
                : null;
            evidence.Add(ComparisonEvidence(
                "temporary TrgOps readback",
                triggerReadback.IsSuccess,
                triggerComparison,
                RenderValue(triggerReadback.Value),
                triggerReadback.Message));
            if (triggerComparison?.IsSemanticMatch != true)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary TrgOps significant-bit readback was not equal.").ConfigureAwait(false);

            var optionalWrite = await WriteReportAttributeAsync(reportControl, "OptFlds", desiredOptionalFields, cancellationToken).ConfigureAwait(false);
            lease.OptionalFieldsTouched = true;
            writes.Add(optionalWrite);
            if (!optionalWrite.IsSuccess)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary OptFlds write failed.").ConfigureAwait(false);

            var optionalReadback = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", cancellationToken).ConfigureAwait(false);
            var optionalComparison = optionalReadback.IsSuccess && optionalReadback.Value is not null
                ? MmsReportControlFieldCodec.CompareOptionalFields(desiredOptionalFields, optionalReadback.Value)
                : null;
            evidence.Add(ComparisonEvidence(
                "temporary OptFlds readback",
                optionalReadback.IsSuccess,
                optionalComparison,
                RenderValue(optionalReadback.Value),
                optionalReadback.Message));
            if (optionalComparison?.IsSemanticMatch != true)
                return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary OptFlds significant-bit readback was not equal.").ConfigureAwait(false);

            reportControl.TriggerOptions = triggerOptions;
            reportControl.OptionalFields = optionalFields;

            // Successful direct write + significant-bit readback proves these child
            // attributes usable in this association even if discovery omitted them.
            if (!reportControl.Attributes.Contains("TrgOps", StringComparer.OrdinalIgnoreCase))
                reportControl.Attributes.Add("TrgOps");
            if (!reportControl.Attributes.Contains("OptFlds", StringComparer.OrdinalIgnoreCase))
                reportControl.Attributes.Add("OptFlds");

            return new MmsDynamicRcbCommissioningFieldPrepareResult
            {
                IsSuccess = true,
                CleanupSucceeded = false,
                Lease = lease,
                WriteSteps = writes,
                Evidence = evidence,
                Message = "Temporary G2.4 TrgOps/OptFlds lease established with IEC significant-bit readback. Raw BER evidence is retained; caller must restore the lease after the proof attempt."
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            evidence.Add($"temporary report-field exception: {ex.GetType().Name}: {ex.Message}");
            return await FailPrepareAndRollbackAsync(lease, writes, evidence, "Temporary report-field preparation failed with a protocol/transport exception.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Restores the original TrgOps/OptFlds MMS values captured by preparation.
    /// The original raw values are written back; success requires the significant IEC
    /// bits to match on readback. Raw padding differences are preserved in evidence.
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

        // Restore OptFlds first, then TrgOps. RptEna is expected false before this call.
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
            var comparison = readback.IsSuccess && readback.Value is not null
                ? MmsReportControlFieldCodec.CompareOptionalFields(lease.OriginalOptionalFields, readback.Value)
                : null;
            evidence.Add(ComparisonEvidence(
                "restore OptFlds readback",
                readback.IsSuccess,
                comparison,
                RenderValue(readback.Value),
                $"write={restoreOptional.IsSuccess}; expected={lease.OriginalOptionalFieldsText}; {readback.Message}"));
            success &= comparison?.IsSemanticMatch == true;
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
            var comparison = readback.IsSuccess && readback.Value is not null
                ? MmsReportControlFieldCodec.CompareTriggerOptions(lease.OriginalTriggerOptions, readback.Value)
                : null;
            evidence.Add(ComparisonEvidence(
                "restore TrgOps readback",
                readback.IsSuccess,
                comparison,
                RenderValue(readback.Value),
                $"write={restoreTrigger.IsSuccess}; expected={lease.OriginalTriggerOptionsText}; {readback.Message}"));
            success &= comparison?.IsSemanticMatch == true;
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
                ? "Temporary G2.4 TrgOps/OptFlds lease restored with IEC significant-bit readback; raw BER evidence retained."
                : "Temporary G2.4 TrgOps/OptFlds restore was not fully proven; treat the RCB as requiring fresh read-only inspection before retry."
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

    private static string ComparisonEvidence(
        string label,
        bool readSuccess,
        MmsReportBitStringComparison? comparison,
        string actualRaw,
        string message)
    {
        if (comparison is null)
            return $"{label}: read={readSuccess}; semanticExact=False; rawExact=False; paddingOnlyDiff=False; value={actualRaw}; result={message}";

        return $"{label}: read={readSuccess}; semanticExact={comparison.IsSemanticMatch}; rawExact={comparison.IsRawExact}; paddingOnlyDiff={comparison.PaddingOnlyDifference}; expected={comparison.ExpectedHex}; actual={comparison.ActualHex}; result={message}";
    }

    private static string RenderValue(MmsDataValue? value)
        => value is null ? "-" : MmsDataCodec.ToDisplayString(value);
}
