using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicDataSetQualificationCoordinatorTests
{
    [Fact]
    public async Task DisabledMode_BlocksWithoutCallingProbeExecutor()
    {
        var calls = 0;

        var result = await MmsDynamicDataSetQualificationCoordinator.RunAsync(
            BuildMembers(8),
            (_, _) =>
            {
                calls++;
                return Task.FromResult(SuccessProbe(BuildMembers(1)));
            });

        Assert.True(result.IsBlocked);
        Assert.Equal(0, calls);
        Assert.Empty(result.Attempts);
        Assert.Equal(MmsDynamicReportQualificationState.Advertised, result.Assessment.State);
    }

    [Fact]
    public async Task ExplicitCommissioning_AllMilestonesSucceed_ReachesLargestWithoutAutoPromotion()
    {
        var calls = new List<int>();
        var options = CommissioningOptions([1, 4, 8]);

        var result = await MmsDynamicDataSetQualificationCoordinator.RunAsync(
            BuildMembers(8),
            (members, _) =>
            {
                calls.Add(members.Count);
                return Task.FromResult(SuccessProbe(members));
            },
            options);

        Assert.False(result.IsBlocked);
        Assert.Equal([1, 4, 8], calls);
        Assert.Equal([1, 4, 8], result.SuccessfulMilestoneMemberCounts);
        Assert.True(result.ReachedLargestRequestedMilestone);
        Assert.Equal(MmsDynamicReportQualificationState.SingleMemberProven, result.Assessment.State);
        Assert.Equal(8, result.Assessment.LargestProvenMemberCount);
        Assert.False(string.IsNullOrWhiteSpace(result.EnvelopeCandidateAttemptId));
    }

    [Fact]
    public async Task FailedEightMemberMilestone_LocalizesAndNeverAttemptsLargerMilestone()
    {
        var calls = new List<string[]>();
        var candidates = BuildMembers(16);
        var badReference = ToReference(candidates[6]);
        var options = CommissioningOptions([1, 4, 8, 16]);

        var result = await MmsDynamicDataSetQualificationCoordinator.RunAsync(
            candidates,
            (members, _) =>
            {
                var refs = members.Select(ToReference).ToArray();
                calls.Add(refs);
                return Task.FromResult(refs.Contains(badReference, StringComparer.OrdinalIgnoreCase)
                    ? SafeFailureProbe(members)
                    : SuccessProbe(members));
            },
            options);

        Assert.Equal(8, result.FailedMilestoneMemberCount);
        Assert.True(result.FailureLocalizationAttempted);
        Assert.False(result.ReachedLargestRequestedMilestone);
        Assert.DoesNotContain(calls, batch => batch.Length == 16);

        // The first half of the failed 8-member batch equals the already-proven milestone-4
        // prefix and must not be probed again. Only the failing side is narrowed.
        Assert.Equal(1, calls.Count(batch => batch.Length == 4));
        Assert.Contains(badReference, result.Assessment.IsolatedRejectedMembers);
        Assert.False(result.RequiresFreshAssociation);
    }

    [Fact]
    public async Task AssociationLoss_StopsWithoutBisectionAndRequiresFreshAssociation()
    {
        var calls = new List<int>();
        var options = CommissioningOptions([1, 4, 8, 16]);

        var result = await MmsDynamicDataSetQualificationCoordinator.RunAsync(
            BuildMembers(16),
            (members, _) =>
            {
                calls.Add(members.Count);
                return Task.FromResult(members.Count == 8
                    ? AssociationLossProbe(members)
                    : SuccessProbe(members));
            },
            options);

        Assert.Equal([1, 4, 8], calls);
        Assert.True(result.RequiresFreshAssociation);
        Assert.False(result.FailureLocalizationAttempted);
        Assert.Equal(8, result.FailedMilestoneMemberCount);
        Assert.DoesNotContain(16, calls);
    }

    [Fact]
    public async Task MaxAttemptBudget_StopsFailureLocalizationDeterministically()
    {
        var options = new MmsDynamicDataSetQualificationCoordinatorOptions
        {
            ExecutionMode = MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning,
            MaxAttempts = 3,
            Ladder = new MmsDynamicDataSetQualificationLadderOptions
            {
                Milestones = [1, 4, 8],
                ApplicationSafetyMemberLimit = 64
            },
            Probe = new MmsDynamicDataSetQualificationProbeOptions
            {
                ApplicationSafetyMemberLimit = 64
            }
        };

        var result = await MmsDynamicDataSetQualificationCoordinator.RunAsync(
            BuildMembers(8),
            (members, _) => Task.FromResult(members.Count < 8
                ? SuccessProbe(members)
                : SafeFailureProbe(members)),
            options);

        Assert.Equal(3, result.Attempts.Count);
        Assert.True(result.AttemptBudgetExhausted);
        Assert.Equal(8, result.FailedMilestoneMemberCount);
    }

    [Fact]
    public async Task MismatchedLadderAndProbeSafetyLimits_AreRejectedBeforeExecution()
    {
        var options = new MmsDynamicDataSetQualificationCoordinatorOptions
        {
            ExecutionMode = MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning,
            Ladder = new MmsDynamicDataSetQualificationLadderOptions
            {
                Milestones = [1, 4, 8],
                ApplicationSafetyMemberLimit = 32
            },
            Probe = new MmsDynamicDataSetQualificationProbeOptions
            {
                ApplicationSafetyMemberLimit = 64
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            MmsDynamicDataSetQualificationCoordinator.RunAsync(
                BuildMembers(8),
                (members, _) => Task.FromResult(SuccessProbe(members)),
                options));
    }

    private static MmsDynamicDataSetQualificationCoordinatorOptions CommissioningOptions(
        IReadOnlyList<int> milestones)
        => new()
        {
            ExecutionMode = MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning,
            MaxAttempts = 16,
            LocalizeFailedBatch = true,
            Ladder = new MmsDynamicDataSetQualificationLadderOptions
            {
                Milestones = milestones,
                ApplicationSafetyMemberLimit = 64
            },
            Probe = new MmsDynamicDataSetQualificationProbeOptions
            {
                ApplicationSafetyMemberLimit = 64,
                RejectKnownNegotiatedPduOverflow = true
            }
        };

    private static MmsObjectReference[] BuildMembers(int count)
        => Enumerable.Range(1, count)
            .Select(index => new MmsObjectReference(
                "LD0",
                $"GGIO1$ST$Ind{index}$stVal",
                "ST"))
            .ToArray();

    private static string ToReference(MmsObjectReference member)
        => $"{member.Domain}/{member.Item}";

    private static MmsDynamicDataSetQualificationProbeResult SuccessProbe(
        IReadOnlyList<MmsObjectReference> members)
        => BuildProbe(members, isSuccess: true, associationSurvived: true, cleanupSucceeded: true);

    private static MmsDynamicDataSetQualificationProbeResult SafeFailureProbe(
        IReadOnlyList<MmsObjectReference> members)
        => BuildProbe(members, isSuccess: false, associationSurvived: true, cleanupSucceeded: true);

    private static MmsDynamicDataSetQualificationProbeResult AssociationLossProbe(
        IReadOnlyList<MmsObjectReference> members)
        => BuildProbe(members, isSuccess: false, associationSurvived: false, cleanupSucceeded: false);

    private static MmsDynamicDataSetQualificationProbeResult BuildProbe(
        IReadOnlyList<MmsObjectReference> members,
        bool isSuccess,
        bool associationSurvived,
        bool cleanupSucceeded)
    {
        var refs = members.Select(ToReference).ToArray();
        var defineState = associationSurvived
            ? MmsAssociationState.MmsInitiated
            : MmsAssociationState.MmsInitiateFailed;
        var deleteAttempted = associationSurvived && cleanupSucceeded;

        return new MmsDynamicDataSetQualificationProbeResult
        {
            IsSuccess = isSuccess,
            FailureStage = isSuccess
                ? MmsDynamicDataSetQualificationFailureStage.None
                : MmsDynamicDataSetQualificationFailureStage.GetNamedVariableListAttributes,
            DataSetReference = "LD0/LLN0.AR_G2Q",
            RequestedMemberReferences = refs,
            ReturnedMemberReferences = isSuccess ? refs : refs.Reverse().ToArray(),
            ApplicationSafetyMemberLimit = 64,
            DefineRequestByteCount = 80 + refs.Length * 32,
            NegotiatedMaxMmsPduSize = 65000,
            RequestWithinKnownNegotiatedPdu = true,
            DirectoryAttempted = true,
            DirectoryVerified = isSuccess,
            DefineEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = true,
                IsSuccess = true,
                MemberReferences = refs,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = defineState
            },
            DeleteEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = deleteAttempted,
                IsSuccess = deleteAttempted,
                MemberReferences = refs,
                StateBefore = defineState,
                StateAfter = defineState
            }
        };
    }
}
