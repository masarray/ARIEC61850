using System.Globalization;

namespace AR.Iec61850.Mms;

public sealed class MmsReportIedTimestampEvidence
{
    public string Reference { get; init; } = string.Empty;
    public string Source { get; init; } = "IED timestamp";
    public Iec61850UtcTimeEvidence Timestamp { get; init; } = new();
}

/// <summary>
/// Keeps the three report timing concepts separate for engineering evidence:
/// the IED data timestamp carried by a reported value, the report TimeOfEntry,
/// and the local client's receive time.
/// </summary>
public sealed class MmsReportTimestampEvidence
{
    public IReadOnlyList<MmsReportIedTimestampEvidence> IedTimestamps { get; init; } = Array.Empty<MmsReportIedTimestampEvidence>();
    public string ReportTimeOfEntryDisplay { get; init; } = string.Empty;
    public Iec61850UtcTimeEvidence ReportTimeOfEntry { get; init; } = new();
    public DateTimeOffset ReceivedAt { get; init; }
    public string ReceivedAtUtc { get; init; } = string.Empty;
    public string ReceivedAtLocal { get; init; } = string.Empty;

    public bool HasReportTimeOfEntryWireEvidence => ReportTimeOfEntry.HasWireProvenance;

    public string Summary =>
        $"IED timestamps={IedTimestamps.Count}; TimeOfEntry={(string.IsNullOrWhiteSpace(ReportTimeOfEntryDisplay) ? "-" : ReportTimeOfEntryDisplay)}; ReceivedAt={ReceivedAtUtc}";

    /// <summary>
    /// Builds report timestamp evidence. Supplying the original decoded
    /// InformationReport allows exact TimeOfEntry wire provenance to be linked
    /// back to the mapped frame; without it, the existing display value remains
    /// visible but is not promoted to raw-wire evidence.
    /// </summary>
    public static MmsReportTimestampEvidence FromFrame(MmsReportFrame frame, MmsInformationReport? decodedReport = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var iedTimestamps = new List<MmsReportIedTimestampEvidence>();
        foreach (var reportValue in frame.Values)
        {
            var timestamp = Iec61850UtcTimeEvidence.Decode(reportValue.Value);
            if (!timestamp.IsDecoded)
                continue;

            var reference = reportValue.MemberReference;
            iedTimestamps.Add(new MmsReportIedTimestampEvidence
            {
                Reference = reference,
                Source = IsDirectTimestampReference(reference) ? "IED .t" : "IED embedded timestamp",
                Timestamp = timestamp
            });
        }

        var timeOfEntryEvidence = FindTimeOfEntryEvidence(frame, decodedReport);
        return new MmsReportTimestampEvidence
        {
            IedTimestamps = iedTimestamps,
            ReportTimeOfEntryDisplay = frame.Header.TimeOfEntry,
            ReportTimeOfEntry = timeOfEntryEvidence,
            ReceivedAt = frame.ReceivedAt,
            ReceivedAtUtc = FormatUtc(frame.ReceivedAt),
            ReceivedAtLocal = FormatLocal(frame.ReceivedAt)
        };
    }

    private static Iec61850UtcTimeEvidence FindTimeOfEntryEvidence(MmsReportFrame frame, MmsInformationReport? decodedReport)
    {
        if (decodedReport == null || string.IsNullOrWhiteSpace(frame.Header.TimeOfEntry))
            return new Iec61850UtcTimeEvidence();

        var limit = frame.InclusionBitstringItemIndex is { } inclusionIndex && inclusionIndex >= 0
            ? Math.Min(inclusionIndex, decodedReport.Items.Count)
            : decodedReport.Items.Count;

        Iec61850UtcTimeEvidence? firstUtcCandidate = null;
        for (var index = 0; index < limit; index++)
        {
            var value = decodedReport.Items[index].Value;
            if (value?.Kind != MmsDataKind.UtcTime)
                continue;

            var evidence = Iec61850UtcTimeEvidence.Decode(value);
            if (!evidence.IsDecoded)
                continue;

            firstUtcCandidate ??= evidence;
            var display = MmsDataValueRenderer.ToCompactString(value);
            if (string.Equals(display, frame.Header.TimeOfEntry, StringComparison.Ordinal))
                return evidence;
        }

        return firstUtcCandidate ?? new Iec61850UtcTimeEvidence();
    }

    private static bool IsDirectTimestampReference(string reference)
        => reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase) ||
           reference.Replace('$', '.').EndsWith(".t", StringComparison.OrdinalIgnoreCase);

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString(Iec61850UtcTimeFormatter.FullPrecisionPattern, CultureInfo.InvariantCulture) + " UTC";

    private static string FormatLocal(DateTimeOffset value)
        => value.ToLocalTime().ToString(Iec61850UtcTimeFormatter.FullPrecisionOffsetPattern, CultureInfo.InvariantCulture);
}
