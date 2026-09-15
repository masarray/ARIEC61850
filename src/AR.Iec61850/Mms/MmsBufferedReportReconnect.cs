namespace AR.Iec61850.Mms;

public enum MmsBufferedReportReconnectStrategy
{
    NotApplicable,
    ReattachWithoutResumeCursor,
    ResumeAfterEntryId,
    BlockedInvalidCursor
}

public sealed class MmsBufferedReportReconnectPlan
{
    public MmsBufferedReportReconnectStrategy Strategy { get; init; }
    public string EntryIdHex { get; init; } = string.Empty;
    public byte[] EntryIdBytes { get; init; } = Array.Empty<byte>();
    public ulong? LastObservedSequenceNumber { get; init; }
    public bool SequenceNumberIsResumeAuthority { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Builds a conservative reconnect plan from captured lifecycle evidence. EntryID is the
/// durable BRCB replay cursor when it was actually present in report evidence. SqNum is
/// retained only as a diagnostic and is never promoted to reconnect authority because it
/// may reset or wrap between attachments.
/// </summary>
public static class MmsBufferedReportReconnectPlanner
{
    public static MmsBufferedReportReconnectPlan Build(
        MmsReportControlCandidate reportControl,
        MmsReportLifecycleSnapshot lifecycle)
    {
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (!reportControl.Buffered || lifecycle.Kind != MmsReportControlKind.Brcb)
        {
            return new MmsBufferedReportReconnectPlan
            {
                Strategy = MmsBufferedReportReconnectStrategy.NotApplicable,
                LastObservedSequenceNumber = lifecycle.Replay.LastSequenceNumber,
                SequenceNumberIsResumeAuthority = false,
                Message = "EntryID replay resume applies only to BRCB lifecycle evidence."
            };
        }

        var entryIdHex = NormalizeHex(lifecycle.Replay.LastEntryIdHex);
        if (string.IsNullOrWhiteSpace(entryIdHex))
        {
            return new MmsBufferedReportReconnectPlan
            {
                Strategy = MmsBufferedReportReconnectStrategy.ReattachWithoutResumeCursor,
                LastObservedSequenceNumber = lifecycle.Replay.LastSequenceNumber,
                SequenceNumberIsResumeAuthority = false,
                Message = "No EntryID was observed. Reattach without an EntryID cursor and use replay duplicate diagnostics; SqNum is not a durable resume cursor."
            };
        }

        if (!TryDecodeHex(entryIdHex, out var entryIdBytes))
        {
            return new MmsBufferedReportReconnectPlan
            {
                Strategy = MmsBufferedReportReconnectStrategy.BlockedInvalidCursor,
                EntryIdHex = entryIdHex,
                LastObservedSequenceNumber = lifecycle.Replay.LastSequenceNumber,
                SequenceNumberIsResumeAuthority = false,
                Message = "Observed EntryID is not a valid even-length hexadecimal octet string; automatic resume positioning is blocked."
            };
        }

        if (!reportControl.Attributes.Contains("EntryID", StringComparer.OrdinalIgnoreCase))
        {
            return new MmsBufferedReportReconnectPlan
            {
                Strategy = MmsBufferedReportReconnectStrategy.ReattachWithoutResumeCursor,
                EntryIdHex = entryIdHex,
                EntryIdBytes = entryIdBytes,
                LastObservedSequenceNumber = lifecycle.Replay.LastSequenceNumber,
                SequenceNumberIsResumeAuthority = false,
                Message = "A valid EntryID was observed, but the discovered BRCB does not expose an EntryID attribute. Reattach without writing a resume cursor and deduplicate replay by observed EntryID."
            };
        }

        return new MmsBufferedReportReconnectPlan
        {
            Strategy = MmsBufferedReportReconnectStrategy.ResumeAfterEntryId,
            EntryIdHex = entryIdHex,
            EntryIdBytes = entryIdBytes,
            LastObservedSequenceNumber = lifecycle.Replay.LastSequenceNumber,
            SequenceNumberIsResumeAuthority = false,
            Message = "A validated EntryID resume cursor is available. Apply it only through an explicit disabled-BRCB reconnect workflow before RptEna=true; do not substitute SqNum for EntryID."
        };
    }

    private static string NormalizeHex(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private static bool TryDecodeHex(string hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (hex.Length == 0 || hex.Length % 2 != 0)
            return false;

        try
        {
            bytes = Convert.FromHexString(hex);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
