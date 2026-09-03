using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26ShadowVerificationTests
{
    [Fact]
    public void MatchingReportAndPollEdges_WithReconnect_PassProductionShadow()
    {
        var evidence = PassingEvidence();
        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(
            evidence,
            StrictOptions());

        Assert.True(result.IsSuccess, result.Summary);
        Assert.True(result.ExactMemberIdentityPassed);
        Assert.True(result.ValueParityPassed);
        Assert.True(result.QualityParityPassed);
        Assert.True(result.TimestampParityPassed);
        Assert.True(result.ReportOrderPassed);
        Assert.True(result.NoMissingReportEdgesPassed);
        Assert.True(result.NoDuplicateReportEdgesPassed);
        Assert.True(result.PollingAuthorityGuardPassed);
        Assert.True(result.ReconnectRegressionPassed);
        Assert.True(result.NoRepeatedMutationLoopPassed);
        Assert.Equal(2, result.PollTransitionCount);
        Assert.Equal(2, result.MatchedPollTransitionToReportCount);
    }

    [Fact]
    public void OneInformationReportFrame_CanCarryMultipleIndexesWithSameSequenceNumber()
    {
        var evidence = PassingEvidence() with
        {
            ReportObservations =
            [
                Report(0, "Open", 10, Time(110), Time(100)),
                Report(1, "Open", 10, Time(110), Time(100))
            ]
        };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.True(result.ReportOrderPassed, result.Summary);
        Assert.True(result.NoDuplicateReportEdgesPassed, result.Summary);
    }

    [Fact]
    public void PollTransitionWithoutMatchingReport_FailsMissingEdgeAndPollingAuthority()
    {
        var evidence = PassingEvidence() with
        {
            ReportObservations = [Report(0, "Open", 10, Time(110), Time(100))]
        };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(result.IsSuccess);
        Assert.False(result.NoMissingReportEdgesPassed);
        Assert.False(result.PollingAuthorityGuardPassed);
        Assert.Contains(result.Failures, failure => failure.Contains("only 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateSameSequenceAndIndex_FailsClosed()
    {
        var reports = PassingEvidence().ReportObservations.ToList();
        reports.Add(Report(0, "Open", 10, Time(111), Time(100)));
        var evidence = PassingEvidence() with { ReportObservations = reports };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(result.IsSuccess);
        Assert.False(result.NoDuplicateReportEdgesPassed);
    }

    [Fact]
    public void ValueQualityOrTimestampMismatch_FailsParity()
    {
        var polls = PassingEvidence().PollObservations.ToArray();
        polls[2] = polls[2] with
        {
            Value = "Closed",
            Quality = "questionable",
            DeviceTimestampUtc = Time(500)
        };
        var evidence = PassingEvidence() with { PollObservations = polls };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(result.IsSuccess);
        Assert.False(result.ValueParityPassed);
        Assert.False(result.QualityParityPassed);
        Assert.False(result.TimestampParityPassed);
    }

    [Fact]
    public void ReconnectWithoutReportAndPollRecovery_FailsClosed()
    {
        var evidence = PassingEvidence() with
        {
            ReportResubscriptionsAfterReconnect = 0,
            PollReferenceRecoveriesAfterReconnect = 0
        };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(result.IsSuccess);
        Assert.False(result.ReconnectRegressionPassed);
    }

    [Fact]
    public void RepeatedDynamicActivationLoop_FailsClosed()
    {
        var evidence = PassingEvidence() with { DynamicActivationAttempts = 3 };

        var result = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(result.IsSuccess);
        Assert.False(result.NoRepeatedMutationLoopPassed);
        Assert.Contains(result.Failures, failure => failure.Contains("repeated mutation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuccessfulTypedShadow_BuildsExistingProductionAcceptanceContract()
    {
        var evidence = PassingEvidence();
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        var acceptance = MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(
            evidence,
            shadow,
            controlRegressionPassed: true,
            staticReportingRegressionPassed: true);

        Assert.True(acceptance.AllPassed);
        Assert.Equal(evidence.EvidenceId, acceptance.FieldEvidenceId);
        Assert.True(acceptance.DynamicInformationReportRegressionPassed);
        Assert.True(acceptance.PollingAuthorityGuardPassed);
        Assert.True(acceptance.ReconnectRegressionPassed);
        Assert.True(acceptance.QualityRegressionPassed);
        Assert.True(acceptance.NoRepeatedMutationLoopPassed);
    }

    [Fact]
    public void FailedShadow_CannotBuildProductionAcceptance()
    {
        var evidence = PassingEvidence() with { ReportObservations = [] };
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(evidence, StrictOptions());

        Assert.False(shadow.IsSuccess);
        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(
                evidence,
                shadow,
                controlRegressionPassed: true,
                staticReportingRegressionPassed: true));
    }

    private static MmsDynamicReportShadowVerificationEvidence PassingEvidence()
        => new()
        {
            EvidenceId = "g2.6-shadow-synthetic",
            ObservedAtUtc = Time(1000),
            MemberReferences = [Member(0), Member(1)],
            ReportObservations =
            [
                Report(0, "Open", 10, Time(110), Time(100)),
                Report(1, "Open", 10, Time(110), Time(100))
            ],
            PollObservations =
            [
                Poll(0, "Closed", Time(90), Time(90)),
                Poll(1, "Closed", Time(90), Time(90)),
                Poll(0, "Open", Time(120), Time(100)),
                Poll(1, "Open", Time(120), Time(100))
            ],
            ReconnectAttempts = 1,
            SuccessfulReconnects = 1,
            ReportResubscriptionsAfterReconnect = 1,
            PollReferenceRecoveriesAfterReconnect = 1,
            DynamicActivationAttempts = 2
        };

    private static MmsDynamicReportShadowVerificationOptions StrictOptions()
        => new()
        {
            MinimumReportEdges = 2,
            MaximumReportToPollLag = TimeSpan.FromSeconds(5),
            MaximumPollTransitionToReportLag = TimeSpan.FromSeconds(5),
            MaximumDeviceTimestampDelta = TimeSpan.FromMilliseconds(250),
            RequireQualityEvidence = true,
            RequireDeviceTimestampEvidence = true,
            RequireReconnectCycle = true,
            MaximumDynamicActivationAttemptsPerAssociation = 1
        };

    private static MmsDynamicReportShadowReportObservation Report(
        int index,
        string value,
        ulong sequence,
        DateTimeOffset receivedAt,
        DateTimeOffset deviceTimestamp)
        => new()
        {
            DataSetIndex = index,
            MemberReference = Member(index),
            Value = value,
            Quality = "good",
            DeviceTimestampUtc = deviceTimestamp,
            ReceivedAtUtc = receivedAt,
            SequenceNumber = sequence
        };

    private static MmsDynamicReportShadowPollObservation Poll(
        int index,
        string value,
        DateTimeOffset readAt,
        DateTimeOffset deviceTimestamp)
        => new()
        {
            DataSetIndex = index,
            MemberReference = Member(index),
            Value = value,
            Quality = "good",
            DeviceTimestampUtc = deviceTimestamp,
            ReadAtUtc = readAt
        };

    private static string Member(int index) => $"LD0/LN0$ST$Pos{index}$stVal";
    private static DateTimeOffset Time(int milliseconds) => DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds);
}
