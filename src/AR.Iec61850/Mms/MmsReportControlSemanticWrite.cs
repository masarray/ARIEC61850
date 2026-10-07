using System.Collections.Concurrent;

namespace AR.Iec61850.Mms;

/// <summary>
/// Binds a well-known IEC 61850 Report Control Block scalar field to the exact MMS
/// value kind used on the wire. This is the semantic boundary between reporting
/// policy and MMS encoding: callers request an IEC field/value, not a raw MMS scalar.
///
/// The contract intentionally covers only scalar mutation fields whose semantics are
/// stable across BRCB/URCB implementations. Bit-string and object-reference fields keep
/// their dedicated codecs.
/// </summary>
public readonly record struct MmsReportControlSemanticWrite
{
    private MmsReportControlSemanticWrite(
        string attribute,
        MmsDataValue value,
        MmsDataKind expectedKind,
        string expectedMmsType)
    {
        Attribute = attribute;
        Value = value;
        ExpectedKind = expectedKind;
        ExpectedMmsType = expectedMmsType;
    }

    public string Attribute { get; }
    public MmsDataValue Value { get; }
    public MmsDataKind ExpectedKind { get; }
    public string ExpectedMmsType { get; }

    public static MmsReportControlSemanticWrite ReportEnable(bool enabled)
        => Boolean("RptEna", enabled);

    public static MmsReportControlSemanticWrite GeneralInterrogation(bool requested)
        => Boolean("GI", requested);

    public static MmsReportControlSemanticWrite Reservation(bool reserved)
        => Boolean("Resv", reserved);

    /// <summary>
    /// IEC 61850 BRCB ResvTms is mapped to MMS INTEGER with signed INT16 wire semantics.
    /// Policy about particular reservation-time values belongs above this codec; this
    /// boundary validates only the standardized scalar representation and width.
    /// </summary>
    public static MmsReportControlSemanticWrite ReservationTime(int seconds)
    {
        if (seconds is < short.MinValue or > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                $"BRCB ResvTms must fit the signed IEC INT16 range {short.MinValue}..{short.MaxValue}.");
        }

        return new MmsReportControlSemanticWrite(
            "ResvTms",
            MmsDataValue.Integer(seconds),
            MmsDataKind.Integer,
            "integer");
    }

    public bool IsCompatibleWith(MmsTypeSpecificationNode? liveType)
        => liveType is not null &&
           Value.Kind == ExpectedKind &&
           string.Equals(liveType.MmsType, ExpectedMmsType, StringComparison.OrdinalIgnoreCase);

    private static MmsReportControlSemanticWrite Boolean(string attribute, bool value)
        => new(
            attribute,
            MmsDataValue.Boolean(value),
            MmsDataKind.Boolean,
            "boolean");
}

public enum MmsReportSemanticTypeEvidenceStatus
{
    NotChecked,
    ExactMatch,
    ExactMismatch,
    Unavailable
}

/// <summary>
/// Evidence for one concrete RCB scalar field on the current MMS association.
/// A successful live GetVariableAccessAttributes result is exact protocol evidence;
/// a failed/unsupported probe remains missing evidence and is not converted into a
/// false blocker. An exact contradiction is a positive blocker and prevents mutation.
/// </summary>
public sealed class MmsReportSemanticTypeEvidence
{
    public MmsReportSemanticTypeEvidenceStatus Status { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Attribute { get; init; } = string.Empty;
    public string ExpectedMmsType { get; init; } = string.Empty;
    public string LiveMmsType { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsExact => Status is
        MmsReportSemanticTypeEvidenceStatus.ExactMatch or
        MmsReportSemanticTypeEvidenceStatus.ExactMismatch;

    public bool AllowsMutation => Status != MmsReportSemanticTypeEvidenceStatus.ExactMismatch;
}

public static class MmsReportSemanticTypePolicy
{
    public static MmsReportSemanticTypeEvidence Evaluate(
        MmsReportControlSemanticWrite write,
        string reference,
        MmsVariableAccessAttributesResult? live)
    {
        if (live?.IsSuccess != true || live.TypeSpecification is null)
        {
            return new MmsReportSemanticTypeEvidence
            {
                Status = MmsReportSemanticTypeEvidenceStatus.Unavailable,
                Reference = reference,
                Attribute = write.Attribute,
                ExpectedMmsType = write.ExpectedMmsType,
                LiveMmsType = live?.MmsType ?? string.Empty,
                Source = live?.Source ?? "IEC semantic contract",
                Message = live is null
                    ? "No live TypeSpecification probe was available; the IEC semantic contract remains the write authority."
                    : $"Live TypeSpecification was unavailable ({live.Message}); the IEC semantic contract remains the write authority."
            };
        }

        var exactMatch = write.IsCompatibleWith(live.TypeSpecification);
        return new MmsReportSemanticTypeEvidence
        {
            Status = exactMatch
                ? MmsReportSemanticTypeEvidenceStatus.ExactMatch
                : MmsReportSemanticTypeEvidenceStatus.ExactMismatch,
            Reference = reference,
            Attribute = write.Attribute,
            ExpectedMmsType = write.ExpectedMmsType,
            LiveMmsType = live.MmsType,
            Source = live.Source,
            Message = exactMatch
                ? $"Live MMS TypeSpecification confirms {write.Attribute} as {live.MmsType}."
                : $"Live MMS TypeSpecification reports {write.Attribute} as {live.MmsType}, conflicting with IEC semantic type {write.ExpectedMmsType}."
        };
    }
}

public sealed partial class MmsClientSession
{
    private readonly ConcurrentDictionary<string, MmsReportSemanticTypeEvidence> _reportSemanticTypeEvidence =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Association-stable type evidence must never cross an MMS re-association.
    /// Volatile RCB state is intentionally not cached here.
    /// </summary>
    private void ResetReportSemanticTypeEvidence()
        => _reportSemanticTypeEvidence.Clear();

    /// <summary>
    /// Normal RCB scalar mutation path. The first mutation of each exact field on an
    /// association performs a bounded exact GetVariableAccessAttributes check. Exact
    /// matching evidence is cached for that association; missing evidence does not become
    /// a false blocker, while an exact type contradiction fails closed before mutation.
    /// </summary>
    private async Task<MmsReportAttributeWriteStep> WriteReportSemanticAttributeAsync(
        MmsReportControlCandidate rcb,
        MmsReportControlSemanticWrite write,
        CancellationToken cancellationToken)
    {
        var reference = BuildReportAttributeReference(rcb, write.Attribute);
        var typeEvidence = await GetReportSemanticTypeEvidenceAsync(
            reference,
            write,
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false);

        if (!typeEvidence.AllowsMutation)
        {
            return new MmsReportAttributeWriteStep
            {
                Attribute = write.Attribute,
                Reference = reference.ToString(),
                Attempted = false,
                IsSuccess = false,
                TypeEvidence = typeEvidence,
                Message = $"RCB write blocked before mutation: {typeEvidence.Message}"
            };
        }

        var step = await WriteReportAttributeAsync(
            rcb,
            write.Attribute,
            write.Value,
            cancellationToken).ConfigureAwait(false);

        if (!step.IsSuccess &&
            step.FailureKind == MmsInteropFailureKind.TypeMismatch &&
            IsMmsInitiated)
        {
            typeEvidence = await GetReportSemanticTypeEvidenceAsync(
                reference,
                write,
                forceRefresh: true,
                cancellationToken).ConfigureAwait(false);
        }

        return WithSemanticTypeEvidence(step, typeEvidence);
    }

    /// <summary>
    /// Cleanup never adds a new network preflight. It reuses any exact type evidence
    /// already learned on this association and otherwise uses the standardized semantic
    /// encoder so cleanup is not delayed by optional diagnostics.
    /// </summary>
    private async Task<MmsReportAttributeWriteStep> TryWriteReportSemanticAttributeForCleanupAsync(
        MmsReportControlCandidate rcb,
        MmsReportControlSemanticWrite write,
        CancellationToken cancellationToken)
    {
        var reference = BuildReportAttributeReference(rcb, write.Attribute);
        _reportSemanticTypeEvidence.TryGetValue(ReportSemanticTypeCacheKey(reference), out var typeEvidence);

        var step = await TryWriteReportAttributeForCleanupAsync(
            rcb,
            write.Attribute,
            write.Value,
            cancellationToken).ConfigureAwait(false);

        return typeEvidence is null ? step : WithSemanticTypeEvidence(step, typeEvidence);
    }

    private async Task<MmsReportSemanticTypeEvidence> GetReportSemanticTypeEvidenceAsync(
        MmsObjectReference reference,
        MmsReportControlSemanticWrite write,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var key = ReportSemanticTypeCacheKey(reference);
        if (!forceRefresh && _reportSemanticTypeEvidence.TryGetValue(key, out var cached))
            return cached;

        MmsVariableAccessAttributesResult? live = null;
        try
        {
            live = await GetVariableAccessAttributesAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            live = new MmsVariableAccessAttributesResult
            {
                IsSuccess = false,
                Reference = reference,
                Source = "GetVariableAccessAttributes",
                Message = $"Type preflight failed safely: {ex.GetType().Name}: {ex.Message}"
            };
        }

        var evidence = MmsReportSemanticTypePolicy.Evaluate(write, reference.ToString(), live);
        if (evidence.IsExact)
            _reportSemanticTypeEvidence[key] = evidence;

        return evidence;
    }

    private static MmsObjectReference BuildReportAttributeReference(
        MmsReportControlCandidate rcb,
        string attribute)
        => MmsObjectReference.Parse($"{rcb.Reference}.{attribute}", rcb.FunctionalConstraint);

    private static string ReportSemanticTypeCacheKey(MmsObjectReference reference)
        => $"{reference.Domain}/{reference.Item}";

    private static MmsReportAttributeWriteStep WithSemanticTypeEvidence(
        MmsReportAttributeWriteStep step,
        MmsReportSemanticTypeEvidence typeEvidence)
        => new()
        {
            Attribute = step.Attribute,
            Reference = step.Reference,
            Attempted = step.Attempted,
            IsSuccess = step.IsSuccess,
            FailureCode = step.FailureCode,
            FailureName = step.FailureName,
            FailureKind = step.FailureKind,
            RecoveryHint = step.RecoveryHint,
            TypeEvidence = typeEvidence,
            Message = $"{step.Message} Type evidence: {typeEvidence.Message}"
        };
}
