namespace AR.Iec61850.Mms;

public enum MmsReportReadinessKind
{
    ReadyStaticDataSet,
    ReadyDynamicSlot,
    OccupiedEnabled,
    ReservedByOtherClient,
    EmptyDynamicSlotNeedsDataSet,
    NeedsAttributeProbe,
    NotUsable
}

public sealed class MmsReportReadiness
{
    public MmsReportControlCandidate ReportControl { get; init; } = new();
    public MmsReportReadinessKind Kind { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public bool IsReadyForSafeSubscription => Kind is MmsReportReadinessKind.ReadyStaticDataSet or MmsReportReadinessKind.ReadyDynamicSlot;
    public string Label => Kind.ToString();
}

public sealed class MmsReportReadinessPlan
{
    public IReadOnlyList<MmsReportReadiness> Items { get; init; } = Array.Empty<MmsReportReadiness>();

    public IReadOnlyDictionary<MmsReportReadinessKind, int> CountByKind()
        => Items
            .GroupBy(x => x.Kind)
            .OrderBy(x => x.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count());

    public IReadOnlyList<MmsReportReadiness> SafeCandidates => Items.Where(x => x.IsReadyForSafeSubscription).ToArray();
    public int BufferedSafeCandidateCount => SafeCandidates.Count(x => x.ReportControl.Buffered);
    public int UnbufferedSafeCandidateCount => SafeCandidates.Count(x => !x.ReportControl.Buffered);

    public string Summary =>
        $"Report readiness: total={Items.Count}, safeCandidates={SafeCandidates.Count} (BRCB={BufferedSafeCandidateCount}, URCB={UnbufferedSafeCandidateCount}), " +
        string.Join(", ", CountByKind().Select(x => $"{x.Key}:{x.Value}"));
}

public static class MmsReportReadinessPlanner
{
    public static MmsReportReadinessPlan Build(MmsReportInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var items = inventory.ReportControls
            .Select(Classify)
            .OrderBy(x => SortKey(x.Kind))
            .ThenByDescending(x => x.ReportControl.Buffered)
            .ThenBy(x => x.ReportControl.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReportControl.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReportControl.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MmsReportReadinessPlan { Items = items };
    }

    private static MmsReportReadiness Classify(MmsReportControlCandidate rcb)
    {
        var rptEna = ParseBool(rcb.EnabledState);
        var reserved = ParseBool(rcb.ReservationState) == true || ParsePositiveInteger(rcb.ReservationTimeSeconds) == true;
        var hasDataSet = !string.IsNullOrWhiteSpace(rcb.DataSetReference);
        var wasProbed = rcb.Status.Contains("probe", StringComparison.OrdinalIgnoreCase) ||
                        rcb.Status.Contains("Attribute", StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrWhiteSpace(rcb.EnabledState) ||
                        !string.IsNullOrWhiteSpace(rcb.ReportId) ||
                        !string.IsNullOrWhiteSpace(rcb.ConfRev);

        if (rptEna == true)
            return Create(rcb, MmsReportReadinessKind.OccupiedEnabled, "Leave untouched; another client or previous session may be using this RCB.", "RptEna is true.");

        if (reserved)
            return Create(rcb, MmsReportReadinessKind.ReservedByOtherClient, "Do not use unless the reservation belongs to this client/session or has expired.", "Reservation flag/timer is active.");

        if (hasDataSet && rptEna == false)
            return Create(rcb, MmsReportReadinessKind.ReadyStaticDataSet, "Candidate for safe subscribe: reserve, verify options, enable RptEna, then send GI.", "RCB has a DataSet and is not enabled.");

        if (!wasProbed)
            return Create(rcb, MmsReportReadinessKind.NeedsAttributeProbe, "Probe DatSet/RptEna/Resv/ResvTms/RptID/ConfRev before classifying.", "Only discovered from directory; runtime attributes are not confirmed yet.");

        if (!hasDataSet && rptEna == false)
            return Create(rcb, MmsReportReadinessKind.EmptyDynamicSlotNeedsDataSet, "Candidate dynamic slot only after CreateDataSet/DatSet write support is implemented and verified.", "RCB is free but has no DataSet.");

        if (!hasDataSet && rptEna == null)
            return Create(rcb, MmsReportReadinessKind.NeedsAttributeProbe, "Probe RptEna and reservation attributes again with a longer timeout/probe count.", "DataSet is empty and RptEna could not be confirmed.");

        return Create(rcb, MmsReportReadinessKind.NotUsable, "Do not select automatically. Keep visible only in Advanced diagnostics.", "RCB state is incomplete or not safe for automatic subscription.");
    }

    private static MmsReportReadiness Create(MmsReportControlCandidate reportControl, MmsReportReadinessKind kind, string action, string reason)
        => new()
        {
            ReportControl = reportControl,
            Kind = kind,
            RecommendedAction = action,
            Reason = reason
        };

    private static int SortKey(MmsReportReadinessKind kind)
        => kind switch
        {
            MmsReportReadinessKind.ReadyStaticDataSet => 0,
            MmsReportReadinessKind.ReadyDynamicSlot => 1,
            MmsReportReadinessKind.EmptyDynamicSlotNeedsDataSet => 2,
            MmsReportReadinessKind.OccupiedEnabled => 3,
            MmsReportReadinessKind.ReservedByOtherClient => 4,
            MmsReportReadinessKind.NeedsAttributeProbe => 5,
            _ => 9
        };

    private static bool? ParseBool(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        if (bool.TryParse(text, out var parsed))
            return parsed;

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    private static bool? ParsePositiveInteger(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        return ulong.TryParse(text, out var number) ? number > 0 : null;
    }
}
