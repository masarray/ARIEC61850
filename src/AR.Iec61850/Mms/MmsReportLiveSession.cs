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
        var reservedByThisClient = false;
        var enabledByThisClient = false;

        try
        {
            if (rcb.Buffered && rcb.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(rcb, "ResvTms", MmsDataValue.Unsigned((ulong)Math.Max(1, reserveSeconds)), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservedByThisClient = reserve.IsSuccess;
                if (!reserve.IsSuccess)
                    warnings.Add("BRCB ResvTms write failed. Some IEDs allow RptEna without explicit reservation; proceeding guarded.");
            }
            else if (!rcb.Buffered && rcb.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
            {
                var reserve = await WriteReportAttributeAsync(rcb, "Resv", MmsDataValue.Boolean(true), cancellationToken).ConfigureAwait(false);
                writes.Add(reserve);
                reservedByThisClient = reserve.IsSuccess;
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
            if (IsMmsInitiated && enabledByThisClient)
            {
                var disable = await TryWriteReportAttributeForCleanupAsync(rcb, "RptEna", MmsDataValue.Boolean(false), CancellationToken.None).ConfigureAwait(false);
                writes.Add(disable);
            }

            if (IsMmsInitiated && reservedByThisClient)
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

            var receiveWindow = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            if (receiveWindow < TimeSpan.FromMilliseconds(50))
                receiveWindow = TimeSpan.FromMilliseconds(50);

            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(receiveWindow);

            byte[] payload;
            try
            {
                payload = await _cotp.ReceiveDataAsync(receiveTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (!MmsInformationReportDecoder.IsInformationReport(payload))
                continue;

            var decoded = MmsInformationReportDecoder.Decode(payload);
            var values = decoded.Items.Select(item => new MmsReportValue
            {
                Index = item.Index,
                Member = item.Index >= 0 && item.Index < members.Count ? members[item.Index] : null,
                Value = item.Value,
                FailureCode = item.FailureCode
            }).ToArray();

            reports.Add(new MmsReportFrame
            {
                ReceivedAt = DateTimeOffset.UtcNow,
                Values = values,
                Message = decoded.Message,
                ResponseHexPreview = decoded.ResponseHexPreview
            });
        }

        return reports;
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
            return await WriteReportAttributeAsync(rcb, attribute, value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            return new MmsReportAttributeWriteStep
            {
                Attribute = attribute,
                Reference = $"{rcb.Reference}.{attribute}",
                Attempted = true,
                IsSuccess = false,
                Message = $"cleanup write failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }
}
