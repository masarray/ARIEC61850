using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26ShadowProductionAcceptancePolicyTests
{
    [Fact]
    public void StrictProductionAcceptance_AllPassed_WhenPairedQualityAndTimestampEvidenceExist()
    {
        var evidence = Evidence(includeQuality: true, includeTimestamp: true);
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(
            evidence,
            Options(requireQuality: true, requireTimestamp: true));

        Assert.True(shadow.IsSuccess, shadow.Summary);

        var acceptance = MmsDynamicReportShadowProductionAcceptancePolicy.BuildStrict(
            evidence,
            shadow,
            controlRegressionPassed: true,
            staticReportingRegressionPassed: true);

        Assert.True(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedQualityEvidence(evidence));
        Assert.True(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedTimestampEvidence(evidence));
        Assert.True(acceptance.QualityRegressionPassed);
        Assert.True(acceptance.AllPassed);
    }

    [Fact]
    public void OptionalQualityShadow_CannotAccidentallyGrantProductionQualityGateWithoutEvidence()
    {
        var evidence = Evidence(includeQuality: false, includeTimestamp: false);
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(
            evidence,
            Options(requireQuality: false, requireTimestamp: false));

        Assert.True(shadow.IsSuccess, shadow.Summary);

        var generic = MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(
            evidence,
            shadow,
            controlRegressionPassed: true,
            staticReportingRegressionPassed: true);
        var strict = MmsDynamicReportShadowProductionAcceptancePolicy.BuildStrict(
            evidence,
            shadow,
            controlRegressionPassed: true,
            staticReportingRegressionPassed: true);

        Assert.True(generic.QualityRegressionPassed);
        Assert.False(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedQualityEvidence(evidence));
        Assert.False(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedTimestampEvidence(evidence));
        Assert.False(strict.QualityRegressionPassed);
        Assert.False(strict.AllPassed);
    }

    [Fact]
    public void QualityWithoutTimestamp_RemainsProductionIneligible()
    {
        var evidence = Evidence(includeQuality: true, includeTimestamp: false);
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(
            evidence,
            Options(requireQuality: true, requireTimestamp: false));

        Assert.True(shadow.IsSuccess, shadow.Summary);
        var strict = MmsDynamicReportShadowProductionAcceptancePolicy.BuildStrict(
            evidence,
            shadow,
            controlRegressionPassed: true,
            staticReportingRegressionPassed: true);

        Assert.True(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedQualityEvidence(evidence));
        Assert.False(MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedTimestampEvidence(evidence));
        Assert.False(strict.QualityRegressionPassed);
        Assert.False(strict.AllPassed);
    }

    [Fact]
    public void FailedShadow_CannotBuildStrictProductionAcceptance()
    {
        var evidence = Evidence(includeQuality: true, includeTimestamp: true) with
        {
            ReportObservations = []
        };
        var shadow = MmsDynamicReportShadowVerificationPolicy.Evaluate(
            evidence,
            Options(requireQuality: true, requireTimestamp: true));

        Assert.False(shadow.IsSuccess);
        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportShadowProductionAcceptancePolicy.BuildStrict(
                evidence,
                shadow,
                controlRegressionPassed: true,
                staticReportingRegressionPassed: true));
    }

    private static MmsDynamicReportShadowVerificationEvidence Evidence(bool includeQuality, bool includeTimestamp)
    {
        var quality = includeQuality ? "good" : string.Empty;
        DateTimeOffset? deviceTimestamp = includeTimestamp ? Time(100) : null;
        return new MmsDynamicReportShadowVerificationEvidence
        {
            EvidenceId = "g2.6-strict-production-test",
            ObservedAtUtc = Time(1000),
            MemberReferences = [Member],
            ReportObservations =
            [
                new MmsDynamicReportShadowReportObservation
                {
                    DataSetIndex = 0,
                    MemberReference = Member,
                    Value = "Open",
                    Quality = quality,
                    DeviceTimestampUtc = deviceTimestamp,
                    ReceivedAtUtc = Time(110),
                    SequenceNumber = 10
                }
            ],
            PollObservations =
            [
                new MmsDynamicReportShadowPollObservation
                {
                    DataSetIndex = 0,
                    MemberReference = Member,
                    Value = "Closed",
                    Quality = quality,
                    DeviceTimestampUtc = includeTimestamp ? Time(90) : null,
                    ReadAtUtc = Time(90)
                },
                new MmsDynamicReportShadowPollObservation
                {
                    DataSetIndex = 0,
                    MemberReference = Member,
                    Value = "Open",
                    Quality = quality,
                    DeviceTimestampUtc = deviceTimestamp,
                    ReadAtUtc = Time(120)
                }
            ],
            ReconnectAttempts = 1,
            SuccessfulReconnects = 1,
            ReportResubscriptionsAfterReconnect = 1,
            PollReferenceRecoveriesAfterReconnect = 1,
            DynamicActivationAttempts = 2
        };
    }

    private static MmsDynamicReportShadowVerificationOptions Options(bool requireQuality, bool requireTimestamp)
        => new()
        {
            MinimumReportEdges = 1,
            MaximumReportToPollLag = TimeSpan.FromSeconds(5),
            MaximumPollTransitionToReportLag = TimeSpan.FromSeconds(5),
            MaximumDeviceTimestampDelta = TimeSpan.FromMilliseconds(250),
            RequireQualityEvidence = requireQuality,
            RequireDeviceTimestampEvidence = requireTimestamp,
            RequireReconnectCycle = true,
            MaximumDynamicActivationAttemptsPerAssociation = 1
        };

    private const string Member = "LD0/LN0$ST$Pos$stVal";
    private static DateTimeOffset Time(int milliseconds) => DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds);
}
