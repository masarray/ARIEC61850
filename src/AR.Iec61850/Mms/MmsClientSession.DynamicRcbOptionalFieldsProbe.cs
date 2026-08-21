namespace AR.Iec61850.Mms;

/// <summary>
/// P1 commissioning-only proof that one free URCB accepts the requested OptFlds
/// significant bits and can be restored to its original significant value.
/// This probe never touches TrgOps, DatSet, Resv, RptEna, GI, or any DataSet service.
/// </summary>
public sealed class MmsDynamicRcbOptionalFieldsProbeResult
{
    public bool IsSuccess { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string RcbReference { get; init; } = string.Empty;
    public string RequestedOptionalFields { get; init; } = string.Empty;
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
    public async Task<MmsDynamicRcbOptionalFieldsProbeResult> ProbeDynamicRcbOptionalFieldsAsync(
        MmsReportControlCandidate reportControl,
        string optionalFields,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionalFields);

        var writes = new List<MmsReportAttributeWriteStep>();
        var evidence = new List<string>();

        if (reportControl.Buffered)
        {
            return new MmsDynamicRcbOptionalFieldsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "P1 OptFlds micro-probe is restricted to one URCB. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedOptionalFields = optionalFields
            };
        }

        if (!MmsReportControlFieldCodec.TryEncodeOptionalFields(optionalFields, out var requested))
        {
            return new MmsDynamicRcbOptionalFieldsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "Requested OptFlds could not be encoded. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedOptionalFields = optionalFields
            };
        }

        var originalRead = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", cancellationToken).ConfigureAwait(false);
        if (!originalRead.IsSuccess || originalRead.Value?.Kind != MmsDataKind.BitString)
        {
            evidence.Add($"P1 original OptFlds read: success={originalRead.IsSuccess}; value={RenderValue(originalRead.Value)}; result={originalRead.Message}");
            return new MmsDynamicRcbOptionalFieldsProbeResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Message = "P1 could not capture original OptFlds as an MMS BitString. No field was changed.",
                RcbReference = reportControl.Reference,
                RequestedOptionalFields = optionalFields,
                RequestedRaw = MmsDataCodec.ToDisplayString(requested),
                Evidence = evidence
            };
        }

        var original = originalRead.Value;
        var originalRaw = MmsDataCodec.ToDisplayString(original);
        var requestedRaw = MmsDataCodec.ToDisplayString(requested);
        evidence.Add($"P1 OptFlds capture: rcb={reportControl.Reference}; original={originalRaw}; requested={requestedRaw}; options={optionalFields}");

        MmsReportBitStringComparison? requestedComparison = null;
        MmsReportBitStringComparison? restoreComparison = null;
        string readbackRaw = string.Empty;
        string restoreReadbackRaw = string.Empty;
        var writeAccepted = false;
        var cleanupSucceeded = false;

        try
        {
            var write = await WriteReportAttributeAsync(reportControl, "OptFlds", requested, cancellationToken).ConfigureAwait(false);
            writes.Add(write);
            writeAccepted = write.IsSuccess;
            evidence.Add($"P1 OptFlds write: attempted={write.Attempted}; success={write.IsSuccess}; result={write.Message}");

            if (write.IsSuccess)
            {
                var readback = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", cancellationToken).ConfigureAwait(false);
                readbackRaw = RenderValue(readback.Value);
                if (readback.IsSuccess && readback.Value is not null)
                    requestedComparison = MmsReportControlFieldCodec.CompareOptionalFields(requested, readback.Value);

                evidence.Add(OptionalFieldsProbeComparisonEvidence(
                    "P1 OptFlds requested readback",
                    readback.IsSuccess,
                    requestedComparison,
                    readbackRaw,
                    readback.Message));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            evidence.Add($"P1 OptFlds write/readback exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                var restoreWrite = await TryWriteReportAttributeForCleanupAsync(
                    reportControl,
                    "OptFlds",
                    original,
                    CancellationToken.None).ConfigureAwait(false);
                writes.Add(restoreWrite);

                var restoreRead = await ReadReportControlFieldValueAsync(reportControl, "OptFlds", CancellationToken.None).ConfigureAwait(false);
                restoreReadbackRaw = RenderValue(restoreRead.Value);
                if (restoreRead.IsSuccess && restoreRead.Value is not null)
                    restoreComparison = MmsReportControlFieldCodec.CompareOptionalFields(original, restoreRead.Value);

                cleanupSucceeded = restoreWrite.IsSuccess &&
                                   restoreRead.IsSuccess &&
                                   restoreComparison?.IsSemanticMatch == true;

                evidence.Add(OptionalFieldsProbeComparisonEvidence(
                    "P1 OptFlds restore readback",
                    restoreRead.IsSuccess,
                    restoreComparison,
                    restoreReadbackRaw,
                    $"write={restoreWrite.IsSuccess}; {restoreRead.Message}"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                evidence.Add($"P1 OptFlds restore exception: {ex.GetType().Name}: {ex.Message}");
                cleanupSucceeded = false;
            }
        }

        var requestedProven = writeAccepted && requestedComparison?.IsSemanticMatch == true;
        var success = requestedProven && cleanupSucceeded;
        return new MmsDynamicRcbOptionalFieldsProbeResult
        {
            IsSuccess = success,
            CleanupSucceeded = cleanupSucceeded,
            Message = success
                ? "P1 OptFlds micro-probe passed: requested significant optional-field bits were read back and the original significant OptFlds value was restored."
                : cleanupSucceeded
                    ? "P1 OptFlds micro-probe did not prove the requested significant optional-field bits, but original OptFlds restore was proven."
                    : "P1 OptFlds micro-probe did not prove safe completion because original OptFlds restore was not proven.",
            RcbReference = reportControl.Reference,
            RequestedOptionalFields = optionalFields,
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

    private static string OptionalFieldsProbeComparisonEvidence(
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
