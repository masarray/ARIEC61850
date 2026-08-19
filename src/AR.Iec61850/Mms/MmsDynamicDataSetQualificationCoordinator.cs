namespace AR.Iec61850.Mms;

/// <summary>
/// Qualification execution is disabled by default. The caller must opt into an explicit
/// commissioning operation; normal reporting startup must never reach this coordinator.
/// </summary>
public enum MmsDynamicDataSetQualificationExecutionMode
{
    Disabled,
    ExplicitCommissioning
}

public sealed class MmsDynamicDataSetQualificationCoordinatorOptions
{
    public MmsDynamicDataSetQualificationExecutionMode ExecutionMode { get; init; } =
        MmsDynamicDataSetQualificationExecutionMode.Disabled;
    public int MaxAttempts { get; init; } = 16;
    public bool LocalizeFailedBatch { get; init; } = true;
    public MmsDynamicDataSetQualificationLadderOptions Ladder { get; init; } = new();
    public MmsDynamicDataSetQualificationProbeOptions Probe { get; init; } = new();
}

public sealed class MmsDynamicDataSetQualificationCoordinatorResult
{
    public bool IsBlocked { get; init; }
    public string BlockReason { get; init; } = string.Empty;
    public IReadOnlyList<MmsDynamicDataSetQualificationAttemptEvidence> Attempts { get; init; } =
        Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>();
    public MmsDynamicDataSetQualificationAssessment Assessment { get; init; } = new();
    public IReadOnlyList<int> SuccessfulMilestoneMemberCounts { get; init; } = Array.Empty<int>();
    public bool ReachedLargestRequestedMilestone { get; init; }
    public bool FailureLocalizationAttempted { get; init; }
    public int FailedMilestoneMemberCount { get; init; }
    public bool AttemptBudgetExhausted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool RequiresFreshAssociation => Assessment.RequiresFreshAssociation;
    public string EnvelopeCandidateAttemptId => Assessment.HasMultiMemberEnvelopeCandidate
        ? Assessment.LargestProvenAttemptId
        : string.Empty;

    public string Summary => IsBlocked
        ? $"Dynamic DataSet qualification blocked: {BlockReason}"
        : $"Dynamic DataSet qualification attempts={Attempts.Count}, successfulMilestones={string.Join(",", SuccessfulMilestoneMemberCounts)}, " +
          $"largestSafeMembers={Assessment.LargestProvenMemberCount}, envelopeCandidate={EnvelopeCandidateAttemptId}, " +
          $"freshAssociationRequired={RequiresFreshAssociation}, attemptBudgetExhausted={AttemptBudgetExhausted}.";
}

public static class MmsDynamicDataSetQualificationCoordinator
{
    public static async Task<MmsDynamicDataSetQualificationCoordinatorResult> RunAsync(
        IReadOnlyList<MmsObjectReference> candidateMembers,
        Func<IReadOnlyList<MmsObjectReference>, CancellationToken, Task<MmsDynamicDataSetQualificationProbeResult>> probeExecutor,
        MmsDynamicDataSetQualificationCoordinatorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateMembers);
        ArgumentNullException.ThrowIfNull(probeExecutor);
        options ??= new MmsDynamicDataSetQualificationCoordinatorOptions();
        ValidateOptions(options);

        var exactCandidates = NormalizeCandidates(candidateMembers);
        var candidateReferences = exactCandidates
            .Select(ToMemberReference)
            .ToArray();

        if (options.ExecutionMode != MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning)
        {
            var assessment = MmsDynamicDataSetQualificationLadder.Assess(
                Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>());
            return new MmsDynamicDataSetQualificationCoordinatorResult
            {
                IsBlocked = true,
                BlockReason = "ExecutionMode is Disabled. Dynamic DataSet qualification requires an explicit commissioning invocation.",
                Assessment = assessment,
                Warnings = ["No MMS qualification mutation was attempted."]
            };
        }

        if (exactCandidates.Length == 0)
        {
            var assessment = MmsDynamicDataSetQualificationLadder.Assess(
                Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>());
            return new MmsDynamicDataSetQualificationCoordinatorResult
            {
                IsBlocked = true,
                BlockReason = "No exact resolved candidate members were supplied.",
                Assessment = assessment,
                Warnings = ["No MMS qualification mutation was attempted."]
            };
        }

        var ladder = MmsDynamicDataSetQualificationLadder.BuildMilestoneBatches(
            candidateReferences,
            options.Ladder);
        if (ladder.Count == 0)
        {
            var assessment = MmsDynamicDataSetQualificationLadder.Assess(
                Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>());
            return new MmsDynamicDataSetQualificationCoordinatorResult
            {
                IsBlocked = true,
                BlockReason = "The qualification ladder produced no bounded milestone batches.",
                Assessment = assessment,
                Warnings = ["No MMS qualification mutation was attempted."]
            };
        }

        var objectByReference = exactCandidates.ToDictionary(
            ToMemberReference,
            member => member,
            StringComparer.OrdinalIgnoreCase);
        var attempts = new List<MmsDynamicDataSetQualificationAttemptEvidence>();
        var successfulMilestones = new List<int>();
        var successfulExactSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var failedMilestoneMemberCount = 0;
        var localizationAttempted = false;
        var budgetExhausted = false;
        var attemptSequence = 0;

        foreach (var milestone in ladder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempts.Count >= options.MaxAttempts)
            {
                budgetExhausted = true;
                warnings.Add($"Qualification stopped before milestone {milestone.MemberCount}: MaxAttempts={options.MaxAttempts} was reached.");
                break;
            }

            var execution = await ExecuteBatchAsync(
                milestone,
                "milestone",
                objectByReference,
                successfulExactSets,
                probeExecutor,
                attempts,
                options.MaxAttempts,
                attemptSequence,
                cancellationToken).ConfigureAwait(false);
            attemptSequence = execution.NextAttemptSequence;
            budgetExhausted |= execution.AttemptBudgetExhausted;

            if (execution.SkippedAsAlreadyProven)
            {
                successfulMilestones.Add(milestone.MemberCount);
                continue;
            }

            var milestoneAttempt = execution.Attempt;
            if (milestoneAttempt is null)
                break;

            if (milestoneAttempt.IsQualificationSuccess)
            {
                successfulMilestones.Add(milestone.MemberCount);
                successfulExactSets.Add(MemberSetKey(milestone.MemberReferences));
                continue;
            }

            failedMilestoneMemberCount = milestone.MemberCount;

            if (milestoneAttempt.RequiresFreshAssociation)
            {
                warnings.Add(
                    $"Qualification stopped after failed {milestone.MemberCount}-member milestone because association continuity or cleanup was not proven. Start a fresh MMS association before another commissioning attempt.");
                break;
            }

            if (!options.LocalizeFailedBatch || milestone.MemberCount <= 1)
                break;

            localizationAttempted = true;
            var localization = await LocalizeFailureAsync(
                milestone,
                objectByReference,
                successfulExactSets,
                probeExecutor,
                attempts,
                options.MaxAttempts,
                attemptSequence,
                cancellationToken).ConfigureAwait(false);
            attemptSequence = localization.NextAttemptSequence;
            budgetExhausted |= localization.AttemptBudgetExhausted;
            warnings.AddRange(localization.Warnings);
            break; // Never continue to a larger milestone after any failed milestone.
        }

        var assessmentResult = MmsDynamicDataSetQualificationLadder.Assess(attempts);
        warnings.AddRange(assessmentResult.Warnings);
        if (assessmentResult.HasMultiMemberEnvelopeCandidate)
        {
            warnings.Add(
                $"Largest multi-member qualification evidence is attempt '{assessmentResult.LargestProvenAttemptId}' at {assessmentResult.LargestProvenMemberCount} member(s). It remains an envelope candidate until explicitly accepted and is not RCB/report permission.");
        }

        return new MmsDynamicDataSetQualificationCoordinatorResult
        {
            Attempts = attempts.ToArray(),
            Assessment = assessmentResult,
            SuccessfulMilestoneMemberCounts = successfulMilestones.ToArray(),
            ReachedLargestRequestedMilestone =
                !budgetExhausted &&
                failedMilestoneMemberCount == 0 &&
                successfulMilestones.Count == ladder.Count &&
                successfulMilestones[^1] == ladder[^1].MemberCount,
            FailureLocalizationAttempted = localizationAttempted,
            FailedMilestoneMemberCount = failedMilestoneMemberCount,
            AttemptBudgetExhausted = budgetExhausted,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static async Task<BatchExecutionResult> ExecuteBatchAsync(
        MmsDynamicDataSetQualificationBatch batch,
        string attemptKind,
        IReadOnlyDictionary<string, MmsObjectReference> objectByReference,
        ISet<string> successfulExactSets,
        Func<IReadOnlyList<MmsObjectReference>, CancellationToken, Task<MmsDynamicDataSetQualificationProbeResult>> probeExecutor,
        ICollection<MmsDynamicDataSetQualificationAttemptEvidence> attempts,
        int maxAttempts,
        int attemptSequence,
        CancellationToken cancellationToken)
    {
        var key = MemberSetKey(batch.MemberReferences);
        if (successfulExactSets.Contains(key))
        {
            return new BatchExecutionResult
            {
                SkippedAsAlreadyProven = true,
                NextAttemptSequence = attemptSequence
            };
        }

        if (attempts.Count >= maxAttempts)
        {
            return new BatchExecutionResult
            {
                AttemptBudgetExhausted = true,
                NextAttemptSequence = attemptSequence
            };
        }

        var memberObjects = batch.MemberReferences
            .Select(reference => objectByReference.TryGetValue(reference, out var member)
                ? member
                : throw new InvalidOperationException($"Qualification batch references unknown candidate '{reference}'."))
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        var probe = await probeExecutor(memberObjects, cancellationToken).ConfigureAwait(false);
        attemptSequence++;
        var attemptId = $"{attemptKind}-{attemptSequence:D2}-m{batch.MemberCount}";
        var evidence = MmsDynamicDataSetQualificationAttemptEvidence.FromProbeResult(
            attemptId,
            probe,
            DateTimeOffset.UtcNow);
        attempts.Add(evidence);
        if (evidence.IsQualificationSuccess)
            successfulExactSets.Add(key);

        return new BatchExecutionResult
        {
            Attempt = evidence,
            NextAttemptSequence = attemptSequence
        };
    }

    private static async Task<LocalizationResult> LocalizeFailureAsync(
        MmsDynamicDataSetQualificationBatch failedBatch,
        IReadOnlyDictionary<string, MmsObjectReference> objectByReference,
        ISet<string> successfulExactSets,
        Func<IReadOnlyList<MmsObjectReference>, CancellationToken, Task<MmsDynamicDataSetQualificationProbeResult>> probeExecutor,
        ICollection<MmsDynamicDataSetQualificationAttemptEvidence> attempts,
        int maxAttempts,
        int attemptSequence,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<MmsDynamicDataSetQualificationBatch>(
            MmsDynamicDataSetQualificationLadder.BisectFailedBatch(failedBatch.MemberReferences));
        var warnings = new List<string>();
        var budgetExhausted = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempts.Count >= maxAttempts)
            {
                budgetExhausted = true;
                warnings.Add("Qualification failure localization stopped because the commissioning attempt budget was exhausted.");
                break;
            }

            var batch = queue.Dequeue();
            var execution = await ExecuteBatchAsync(
                batch,
                "bisect",
                objectByReference,
                successfulExactSets,
                probeExecutor,
                attempts,
                maxAttempts,
                attemptSequence,
                cancellationToken).ConfigureAwait(false);
            attemptSequence = execution.NextAttemptSequence;
            budgetExhausted |= execution.AttemptBudgetExhausted;

            if (execution.SkippedAsAlreadyProven)
                continue;

            var attempt = execution.Attempt;
            if (attempt is null)
                break;
            if (attempt.IsQualificationSuccess)
                continue;

            if (attempt.RequiresFreshAssociation)
            {
                warnings.Add(
                    $"Failure localization stopped at {batch.MemberCount} member(s) because association continuity or cleanup was not proven. A fresh MMS association is required.");
                break;
            }

            if (batch.MemberCount > 1)
            {
                foreach (var child in MmsDynamicDataSetQualificationLadder.BisectFailedBatch(batch.MemberReferences))
                    queue.Enqueue(child);
            }
        }

        return new LocalizationResult
        {
            NextAttemptSequence = attemptSequence,
            AttemptBudgetExhausted = budgetExhausted,
            Warnings = warnings
        };
    }

    private static MmsObjectReference[] NormalizeCandidates(IReadOnlyList<MmsObjectReference> candidates)
    {
        var result = new List<MmsObjectReference>(candidates.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var reference = ToMemberReference(candidate);
            if (!seen.Add(reference))
                throw new ArgumentException($"Qualification candidate is duplicated: {reference}", nameof(candidates));
            result.Add(candidate);
        }

        return result.ToArray();
    }

    private static string ToMemberReference(MmsObjectReference member)
    {
        if (string.IsNullOrWhiteSpace(member.Domain) || string.IsNullOrWhiteSpace(member.Item))
            throw new ArgumentException("Qualification requires fully resolved MMS member domain/item references.", nameof(member));
        return $"{member.Domain.Trim()}/{member.Item.Trim()}";
    }

    private static string MemberSetKey(IReadOnlyList<string> memberReferences)
        => string.Join("\u001F", memberReferences.Select(reference => reference.Trim()));

    private static void ValidateOptions(MmsDynamicDataSetQualificationCoordinatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxAttempts < 1 || options.MaxAttempts > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be between 1 and 64.");
        MmsDynamicDataSetQualificationLadder.ValidateOptions(options.Ladder);
        MmsDynamicDataSetQualificationPolicy.ValidateOptions(options.Probe);
        if (options.Probe.ApplicationSafetyMemberLimit != options.Ladder.ApplicationSafetyMemberLimit)
        {
            throw new ArgumentException(
                "Coordinator ladder and probe must use the same ApplicationSafetyMemberLimit so planning cannot exceed the mutation guard.",
                nameof(options));
        }
    }

    private sealed class BatchExecutionResult
    {
        public MmsDynamicDataSetQualificationAttemptEvidence? Attempt { get; init; }
        public bool SkippedAsAlreadyProven { get; init; }
        public bool AttemptBudgetExhausted { get; init; }
        public int NextAttemptSequence { get; init; }
    }

    private sealed class LocalizationResult
    {
        public bool AttemptBudgetExhausted { get; init; }
        public int NextAttemptSequence { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Runs the G2 qualification ladder only when ExecutionMode is ExplicitCommissioning.
    /// This wrapper reuses one temporary DataSet reference sequentially; every successful
    /// trial must delete the list before the next trial. Association/cleanup failure stops
    /// the coordinator and requires a fresh session from the caller.
    /// </summary>
    public Task<MmsDynamicDataSetQualificationCoordinatorResult> RunDynamicDataSetQualificationCommissioningAsync(
        string dataSetReference,
        IReadOnlyList<MmsObjectReference> candidateMembers,
        MmsDynamicDataSetQualificationCoordinatorOptions? options = null,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetReference);
        options ??= new MmsDynamicDataSetQualificationCoordinatorOptions();

        return MmsDynamicDataSetQualificationCoordinator.RunAsync(
            candidateMembers,
            (members, token) => ProbeDynamicDataSetQualificationAsync(
                dataSetReference,
                members,
                options.Probe,
                directory,
                token),
            options,
            cancellationToken);
    }
}
