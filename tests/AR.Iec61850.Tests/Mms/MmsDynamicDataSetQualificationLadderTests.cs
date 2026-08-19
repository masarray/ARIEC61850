using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicDataSetQualificationLadderTests
{
    [Fact]
    public void DefaultMilestones_BuildConservativePrefixLadder()
    {
        var batches = MmsDynamicDataSetQualificationLadder.BuildMilestoneBatches(
            BuildMemberReferences(40));

        Assert.Equal([1, 4, 8, 16, 32], batches.Select(batch => batch.MemberCount).ToArray());
        Assert.Equal("LD0/GGIO1$ST$Ind1$stVal", batches[0].MemberReferences[0]);
        Assert.Equal("LD0/GGIO1$ST$Ind32$stVal", batches[^1].MemberReferences[^1]);
    }

    [Fact]
    public void TerminalCandidateCount_IsIncludedOnlyWhenExplicitlyRequested()
    {
        var candidates = BuildMemberReferences(20);

        var normal = MmsDynamicDataSetQualificationLadder.BuildMilestoneBatches(candidates);
        var terminal = MmsDynamicDataSetQualificationLadder.BuildMilestoneBatches(
            candidates,
            new MmsDynamicDataSetQualificationLadderOptions
            {
                IncludeTerminalCandidateCount = true
            });

        Assert.Equal([1, 4, 8, 16], normal.Select(batch => batch.MemberCount).ToArray());
        Assert.Equal([1, 4, 8, 16, 20], terminal.Select(batch => batch.MemberCount).ToArray());
    }

    [Fact]
    public void LadderOptions_RequireOneFirstAndStrictlyIncreasingMilestones()
    {
        Assert.Throws<ArgumentException>(() =>
            MmsDynamicDataSetQualificationLadder.ValidateOptions(
                new MmsDynamicDataSetQualificationLadderOptions
                {
                    Milestones = [2, 4, 8]
                }));

        Assert.Throws<ArgumentException>(() =>
            MmsDynamicDataSetQualificationLadder.ValidateOptions(
                new MmsDynamicDataSetQualificationLadderOptions
                {
                    Milestones = [1, 4, 4, 8]
                }));
    }

    [Fact]
    public void BisectEightMembers_ProducesTwoExactOrderedFourMemberBatches()
    {
        var failed = BuildMemberReferences(8);
        var split = MmsDynamicDataSetQualificationLadder.BisectFailedBatch(failed);

        Assert.Equal(2, split.Count);
        Assert.Equal(4, split[0].MemberCount);
        Assert.Equal(4, split[1].MemberCount);
        Assert.Equal(failed[..4], split[0].MemberReferences);
        Assert.Equal(failed[4..], split[1].MemberReferences);
    }

    [Fact]
    public void BisectOddBatch_PreservesEveryMemberExactlyOnce()
    {
        var failed = BuildMemberReferences(5);
        var split = MmsDynamicDataSetQualificationLadder.BisectFailedBatch(failed);

        Assert.Equal([2, 3], split.Select(batch => batch.MemberCount).ToArray());
        Assert.Equal(failed, split.SelectMany(batch => batch.MemberReferences).ToArray());
    }

    [Fact]
    public void Assessment_WithoutEvidence_RemainsAdvertised()
    {
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
            Array.Empty<MmsDynamicDataSetQualificationAttemptEvidence>());

        Assert.Equal(MmsDynamicReportQualificationState.Advertised, assessment.State);
        Assert.Equal(0, assessment.LargestProvenMemberCount);
        Assert.False(assessment.HasMultiMemberEnvelopeCandidate);
        Assert.False(assessment.RequiresFreshAssociation);
    }

    [Fact]
    public void Assessment_SingleMemberSuccess_AdvancesOnlyToSingleMemberProven()
    {
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Success("q1", BuildMemberReferences(1), 96)
        ]);

        Assert.Equal(MmsDynamicReportQualificationState.SingleMemberProven, assessment.State);
        Assert.Equal(1, assessment.LargestProvenMemberCount);
        Assert.False(assessment.HasMultiMemberEnvelopeCandidate);
    }

    [Fact]
    public void Assessment_MultiMemberSuccess_IsEnvelopeCandidateButNotImplicitPromotion()
    {
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Success("q1", BuildMemberReferences(1), 96),
            Success("q4", BuildMemberReferences(4), 212),
            Success("q8", BuildMemberReferences(8), 384)
        ]);

        Assert.Equal(MmsDynamicReportQualificationState.SingleMemberProven, assessment.State);
        Assert.True(assessment.HasMultiMemberEnvelopeCandidate);
        Assert.Equal(8, assessment.LargestProvenMemberCount);
        Assert.Equal(384, assessment.LargestProvenDefineRequestByteCount);
        Assert.Equal("q8", assessment.LargestProvenAttemptId);
        Assert.Contains(assessment.Warnings, warning =>
            warning.Contains("explicitly accepted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_AssociationLoss_RequiresFreshAssociation()
    {
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Success("q1", BuildMemberReferences(1), 96),
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "q8-fail",
                ObservedAtUtc = DateTimeOffset.Parse("2026-08-19T09:00:00Z"),
                MemberReferences = BuildMemberReferences(8),
                DefineRequestByteCount = 384,
                IsSuccess = false,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.DefineNamedVariableList,
                DynamicMutationAttempted = true,
                AssociationSurvived = false,
                CleanupSucceeded = false
            }
        ]);

        Assert.True(assessment.RequiresFreshAssociation);
        Assert.Contains(assessment.Warnings, warning =>
            warning.Contains("fresh MMS association", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_CleanupSafeSingleFailure_IsolatedAsRejectedMember()
    {
        var bad = "LD0/GGIO1$ST$Bad$stVal";
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Success("q1-good", BuildMemberReferences(1), 96),
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "q1-bad",
                ObservedAtUtc = DateTimeOffset.Parse("2026-08-19T09:01:00Z"),
                MemberReferences = [bad],
                DefineRequestByteCount = 101,
                IsSuccess = false,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.GetNamedVariableListAttributes,
                DynamicMutationAttempted = true,
                AssociationSurvived = true,
                CleanupSucceeded = true
            }
        ]);

        Assert.Contains(bad, assessment.IsolatedRejectedMembers);
        Assert.False(assessment.RequiresFreshAssociation);
    }

    [Fact]
    public void AcceptExactEnvelope_RequiresExplicitSuccessfulMultiMemberAttempt()
    {
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Success("q1", BuildMemberReferences(1), 96),
            Success("q4", BuildMemberReferences(4), 212)
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "q1"));

        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "q4");

        Assert.Equal(MmsDynamicReportQualificationState.EnvelopeQualified, envelope.State);
        Assert.Equal("q4", envelope.SourceAttemptId);
        Assert.Equal(4, envelope.ProvenMemberCount);
        Assert.Equal(212, envelope.ProvenDefineRequestByteCount);
        Assert.Equal(BuildMemberReferences(4), envelope.ExactProvenMemberReferences);
    }

    [Fact]
    public void ProbeResultProjection_PreservesQualificationSafetyEvidence()
    {
        var probe = new MmsDynamicDataSetQualificationProbeResult
        {
            IsSuccess = true,
            FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
            DataSetReference = "LD0/LLN0.AR_G2Q_04",
            RequestedMemberReferences = BuildMemberReferences(4),
            DefineRequestByteCount = 212,
            NegotiatedMaxMmsPduSize = 65000,
            RequestWithinKnownNegotiatedPdu = true,
            DirectoryAttempted = true,
            DirectoryVerified = true,
            DefineEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = true,
                IsSuccess = true,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = MmsAssociationState.MmsInitiated,
                MemberReferences = BuildMemberReferences(4)
            },
            DeleteEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = true,
                IsSuccess = true,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = MmsAssociationState.MmsInitiated,
                MemberReferences = BuildMemberReferences(4)
            }
        };

        var evidence = MmsDynamicDataSetQualificationAttemptEvidence.FromProbeResult(
            "q4",
            probe,
            DateTimeOffset.Parse("2026-08-19T09:02:00Z"));

        Assert.True(evidence.IsQualificationSuccess);
        Assert.False(evidence.RequiresFreshAssociation);
        Assert.Equal(4, evidence.MemberCount);
        Assert.Equal(212, evidence.DefineRequestByteCount);
        Assert.Equal(65000, evidence.NegotiatedMaxMmsPduSize);
    }

    private static MmsDynamicDataSetQualificationAttemptEvidence Success(
        string id,
        IReadOnlyList<string> members,
        int requestBytes)
        => new()
        {
            AttemptId = id,
            ObservedAtUtc = DateTimeOffset.Parse("2026-08-19T08:00:00Z").AddMinutes(members.Count),
            DataSetReference = $"LD0/LLN0.AR_{id}",
            MemberReferences = members.ToArray(),
            DefineRequestByteCount = requestBytes,
            NegotiatedMaxMmsPduSize = 65000,
            RequestWithinKnownNegotiatedPdu = true,
            IsSuccess = true,
            FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
            DynamicMutationAttempted = true,
            AssociationSurvived = true,
            CleanupSucceeded = true
        };

    private static string[] BuildMemberReferences(int count)
        => Enumerable.Range(1, count)
            .Select(index => $"LD0/GGIO1$ST$Ind{index}$stVal")
            .ToArray();
}
