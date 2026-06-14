namespace AR.Iec61850.Mms;

public enum MmsReportPresentationMode
{
    StaticDataSet,
    DynamicSlot,
    Occupied,
    Incomplete
}

public sealed record MmsReportPresentation(
    string Reference,
    string ModeLabel,
    string StatusIcon,
    string StatusText,
    string DataSetReference,
    bool IsStatic,
    bool IsDynamicSlot,
    bool IsOccupied,
    bool IsReadyForEnable,
    IReadOnlyList<string> Warnings)
{
    public string TreeStatus => StatusIcon;
    public string DetailStatus => string.IsNullOrWhiteSpace(StatusText) ? ModeLabel : $"{ModeLabel} • {StatusText}";
}

public static class MmsReportPresentationBuilder
{
    public static MmsReportPresentation Build(MmsReportControlCandidate rcb, IReadOnlyCollection<string>? knownDataSets = null)
    {
        ArgumentNullException.ThrowIfNull(rcb);
        var warnings = new List<string>();
        var hasDataSet = !string.IsNullOrWhiteSpace(rcb.DataSetReference);
        var hasDataSetDirectory = hasDataSet && (knownDataSets == null || knownDataSets.Count == 0 || knownDataSets.Contains(rcb.DataSetReference, StringComparer.OrdinalIgnoreCase));
        var enabled = ParseBool(rcb.EnabledState) == true;
        var reserved = ParseBool(rcb.ReservationState) == true || ParsePositiveInteger(rcb.ReservationTimeSeconds) == true;
        var occupied = enabled || reserved;

        if (hasDataSet && !hasDataSetDirectory)
            warnings.Add("RCB has DatSet but the DataSet directory was not read or did not match; report values cannot be mapped safely yet.");
        if (string.IsNullOrWhiteSpace(rcb.ConfRev))
            warnings.Add("ConfRev was not decoded; keep first enable attempts guarded.");
        if (string.IsNullOrWhiteSpace(rcb.TriggerOptions))
            warnings.Add("TrgOps was not decoded; keep current IED trigger settings.");
        if (string.IsNullOrWhiteSpace(rcb.OptionalFields))
            warnings.Add("OptFlds was not decoded; keep current IED optional fields.");

        if (occupied)
        {
            return new MmsReportPresentation(
                rcb.Reference,
                rcb.Buffered ? "BRCB occupied" : "URCB occupied",
                "🔒",
                enabled ? "enabled by a client" : "reserved by a client",
                rcb.DataSetReference,
                hasDataSet,
                false,
                true,
                false,
                warnings);
        }

        if (hasDataSet)
        {
            return new MmsReportPresentation(
                rcb.Reference,
                rcb.Buffered ? "BRCB static" : "URCB static",
                hasDataSetDirectory ? "✓" : "!",
                hasDataSetDirectory ? "static DataSet mapped" : "static DataSet not mapped",
                rcb.DataSetReference,
                true,
                false,
                false,
                hasDataSetDirectory,
                warnings);
        }

        return new MmsReportPresentation(
            rcb.Reference,
            rcb.Buffered ? "BRCB dynamic slot" : "URCB dynamic slot",
            "◇",
            "free slot; requires dynamic DataSet or DatSet write",
            string.Empty,
            false,
            true,
            false,
            true,
            warnings);
    }

    public static MmsReportPresentation Build(AR.Iec61850.Discovery.LiveIedReportControlModel rcb, IReadOnlyCollection<string>? knownDataSets = null)
    {
        ArgumentNullException.ThrowIfNull(rcb);
        return Build(new MmsReportControlCandidate
        {
            Domain = rcb.Domain,
            LogicalNode = rcb.LogicalNode,
            FunctionalConstraint = rcb.Buffered ? "BR" : "RP",
            Name = rcb.Name,
            Reference = rcb.Reference,
            Buffered = rcb.Buffered,
            DataSetReference = rcb.DataSetReference,
            ReportId = rcb.ReportId,
            ConfRev = rcb.ConfRev,
            TriggerOptions = rcb.TriggerOptions,
            OptionalFields = rcb.OptionalFields,
            BufferTimeMs = rcb.BufferTimeMs,
            IntegrityPeriodMs = rcb.IntegrityPeriodMs,
            EnabledState = rcb.EnabledState,
            ReservationState = rcb.ReservationState,
            ReservationTimeSeconds = rcb.ReservationTimeSeconds,
            Status = rcb.Status
        }, knownDataSets);
    }

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static bool? ParsePositiveInteger(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return ulong.TryParse(text, out var number) ? number > 0 : null;
    }
}
