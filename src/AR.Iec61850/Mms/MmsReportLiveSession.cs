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

    public string MemberReference => Member?.UserReference ?? $"report-item[{Index}]";
    public string DisplayValue => Value == null
        ? $"failure={FailureCode}"
        : MmsDataValueRenderer.ToCompactString(Value, Member?.UserReference);
}

public sealed class MmsReportFrame
{
    public DateTimeOffset ReceivedAt { get; init; }
    public IReadOnlyList<MmsReportValue> Values { get; init; } = Array.Empty<MmsReportValue>();
    public int RawAccessResultCount { get; init; }
    public int? InclusionBitstringItemIndex { get; init; }
    public IReadOnlyList<int> IncludedDataSetIndexes { get; init; } = Array.Empty<int>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsStaticReportSessionResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<MmsReportFrame> Reports { get; init; } = Array.Empty<MmsReportFrame>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
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
        IReadOnlyList<MmsReportValue> Values,
        IReadOnlyList<int> IncludedDataSetIndexes,
        int? InclusionBitstringItemIndex,
        string? Message);

    private static bool TryMapIec61850ReportValues(
        IReadOnlyList<MmsInformationReportItem> items,
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        out ReportValueMapping mapping)
    {
        mapping = new ReportValueMapping(false, Array.Empty<MmsReportValue>(), Array.Empty<int>(), null, null);
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
            for (var includedOffset = 0; includedOffset < includedMemberIndexes.Count; includedOffset++)
            {
                var memberIndex = includedMemberIndexes[includedOffset];
                var valueItem = items[valuesStart + includedOffset];
                mapped.Add(new MmsReportValue
                {
                    Index = memberIndex,
                    Member = memberIndex >= 0 && memberIndex < members.Count ? members[memberIndex] : null,
                    Value = valueItem.Value,
                    FailureCode = valueItem.FailureCode
                });
            }

            mapping = new ReportValueMapping(
                true,
                mapped,
                includedMemberIndexes,
                index,
                $"IEC 61850 InformationReport mapped {mapped.Count}/{members.Count} included DataSet value(s). inclusionItem={index}, included=[{string.Join(",", includedMemberIndexes)}], rawAccessResults={items.Count}.");
            return true;
        }

        return false;
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
}

public sealed partial class MmsClientSession
{
    public async Task<MmsStaticReportSessionResult> RunGuardedStaticReportSessionAsync(
        MmsReportSubscriptionPlan plan,
        TimeSpan listenDuration,
        int reserveSeconds = 30,
        bool triggerGeneralInterrogation = true,
        CancellationToken cancellationToken = default)
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
            Warnings = warnings,
            Message = $"Static report guarded session complete: writes={writes.Count}, reports={reports.Count}."
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
            Message = $"Dynamic report guarded session complete: writes={writes.Count}, reports={reports.Count}."
        };
    }

    public async Task<IReadOnlyList<MmsReportFrame>> ReceiveInformationReportsAsync(
        IReadOnlyList<MmsDataSetDirectoryMember> members,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        members ??= Array.Empty<MmsDataSetDirectoryMember>();
        var reports = new List<MmsReportFrame>();
        var deadline = DateTimeOffset.UtcNow + (duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : duration);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            if (TryDequeueInformationReport(out var queuedPayload))
            {
                TryAppendInformationReport(queuedPayload, members, reports);
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
