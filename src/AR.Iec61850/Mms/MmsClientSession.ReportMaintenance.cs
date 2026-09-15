namespace AR.Iec61850.Mms;

public sealed class MmsBufferedReportPurgeResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public MmsReportRcbSnapshot? Before { get; init; }
    public MmsReportAttributeWriteStep? Write { get; init; }
    public MmsReportRcbSnapshot? After { get; init; }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Explicit, fail-closed BRCB buffer purge. This never runs as part of ordinary
    /// monitor start/stop/reconnect handling. The caller must explicitly acknowledge the
    /// destructive operation, the RCB must advertise PurgeBuf, and readback must show
    /// RptEna=false before the write is attempted.
    /// </summary>
    public async Task<MmsBufferedReportPurgeResult> PurgeBufferedReportAsync(
        MmsReportControlCandidate reportControl,
        bool acknowledgeBufferedEventLoss,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(reportControl);

        if (!acknowledgeBufferedEventLoss)
        {
            return new MmsBufferedReportPurgeResult
            {
                IsSuccess = false,
                Message = "BRCB PurgeBuf was not attempted because buffered-event loss was not explicitly acknowledged."
            };
        }

        if (!reportControl.Buffered)
        {
            return new MmsBufferedReportPurgeResult
            {
                IsSuccess = false,
                Message = "PurgeBuf applies only to buffered report control blocks."
            };
        }

        if (!reportControl.Attributes.Contains("PurgeBuf", StringComparer.OrdinalIgnoreCase))
        {
            return new MmsBufferedReportPurgeResult
            {
                IsSuccess = false,
                Message = "BRCB does not advertise a PurgeBuf attribute; no write was attempted."
            };
        }

        var before = await CaptureReportControlSnapshotAsync(reportControl, "before-purgebuf", cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || ParseExplicitBool(before.EnabledState) != false)
        {
            return new MmsBufferedReportPurgeResult
            {
                IsSuccess = false,
                Before = before,
                Message = before.IsSuccess
                    ? "PurgeBuf requires explicit RptEna=false readback; enabled/unknown RCB state was left untouched."
                    : $"PurgeBuf requires a successful pre-write RCB snapshot: {before.Message}"
            };
        }

        var write = await WriteReportAttributeAsync(
            reportControl,
            "PurgeBuf",
            MmsDataValue.Boolean(true),
            cancellationToken).ConfigureAwait(false);
        if (!write.IsSuccess)
        {
            return new MmsBufferedReportPurgeResult
            {
                IsSuccess = false,
                Before = before,
                Write = write,
                Message = $"PurgeBuf write failed: {write.Message}"
            };
        }

        var after = await CaptureReportControlSnapshotAsync(reportControl, "after-purgebuf", cancellationToken).ConfigureAwait(false);
        return new MmsBufferedReportPurgeResult
        {
            IsSuccess = write.IsSuccess && after.IsSuccess,
            Before = before,
            Write = write,
            After = after,
            Message = after.IsSuccess
                ? "BRCB PurgeBuf write completed and post-write RCB readback succeeded."
                : $"BRCB PurgeBuf write succeeded but post-write RCB readback failed: {after.Message}"
        };
    }

    private static bool? ParseExplicitBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text is "1")
            return true;
        if (text is "0")
            return false;
        return null;
    }
}
