namespace AR.Iec61850.Mms;

public sealed class MmsReportAttributeWriteStep
{
    public string Attribute { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public bool Attempted { get; init; }
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReportValue
{
    public int Index { get; init; }
    public MmsDataSetDirectoryMember? Member { get; init; }
    public MmsDataValue? Value { get; init; }
    public int? FailureCode { get; init; }
    public string DataReference { get; init; } = string.Empty;
    public IReadOnlyList<string> ReasonForInclusion { get; init; } = Array.Empty<string>();

    public string MemberReference => Member?.UserReference ?? $"report-item[{Index}]";
    public string DisplayValue => Value == null
        ? $"failure={FailureCode}"
        : MmsDataValueRenderer.ToCompactString(Value, Member?.UserReference);
    public string ReasonSummary => ReasonForInclusion.Count == 0 ? "-" : string.Join(",", ReasonForInclusion);
}

public sealed class MmsReportFrame
{
    public DateTimeOffset ReceivedAt { get; init; }
    public MmsReportHeader Header { get; init; } = new();
    public IReadOnlyList<MmsReportValue> Values { get; init; } = Array.Empty<MmsReportValue>();
    public int RawAccessResultCount { get; init; }
    public int? InclusionBitstringItemIndex { get; init; }
    public IReadOnlyList<int> IncludedDataSetIndexes { get; init; } = Array.Empty<int>();
    public string DecoderMode { get; init; } = string.Empty;
    public IReadOnlyList<string> ParseWarnings { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;

    public string StreamKey
    {
        get
        {
            var rptId = string.IsNullOrWhiteSpace(Header.ReportId) ? "-" : Header.ReportId.Trim();
            var dataSet = string.IsNullOrWhiteSpace(Header.DataSetReference) ? "-" : Header.DataSetReference.Trim();
            var confRev = Header.ConfRev?.ToString() ?? "-";
            return $"{rptId}|ds={dataSet}|conf={confRev}";
        }
    }
}

public sealed class MmsReportHeader
{
    public string ReportId { get; init; } = string.Empty;
    public MmsReportOptionalFields OptionalFields { get; init; } = new();
    public ulong? SequenceNumber { get; init; }
    public ulong? SubSequenceNumber { get; init; }
    public bool? MoreSegmentsFollow { get; init; }
    public string TimeOfEntry { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public bool? BufferOverflow { get; init; }
    public string EntryIdHex { get; init; } = string.Empty;
    public ulong? ConfRev { get; init; }

    public bool HasAny =>
        !string.IsNullOrWhiteSpace(ReportId) ||
        OptionalFields.SetBitIndexes.Count > 0 ||
        SequenceNumber.HasValue ||
        !string.IsNullOrWhiteSpace(TimeOfEntry) ||
        !string.IsNullOrWhiteSpace(DataSetReference) ||
        BufferOverflow.HasValue ||
        !string.IsNullOrWhiteSpace(EntryIdHex) ||
        ConfRev.HasValue;

    public string Summary
    {
        get
        {
            var fields = new List<string>();
            if (!string.IsNullOrWhiteSpace(ReportId))
                fields.Add($"RptID={ReportId}");
            if (SequenceNumber.HasValue)
                fields.Add($"SqNum={SequenceNumber.Value}");
            if (SubSequenceNumber.HasValue)
                fields.Add($"SubSqNum={SubSequenceNumber.Value}");
            if (MoreSegmentsFollow.HasValue)
                fields.Add($"MoreSegmentsFollow={MoreSegmentsFollow.Value.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(TimeOfEntry))
                fields.Add($"TimeOfEntry={TimeOfEntry}");
            if (!string.IsNullOrWhiteSpace(DataSetReference))
                fields.Add($"DatSet={DataSetReference}");
            if (BufferOverflow.HasValue)
                fields.Add($"BufOvfl={BufferOverflow.Value.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(EntryIdHex))
                fields.Add($"EntryID={EntryIdHex}");
            if (ConfRev.HasValue)
                fields.Add($"ConfRev={ConfRev.Value}");
            if (OptionalFields.SetBitIndexes.Count > 0)
                fields.Add($"OptFlds={OptionalFields.Summary}");

            return fields.Count == 0 ? "-" : string.Join("; ", fields);
        }
    }
}

public sealed class MmsReportOptionalFields
{
    public string RawHex { get; init; } = string.Empty;
    public IReadOnlyList<int> SetBitIndexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

    public bool HasSequenceNumber => Has("sequence-number");
    public bool HasReportTimestamp => Has("report-time-stamp");
    public bool HasReasonForInclusion => Has("reason-for-inclusion");
    public bool HasDataSetName => Has("data-set-name");
    public bool HasDataReference => Has("data-reference");
    public bool HasBufferOverflow => Has("buffer-overflow");
    public bool HasEntryId => Has("entryID");
    public bool HasConfRevision => Has("conf-revision");
    public bool HasSegmentation => Has("segmentation");

    public string Summary
    {
        get
        {
            if (Names.Count == 0 && SetBitIndexes.Count == 0)
                return "-";

            var names = Names.Count == 0 ? "-" : string.Join(",", Names);
            var bits = SetBitIndexes.Count == 0 ? "-" : string.Join(",", SetBitIndexes);
            return $"{names} bits=[{bits}] raw={RawHex}";
        }
    }

    private bool Has(string name)
        => Names.Contains(name, StringComparer.OrdinalIgnoreCase);
}

public sealed class MmsReportPollRead
{
    public DateTimeOffset ReadAt { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string SelectedReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string DisplayValue { get; init; } = "-";
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReportSoakSnapshot
{
    public DateTimeOffset CapturedAt { get; init; }
    public double ElapsedSeconds { get; init; }
    public int ReportCount { get; init; }
    public int ValueCount { get; init; }
    public int PollReadCount { get; init; }
    public int PollReadSuccessCount { get; init; }
    public int PollReadFailureCount { get; init; }
    public int PendingConfirmedOperationCount { get; init; }
    public int QueuedInformationReportCount { get; init; }
    public string LastReceiveRoutingSummary { get; init; } = string.Empty;

    public string Summary =>
        $"{CapturedAt:yyyy-MM-dd HH:mm:ss.fff} UTC elapsed={ElapsedSeconds:0.###}s reports={ReportCount} values={ValueCount} poll={PollReadSuccessCount}/{PollReadCount} pending={PendingConfirmedOperationCount} queuedReports={QueuedInformationReportCount}";
}

public sealed class MmsRcbClaimAttempt
{
    public int AttemptNumber { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string PlanMode { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public bool IsFallback { get; init; }
    public string WriteAttribute { get; init; } = string.Empty;
    public string WriteReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsStaticReportSessionResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<MmsReportFrame> Reports { get; init; } = Array.Empty<MmsReportFrame>();
    public IReadOnlyList<MmsReportPollRead> PollReads { get; init; } = Array.Empty<MmsReportPollRead>();
    public IReadOnlyList<MmsReportSoakSnapshot> SoakSnapshots { get; init; } = Array.Empty<MmsReportSoakSnapshot>();
    public IReadOnlyList<MmsRcbClaimAttempt> RcbClaimAttempts { get; init; } = Array.Empty<MmsRcbClaimAttempt>();
    public IReadOnlyList<MmsRcbContentionProbeResult> RcbContentionProbes { get; init; } = Array.Empty<MmsRcbContentionProbeResult>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public MmsReportSessionDiagnostics Diagnostics { get; init; } = new();
    public MmsReportSessionVerification Verification { get; init; } = new();
    public string Message { get; init; } = string.Empty;
}

public enum MmsReportVerificationSeverity
{
    Pass,
    Warning,
    Fail
}

public sealed class MmsReportVerificationCheck
{
    public string Stage { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Observed { get; init; } = string.Empty;
    public MmsReportVerificationSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool IsPass => Severity == MmsReportVerificationSeverity.Pass;
    public bool IsWarning => Severity == MmsReportVerificationSeverity.Warning;
    public bool IsFail => Severity == MmsReportVerificationSeverity.Fail;
}

public sealed class MmsReportRcbSnapshot
{
    public string Stage { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string EnabledState { get; init; } = string.Empty;
    public string ReservationState { get; init; } = string.Empty;
    public string ReservationTimeSeconds { get; init; } = string.Empty;
    public string BufferTimeMs { get; init; } = string.Empty;
    public string IntegrityPeriodMs { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public IReadOnlyList<string> ProbeDiagnostics { get; init; } = Array.Empty<string>();

    public string Summary => IsSuccess
        ? $"{Stage}: {Reference} RptEna={TextOrDash(EnabledState)} DatSet={TextOrDash(DataSetReference)} Resv={TextOrDash(ReservationState)} ResvTms={TextOrDash(ReservationTimeSeconds)} ConfRev={TextOrDash(ConfRev)}"
        : $"{Stage}: snapshot failed for {Reference}: {Message}";

    internal static MmsReportRcbSnapshot FromCandidate(string stage, MmsReportControlCandidate candidate, bool success, string message)
        => new()
        {
            Stage = stage,
            CapturedAt = DateTimeOffset.UtcNow,
            IsSuccess = success,
            Message = message,
            Reference = candidate.Reference,
            Mode = candidate.Mode,
            DataSetReference = candidate.DataSetReference,
            ReportId = candidate.ReportId,
            ConfRev = candidate.ConfRev,
            EnabledState = candidate.EnabledState,
            ReservationState = candidate.ReservationState,
            ReservationTimeSeconds = candidate.ReservationTimeSeconds,
            BufferTimeMs = candidate.BufferTimeMs,
            IntegrityPeriodMs = candidate.IntegrityPeriodMs,
            TriggerOptions = candidate.TriggerOptions,
            OptionalFields = candidate.OptionalFields,
            ProbeDiagnostics = candidate.ProbeDiagnostics.ToArray()
        };

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}

public sealed class MmsReportDataSetSnapshot
{
    public string Stage { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsSuccess { get; init; }
    public bool Exists { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public bool? IsDeletable { get; init; }
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;

    public string Summary => Exists
        ? $"{Stage}: {DataSetReference} exists members={MemberCount} deletable={IsDeletable?.ToString().ToLowerInvariant() ?? "unknown"}"
        : $"{Stage}: {DataSetReference} not readable/deleted: {Message}";
}

public sealed class MmsReportSessionVerification
{
    public IReadOnlyList<MmsReportVerificationCheck> Checks { get; init; } = Array.Empty<MmsReportVerificationCheck>();
    public IReadOnlyList<MmsReportRcbSnapshot> RcbSnapshots { get; init; } = Array.Empty<MmsReportRcbSnapshot>();
    public IReadOnlyList<MmsReportDataSetSnapshot> DataSetSnapshots { get; init; } = Array.Empty<MmsReportDataSetSnapshot>();

    public int PassCount => Checks.Count(x => x.IsPass);
    public int WarningCount => Checks.Count(x => x.IsWarning);
    public int FailureCount => Checks.Count(x => x.IsFail);
    public string OverallStatus => FailureCount > 0 ? "FAIL" : WarningCount > 0 ? "PASS_WITH_WARNING" : "PASS";
    public string Summary => $"verification={OverallStatus}, pass={PassCount}, warnings={WarningCount}, failures={FailureCount}, rcbSnapshots={RcbSnapshots.Count}, dataSetSnapshots={DataSetSnapshots.Count}";
}

public static class MmsReportFrameMapper
{
    public static MmsReportFrame Map(
        MmsInformationReport decoded,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        members ??= Array.Empty<MmsDataSetDirectoryMember>();

        var values = TryMapIec61850ReportValues(decoded.Items, members, out var mapped)
            ? mapped.Values
            : decoded.Items.Select(item => new MmsReportValue
            {
                Index = item.Index,
                Member = item.Index >= 0 && item.Index < members.Count ? members[item.Index] : null,
                Value = item.Value,
                FailureCode = item.FailureCode
            }).ToArray();

        return new MmsReportFrame
        {
            ReceivedAt = receivedAt,
            Header = mapped.Header,
            Values = values,
            RawAccessResultCount = decoded.Items.Count,
            InclusionBitstringItemIndex = mapped.InclusionBitstringItemIndex,
            IncludedDataSetIndexes = mapped.IncludedDataSetIndexes,
            DecoderMode = mapped.DecoderMode,
            ParseWarnings = mapped.ParseWarnings,
            Message = mapped.Message ?? decoded.Message,
            ResponseHexPreview = decoded.ResponseHexPreview
        };
    }

    /// <summary>
    /// Decodes report identity and optional header fields without requiring a
    /// DataSet member list. Routing must happen before value projection when
    /// multiple persistent monitors share one MMS association.
    /// </summary>
    public static MmsReportHeader DecodeHeader(MmsInformationReport decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        var items = decoded.Items;
        if (items.Count == 0)
            return new MmsReportHeader();

        var reportId = IsTextValue(items[0].Value) ? ToText(items[0].Value) : string.Empty;
        if (items.Count < 2 || items[1].Value?.Kind != MmsDataKind.BitString)
            return new MmsReportHeader { ReportId = reportId };

        var optionalFields = DecodeOptionalFields(items[1].Value!);
        var cursor = 2;
        ulong? sequenceNumber = null;
        ulong? subSequenceNumber = null;
        bool? moreSegmentsFollow = null;
        var timeOfEntry = string.Empty;
        var dataSet = string.Empty;
        bool? bufferOverflow = null;
        var entryIdHex = string.Empty;
        ulong? confRev = null;

        if (optionalFields.HasSequenceNumber)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var sequence))
                sequenceNumber = sequence;
            cursor++;
        }

        if (optionalFields.HasReportTimestamp)
        {
            if (cursor < items.Count && items[cursor].Value is { } timeValue && IsTimeValue(timeValue))
                timeOfEntry = MmsDataValueRenderer.ToCompactString(timeValue);
            cursor++;
        }

        if (optionalFields.HasDataSetName)
        {
            if (cursor < items.Count && IsTextValue(items[cursor].Value))
                dataSet = ToText(items[cursor].Value);
            cursor++;
        }

        if (optionalFields.HasBufferOverflow)
        {
            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.Boolean && items[cursor].Value?.Value is bool flag)
                bufferOverflow = flag;
            cursor++;
        }

        if (optionalFields.HasEntryId)
        {
            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.OctetString)
                entryIdHex = Convert.ToHexString(items[cursor].Value!.RawValue.ToArray());
            cursor++;
        }

        if (optionalFields.HasConfRevision)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var revision))
                confRev = revision;
            cursor++;
        }

        if (optionalFields.HasSegmentation)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var subSequence))
                subSequenceNumber = subSequence;
            cursor++;

            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.Boolean && items[cursor].Value?.Value is bool more)
                moreSegmentsFollow = more;
        }

        return new MmsReportHeader
        {
            ReportId = reportId,
            OptionalFields = optionalFields,
            SequenceNumber = sequenceNumber,
            SubSequenceNumber = subSequenceNumber,
            MoreSegmentsFollow = moreSegmentsFollow,
            TimeOfEntry = timeOfEntry,
            DataSetReference = dataSet,
            BufferOverflow = bufferOverflow,
            EntryIdHex = entryIdHex,
            ConfRev = confRev
        };
    }

    private readonly record struct ReportValueMapping(
        bool IsMapped,
        MmsReportHeader Header,
        IReadOnlyList<MmsReportValue> Values,
        IReadOnlyList<int> IncludedDataSetIndexes,
        int? InclusionBitstringItemIndex,
        string DecoderMode,
        IReadOnlyList<string> ParseWarnings,
        string? Message);

    private static bool TryMapIec61850ReportValues(
        IReadOnlyList<MmsInformationReportItem> items,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        out ReportValueMapping mapping)
    {
        mapping = new ReportValueMapping(false, new MmsReportHeader(), Array.Empty<MmsReportValue>(), Array.Empty<int>(), null, string.Empty, Array.Empty<string>(), null);
        if (items.Count == 0 || members.Count == 0)
            return false;

        if (TryMapOptFldsDrivenReportValues(items, members, out mapping))
            return true;

        for (var index = 5; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Value?.Kind != MmsDataKind.BitString)
                continue;

            if (!TryDecodeInclusionBits(item.Value, members.Count, out var includedMemberIndexes))
                continue;

            if (includedMemberIndexes.Count == 0)
                continue;

            var valuesStart = index + 1;
            if (valuesStart + includedMemberIndexes.Count > items.Count)
                continue;

            var mapped = new List<MmsReportValue>();
            var trailing = DecodeTrailingReportValueMetadata(items, valuesStart + includedMemberIndexes.Count, includedMemberIndexes.Count);
            for (var includedOffset = 0; includedOffset < includedMemberIndexes.Count; includedOffset++)
            {
                var memberIndex = includedMemberIndexes[includedOffset];
                var valueItem = items[valuesStart + includedOffset];
                var metadata = includedOffset < trailing.Count ? trailing[includedOffset] : new ReportValueMetadata();
                mapped.Add(new MmsReportValue
                {
                    Index = memberIndex,
                    Member = memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex] : null,
                    Value = valueItem.Value,
                    FailureCode = valueItem.FailureCode,
                    DataReference = metadata.DataReference,
                    ReasonForInclusion = metadata.ReasonForInclusion
                });
            }

            var header = DecodeReportHeader(items, index);
            mapping = new ReportValueMapping(
                true,
                header,
                mapped,
                includedMemberIndexes,
                index,
                "heuristic-scan",
                new[] { "Report value mapping used legacy heuristic inclusion-bitstring scan; prefer OptFlds-driven decode for multi-vendor evidence." },
                $"IEC 61850 InformationReport mapped {mapped.Count}/{members.Count} included DataSet value(s). inclusionItem={index}, included=[{string.Join(",", includedMemberIndexes)}], rawAccessResults={items.Count}, header={header.Summary}.");
            return true;
        }

        return false;
    }

    private static bool TryMapOptFldsDrivenReportValues(
        IReadOnlyList<MmsInformationReportItem> items,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        out ReportValueMapping mapping)
    {
        mapping = new ReportValueMapping(false, new MmsReportHeader(), Array.Empty<MmsReportValue>(), Array.Empty<int>(), null, string.Empty, Array.Empty<string>(), null);
        var warnings = new List<string>();
        if (items.Count < 3 || members.Count == 0)
            return false;

        var cursor = 0;
        if (!IsTextValue(items[cursor].Value))
            return false;

        var reportId = ToText(items[cursor].Value);
        cursor++;

        if (cursor >= items.Count || items[cursor].Value?.Kind != MmsDataKind.BitString)
            return false;

        var optionalFields = DecodeOptionalFields(items[cursor].Value!);
        if (optionalFields.Names.Count == 0 && optionalFields.SetBitIndexes.Count == 0)
            return false;

        cursor++;
        ulong? sequenceNumber = null;
        ulong? subSequenceNumber = null;
        bool? moreSegmentsFollow = null;
        var timeOfEntry = string.Empty;
        var dataSet = string.Empty;
        bool? bufferOverflow = null;
        var entryIdHex = string.Empty;
        ulong? confRev = null;

        if (optionalFields.HasSequenceNumber)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var sequence))
                sequenceNumber = sequence;
            else
                warnings.Add($"OptFlds expected SqNum at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasReportTimestamp)
        {
            if (cursor < items.Count && items[cursor].Value is { } timeValue && IsTimeValue(timeValue))
                timeOfEntry = MmsDataValueRenderer.ToCompactString(timeValue);
            else
                warnings.Add($"OptFlds expected TimeOfEntry at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasDataSetName)
        {
            if (cursor < items.Count && IsTextValue(items[cursor].Value))
                dataSet = ToText(items[cursor].Value);
            else
                warnings.Add($"OptFlds expected DatSet at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasBufferOverflow)
        {
            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.Boolean && items[cursor].Value?.Value is bool flag)
                bufferOverflow = flag;
            else
                warnings.Add($"OptFlds expected BufOvfl at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasEntryId)
        {
            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.OctetString)
                entryIdHex = Convert.ToHexString(items[cursor].Value!.RawValue.ToArray());
            else
                warnings.Add($"OptFlds expected EntryID at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasConfRevision)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var revision))
                confRev = revision;
            else
                warnings.Add($"OptFlds expected ConfRev at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (optionalFields.HasSegmentation)
        {
            if (cursor < items.Count && items[cursor].Value != null && TryToUnsigned(items[cursor].Value!, out var subSequence))
                subSequenceNumber = subSequence;
            else
                warnings.Add($"OptFlds expected SubSqNum at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;

            if (cursor < items.Count && items[cursor].Value?.Kind == MmsDataKind.Boolean && items[cursor].Value?.Value is bool more)
                moreSegmentsFollow = more;
            else
                warnings.Add($"OptFlds expected MoreSegmentsFollow at item {cursor}, but value was {DescribeReportItem(items, cursor)}.");
            cursor++;
        }

        if (cursor >= items.Count || items[cursor].Value?.Kind != MmsDataKind.BitString)
        {
            warnings.Add($"OptFlds-driven decode could not find inclusion bitstring at item {cursor}; falling back to heuristic scan.");
            return false;
        }

        var inclusionIndex = cursor;
        if (!TryDecodeInclusionBits(items[cursor].Value!, members.Count, out var includedMemberIndexes) || includedMemberIndexes.Count == 0)
        {
            warnings.Add($"OptFlds-driven decode found inclusion item {cursor}, but no DataSet member bits were set.");
            return false;
        }

        cursor++;
        if (cursor + includedMemberIndexes.Count > items.Count)
        {
            warnings.Add($"OptFlds-driven decode expected {includedMemberIndexes.Count} report value item(s), but only {items.Count - cursor} remain.");
            return false;
        }

        var valueItemsStart = cursor;
        cursor += includedMemberIndexes.Count;

        var dataReferences = new string[includedMemberIndexes.Count];
        if (optionalFields.HasDataReference)
        {
            if (HasConsecutiveValues(items, cursor, includedMemberIndexes.Count, IsTextValue))
            {
                for (var offset = 0; offset < includedMemberIndexes.Count; offset++)
                    dataReferences[offset] = ToText(items[cursor + offset].Value);
                cursor += includedMemberIndexes.Count;
            }
            else
            {
                warnings.Add($"OptFlds indicates data-reference list, but {includedMemberIndexes.Count} text item(s) were not found after report values.");
            }
        }

        var reasons = Enumerable.Range(0, includedMemberIndexes.Count)
            .Select(_ => (IReadOnlyList<string>)Array.Empty<string>())
            .ToArray();
        if (optionalFields.HasReasonForInclusion)
        {
            if (HasConsecutiveValues(items, cursor, includedMemberIndexes.Count, IsBitStringValue))
            {
                for (var offset = 0; offset < includedMemberIndexes.Count; offset++)
                    reasons[offset] = DecodeReasonForInclusion(items[cursor + offset].Value).Names;
                cursor += includedMemberIndexes.Count;
            }
            else
            {
                warnings.Add($"OptFlds indicates reason-for-inclusion list, but {includedMemberIndexes.Count} bit-string item(s) were not found after report values/data-references.");
            }
        }

        if (cursor < items.Count)
            warnings.Add($"OptFlds-driven decode left {items.Count - cursor} trailing AccessResult item(s) unconsumed.");

        var mapped = new List<MmsReportValue>();
        for (var includedOffset = 0; includedOffset < includedMemberIndexes.Count; includedOffset++)
        {
            var memberIndex = includedMemberIndexes[includedOffset];
            var valueItem = items[valueItemsStart + includedOffset];
            mapped.Add(new MmsReportValue
            {
                Index = memberIndex,
                Member = memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex] : null,
                Value = valueItem.Value,
                FailureCode = valueItem.FailureCode,
                DataReference = dataReferences[includedOffset] ?? string.Empty,
                ReasonForInclusion = reasons[includedOffset]
            });
        }

        var header = new MmsReportHeader
        {
            ReportId = reportId,
            OptionalFields = optionalFields,
            SequenceNumber = sequenceNumber,
            SubSequenceNumber = subSequenceNumber,
            MoreSegmentsFollow = moreSegmentsFollow,
            TimeOfEntry = timeOfEntry,
            DataSetReference = dataSet,
            BufferOverflow = bufferOverflow,
            EntryIdHex = entryIdHex,
            ConfRev = confRev
        };

        mapping = new ReportValueMapping(
            true,
            header,
            mapped,
            includedMemberIndexes,
            inclusionIndex,
            "optflds-driven",
            warnings,
            $"IEC 61850 InformationReport OptFlds-driven decode mapped {mapped.Count}/{includedMemberIndexes.Count} included DataSet value(s). inclusionItem={inclusionIndex}, included=[{string.Join(",", includedMemberIndexes)}], rawAccessResults={items.Count}, header={header.Summary}.");
        return true;
    }

    private sealed class ReportValueMetadata
    {
        public string DataReference { get; init; } = string.Empty;
        public IReadOnlyList<string> ReasonForInclusion { get; init; } = Array.Empty<string>();
    }

    private static IReadOnlyList<ReportValueMetadata> DecodeTrailingReportValueMetadata(
        IReadOnlyList<MmsInformationReportItem> items,
        int startIndex,
        int includedCount)
    {
        if (includedCount <= 0 || startIndex >= items.Count)
            return Array.Empty<ReportValueMetadata>();

        var metadata = Enumerable.Range(0, includedCount).Select(_ => new ReportValueMetadata()).ToArray();
        var cursor = startIndex;

        if (HasConsecutiveValues(items, cursor, includedCount, IsTextValue))
        {
            for (var offset = 0; offset < includedCount; offset++)
            {
                metadata[offset] = new ReportValueMetadata
                {
                    DataReference = ToText(items[cursor + offset].Value),
                    ReasonForInclusion = metadata[offset].ReasonForInclusion
                };
            }

            cursor += includedCount;
        }

        if (HasConsecutiveValues(items, cursor, includedCount, IsBitStringValue))
        {
            for (var offset = 0; offset < includedCount; offset++)
            {
                metadata[offset] = new ReportValueMetadata
                {
                    DataReference = metadata[offset].DataReference,
                    ReasonForInclusion = DecodeReasonForInclusion(items[cursor + offset].Value).Names
                };
            }
        }

        return metadata;
    }

    private static bool HasConsecutiveValues(
        IReadOnlyList<MmsInformationReportItem> items,
        int startIndex,
        int count,
        Func<MmsDataValue?, bool> predicate)
    {
        if (startIndex < 0 || count <= 0 || startIndex + count > items.Count)
            return false;

        for (var offset = 0; offset < count; offset++)
        {
            if (!predicate(items[startIndex + offset].Value))
                return false;
        }

        return true;
    }

    private static MmsReportHeader DecodeReportHeader(
        IReadOnlyList<MmsInformationReportItem> items,
        int inclusionBitstringIndex)
    {
        if (inclusionBitstringIndex <= 0)
            return new MmsReportHeader();

        var reportId = string.Empty;
        var dataSet = string.Empty;
        var timeOfEntry = string.Empty;
        bool? bufferOverflow = null;
        var entryIdHex = string.Empty;
        MmsReportOptionalFields optionalFields = new();
        var numeric = new List<ulong>();

        for (var index = 0; index < inclusionBitstringIndex && index < items.Count; index++)
        {
            var value = items[index].Value;
            if (value == null)
                continue;

            if (index == 0 && IsTextValue(value))
            {
                reportId = ToText(value);
                continue;
            }

            if (optionalFields.SetBitIndexes.Count == 0 && value.Kind == MmsDataKind.BitString)
            {
                optionalFields = DecodeOptionalFields(value);
                continue;
            }

            if (IsTextValue(value))
            {
                var text = ToText(value);
                if (string.IsNullOrWhiteSpace(dataSet) && LooksLikeDataSetReference(text))
                    dataSet = text;
                continue;
            }

            if (TryToUnsigned(value, out var number))
            {
                numeric.Add(number);
                continue;
            }

            if (value.Kind is MmsDataKind.UtcTime or MmsDataKind.BinaryTime ||
                (value.Kind == MmsDataKind.Unknown && value.UnknownTagNumber == 12))
            {
                timeOfEntry = MmsDataValueRenderer.ToCompactString(value);
                continue;
            }

            if (value.Kind == MmsDataKind.Boolean && bufferOverflow == null && value.Value is bool flag)
            {
                bufferOverflow = flag;
                continue;
            }

            if (value.Kind == MmsDataKind.OctetString && string.IsNullOrWhiteSpace(entryIdHex))
                entryIdHex = Convert.ToHexString(value.RawValue.ToArray());
        }

        var sequenceNumber = numeric.Count > 0 ? numeric[0] : (ulong?)null;
        var confRev = numeric.Count > 1 ? numeric[^1] : (ulong?)null;
        if (numeric.Count == 1 && optionalFields.HasConfRevision && !optionalFields.HasSequenceNumber)
        {
            confRev = numeric[0];
            sequenceNumber = null;
        }

        return new MmsReportHeader
        {
            ReportId = reportId,
            OptionalFields = optionalFields,
            SequenceNumber = sequenceNumber,
            TimeOfEntry = timeOfEntry,
            DataSetReference = dataSet,
            BufferOverflow = bufferOverflow,
            EntryIdHex = entryIdHex,
            ConfRev = confRev
        };
    }

    private static bool TryDecodeInclusionBits(MmsDataValue bitString, int memberCount, out IReadOnlyList<int> includedIndexes)
    {
        includedIndexes = Array.Empty<int>();
        if (memberCount <= 0 || bitString.Kind != MmsDataKind.BitString || bitString.RawValue.Count < 2)
            return false;

        var unusedBits = bitString.RawValue[0];
        var dataBytes = bitString.RawValue.Skip(1).ToArray();
        var totalBits = dataBytes.Length * 8 - unusedBits;
        if (totalBits < memberCount)
            return false;

        var included = new List<int>();
        for (var memberIndex = 0; memberIndex < memberCount; memberIndex++)
        {
            var byteIndex = memberIndex / 8;
            var bitIndex = 7 - (memberIndex % 8);
            if (((dataBytes[byteIndex] >> bitIndex) & 0x01) != 0)
                included.Add(memberIndex);
        }

        includedIndexes = included;
        return true;
    }

    private static MmsReportOptionalFields DecodeOptionalFields(MmsDataValue bitString)
    {
        var setBits = DecodeSetBitIndexes(bitString).ToArray();
        var names = setBits
            .Select(OptionalFieldName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var raw = bitString.RawValue.Count <= 1
            ? string.Empty
            : Convert.ToHexString(bitString.RawValue.Skip(1).ToArray());

        return new MmsReportOptionalFields
        {
            RawHex = raw,
            SetBitIndexes = setBits,
            Names = names
        };
    }

    private static MmsReportOptionalFields DecodeReasonForInclusion(MmsDataValue? bitString)
    {
        if (bitString?.Kind != MmsDataKind.BitString)
            return new MmsReportOptionalFields();

        var setBits = DecodeSetBitIndexes(bitString).ToArray();
        var names = setBits
            .Select(ReasonForInclusionName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MmsReportOptionalFields
        {
            RawHex = bitString.RawValue.Count <= 1
                ? string.Empty
                : Convert.ToHexString(bitString.RawValue.Skip(1).ToArray()),
            SetBitIndexes = setBits,
            Names = names
        };
    }

    private static IEnumerable<int> DecodeSetBitIndexes(MmsDataValue bitString)
    {
        if (bitString.Kind != MmsDataKind.BitString || bitString.RawValue.Count < 2)
            yield break;

        var unusedBits = bitString.RawValue[0];
        var dataBytes = bitString.RawValue.Skip(1).ToArray();
        var totalBits = dataBytes.Length * 8 - unusedBits;
        for (var bit = 0; bit < totalBits; bit++)
        {
            var byteIndex = bit / 8;
            var bitIndex = 7 - (bit % 8);
            if (((dataBytes[byteIndex] >> bitIndex) & 0x01) != 0)
                yield return bit;
        }
    }

    private static string OptionalFieldName(int bitIndex)
        => bitIndex switch
        {
            0 => "reserved",
            1 => "sequence-number",
            2 => "report-time-stamp",
            3 => "reason-for-inclusion",
            4 => "data-set-name",
            5 => "data-reference",
            6 => "buffer-overflow",
            7 => "entryID",
            8 => "conf-revision",
            9 => "segmentation",
            _ => $"bit-{bitIndex}"
        };

    private static string ReasonForInclusionName(int bitIndex)
        => bitIndex switch
        {
            0 => "data-change",
            1 => "quality-change",
            2 => "data-update",
            3 => "integrity",
            4 => "general-interrogation",
            5 => "application-trigger",
            _ => $"bit-{bitIndex}"
        };

    private static bool IsTimeValue(MmsDataValue value)
        => value.Kind is MmsDataKind.UtcTime or MmsDataKind.BinaryTime ||
           (value.Kind == MmsDataKind.Unknown && value.UnknownTagNumber == 12);

    private static string DescribeReportItem(IReadOnlyList<MmsInformationReportItem> items, int index)
    {
        if (index < 0 || index >= items.Count)
            return "<missing>";

        var item = items[index];
        if (item.Value == null)
            return item.FailureCode.HasValue ? $"failure={item.FailureCode.Value}" : "<null>";

        return item.Value.Kind.ToString();
    }

    private static bool IsTextValue(MmsDataValue? value)
        => value?.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString;

    private static bool IsBitStringValue(MmsDataValue? value)
        => value?.Kind == MmsDataKind.BitString;

    private static string ToText(MmsDataValue? value)
        => value?.Value?.ToString() ?? string.Empty;

    private static bool LooksLikeDataSetReference(string value)
        => value.Contains('/', StringComparison.OrdinalIgnoreCase) ||
           value.Contains("DataSet", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("dataset", StringComparison.OrdinalIgnoreCase);

    private static bool TryToUnsigned(MmsDataValue value, out ulong number)
    {
        if (value.Kind == MmsDataKind.Unsigned && value.Value is ulong unsigned)
        {
            number = unsigned;
            return true;
        }

        if (value.Kind == MmsDataKind.Integer && value.Value is long signed && signed >= 0)
        {
            number = (ulong)signed;
            return true;
        }

        number = 0;
        return false;
    }
}

public sealed partial class MmsClientSession
{
    public async Task<MmsStaticReportSessionResult> RunGuardedStaticReportSessionAsync(
        MmsReportSubscriptionPlan plan,
        TimeSpan listenDuration,
        int reserveSeconds = 30,
        bool triggerGeneralInterrogation = true,
        CancellationToken cancellationToken = default,
        MmsIedModelDirectory? pollDirectory = null,
        IReadOnlyList<string>? pollReferences = null,
        TimeSpan? pollInterval = null,
        TimeSpan? periodicGeneralInterrogationInterval = null,
        TimeSpan? soakSnapshotInterval = null)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsReady || plan.ReportControl == null)
        {
            return new MmsStaticReportSessionResult
            {
                IsSuccess = false,
                Message = "Static report session requires a ready plan with selected RCB."
            };
        }

        var rcb = plan.ReportControl;
        var writes = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();
        var reports = new List<MmsReportFrame>();
        var pollReads = new List<MmsReportPollRead>();
        var soakSnapshots = new List<MmsReportSoakSnapshot>();
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt;
        var verificationChecks = new List<MmsReportVerificationCheck>();
        var rcbSnapshots = new List<MmsReportRcbSnapshot>();
        var dataSetSnapshots = new List<MmsReportDataSetSnapshot>();
        var reservationTouched = false;
        var enabledByThisClient = false;

        try
        {
            var beforeSnapshot = await CaptureReportControlSnapshotAsync(rcb, "before", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(beforeSnapshot);
            AddRcbStateChecks(verificationChecks, beforeSnapshot, expectedRptEna: false, expectedDataSet: plan.DataSetReference, stage: "before");

            if (!string.IsNullOrWhiteSpace(plan.DataSetReference))
            {
                var dataSetBefore = await CaptureDataSetSnapshotAsync(plan.DataSetReference, plan.Members, "before", null, cancellationToken).ConfigureAwait(false);
                dataSetSnapshots.Add(dataSetBefore);
                AddDataSetExistsCheck(verificationChecks, dataSetBefore, expectedMembers: plan.Members, stage: "before");
            }

            if (rcb.Buffered && rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("BRCB ResvTms pre-reserve was skipped. This relay accepts ownership through RptEna=true and rejects or side-effects explicit ResvTms writes.");
            }
            else if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(rcb, "Resv", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = true;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv write failed. Proceeding guarded only if RptEna is accepted by the IED.");
            }

            var enable = await WriteReportAttributeAsync(rcb, "RptEna", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            enabledByThisClient = enable.IsSuccess;
            if (!enable.IsSuccess)
            {
                verificationChecks.Add(FailCheck("after-enable", $"{rcb.Reference}.RptEna", "write accepted", enable.Message, "RptEna=true write failed."));
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots),
                    Message = "RptEna=true failed; report session was not started."
                };
            }

            var afterEnableSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-enable", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterEnableSnapshot);
            AddRcbStateChecks(verificationChecks, afterEnableSnapshot, expectedRptEna: true, expectedDataSet: plan.DataSetReference, stage: "after-enable");

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed or is not supported by this RCB. Waiting for spontaneous/integrity reports only.");
            }

            Func<CancellationToken, Task<MmsReportAttributeWriteStep>>? periodicGiWriter = null;
            if (triggerGeneralInterrogation &&
                periodicGeneralInterrogationInterval.HasValue &&
                periodicGeneralInterrogationInterval.Value > TimeSpan.Zero)
            {
                periodicGiWriter = async token =>
                {
                    var step = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), token).ConfigureAwait(false);
                    return new MmsReportAttributeWriteStep
                    {
                        Attribute = "GI(periodic)",
                        Reference = step.Reference,
                        Attempted = step.Attempted,
                        IsSuccess = step.IsSuccess,
                        Message = step.Message
                    };
                };
            }

            var received = await ReceiveInformationReportsAsync(
                plan.Members,
                listenDuration,
                pollDirectory,
                pollReferences,
                pollInterval,
                pollReads,
                soakSnapshots,
                soakSnapshotInterval,
                periodicGiWriter,
                periodicGeneralInterrogationInterval,
                writes,
                cancellationToken).ConfigureAwait(false);
            reports.AddRange(received);
            AddReportReceptionCheck(verificationChecks, reports, triggerGeneralInterrogation ? "after-gi" : "during-monitor");
        }
        finally
        {
            if (enabledByThisClient)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(rcb, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(disable);
                if (!disable.IsSuccess)
                    verificationChecks.Add(FailCheck("after-cleanup", $"{rcb.Reference}.RptEna", "write false accepted", disable.Message, "RptEna=false cleanup write failed."));

                var afterCleanupSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-cleanup", CancellationToken.None).ConfigureAwait(false);
                rcbSnapshots.Add(afterCleanupSnapshot);
                AddRcbStateChecks(verificationChecks, afterCleanupSnapshot, expectedRptEna: false, expectedDataSet: plan.DataSetReference, stage: "after-cleanup");
            }

            if (reservationTouched)
            {
                var release = rcb.Buffered
                    ? await TryWriteReportAttributeForCleanupAsync(rcb, "ResvTms", MmsDataValue.Unsigned(0), CancellationToken.None).ConfigureAwait(false)
                    : await TryWriteReportAttributeForCleanupAsync(rcb, "Resv", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(release);
            }
        }

        completedAt = DateTimeOffset.UtcNow;
        if (soakSnapshots.Count == 0 || soakSnapshots[^1].ReportCount != reports.Count || soakSnapshots[^1].PollReadCount != pollReads.Count)
            soakSnapshots.Add(CreateSoakSnapshot(startedAt, reports, pollReads));

        var diagnostics = MmsReportSessionDiagnostics.Analyze(reports, pollReads, writes);
        var verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots);
        return new MmsStaticReportSessionResult
        {
            IsSuccess = enabledByThisClient && diagnostics.OverallStatus != "FAIL" && verification.FailureCount == 0,
            WriteSteps = writes,
            Reports = reports,
            PollReads = pollReads,
            SoakSnapshots = soakSnapshots,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Warnings = warnings,
            Diagnostics = diagnostics,
            Verification = verification,
            Message = $"Static report guarded session complete: writes={writes.Count}, reports={reports.Count}, pollReads={pollReads.Count}."
        };
    }

    public async Task<MmsStaticReportSessionResult> RunGuardedDynamicReportSessionAsync(
        MmsReportSubscriptionPlan plan,
        TimeSpan listenDuration,
        int reserveSeconds = 30,
        bool triggerGeneralInterrogation = true,
        bool deleteDataSetOnCleanup = true,
        CancellationToken cancellationToken = default,
        MmsIedModelDirectory? directory = null)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsReady ||
            plan.Mode != MmsReportSubscriptionPlanMode.DynamicDataSet ||
            plan.ReportControl == null ||
            plan.DynamicPoints.Count == 0 ||
            string.IsNullOrWhiteSpace(plan.DataSetReference))
        {
            return new MmsStaticReportSessionResult
            {
                IsSuccess = false,
                Message = "Dynamic report session requires a ready dynamic plan with selected RCB, DataSet reference, and resolved points."
            };
        }

        var rcb = plan.ReportControl;
        var writes = new List<MmsReportAttributeWriteStep>();
        var warnings = new List<string>();
        var reports = new List<MmsReportFrame>();
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt;
        var verificationChecks = new List<MmsReportVerificationCheck>();
        var rcbSnapshots = new List<MmsReportRcbSnapshot>();
        var dataSetSnapshots = new List<MmsReportDataSetSnapshot>();
        var dataSetCreated = false;
        var reservationTouched = false;
        var enabledByThisClient = false;
        var originalDataSetReference = rcb.DataSetReference;

        try
        {
            var beforeSnapshot = await CaptureReportControlSnapshotAsync(rcb, "before", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(beforeSnapshot);
            AddRcbStateChecks(verificationChecks, beforeSnapshot, expectedRptEna: false, expectedDataSet: originalDataSetReference, stage: "before");

            var define = await DefineNamedVariableListAsync(
                plan.DataSetReference,
                plan.DynamicPoints.Select(x => x.ToObjectReference()),
                cancellationToken).ConfigureAwait(false);
            writes.Add(ToWriteStep("DefineNamedVariableList", plan.DataSetReference, define.IsSuccess, define.Message));
            dataSetCreated = define.IsSuccess;
            if (!define.IsSuccess)
            {
                verificationChecks.Add(FailCheck("after-create", plan.DataSetReference, "DefineNamedVariableList OK", define.Message, "Dynamic DataSet create failed."));
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots),
                    Message = "Dynamic DataSet create failed; report session was not started."
                };
            }

            var afterCreateDataSet = await CaptureDataSetSnapshotAsync(plan.DataSetReference, plan.Members, "after-create", directory, cancellationToken).ConfigureAwait(false);
            dataSetSnapshots.Add(afterCreateDataSet);
            AddDataSetExistsCheck(verificationChecks, afterCreateDataSet, expectedMembers: plan.Members, stage: "after-create");

            if (rcb.Buffered && rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("BRCB ResvTms pre-reserve was skipped. This relay accepts ownership through RptEna=true and rejects or side-effects explicit ResvTms writes.");
            }
            else if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(rcb, "Resv", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservationTouched = true;
                if (!reserve.IsSuccess)
                    warnings.Add("URCB Resv write failed. Proceeding only if DatSet/RptEna are accepted by the IED.");
            }

            var dataSetValue = ToReportDataSetAttributeValue(plan.DataSetReference);
            var dataSetWrite = await WriteReportAttributeAsync(rcb, "DatSet", MmsDataValue.VisibleString(dataSetValue), cancellationToken).ConfigureAwait(false);
            writes.Add(dataSetWrite);
            if (!dataSetWrite.IsSuccess)
            {
                verificationChecks.Add(FailCheck("after-bind", $"{rcb.Reference}.DatSet", plan.DataSetReference, dataSetWrite.Message, "RCB.DatSet write failed."));
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots),
                    Message = "RCB.DatSet write failed; report session was not started."
                };
            }

            var afterBindSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-bind", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterBindSnapshot);
            AddRcbStateChecks(verificationChecks, afterBindSnapshot, expectedRptEna: false, expectedDataSet: plan.DataSetReference, stage: "after-bind");

            var enable = await WriteReportAttributeAsync(rcb, "RptEna", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            enabledByThisClient = enable.IsSuccess;
            if (!enable.IsSuccess)
            {
                verificationChecks.Add(FailCheck("after-enable", $"{rcb.Reference}.RptEna", "write accepted", enable.Message, "RptEna=true write failed."));
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots),
                    Message = "RptEna=true failed; dynamic report session was not started."
                };
            }

            var afterEnableSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-enable", cancellationToken).ConfigureAwait(false);
            rcbSnapshots.Add(afterEnableSnapshot);
            AddRcbStateChecks(verificationChecks, afterEnableSnapshot, expectedRptEna: true, expectedDataSet: plan.DataSetReference, stage: "after-enable");

            if (triggerGeneralInterrogation)
            {
                var gi = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed or is not supported by this RCB. Waiting for spontaneous/integrity reports only.");
            }

            var received = await ReceiveInformationReportsAsync(plan.Members, listenDuration, cancellationToken).ConfigureAwait(false);
            reports.AddRange(received);
            AddReportReceptionCheck(verificationChecks, reports, triggerGeneralInterrogation ? "after-gi" : "during-monitor");
        }
        finally
        {
            if (enabledByThisClient)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(rcb, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(disable);
                if (!disable.IsSuccess)
                    verificationChecks.Add(FailCheck("after-cleanup", $"{rcb.Reference}.RptEna", "write false accepted", disable.Message, "RptEna=false cleanup write failed."));
            }

            if (dataSetCreated)
            {
                var restoreValue = string.IsNullOrWhiteSpace(originalDataSetReference)
                    ? string.Empty
                    : ToReportDataSetAttributeValue(originalDataSetReference);
                var restore = await TryWriteReportAttributeForCleanupAsync(rcb, "DatSet", MmsDataValue.VisibleString(restoreValue), CancellationToken.None).ConfigureAwait(false);
                writes.Add(restore);
                if (!restore.IsSuccess)
                    verificationChecks.Add(FailCheck("after-cleanup", $"{rcb.Reference}.DatSet", TextOrDash(originalDataSetReference), restore.Message, "RCB.DatSet restore/clear write failed."));

                var afterCleanupSnapshot = await CaptureReportControlSnapshotAsync(rcb, "after-cleanup", CancellationToken.None).ConfigureAwait(false);
                rcbSnapshots.Add(afterCleanupSnapshot);
                AddRcbStateChecks(verificationChecks, afterCleanupSnapshot, expectedRptEna: false, expectedDataSet: originalDataSetReference, stage: "after-cleanup");
            }

            if (reservationTouched)
            {
                var release = rcb.Buffered
                    ? await TryWriteReportAttributeForCleanupAsync(rcb, "ResvTms", MmsDataValue.Unsigned(0), CancellationToken.None).ConfigureAwait(false)
                    : await TryWriteReportAttributeForCleanupAsync(rcb, "Resv", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(release);
            }

            if (dataSetCreated && deleteDataSetOnCleanup)
            {
                try
                {
                    var delete = await DeleteNamedVariableListAsync(plan.DataSetReference, CancellationToken.None).ConfigureAwait(false);
                    writes.Add(ToWriteStep("DeleteNamedVariableList", plan.DataSetReference, delete.IsSuccess, delete.Message));
                    if (!delete.IsSuccess)
                        verificationChecks.Add(FailCheck("after-delete", plan.DataSetReference, "DeleteNamedVariableList OK", delete.Message, "Dynamic DataSet delete failed."));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    writes.Add(ToWriteStep("DeleteNamedVariableList", plan.DataSetReference, false, $"cleanup delete failed: {ex.GetType().Name}: {ex.Message}"));
                    verificationChecks.Add(FailCheck("after-delete", plan.DataSetReference, "DeleteNamedVariableList OK", ex.Message, "Dynamic DataSet delete threw an exception."));
                }

                var afterDeleteDataSet = await CaptureDataSetSnapshotAsync(plan.DataSetReference, plan.Members, "after-delete", directory, CancellationToken.None).ConfigureAwait(false);
                dataSetSnapshots.Add(afterDeleteDataSet);
                AddDataSetDeletedCheck(verificationChecks, afterDeleteDataSet, "after-delete");
            }
        }

        completedAt = DateTimeOffset.UtcNow;
        var diagnostics = MmsReportSessionDiagnostics.Analyze(reports, Array.Empty<MmsReportPollRead>(), writes);
        var verification = BuildVerification(verificationChecks, rcbSnapshots, dataSetSnapshots);
        return new MmsStaticReportSessionResult
        {
            IsSuccess = enabledByThisClient && diagnostics.OverallStatus != "FAIL" && verification.FailureCount == 0,
            WriteSteps = writes,
            Reports = reports,
            SoakSnapshots = Array.Empty<MmsReportSoakSnapshot>(),
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Warnings = warnings,
            Diagnostics = diagnostics,
            Verification = verification,
            Message = $"Dynamic report guarded session complete: writes={writes.Count}, reports={reports.Count}."
        };
    }

    public async Task<IReadOnlyList<MmsReportFrame>> ReceiveInformationReportsAsync(
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => await ReceiveInformationReportsAsync(
            members,
            duration,
            pollDirectory: null,
            pollReferences: null,
            pollInterval: null,
            pollReads: null,
            soakSnapshots: null,
            soakSnapshotInterval: null,
            periodicGeneralInterrogationWriter: null,
            periodicGeneralInterrogationInterval: null,
            writeSteps: null,
            cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<MmsReportFrame>> ReceiveInformationReportsAsync(
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        TimeSpan duration,
        MmsIedModelDirectory? pollDirectory,
        IReadOnlyList<string>? pollReferences,
        TimeSpan? pollInterval,
        List<MmsReportPollRead>? pollReads,
        List<MmsReportSoakSnapshot>? soakSnapshots,
        TimeSpan? soakSnapshotInterval,
        Func<CancellationToken, Task<MmsReportAttributeWriteStep>>? periodicGeneralInterrogationWriter,
        TimeSpan? periodicGeneralInterrogationInterval,
        List<MmsReportAttributeWriteStep>? writeSteps,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        members ??= Array.Empty<MmsDataSetDirectoryMember>();
        pollReferences = pollReferences?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var reports = new List<MmsReportFrame>();
        var deadline = DateTimeOffset.UtcNow + (duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : duration);
        var effectivePollInterval = pollInterval.GetValueOrDefault(TimeSpan.FromSeconds(1));
        if (effectivePollInterval <= TimeSpan.Zero)
            effectivePollInterval = TimeSpan.FromSeconds(1);

        var startedAt = DateTimeOffset.UtcNow;
        var nextPollAt = startedAt;
        var effectiveSnapshotInterval = soakSnapshotInterval.GetValueOrDefault(TimeSpan.Zero);
        var nextSnapshotAt = effectiveSnapshotInterval > TimeSpan.Zero ? startedAt : DateTimeOffset.MaxValue;
        var effectiveGiInterval = periodicGeneralInterrogationInterval.GetValueOrDefault(TimeSpan.Zero);
        var nextPeriodicGiAt = effectiveGiInterval > TimeSpan.Zero ? startedAt + effectiveGiInterval : DateTimeOffset.MaxValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var remaining = deadline - now;
            if (remaining <= TimeSpan.Zero)
                break;

            if (soakSnapshots != null && effectiveSnapshotInterval > TimeSpan.Zero && now >= nextSnapshotAt)
            {
                soakSnapshots.Add(CreateSoakSnapshot(startedAt, reports, pollReads is null ? Array.Empty<MmsReportPollRead>() : pollReads));
                nextSnapshotAt = now + effectiveSnapshotInterval;
            }

            if (periodicGeneralInterrogationWriter != null && effectiveGiInterval > TimeSpan.Zero && now >= nextPeriodicGiAt)
            {
                var giStep = await periodicGeneralInterrogationWriter(cancellationToken).ConfigureAwait(false);
                writeSteps?.Add(giStep);
                nextPeriodicGiAt = now + effectiveGiInterval;
            }

            var drainedQueuedReport = false;
            if (TryDequeueInformationReport(out var queuedPayload))
            {
                TryAppendInformationReport(queuedPayload, members, reports);
                drainedQueuedReport = true;
            }

            if (drainedQueuedReport)
                continue;

            if (pollDirectory != null &&
                pollReads != null &&
                pollReferences.Count > 0 &&
                DateTimeOffset.UtcNow >= nextPollAt)
            {
                foreach (var reference in pollReferences)
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        break;

                    var read = await ReadReportPollReferenceAsync(pollDirectory, reference, cancellationToken).ConfigureAwait(false);
                    pollReads.Add(read);
                }

                nextPollAt = DateTimeOffset.UtcNow + effectivePollInterval;
                continue;
            }

            if (IsReceivePumpRunning)
            {
                var delay = remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!_cotp.HasDataAvailable)
            {
                var delay = remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            byte[] payload;
            try
            {
                payload = await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            var route = _receiveRouter.Route(payload);
            LastReceiveRoutingSummary = route.Message;
            if (route.Action != MmsReceiveRouteAction.QueuedInformationReport)
                continue;

            if (TryDequeueInformationReport(out var routedPayload))
                TryAppendInformationReport(routedPayload, members, reports);
        }

        return reports;
    }

    private MmsReportSoakSnapshot CreateSoakSnapshot(
        DateTimeOffset startedAt,
        IReadOnlyList<MmsReportFrame> reports,
        IReadOnlyList<MmsReportPollRead> pollReads)
        => new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            ElapsedSeconds = Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalSeconds),
            ReportCount = reports.Count,
            ValueCount = reports.Sum(x => x.Values.Count),
            PollReadCount = pollReads.Count,
            PollReadSuccessCount = pollReads.Count(x => x.IsSuccess),
            PollReadFailureCount = pollReads.Count(x => !x.IsSuccess),
            PendingConfirmedOperationCount = PendingConfirmedOperationCount,
            QueuedInformationReportCount = QueuedInformationReportCount,
            LastReceiveRoutingSummary = LastReceiveRoutingSummary
        };

    private async Task<MmsReportPollRead> ReadReportPollReferenceAsync(
        MmsIedModelDirectory directory,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await ReadSmartAsync(directory, reference, cancellationToken).ConfigureAwait(false);
            return new MmsReportPollRead
            {
                ReadAt = DateTimeOffset.UtcNow,
                Reference = reference,
                SelectedReference = read.SelectedPoint?.UserReference ?? string.Empty,
                FunctionalConstraint = read.SelectedPoint?.FunctionalConstraint ?? string.Empty,
                IsSuccess = read.IsSuccess,
                DisplayValue = read.ReadResult.Value == null ? "-" : MmsDataValueRenderer.ToCompactString(read.ReadResult.Value, read.SelectedPoint?.UserReference),
                Message = read.Message
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            return new MmsReportPollRead
            {
                ReadAt = DateTimeOffset.UtcNow,
                Reference = reference,
                IsSuccess = false,
                Message = $"poll read failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static void TryAppendInformationReport(
        byte[] payload,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        List<MmsReportFrame> reports)
    {
        if (!MmsInformationReportDecoder.IsInformationReport(payload))
            return;

        var decoded = MmsInformationReportDecoder.Decode(payload);
        reports.Add(MmsReportFrameMapper.Map(decoded, members, DateTimeOffset.UtcNow));
    }

    private async Task<MmsReportRcbSnapshot> CaptureReportControlSnapshotAsync(
        MmsReportControlCandidate source,
        string stage,
        CancellationToken cancellationToken)
    {
        var clone = CloneReportControlCandidate(source);
        try
        {
            await ProbeReportControlAttributesAsync(clone, cancellationToken).ConfigureAwait(false);
            return MmsReportRcbSnapshot.FromCandidate(stage, clone, success: true, "RCB readback snapshot captured.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MmsReportRcbSnapshot.FromCandidate(stage, source, success: false, $"RCB readback failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<MmsReportDataSetSnapshot> CaptureDataSetSnapshotAsync(
        string dataSetReference,
        IReadOnlyList<MmsDataSetDirectoryMember> expectedMembers,
        string stage,
        MmsIedModelDirectory? directory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dataSetReference))
        {
            return new MmsReportDataSetSnapshot
            {
                Stage = stage,
                CapturedAt = DateTimeOffset.UtcNow,
                IsSuccess = false,
                Exists = false,
                Message = "No DataSet reference was provided for verification."
            };
        }

        try
        {
            var result = await GetDataSetDirectoryAsync(dataSetReference, directory, cancellationToken).ConfigureAwait(false);
            return new MmsReportDataSetSnapshot
            {
                Stage = stage,
                CapturedAt = DateTimeOffset.UtcNow,
                IsSuccess = result.IsSuccess,
                Exists = result.IsSuccess,
                DataSetReference = dataSetReference,
                MemberCount = result.Members.Count,
                IsDeletable = result.IsDeletable,
                MemberReferences = result.Members.Select(x => x.UserReference).ToArray(),
                Message = result.Message
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MmsReportDataSetSnapshot
            {
                Stage = stage,
                CapturedAt = DateTimeOffset.UtcNow,
                IsSuccess = false,
                Exists = false,
                DataSetReference = dataSetReference,
                Message = $"DataSet readback failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static MmsReportControlCandidate CloneReportControlCandidate(MmsReportControlCandidate source)
        => new()
        {
            Domain = source.Domain,
            LogicalNode = source.LogicalNode,
            FunctionalConstraint = source.FunctionalConstraint,
            Name = source.Name,
            Reference = source.Reference,
            Buffered = source.Buffered,
            DataSetReference = source.DataSetReference,
            ReportId = source.ReportId,
            ConfRev = source.ConfRev,
            IntegrityPeriodMs = source.IntegrityPeriodMs,
            EnabledState = source.EnabledState,
            ReservationState = source.ReservationState,
            ReservationTimeSeconds = source.ReservationTimeSeconds,
            BufferTimeMs = source.BufferTimeMs,
            TriggerOptions = source.TriggerOptions,
            OptionalFields = source.OptionalFields,
            Status = source.Status,
            Attributes = source.Attributes.ToList()
        };

    private static MmsReportSessionVerification BuildVerification(
        IReadOnlyList<MmsReportVerificationCheck> checks,
        IReadOnlyList<MmsReportRcbSnapshot> rcbSnapshots,
        IReadOnlyList<MmsReportDataSetSnapshot> dataSetSnapshots)
        => new()
        {
            Checks = checks.ToArray(),
            RcbSnapshots = rcbSnapshots.ToArray(),
            DataSetSnapshots = dataSetSnapshots.ToArray()
        };

    private static void AddRcbStateChecks(
        List<MmsReportVerificationCheck> checks,
        MmsReportRcbSnapshot snapshot,
        bool? expectedRptEna,
        string? expectedDataSet,
        string stage)
    {
        if (!snapshot.IsSuccess)
        {
            checks.Add(WarningCheck(stage, snapshot.Reference, "RCB snapshot readable", snapshot.Message, "RCB state could not be read back; write response remains unverified."));
            return;
        }

        if (expectedRptEna.HasValue)
        {
            var observed = ParseReportBool(snapshot.EnabledState);
            if (!observed.HasValue)
            {
                checks.Add(WarningCheck(stage, $"{snapshot.Reference}.RptEna", expectedRptEna.Value.ToString().ToLowerInvariant(), TextOrDash(snapshot.EnabledState), "RptEna readback is not explicit."));
            }
            else if (observed.Value != expectedRptEna.Value)
            {
                checks.Add(FailCheck(stage, $"{snapshot.Reference}.RptEna", expectedRptEna.Value.ToString().ToLowerInvariant(), observed.Value.ToString().ToLowerInvariant(), "RptEna readback did not match expected state."));
            }
            else
            {
                checks.Add(PassCheck(stage, $"{snapshot.Reference}.RptEna", expectedRptEna.Value.ToString().ToLowerInvariant(), observed.Value.ToString().ToLowerInvariant(), "RptEna readback verified."));
            }
        }

        if (expectedDataSet != null)
        {
            var expected = NormalizeReportReference(expectedDataSet);
            var observed = NormalizeReportReference(snapshot.DataSetReference);
            if (string.IsNullOrWhiteSpace(expected))
            {
                if (string.IsNullOrWhiteSpace(observed))
                    checks.Add(PassCheck(stage, $"{snapshot.Reference}.DatSet", "empty", TextOrDash(snapshot.DataSetReference), "RCB.DatSet is empty/restored."));
                else
                    checks.Add(FailCheck(stage, $"{snapshot.Reference}.DatSet", "empty", snapshot.DataSetReference, "RCB.DatSet was not cleared/restored."));
            }
            else if (string.IsNullOrWhiteSpace(observed))
            {
                checks.Add(WarningCheck(stage, $"{snapshot.Reference}.DatSet", expectedDataSet ?? string.Empty, TextOrDash(snapshot.DataSetReference), "RCB.DatSet readback is empty or unsupported."));
            }
            else if (!observed.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(FailCheck(stage, $"{snapshot.Reference}.DatSet", expectedDataSet ?? string.Empty, snapshot.DataSetReference, "RCB.DatSet readback did not match expected DataSet."));
            }
            else
            {
                checks.Add(PassCheck(stage, $"{snapshot.Reference}.DatSet", expectedDataSet ?? string.Empty, snapshot.DataSetReference, "RCB.DatSet readback verified."));
            }
        }

        if (stage.Equals("before", StringComparison.OrdinalIgnoreCase) || stage.Equals("after-cleanup", StringComparison.OrdinalIgnoreCase))
            AddReservationVerificationCheck(checks, snapshot, stage);
    }

    private static void AddDataSetExistsCheck(
        List<MmsReportVerificationCheck> checks,
        MmsReportDataSetSnapshot snapshot,
        IReadOnlyList<MmsDataSetDirectoryMember> expectedMembers,
        string stage)
    {
        if (!snapshot.Exists)
        {
            checks.Add(FailCheck(stage, snapshot.DataSetReference, $"DataSet readable with {expectedMembers.Count} member(s)", snapshot.Message, "DataSet directory readback failed."));
            return;
        }

        var expected = expectedMembers.Select(x => NormalizeReportReference(x.UserReference)).ToArray();
        var observed = snapshot.MemberReferences.Select(NormalizeReportReference).ToArray();
        var countMatches = snapshot.MemberCount == expectedMembers.Count;
        var orderMatches = countMatches && expected.SequenceEqual(observed, StringComparer.OrdinalIgnoreCase);
        if (orderMatches)
        {
            checks.Add(PassCheck(stage, snapshot.DataSetReference, $"{expectedMembers.Count} member(s) in requested order", $"{snapshot.MemberCount} member(s)", "DataSet directory readback verified."));
        }
        else
        {
            checks.Add(FailCheck(stage, snapshot.DataSetReference, $"{expectedMembers.Count} member(s) in requested order", $"{snapshot.MemberCount} member(s): {string.Join(",", snapshot.MemberReferences)}", "DataSet member count/order mismatch."));
        }
    }

    private static void AddDataSetDeletedCheck(
        List<MmsReportVerificationCheck> checks,
        MmsReportDataSetSnapshot snapshot,
        string stage)
    {
        if (!snapshot.Exists)
        {
            checks.Add(PassCheck(stage, snapshot.DataSetReference, "not readable after delete", snapshot.Message, "Dynamic DataSet delete verified by readback."));
            return;
        }

        checks.Add(FailCheck(stage, snapshot.DataSetReference, "not readable after delete", snapshot.Summary, "Dynamic DataSet is still readable after delete."));
    }

    private static void AddReportReceptionCheck(List<MmsReportVerificationCheck> checks, IReadOnlyList<MmsReportFrame> reports, string stage)
    {
        if (reports.Count > 0)
            checks.Add(PassCheck(stage, "InformationReport", "at least 1 report", reports.Count.ToString(), "InformationReport received."));
        else
            checks.Add(FailCheck(stage, "InformationReport", "at least 1 report", "0", "No InformationReport was received during the guarded session."));
    }

    private static MmsReportVerificationCheck PassCheck(string stage, string target, string expected, string observed, string message)
        => new() { Stage = stage, Target = target, Expected = expected, Observed = observed, Severity = MmsReportVerificationSeverity.Pass, Message = message };

    private static MmsReportVerificationCheck WarningCheck(string stage, string target, string expected, string observed, string message)
        => new() { Stage = stage, Target = target, Expected = expected, Observed = observed, Severity = MmsReportVerificationSeverity.Warning, Message = message };

    private static MmsReportVerificationCheck FailCheck(string stage, string target, string expected, string observed, string message)
        => new() { Stage = stage, Target = target, Expected = expected, Observed = observed, Severity = MmsReportVerificationSeverity.Fail, Message = message };

    private static bool? ParseReportBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        if (bool.TryParse(text, out var parsed))
            return parsed;

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    private static void AddReservationVerificationCheck(
        List<MmsReportVerificationCheck> checks,
        MmsReportRcbSnapshot snapshot,
        string stage)
    {
        var resvFlag = ParseReportBool(snapshot.ReservationState);
        var resvTimer = ParsePositiveInteger(snapshot.ReservationTimeSeconds);
        var rptEna = ParseReportBool(snapshot.EnabledState);
        var observed = $"Resv={TextOrDash(snapshot.ReservationState)} ResvTms={TextOrDash(snapshot.ReservationTimeSeconds)}";

        if (resvFlag == true)
        {
            checks.Add(FailCheck(
                stage,
                $"{snapshot.Reference}.reservation",
                "not active",
                observed,
                "RCB reservation flag is active before selection or after cleanup."));
            return;
        }

        if (resvTimer == true)
        {
            if (snapshot.Mode.Equals("BRCB", StringComparison.OrdinalIgnoreCase) && rptEna == false)
            {
                checks.Add(WarningCheck(
                    stage,
                    $"{snapshot.Reference}.reservation",
                    "not active or lease-only",
                    observed,
                    "BRCB ResvTms lease timer is still visible while RptEna=false. Treat as relay ownership lease/timeout behavior, not cleanup failure."));
                return;
            }

            checks.Add(FailCheck(
                stage,
                $"{snapshot.Reference}.reservation",
                "not active",
                observed,
                "RCB reservation timer is active before selection or after cleanup."));
            return;
        }

        if (resvFlag.HasValue || resvTimer.HasValue)
        {
            checks.Add(PassCheck(
                stage,
                $"{snapshot.Reference}.reservation",
                "not active",
                observed,
                "RCB reservation readback verified as inactive."));
        }
    }

    private static bool? ParsePositiveInteger(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        return ulong.TryParse(text, out var number) ? number > 0 : null;
    }

    private static string NormalizeReportReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private async Task<MmsReportAttributeWriteStep> WriteReportAttributeAsync(
        MmsReportControlCandidate rcb,
        string attribute,
        MmsDataValue value,
        CancellationToken cancellationToken)
    {
        var reference = MmsObjectReference.Parse($"{rcb.Reference}.{attribute}", rcb.FunctionalConstraint);
        var result = await WriteSingleVariableAsync(reference, value, cancellationToken).ConfigureAwait(false);
        return new MmsReportAttributeWriteStep
        {
            Attribute = attribute,
            Reference = reference.ToString(),
            Attempted = true,
            IsSuccess = result.IsSuccess,
            Message = result.Message
        };
    }

    private async Task<MmsReportAttributeWriteStep> TryWriteReportAttributeForCleanupAsync(
        MmsReportControlCandidate rcb,
        string attribute,
        MmsDataValue value,
        CancellationToken cancellationToken)
    {
        try
        {
            var first = await WriteReportAttributeAsync(rcb, attribute, value, cancellationToken).ConfigureAwait(false);
            if (first.IsSuccess || IsTransportConnected)
                return first;

            var reconnected = await TryReconnectForCleanupAsync().ConfigureAwait(false);
            if (!reconnected)
            {
                return new MmsReportAttributeWriteStep
                {
                    Attribute = first.Attribute,
                    Reference = first.Reference,
                    Attempted = true,
                    IsSuccess = false,
                    Message = $"cleanup reconnect failed. First attempt: {first.Message}"
                };
            }

            var retry = await WriteReportAttributeAsync(rcb, attribute, value, cancellationToken).ConfigureAwait(false);
            return new MmsReportAttributeWriteStep
            {
                Attribute = retry.Attribute,
                Reference = retry.Reference,
                Attempted = true,
                IsSuccess = retry.IsSuccess,
                Message = retry.IsSuccess
                    ? $"cleanup retry after reconnect succeeded. First attempt: {first.Message}"
                    : $"cleanup retry after reconnect failed: {retry.Message}. First attempt: {first.Message}"
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            if (!IsTransportConnected && await TryReconnectForCleanupAsync().ConfigureAwait(false))
            {
                try
                {
                    var retry = await WriteReportAttributeAsync(rcb, attribute, value, cancellationToken).ConfigureAwait(false);
                    return new MmsReportAttributeWriteStep
                    {
                        Attribute = retry.Attribute,
                        Reference = retry.Reference,
                        Attempted = true,
                        IsSuccess = retry.IsSuccess,
                        Message = retry.IsSuccess
                            ? $"cleanup retry after reconnect succeeded. First exception: {ex.GetType().Name}: {ex.Message}"
                            : $"cleanup retry after reconnect failed: {retry.Message}. First exception: {ex.GetType().Name}: {ex.Message}"
                    };
                }
                catch (Exception retryEx) when (retryEx is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    return new MmsReportAttributeWriteStep
                    {
                        Attribute = attribute,
                        Reference = $"{rcb.Reference}.{attribute}",
                        Attempted = true,
                        IsSuccess = false,
                        Message = $"cleanup retry after reconnect threw {retryEx.GetType().Name}: {retryEx.Message}. First exception: {ex.GetType().Name}: {ex.Message}"
                    };
                }
            }

            return new MmsReportAttributeWriteStep
            {
                Attribute = attribute,
                Reference = $"{rcb.Reference}.{attribute}",
                Attempted = true,
                IsSuccess = false,
                Message = $"cleanup write failed: {ex.GetType().Name}: {ex.Message}"
            };
        };
    }

    private async Task<bool> TryReconnectForCleanupAsync()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await AssociateAsync(resetAssociationDiagnostics: false, cleanupTimeout.Token).ConfigureAwait(false);
                if (IsMmsInitiated && IsTransportConnected)
                    return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
            }
        }

        return false;
    }

    private static MmsReportAttributeWriteStep ToWriteStep(string attribute, string reference, bool success, string message)
        => new()
        {
            Attribute = attribute,
            Reference = reference,
            Attempted = true,
            IsSuccess = success,
            Message = message
        };

    private static string ToReportDataSetAttributeValue(string dataSetReference)
    {
        if (string.IsNullOrWhiteSpace(dataSetReference))
            return string.Empty;

        var (domain, itemName) = MmsDataSetDirectoryRequest.ParseDataSetReference(dataSetReference);
        return $"{domain}/{itemName}";
    }
}
