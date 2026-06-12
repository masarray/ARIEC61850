using System.Numerics;

namespace AR.Iec61850.Mms;

public sealed class MmsReportSessionDiagnostics
{
    public int ReportCount { get; init; }
    public int HeaderDecodedCount { get; init; }
    public int MappingFailureCount { get; init; }
    public int PartialMappingFailureCount { get; init; }
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
    public int SequenceResetCount { get; init; }
    public int SequenceRegressionCount { get; init; }
    public int EntryIdGapCount { get; init; }
    public int EntryIdRegressionCount { get; init; }
    public IReadOnlyDictionary<string, int> ReasonCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public string OverallStatus
    {
        get
        {
            if (WriteFailureCount > 0 || MappingFailureCount > 0 || PollReadFailureCount > 0)
                return "FAIL";

            if (BufferOverflowObserved || PartialMappingFailureCount > 0 || DuplicateReportKeyCount > 0 ||
                SequenceGapCount > 0 || SequenceResetCount > 0 || SequenceRegressionCount > 0 || EntryIdGapCount > 0 || EntryIdRegressionCount > 0)
                return "PASS_WITH_WARNING";

            return "PASS";
        }
    }

    public IReadOnlyList<string> WarningMessages
    {
        get
        {
            var warnings = new List<string>();
            if (BufferOverflowObserved)
                warnings.Add("BRCB buffer-overflow flag was observed. Treat the session as usable evidence with a warning; check EntryID continuity and relay buffered-report history.");
            if (PartialMappingFailureCount > 0)
                warnings.Add($"{PartialMappingFailureCount} report(s) had fewer mapped values than included DataSet indexes.");
            if (DuplicateReportKeyCount > 0)
                warnings.Add($"{DuplicateReportKeyCount} duplicate report key(s) were observed; inspect whether these are retransmissions or true duplicates.");
            if (SequenceGapCount > 0)
                warnings.Add($"{SequenceGapCount} sequence gap(s) were observed per report stream.");
            if (SequenceResetCount > 0)
                warnings.Add($"{SequenceResetCount} sequence reset-to-zero event(s) were observed per report stream. This is usually a report burst/GI or vendor sequence reset warning, not a hard failure by itself.");
            if (SequenceRegressionCount > 0)
                warnings.Add($"{SequenceRegressionCount} sequence regression(s) were observed per report stream after excluding reset-to-zero events.");
            if (EntryIdGapCount > 0)
                warnings.Add($"{EntryIdGapCount} numeric EntryID gap(s) were observed. EntryID is treated as opaque by default; numeric gap is a heuristic warning, not a hard failure.");
            if (EntryIdRegressionCount > 0)
                warnings.Add($"{EntryIdRegressionCount} numeric EntryID regression/repeat(s) were observed. EntryID is treated as opaque by default; inspect raw reports before declaring data loss.");
            return warnings;
        }
    }

    public string Summary =>
        $"diagnostics={OverallStatus}, reports={ReportCount}, values={ValueCount}, mappedFailures={MappingFailureCount}, partialMappings={PartialMappingFailureCount}, " +
        $"pollReads={PollReadSuccessCount}/{PollReadCount}, writeFailures={WriteFailureCount}, " +
        $"seqGaps={SequenceGapCount}, seqResets={SequenceResetCount}, seqRegressions={SequenceRegressionCount}, " +
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
        var sequenceResets = 0;
        var sequenceRegressions = 0;
        foreach (var stream in reports
                     .Where(r => r.Header.SequenceNumber.HasValue)
                     .GroupBy(BuildReportStreamKey, StringComparer.OrdinalIgnoreCase))
        {
            ulong? previousSequence = null;
            foreach (var sequence in stream.Select(x => x.Header.SequenceNumber!.Value))
            {
                if (previousSequence.HasValue)
                {
                    if (sequence > previousSequence.Value + 1)
                        sequenceGaps++;
                    else if (sequence < previousSequence.Value)
                    {
                        if (sequence == 0)
                            sequenceResets++;
                        else
                            sequenceRegressions++;
                    }
                }

                previousSequence = sequence;
            }
        }

        var entryIdGaps = 0;
        var entryIdRegressions = 0;
        foreach (var stream in reports
                     .Where(r => !string.IsNullOrWhiteSpace(r.Header.EntryIdHex))
                     .GroupBy(BuildReportStreamKey, StringComparer.OrdinalIgnoreCase))
        {
            BigInteger? previousEntryId = null;
            foreach (var item in stream
                         .Select(x => x.Header.EntryIdHex)
                         .Select(x => new { Hex = x, Parsed = TryParseHex(x, out var parsed), Value = parsed })
                         .Where(x => x.Parsed))
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
        }

        var mappingFailures = 0;
        var partialMappingFailures = 0;
        foreach (var report in reports)
        {
            if (!report.InclusionBitstringItemIndex.HasValue || report.IncludedDataSetIndexes.Count == 0)
            {
                mappingFailures++;
                continue;
            }

            if (report.Values.Count == 0)
            {
                mappingFailures++;
                continue;
            }

            if (report.Values.Count < report.IncludedDataSetIndexes.Count)
                partialMappingFailures++;
        }

        return new MmsReportSessionDiagnostics
        {
            ReportCount = reports.Count,
            HeaderDecodedCount = reports.Count(x => x.Header.HasAny),
            MappingFailureCount = mappingFailures,
            PartialMappingFailureCount = partialMappingFailures,
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
            SequenceResetCount = sequenceResets,
            SequenceRegressionCount = sequenceRegressions,
            EntryIdGapCount = entryIdGaps,
            EntryIdRegressionCount = entryIdRegressions,
            ReasonCounts = reasonCounts
        };
    }

    private static string BuildReportKey(MmsReportFrame report)
    {
        var streamKey = BuildReportStreamKey(report);
        if (!string.IsNullOrWhiteSpace(report.Header.EntryIdHex))
            return $"{streamKey}|entry={report.Header.EntryIdHex}";

        if (report.Header.SequenceNumber.HasValue)
            return $"{streamKey}|sq={report.Header.SequenceNumber.Value}|time={report.Header.TimeOfEntry}";

        return string.Empty;
    }

    private static string BuildReportStreamKey(MmsReportFrame report)
    {
        var reportId = string.IsNullOrWhiteSpace(report.Header.ReportId) ? "-" : report.Header.ReportId.Trim();
        var dataSet = string.IsNullOrWhiteSpace(report.Header.DataSetReference) ? "-" : report.Header.DataSetReference.Trim();
        var confRev = report.Header.ConfRev?.ToString() ?? "-";
        return $"{reportId}|ds={dataSet}|conf={confRev}";
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
