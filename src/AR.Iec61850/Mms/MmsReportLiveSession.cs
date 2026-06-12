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
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsReportHeader
{
    public string ReportId { get; init; } = string.Empty;
    public MmsReportOptionalFields OptionalFields { get; init; } = new();
    public ulong? SequenceNumber { get; init; }
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

public sealed class MmsStaticReportSessionResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<MmsReportFrame> Reports { get; init; } = Array.Empty<MmsReportFrame>();
    public IReadOnlyList<MmsReportPollRead> PollReads { get; init; } = Array.Empty<MmsReportPollRead>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public MmsReportSessionDiagnostics Diagnostics { get; init; } = new();
    public string Message { get; init; } = string.Empty;
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
            Message = mapped.Message ?? decoded.Message,
            ResponseHexPreview = decoded.ResponseHexPreview
        };
    }

    private readonly record struct ReportValueMapping(
        bool IsMapped,
        MmsReportHeader Header,
        IReadOnlyList<MmsReportValue> Values,
        IReadOnlyList<int> IncludedDataSetIndexes,
        int? InclusionBitstringItemIndex,
        string? Message);

    private static bool TryMapIec61850ReportValues(
        IReadOnlyList<MmsInformationReportItem> items,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        out ReportValueMapping mapping)
    {
        mapping = new ReportValueMapping(false, new MmsReportHeader(), Array.Empty<MmsReportValue>(), Array.Empty<int>(), null, null);
        if (items.Count == 0 || members.Count == 0)
            return false;

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
                $"IEC 61850 InformationReport mapped {mapped.Count}/{members.Count} included DataSet value(s). inclusionItem={index}, included=[{string.Join(",", includedMemberIndexes)}], rawAccessResults={items.Count}, header={header.Summary}.");
            return true;
        }

        return false;
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
        TimeSpan? pollInterval = null)
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
        var reservationTouched = false;
        var enabledByThisClient = false;

        try
        {
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
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Message = "RptEna=true failed; report session was not started."
                };
            }

            if (triggerGeneralInterrogation && rcb.Attributes.Contains("GI", StringComparer.OrdinalIgnoreCase))
            {
                var gi = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed. Waiting for spontaneous/integrity reports only.");
            }

            var received = await ReceiveInformationReportsAsync(
                plan.Members,
                listenDuration,
                pollDirectory,
                pollReferences,
                pollInterval,
                pollReads,
                cancellationToken).ConfigureAwait(false);
            reports.AddRange(received);
        }
        finally
        {
            if (enabledByThisClient)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(rcb, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(disable);
            }

            if (reservationTouched)
            {
                var release = rcb.Buffered
                    ? await TryWriteReportAttributeForCleanupAsync(rcb, "ResvTms", MmsDataValue.Unsigned(0), CancellationToken.None).ConfigureAwait(false)
                    : await TryWriteReportAttributeForCleanupAsync(rcb, "Resv", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(release);
            }
        }

        return new MmsStaticReportSessionResult
        {
            IsSuccess = enabledByThisClient,
            WriteSteps = writes,
            Reports = reports,
            PollReads = pollReads,
            Warnings = warnings,
            Diagnostics = MmsReportSessionDiagnostics.Analyze(reports, pollReads, writes),
            Message = $"Static report guarded session complete: writes={writes.Count}, reports={reports.Count}, pollReads={pollReads.Count}."
        };
    }

    public async Task<MmsStaticReportSessionResult> RunGuardedDynamicReportSessionAsync(
        MmsReportSubscriptionPlan plan,
        TimeSpan listenDuration,
        int reserveSeconds = 30,
        bool triggerGeneralInterrogation = true,
        bool deleteDataSetOnCleanup = true,
        CancellationToken cancellationToken = default)
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
        var dataSetCreated = false;
        var reservationTouched = false;
        var enabledByThisClient = false;
        var originalDataSetReference = rcb.DataSetReference;

        try
        {
            var define = await DefineNamedVariableListAsync(
                plan.DataSetReference,
                plan.DynamicPoints.Select(x => x.ToObjectReference()),
                cancellationToken).ConfigureAwait(false);
            writes.Add(ToWriteStep("DefineNamedVariableList", plan.DataSetReference, define.IsSuccess, define.Message));
            dataSetCreated = define.IsSuccess;
            if (!define.IsSuccess)
            {
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Message = "Dynamic DataSet create failed; report session was not started."
                };
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
                    warnings.Add("URCB Resv write failed. Proceeding only if DatSet/RptEna are accepted by the IED.");
            }

            var dataSetValue = ToReportDataSetAttributeValue(plan.DataSetReference);
            var dataSetWrite = await WriteReportAttributeAsync(rcb, "DatSet", MmsDataValue.VisibleString(dataSetValue), cancellationToken).ConfigureAwait(false);
            writes.Add(dataSetWrite);
            if (!dataSetWrite.IsSuccess)
            {
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Message = "RCB.DatSet write failed; report session was not started."
                };
            }

            var enable = await WriteReportAttributeAsync(rcb, "RptEna", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
            writes.Add(enable);
            enabledByThisClient = enable.IsSuccess;
            if (!enable.IsSuccess)
            {
                return new MmsStaticReportSessionResult
                {
                    IsSuccess = false,
                    WriteSteps = writes,
                    Warnings = warnings,
                    Message = "RptEna=true failed; dynamic report session was not started."
                };
            }

            if (triggerGeneralInterrogation && rcb.Attributes.Contains("GI", StringComparer.OrdinalIgnoreCase))
            {
                var gi = await WriteReportAttributeAsync(rcb, "GI", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(gi);
                if (!gi.IsSuccess)
                    warnings.Add("GI=true write failed. Waiting for spontaneous/integrity reports only.");
            }

            var received = await ReceiveInformationReportsAsync(plan.Members, listenDuration, cancellationToken).ConfigureAwait(false);
            reports.AddRange(received);
        }
        finally
        {
            if (enabledByThisClient)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(rcb, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(disable);
            }

            if (dataSetCreated)
            {
                var restoreValue = string.IsNullOrWhiteSpace(originalDataSetReference)
                    ? string.Empty
                    : ToReportDataSetAttributeValue(originalDataSetReference);
                var restore = await TryWriteReportAttributeForCleanupAsync(rcb, "DatSet", MmsDataValue.VisibleString(restoreValue), CancellationToken.None).ConfigureAwait(false);
                writes.Add(restore);
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
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    writes.Add(ToWriteStep("DeleteNamedVariableList", plan.DataSetReference, false, $"cleanup delete failed: {ex.GetType().Name}: {ex.Message}"));
                }
            }
        }

        return new MmsStaticReportSessionResult
        {
            IsSuccess = enabledByThisClient,
            WriteSteps = writes,
            Reports = reports,
            Warnings = warnings,
            Diagnostics = MmsReportSessionDiagnostics.Analyze(reports, Array.Empty<MmsReportPollRead>(), writes),
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
            cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<MmsReportFrame>> ReceiveInformationReportsAsync(
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        TimeSpan duration,
        MmsIedModelDirectory? pollDirectory,
        IReadOnlyList<string>? pollReferences,
        TimeSpan? pollInterval,
        List<MmsReportPollRead>? pollReads,
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

        var nextPollAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

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
