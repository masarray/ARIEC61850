namespace AR.Iec61850.Mms;

/// <summary>
/// P0 commissioning-only proof that one free URCB accepts the requested TrgOps
/// significant bits and can be restored to its original significant value.
/// This probe never touches DatSet, OptFlds, Resv, RptEna, GI, or any DataSet service.
/// </summary>
public sealed class MmsDynamicRcbTriggerOptionsProbeResult
{
    public bool IsSuccess { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string RcbReference { get; init; } = string.Empty;
    public string RequestedTriggerOptions { get; init; } = string.Empty;
    public string OriginalRaw { get; init; } = string.Empty;
    public string RequestedRaw { get; init; } = string.Empty;
    public string ReadbackRaw { get; init; } = string.Empty;
    public string RestoreReadbackRaw { get; init; } = string.Empty;
    public MmsReportBitStringComparison? RequestedComparison { get; init; }
    public MmsReportBitStringComparison? RestoreComparison { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public sealed partial class MmsClientSession
{
    public async Task<MmsDynamicRcbTriggerOptionsProbeResult> ProbeDynamicRcbTriggerOptionsAsync(
        MmsReportControlCandidate reportControl,
        string triggerOptions,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerOptions);

        var writes = new List<MmsReportAttributeWriteStep>();
        var evidence = new List<string>();

        if (reportControl.Buffered)
        {
            return new MmsDynamicRcbTriggerOptionsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "P0 TrgOps micro-probe is restricted to one URCB. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedTriggerOptions = triggerOptions
            };
        }

        if (!MmsReportControlFieldCodec.TryEncodeTriggerOptions(triggerOptions, out var requested))
        {
            return new MmsDynamicRcbTriggerOptionsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Requested TrgOps could not be encoded with the IEC reserved-bit mapping. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedTriggerOptions = triggerOptions
            };
        }

        var originalRead = await ReadReportControlFieldValueAsync(reportControl, "TrgOps", cancellationToken).ConfigureAwait(false);
        if (!originalRead.IsSuccess || originalRead.Value?.Kind != MmsDataKind.BitString)
        {
            evidence.Add($"P0 original TrgOps read: success={originalRead.IsSuccess}; value={RenderValue(originalRead.Value)}; result={originalRead.Message}");
            return new MmsDynamicRcbTriggerOptionsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "P0 could not capture original TrgOps as an MMS BitString. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedTriggerOptions = triggerOptions,
                RequestedRaw = MmsDataCodec.ToDisplayString(requested),
                Evidence = evidence
            };
        }

        var original = originalRead.Value;
        var originalRaw = MmsDataCodec.ToDisplayString(original);
        var requestedRaw = MmsDataCodec.ToDisplayString(requested);
        evidence.Add($"P0 TrgOps capture: rcb={reportControl.Reference}; original={originalRaw}; requested={requestedRaw}; options={triggerOptions}");

        MmsReportBitStringComparison? requestedComparison = null;
        MmsReportBitStringComparison? restoreComparison = null;
        string readbackRaw = string.Empty;
        string restoreReadbackRaw = string.Empty;
        var writeAccepted = false;
        var cleanupSucceeded = false;

        try
        {
            var write = await WriteReportAttributeAsync(reportControl, "TrgOps", requested, cancellationToken).ConfigureAwait(false);
            writes.Add(write);
            writeAccepted = write.IsSuccess;
            evidence.Add($"P0 TrgOps write: attempted={write.Attempted}; success={write.IsSuccess}; result={write.Message}");

            if (write.IsSuccess)
            {
                var readback = await ReadReportControlFieldValueAsync(reportControl, "TrgOps", cancellationToken).ConfigureAwait(false);
                readbackRaw = RenderValue(readback.Value);
                if (readback.IsSuccess && readback.Value is not null)
                    requestedComparison = MmsReportControlFieldCodec.CompareTriggerOptions(requested, readback.Value);

                evidence.Add(ComparisonEvidence(
                    "P0 TrgOps requested readback",
                    readback.IsSuccess,
                    requestedComparison,
                    readbackRaw,
                    readback.Message));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            evidence.Add($"P0 TrgOps write/readback exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                var restoreWrite = await TryWriteReportAttributeForCleanupAsync(
                    reportControl,
                    "TrgOps",
                    original,
                    CancellationToken.None).ConfigureAwait(false);
                writes.Add(restoreWrite);

                var restoreRead = await ReadReportControlFieldValueAsync(reportControl, "TrgOps", CancellationToken.None).ConfigureAwait(false);
                restoreReadbackRaw = RenderValue(restoreRead.Value);
                if (restoreRead.IsSuccess && restoreRead.Value is not null)
                    restoreComparison = MmsReportControlFieldCodec.CompareTriggerOptions(original, restoreRead.Value);

                cleanupSucceeded = restoreWrite.IsSuccess &&
                                   restoreRead.IsSuccess &&
                                   restoreComparison?.IsSemanticMatch == true;

                evidence.Add(ComparisonEvidence(
                    "P0 TrgOps restore readback",
                    restoreRead.IsSuccess,
                    restoreComparison,
                    restoreReadbackRaw,
                    $"write={restoreWrite.IsSuccess}; {restoreRead.Message}"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                evidence.Add($"P0 TrgOps restore exception: {ex.GetType().Name}: {ex.Message}");
                cleanupSucceeded = false;
            }
        }

        var requestedProven = writeAccepted && requestedComparison?.IsSemanticMatch == true;
        var success = requestedProven && cleanupSucceeded;
        return new MmsDynamicRcbTriggerOptionsProbeResult
        {
            IsSuccess = success,
            CleanupSucceeded = cleanupSucceeded,
            Message = success
                ? "P0 TrgOps micro-probe passed: requested significant trigger bits were read back and the original significant TrgOps value was restored."
                : cleanupSucceeded
                    ? "P0 TrgOps micro-probe did not prove the requested significant trigger bits, but original TrgOps restore was proven."
                    : "P0 TrgOps micro-probe did not prove safe completion because original TrgOps restore was not proven.",
            RcbReference = reportControl.Reference,
            RequestedTriggerOptions = triggerOptions,
            OriginalRaw = originalRaw,
            RequestedRaw = requestedRaw,
            ReadbackRaw = readbackRaw,
            RestoreReadbackRaw = restoreReadbackRaw,
            RequestedComparison = requestedComparison,
            RestoreComparison = restoreComparison,
            WriteSteps = writes,
            Evidence = evidence
        };
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
}
