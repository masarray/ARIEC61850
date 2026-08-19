namespace AR.Iec61850.Mms;

/// <summary>
/// G2 production-readiness states. NVL qualification evidence can advance only through
/// EnvelopeQualified. RCB/report states are reserved for later G2 gates and must not be
/// inferred from NamedVariableList evidence.
/// </summary>
public enum MmsDynamicReportQualificationState
{
    Advertised,
    SingleMemberProven,
    EnvelopeQualified,
    RcbActivationProven,
    InformationReportProven,
    ProductionEligible
}

public sealed class MmsDynamicDataSetQualificationLadderOptions
{
    public IReadOnlyList<int> Milestones { get; init; } = [1, 4, 8, 16, 32];
    public int ApplicationSafetyMemberLimit { get; init; } =
        MmsDynamicDataSetQualificationProbeOptions.DefaultApplicationSafetyMemberLimit;
    public bool IncludeTerminalCandidateCount { get; init; }
}

public sealed class MmsDynamicDataSetQualificationBatch
{
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public int MemberCount => MemberReferences.Count;
}

public sealed class MmsDynamicDataSetQualificationAttemptEvidence
{
    public string AttemptId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public int DefineRequestByteCount { get; init; }
    public int? NegotiatedMaxMmsPduSize { get; init; }
    public bool RequestWithinKnownNegotiatedPdu { get; init; } = true;
    public bool IsSuccess { get; init; }
    public MmsDynamicDataSetQualificationFailureStage FailureStage { get; init; }
    public bool DynamicMutationAttempted { get; init; }
    public bool AssociationSurvived { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Diagnostic { get; init; } = string.Empty;

    public int MemberCount => MemberReferences.Count;

    public bool IsQualificationSuccess =>
        IsSuccess &&
        FailureStage == MmsDynamicDataSetQualificationFailureStage.None &&
        AssociationSurvived &&
        CleanupSucceeded &&
        RequestWithinKnownNegotiatedPdu;

    public bool RequiresFreshAssociation =>
        !AssociationSurvived ||
        (DynamicMutationAttempted && !CleanupSucceeded);

    public bool IsIsolatedRejectedSingleMember =>
        MemberCount == 1 &&
        !IsSuccess &&
        FailureStage is not MmsDynamicDataSetQualificationFailureStage.None and
            not MmsDynamicDataSetQualificationFailureStage.Preflight &&
        AssociationSurvived &&
        CleanupSucceeded;

    public static MmsDynamicDataSetQualificationAttemptEvidence FromProbeResult(
        string attemptId,
        MmsDynamicDataSetQualificationProbeResult result,
        DateTimeOffset? observedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(result);

        return new MmsDynamicDataSetQualificationAttemptEvidence
        {
            AttemptId = attemptId.Trim(),
            ObservedAtUtc = observedAtUtc ?? DateTimeOffset.UtcNow,
            DataSetReference = result.DataSetReference,
            MemberReferences = result.RequestedMemberReferences.ToArray(),
            DefineRequestByteCount = result.DefineRequestByteCount,
            NegotiatedMaxMmsPduSize = result.NegotiatedMaxMmsPduSize,
            RequestWithinKnownNegotiatedPdu = result.RequestWithinKnownNegotiatedPdu,
            IsSuccess = result.IsSuccess,
            FailureStage = result.FailureStage,
            DynamicMutationAttempted = result.DynamicMutationAttempted,
            AssociationSurvived = result.AssociationSurvived,
            CleanupSucceeded = result.CleanupSucceeded,
            Diagnostic = result.Summary
        };
    }
}

public sealed class MmsDynamicDataSetQualificationAssessment
{
    public MmsDynamicReportQualificationState State { get; init; } = MmsDynamicReportQualificationState.Advertised;
    public IReadOnlyList<MmsDynamicDataSetQualificationAttemptEvidence> Attempts { get; init; } =
        Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>();
    public int LargestProvenMemberCount { get; init; }
    public int LargestProvenDefineRequestByteCount { get; init; }
    public string LargestProvenAttemptId { get; init; } = string.Empty;
    public bool HasMultiMemberEnvelopeCandidate { get; init; }
    public bool RequiresFreshAssociation { get; init; }
    public IReadOnlyList<string> IsolatedRejectedMembers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Exact evidence for one explicitly accepted multi-member envelope. This object does not
/// generalize the result to unrelated members or a device model. G2.3 owns persisted profile
/// identity and safe generalization rules.
/// </summary>
public sealed class MmsDynamicDataSetQualifiedEnvelope
{
    public MmsDynamicReportQualificationState State { get; init; } = MmsDynamicReportQualificationState.EnvelopeQualified;
    public string SourceAttemptId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> ExactProvenMemberReferences { get; init; } = Array.Empty<string>();
    public int ProvenMemberCount { get; init; }
    public int ProvenDefineRequestByteCount { get; init; }
    public int? NegotiatedMaxMmsPduSize { get; init; }
    public DateTimeOffset ProvenAtUtc { get; init; }
}

public static class MmsDynamicDataSetQualificationLadder
{
    public static IReadOnlyList<MmsDynamicDataSetQualificationBatch> BuildMilestoneBatches(
        IReadOnlyList<string> candidateMemberReferences,
        MmsDynamicDataSetQualificationLadderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candidateMemberReferences);
        options ??= new MmsDynamicDataSetQualificationLadderOptions();
        ValidateOptions(options);

        var candidates = ValidateAndNormalizeCandidates(candidateMemberReferences);
        if (candidates.Length == 0)
            return Array.Empty<MmsDynamicDataSetQualificationBatch>();

        var upperBound = Math.Min(candidates.Length, options.ApplicationSafetyMemberLimit);
        var requestedCounts = options.Milestones
            .Where(count => count <= upperBound)
            .Distinct()
            .ToList();

        if (options.IncludeTerminalCandidateCount &&
            upperBound > 1 &&
            !requestedCounts.Contains(upperBound))
        {
            requestedCounts.Add(upperBound);
        }

        return requestedCounts
            .OrderBy(count => count)
            .Select(count => new MmsDynamicDataSetQualificationBatch
            {
                Label = $"milestone-{count}",
                MemberReferences = candidates.Take(count).ToArray()
            })
            .ToArray();
    }

    public static IReadOnlyList<MmsDynamicDataSetQualificationBatch> BisectFailedBatch(
        IReadOnlyList<string> failedMemberReferences)
    {
        ArgumentNullException.ThrowIfNull(failedMemberReferences);
        var members = ValidateAndNormalizeCandidates(failedMemberReferences);
        if (members.Length <= 1)
            return Array.Empty<MmsDynamicDataSetQualificationBatch>();

        var leftCount = members.Length / 2;
        var rightCount = members.Length - leftCount;

        return
        [
            new MmsDynamicDataSetQualificationBatch
            {
                Label = $"bisect-left-{leftCount}",
                MemberReferences = members.Take(leftCount).ToArray()
            },
            new MmsDynamicDataSetQualificationBatch
            {
                Label = $"bisect-right-{rightCount}",
                MemberReferences = members.Skip(leftCount).ToArray()
            }
        ];
    }

    public static MmsDynamicDataSetQualificationAssessment Assess(
        IEnumerable<MmsDynamicDataSetQualificationAttemptEvidence> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var materialized = attempts
            .OrderBy(attempt => attempt.ObservedAtUtc)
            .ThenBy(attempt => attempt.AttemptId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ValidateAttempts(materialized);

        var successful = materialized
            .Where(attempt => attempt.IsQualificationSuccess)
            .OrderByDescending(attempt => attempt.MemberCount)
            .ThenByDescending(attempt => attempt.DefineRequestByteCount)
            .ThenBy(attempt => attempt.AttemptId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var largest = successful.FirstOrDefault();
        var singleMemberProven = successful.Any(attempt => attempt.MemberCount == 1);
        var rejectedSingles = materialized
            .Where(attempt => attempt.IsIsolatedRejectedSingleMember)
            .SelectMany(attempt => attempt.MemberReferences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiresFreshAssociation = materialized.Any(attempt => attempt.RequiresFreshAssociation);

        var warnings = new List<string>();
        if (requiresFreshAssociation)
        {
            warnings.Add(
                "At least one qualification attempt lost association continuity or failed cleanup. A fresh MMS association is required before further qualification mutation.");
        }
        if (rejectedSingles.Length > 0)
        {
            warnings.Add(
                $"{rejectedSingles.Length} member(s) were isolated as single-member qualification failures and must not be generalized as safe dynamic members.");
        }
        if (largest is not null && largest.MemberCount > 1)
        {
            warnings.Add(
                $"Multi-member evidence exists at {largest.MemberCount} member(s), but it remains an envelope candidate until explicitly accepted; it is not production dynamic-report permission.");
        }

        return new MmsDynamicDataSetQualificationAssessment
        {
            State = singleMemberProven
                ? MmsDynamicReportQualificationState.SingleMemberProven
                : MmsDynamicReportQualificationState.Advertised,
            Attempts = materialized,
            LargestProvenMemberCount = largest?.MemberCount ?? 0,
            LargestProvenDefineRequestByteCount = largest?.DefineRequestByteCount ?? 0,
            LargestProvenAttemptId = largest?.AttemptId ?? string.Empty,
            HasMultiMemberEnvelopeCandidate = successful.Any(attempt => attempt.MemberCount > 1),
            RequiresFreshAssociation = requiresFreshAssociation,
            IsolatedRejectedMembers = rejectedSingles,
            Warnings = warnings
        };
    }

    public static MmsDynamicDataSetQualifiedEnvelope AcceptExactEnvelope(
        MmsDynamicDataSetQualificationAssessment assessment,
        string successfulAttemptId)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(successfulAttemptId);

        var attempt = assessment.Attempts.FirstOrDefault(candidate =>
            candidate.AttemptId.Equals(successfulAttemptId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (attempt is null)
            throw new ArgumentException($"Qualification attempt '{successfulAttemptId}' was not found.", nameof(successfulAttemptId));
        if (!attempt.IsQualificationSuccess)
            throw new InvalidOperationException("Only an association-surviving, cleanup-safe successful qualification attempt can be accepted as an envelope.");
        if (attempt.MemberCount <= 1)
            throw new InvalidOperationException("A one-member probation can prove the service primitive but cannot be accepted as a multi-member envelope.");

        return new MmsDynamicDataSetQualifiedEnvelope
        {
            State = MmsDynamicReportQualificationState.EnvelopeQualified,
            SourceAttemptId = attempt.AttemptId,
            DataSetReference = attempt.DataSetReference,
            ExactProvenMemberReferences = attempt.MemberReferences.ToArray(),
            ProvenMemberCount = attempt.MemberCount,
            ProvenDefineRequestByteCount = attempt.DefineRequestByteCount,
            NegotiatedMaxMmsPduSize = attempt.NegotiatedMaxMmsPduSize,
            ProvenAtUtc = attempt.ObservedAtUtc
        };
    }

    public static void ValidateOptions(MmsDynamicDataSetQualificationLadderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ApplicationSafetyMemberLimit < 1 ||
            options.ApplicationSafetyMemberLimit > MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"ApplicationSafetyMemberLimit must be between 1 and {MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit}.");
        }

        if (options.Milestones is null || options.Milestones.Count == 0)
            throw new ArgumentException("At least one qualification milestone is required.", nameof(options));
        if (options.Milestones[0] != 1)
            throw new ArgumentException("The qualification ladder must start at one member.", nameof(options));

        var previous = 0;
        foreach (var milestone in options.Milestones)
        {
            if (milestone <= previous)
                throw new ArgumentException("Qualification milestones must be strictly increasing.", nameof(options));
            if (milestone > options.ApplicationSafetyMemberLimit)
                throw new ArgumentException("Qualification milestone exceeds the application safety member limit.", nameof(options));
            previous = milestone;
        }
    }

    private static string[] ValidateAndNormalizeCandidates(IReadOnlyList<string> candidateMemberReferences)
    {
        var normalized = candidateMemberReferences
            .Select((reference, index) => string.IsNullOrWhiteSpace(reference)
                ? throw new ArgumentException($"Qualification candidate at index {index} is empty.", nameof(candidateMemberReferences))
                : reference.Trim())
            .ToArray();

        var duplicate = normalized
            .GroupBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Qualification candidate is duplicated: {duplicate.Key}", nameof(candidateMemberReferences));

        return normalized;
    }

    private static void ValidateAttempts(IReadOnlyList<MmsDynamicDataSetQualificationAttemptEvidence> attempts)
    {
        var duplicateAttemptId = attempts
            .Where(attempt => !string.IsNullOrWhiteSpace(attempt.AttemptId))
            .GroupBy(attempt => attempt.AttemptId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAttemptId is not null)
            throw new ArgumentException($"Qualification attempt ID is duplicated: {duplicateAttemptId.Key}", nameof(attempts));

        foreach (var attempt in attempts)
        {
            if (string.IsNullOrWhiteSpace(attempt.AttemptId))
                throw new ArgumentException("Qualification attempt ID is required.", nameof(attempts));
            if (attempt.MemberReferences.Count == 0)
                throw new ArgumentException($"Qualification attempt '{attempt.AttemptId}' has no members.", nameof(attempts));
        }
    }
}
