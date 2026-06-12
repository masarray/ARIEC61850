using System.Numerics;

namespace AR.Iec61850.Mms;

public sealed class MmsReportSessionDiagnostics
{
    public int ReportCount { get; init; }
    public int HeaderDecodedCount { get; init; }
    public int MappingFailureCount { get; init; }
    public int ValueCount { get; init; }
    public int WriteStepCount { get; init; }
    public int WriteFailureCount { get; init; }
    public int PollReadCount { get; init; }
    public int PollReadSuccessCount { get; init; }
    public int PollReadFailureCount { get; init; }
    public bool BufferOverflowObserved { get; init; }
    public string FirstEntryIdHex { get; init; } = string.Empty;
    public string LastEntryIdHex { get; init; } = string.Empty;
    public int DuplicateReportKeyCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int SequenceRegressionCount { get; init; }
    public int EntryIdGapCount { get; init; }
    public int EntryIdRegressionCount { get; init; }
    public IReadOnlyDictionary<string, int> ReasonCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public string Summary =>
        $"reports={ReportCount}, values={ValueCount}, mappedFailures={MappingFailureCount}, " +
        $"pollReads={PollReadSuccessCount}/{PollReadCount}, writeFailures={WriteFailureCount}, " +
        $"seqGaps={SequenceGapCount}, seqRegressions={SequenceRegressionCount}, " +
        $"entryIdGaps={EntryIdGapCount}, entryIdRegressions={EntryIdRegressionCount}, " +
        $"duplicates={DuplicateReportKeyCount}, bufOvfl={BufferOverflowObserved.ToString().ToLowerInvariant()}";

    public static MmsReportSessionDiagnostics Analyze(
        IReadOnlyList<MmsReportFrame> reports,
        IReadOnlyList<MmsReportPollRead>? pollReads = null,
        IReadOnlyList<MmsReportAttributeWriteStep>? writeSteps = null)
    {
        reports ??= Array.Empty<MmsReportFrame>();
        pollReads ??= Array.Empty<MmsReportPollRead>();
        writeSteps ??= Array.Empty<MmsReportAttributeWriteStep>();

        var reasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var reason in reports.SelectMany(r => r.Values).SelectMany(v => v.ReasonForInclusion))
            reasonCounts[reason] = reasonCounts.TryGetValue(reason, out var count) ? count + 1 : 1;

        var duplicateKeys = 0;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in reports)
        {
            var key = BuildReportKey(report);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!seenKeys.Add(key))
                duplicateKeys++;
        }

        var sequenceGaps = 0;
        var sequenceRegressions = 0;
        ulong? previousSequence = null;
        foreach (var sequence in reports.Select(x => x.Header.SequenceNumber).Where(x => x.HasValue).Select(x => x!.Value))
        {
            if (previousSequence.HasValue)
            {
                if (sequence > previousSequence.Value + 1)
                    sequenceGaps++;
                else if (sequence < previousSequence.Value)
                    sequenceRegressions++;
            }

            previousSequence = sequence;
        }

        var parsedEntryIds = reports
            .Select(x => x.Header.EntryIdHex)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new { Hex = x, Parsed = TryParseHex(x, out var parsed), Value = parsed })
            .Where(x => x.Parsed)
            .ToArray();

        var entryIdGaps = 0;
        var entryIdRegressions = 0;
        BigInteger? previousEntryId = null;
        foreach (var item in parsedEntryIds)
        {
            if (previousEntryId.HasValue)
            {
                if (item.Value > previousEntryId.Value + BigInteger.One)
                    entryIdGaps++;
                else if (item.Value <= previousEntryId.Value)
                    entryIdRegressions++;
            }

            previousEntryId = item.Value;
        }

        return new MmsReportSessionDiagnostics
        {
            ReportCount = reports.Count,
            HeaderDecodedCount = reports.Count(x => x.Header.HasAny),
            MappingFailureCount = reports.Count(x => !x.InclusionBitstringItemIndex.HasValue || x.Values.Count == 0),
            ValueCount = reports.Sum(x => x.Values.Count),
            WriteStepCount = writeSteps.Count,
            WriteFailureCount = writeSteps.Count(x => !x.IsSuccess),
            PollReadCount = pollReads.Count,
            PollReadSuccessCount = pollReads.Count(x => x.IsSuccess),
            PollReadFailureCount = pollReads.Count(x => !x.IsSuccess),
            BufferOverflowObserved = reports.Any(x => x.Header.BufferOverflow == true),
            FirstEntryIdHex = reports.Select(x => x.Header.EntryIdHex).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            LastEntryIdHex = reports.Select(x => x.Header.EntryIdHex).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            DuplicateReportKeyCount = duplicateKeys,
            SequenceGapCount = sequenceGaps,
            SequenceRegressionCount = sequenceRegressions,
            EntryIdGapCount = entryIdGaps,
            EntryIdRegressionCount = entryIdRegressions,
            ReasonCounts = reasonCounts
        };
    }

    private static string BuildReportKey(MmsReportFrame report)
    {
        var reportId = report.Header.ReportId;
        if (!string.IsNullOrWhiteSpace(report.Header.EntryIdHex))
            return $"{reportId}|entry={report.Header.EntryIdHex}";

        if (report.Header.SequenceNumber.HasValue)
            return $"{reportId}|sq={report.Header.SequenceNumber.Value}|time={report.Header.TimeOfEntry}";

        return string.Empty;
    }

    private static bool TryParseHex(string value, out BigInteger parsed)
    {
        parsed = BigInteger.Zero;
        var text = value.Trim();
        if (text.Length == 0)
            return false;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (text.Length == 0 || text.Any(c => !Uri.IsHexDigit(c)))
            return false;

        var bytes = Convert.FromHexString(text.Length % 2 == 0 ? text : "0" + text);
        parsed = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return true;
    }
}
