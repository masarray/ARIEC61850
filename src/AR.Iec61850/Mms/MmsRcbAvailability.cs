namespace AR.Iec61850.Mms;

public enum MmsRcbOperationalAvailability
{
    Available,
    InUse,
    UsedByCaller,
    Unknown,
    NoDataSet,
    DataSetEmpty,
    DataSetUnreadable
}

public enum MmsRcbAvailabilityConfidence
{
    Exact,
    Reduced,
    Unknown
}

public sealed class MmsRcbAvailabilityOptions
{
    public int MaxReportControls { get; init; } = 512;
    public bool ReadDataSetDirectories { get; init; } = true;
    public IReadOnlySet<string> CallerOwnedRcbReferences { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MmsRcbAvailabilitySnapshot
{
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string BufferTimeMs { get; init; } = string.Empty;
    public string IntegrityPeriodMs { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string EnabledState { get; init; } = string.Empty;
    public string ReservationState { get; init; } = string.Empty;
    public string ReservationTimeSeconds { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public bool DataSetDirectoryRead { get; init; }
    public bool DataSetDirectorySuccess { get; init; }
    public bool? DataSetIsDeletable { get; init; }
    public int DataSetMemberCount { get; init; }
    public IReadOnlyList<MmsDataSetDirectoryMember> DataSetMembers { get; init; } = Array.Empty<MmsDataSetDirectoryMember>();
    public MmsRcbOperationalAvailability Availability { get; init; }
    public MmsRcbAvailabilityConfidence Confidence { get; init; } = MmsRcbAvailabilityConfidence.Unknown;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> ProbeDiagnostics { get; init; } = Array.Empty<string>();

    public bool IsSelectable => Availability is MmsRcbOperationalAvailability.Available or MmsRcbOperationalAvailability.UsedByCaller;
    public string Type => Buffered ? "Buffered" : "Unbuffered";
    public string Summary => $"{Reference}: {Availability} ({Confidence}) - {Reason}";
}

public sealed class MmsRcbAvailabilityResult
{
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<MmsRcbAvailabilitySnapshot> ReportControls { get; init; } = Array.Empty<MmsRcbAvailabilitySnapshot>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public int AvailableCount => ReportControls.Count(item => item.Availability == MmsRcbOperationalAvailability.Available);
    public int InUseCount => ReportControls.Count(item => item.Availability == MmsRcbOperationalAvailability.InUse);
    public int UnknownCount => ReportControls.Count(item => item.Availability == MmsRcbOperationalAvailability.Unknown);
    public string Summary => $"RCB availability checked: total={ReportControls.Count}, available={AvailableCount}, in-use={InUseCount}, unknown={UnknownCount}.";
}

public static class MmsRcbAvailabilityEvaluator
{
    public static MmsRcbAvailabilitySnapshot Evaluate(
        MmsReportControlCandidate candidate,
        MmsDataSetDirectoryResult? dataSetDirectory,
        bool callerOwned,
        DateTimeOffset? checkedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var enabled = ParseBool(candidate.EnabledState);
        var reserved = ParseBool(candidate.ReservationState);
        var reservationSeconds = ParseUnsigned(candidate.ReservationTimeSeconds);
        var hasOwner = HasOwner(candidate.Owner);
        var hasDataSet = !string.IsNullOrWhiteSpace(candidate.DataSetReference);
        var directoryRead = dataSetDirectory is not null;
        var directorySuccess = dataSetDirectory?.IsSuccess == true;
        var memberCount = dataSetDirectory?.Members.Count ?? 0;

        var availability = MmsRcbOperationalAvailability.Unknown;
        var confidence = MmsRcbAvailabilityConfidence.Unknown;
        var reason = "Runtime ownership state could not be proven from the exposed RCB attributes.";

        if (callerOwned)
        {
            availability = MmsRcbOperationalAvailability.UsedByCaller;
            confidence = MmsRcbAvailabilityConfidence.Exact;
            reason = "This RCB is active in the caller's current association/session.";
        }
        else if (enabled == true || reserved == true || reservationSeconds > 0 || hasOwner)
        {
            availability = MmsRcbOperationalAvailability.InUse;
            confidence = MmsRcbAvailabilityConfidence.Exact;
            reason = BuildBusyReason(candidate, enabled, reserved, reservationSeconds, hasOwner);
        }
        else if (!hasDataSet)
        {
            availability = MmsRcbOperationalAvailability.NoDataSet;
            confidence = enabled == false ? MmsRcbAvailabilityConfidence.Exact : MmsRcbAvailabilityConfidence.Reduced;
            reason = "The RCB does not reference a static DataSet and cannot be exported as a populated legacy-SAS report block.";
        }
        else if (directoryRead && !directorySuccess)
        {
            availability = MmsRcbOperationalAvailability.DataSetUnreadable;
            confidence = MmsRcbAvailabilityConfidence.Exact;
            reason = string.IsNullOrWhiteSpace(dataSetDirectory?.Message)
                ? "The referenced DataSet directory could not be read."
                : $"The referenced DataSet directory could not be read: {dataSetDirectory.Message}";
        }
        else if (directorySuccess && memberCount == 0)
        {
            availability = MmsRcbOperationalAvailability.DataSetEmpty;
            confidence = MmsRcbAvailabilityConfidence.Exact;
            reason = "The referenced DataSet is empty.";
        }
        else if (enabled == false && ReservationIsExplicitlyFree(candidate, reserved, reservationSeconds, hasOwner))
        {
            availability = MmsRcbOperationalAvailability.Available;
            confidence = MmsRcbAvailabilityConfidence.Exact;
            reason = "RptEna is false, reservation state is explicitly free, and the referenced DataSet is populated.";
        }
        else if (enabled == false && candidate.Buffered && directorySuccess && memberCount > 0)
        {
            // Some Edition 1 BRCBs expose RptEna but do not expose Owner/ResvTms.
            // Do not convert the missing reservation evidence into a green Available state.
            availability = MmsRcbOperationalAvailability.Unknown;
            confidence = MmsRcbAvailabilityConfidence.Reduced;
            reason = "RptEna is false and the DataSet is populated, but this BRCB does not expose enough reservation evidence to prove availability.";
        }
        else if (enabled == false && !candidate.Buffered && reserved == null)
        {
            availability = MmsRcbOperationalAvailability.Unknown;
            confidence = MmsRcbAvailabilityConfidence.Reduced;
            reason = "RptEna is false, but the URCB Resv state was not returned.";
        }

        return new MmsRcbAvailabilitySnapshot
        {
            CheckedAtUtc = checkedAtUtc ?? DateTimeOffset.UtcNow,
            Reference = candidate.Reference,
            Domain = candidate.Domain,
            LogicalNode = candidate.LogicalNode,
            Name = candidate.Name,
            Mode = candidate.Mode,
            Buffered = candidate.Buffered,
            DataSetReference = candidate.DataSetReference,
            ReportId = candidate.ReportId,
            ConfRev = candidate.ConfRev,
            BufferTimeMs = candidate.BufferTimeMs,
            IntegrityPeriodMs = candidate.IntegrityPeriodMs,
            TriggerOptions = candidate.TriggerOptions,
            OptionalFields = candidate.OptionalFields,
            EnabledState = candidate.EnabledState,
            ReservationState = candidate.ReservationState,
            ReservationTimeSeconds = candidate.ReservationTimeSeconds,
            Owner = candidate.Owner,
            DataSetDirectoryRead = directoryRead,
            DataSetDirectorySuccess = directorySuccess,
            DataSetIsDeletable = dataSetDirectory?.IsDeletable,
            DataSetMemberCount = memberCount,
            DataSetMembers = dataSetDirectory?.Members.ToArray() ?? Array.Empty<MmsDataSetDirectoryMember>(),
            Availability = availability,
            Confidence = confidence,
            Reason = reason,
            ProbeDiagnostics = candidate.ProbeDiagnostics.ToArray()
        };
    }

    private static bool ReservationIsExplicitlyFree(
        MmsReportControlCandidate candidate,
        bool? reserved,
        ulong? reservationSeconds,
        bool hasOwner)
    {
        if (hasOwner)
            return false;

        return candidate.Buffered
            ? reservationSeconds == 0
            : reserved == false;
    }

    private static string BuildBusyReason(
        MmsReportControlCandidate candidate,
        bool? enabled,
        bool? reserved,
        ulong? reservationSeconds,
        bool hasOwner)
    {
        var evidence = new List<string>();
        if (enabled == true)
            evidence.Add("RptEna=true");
        if (reserved == true)
            evidence.Add("Resv=true");
        if (reservationSeconds > 0)
            evidence.Add($"ResvTms={reservationSeconds}");
        if (hasOwner)
            evidence.Add($"Owner={candidate.Owner}");
        return $"The RCB is in use or reserved ({string.Join(", ", evidence)}).";
    }

    internal static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text is "1" or "01" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text is "0" or "00" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    internal static ulong? ParseUnsigned(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;
        return ulong.TryParse(text, out var parsed) ? parsed : null;
    }

    internal static bool HasOwner(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" || text == "[]" || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            return false;

        var compact = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length > 0 && compact.Any(character => character != '0');
    }

    internal static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}