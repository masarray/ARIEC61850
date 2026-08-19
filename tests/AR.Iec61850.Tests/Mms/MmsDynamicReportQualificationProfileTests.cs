using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicReportQualificationProfileTests
{
    [Fact]
    public void EnvelopeProfile_RemainsEnvelopeQualifiedAndRetainsExactEvidence()
    {
        var fixture = BuildEnvelopeFixture();

        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            fixture.Envelope,
            fixture.Assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 2,
                ObservedFreeUrcbSlots = 30,
                ObservedAtUtc = Time(10),
                EvidenceId = "capacity-1"
            },
            "field-qualification-1",
            Time(11));

        Assert.Equal(MmsDynamicReportQualificationState.EnvelopeQualified, profile.State);
        Assert.Equal(8, profile.ProvenSafeMemberCount);
        Assert.Equal(384, profile.ProvenSafeDefineRequestByteCount);
        Assert.Equal(65000, profile.NegotiatedMaxMmsPduSize);
        Assert.Equal(30, profile.CapacityEvidence!.ObservedFreeUrcbSlots);
        Assert.Contains("capacity-1", profile.SourceEvidenceIds);
        Assert.Contains("field-qualification-1", profile.SourceEvidenceIds);
        Assert.Null(profile.RcbActivationProof);
        Assert.Null(profile.InformationReportProof);
        Assert.Null(profile.ProductionAcceptance);
    }

    [Fact]
    public void IdentityChange_InvalidatesProfileForProductionUse()
    {
        var profile = BuildEnvelopeProfile();
        var changed = Identity() with { FirmwareRevision = "9.9.9" };

        var compatibility = MmsDynamicReportQualificationProfilePolicy.CheckIdentityCompatibility(profile, changed);

        Assert.False(compatibility.IsCompatible);
        Assert.Equal(MmsDynamicReportProfileCompatibilityStatus.FirmwareRevisionMismatch, compatibility.Status);
        Assert.False(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(profile, changed, out var reason));
        Assert.Contains("firmware", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOptionalCurrentIdentityField_FailsWhenPersistedEvidenceHadValue()
    {
        var profile = BuildEnvelopeProfile();
        var current = Identity() with { ProfileRevision = string.Empty };

        var compatibility = MmsDynamicReportQualificationProfilePolicy.CheckIdentityCompatibility(profile, current);

        Assert.Equal(MmsDynamicReportProfileCompatibilityStatus.ProfileRevisionMismatch, compatibility.Status);
        Assert.False(compatibility.IsCompatible);
    }

    [Fact]
    public void RcbActivation_CannotAdvanceWithoutEveryActivationGate()
    {
        var profile = BuildEnvelopeProfile();
        var incomplete = ValidRcbProof() with { RptEnaAccepted = false };

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
                profile,
                Identity(),
                incomplete));
    }

    [Fact]
    public void RcbActivation_RequiresMembersInsideAcceptedEnvelope()
    {
        var profile = BuildEnvelopeProfile();
        var invalid = ValidRcbProof() with
        {
            MemberReferences =
            [
                "LD0/GGIO1$ST$Ind1$stVal",
                "LD0/GGIO1$ST$Outside$stVal"
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
                profile,
                Identity(),
                invalid));
    }

    [Fact]
    public void SuccessfulRcbActivation_AdvancesOnlyToRcbActivationProven()
    {
        var profile = BuildEnvelopeProfile();

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            ValidRcbProof());

        Assert.Equal(MmsDynamicReportQualificationState.RcbActivationProven, activated.State);
        Assert.True(activated.RcbActivationProof!.IsSuccess);
        Assert.Null(activated.InformationReportProof);
        Assert.False(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
            activated,
            Identity(),
            out var reason));
        Assert.Contains("not ProductionEligible", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RptEnaWithoutInformationReport_CannotBecomeInformationReportProven()
    {
        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            BuildEnvelopeProfile(),
            Identity(),
            ValidRcbProof());
        var noReport = ValidInformationReportProof() with { ActualInformationReportReceived = false };

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
                activated,
                Identity(),
                noReport));
    }

    [Fact]
    public void InformationReportProof_RequiresExactRcbDatasetAndMemberIdentity()
    {
        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            BuildEnvelopeProfile(),
            Identity(),
            ValidRcbProof());
        var wrongOrder = ValidInformationReportProof() with
        {
            MemberReferences = ValidInformationReportProof().MemberReferences.Reverse().ToArray()
        };

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
                activated,
                Identity(),
                wrongOrder));
    }

    [Fact]
    public void ActualMappedInformationReport_AdvancesOnlyToInformationReportProven()
    {
        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            BuildEnvelopeProfile(),
            Identity(),
            ValidRcbProof());

        var reportProven = MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
            activated,
            Identity(),
            ValidInformationReportProof());

        Assert.Equal(MmsDynamicReportQualificationState.InformationReportProven, reportProven.State);
        Assert.True(reportProven.InformationReportProof!.IsSuccess);
        Assert.False(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
            reportProven,
            Identity(),
            out _));
    }

    [Fact]
    public void ProductionEligible_RequiresEveryG26PhysicalRegressionGate()
    {
        var reportProven = BuildInformationReportProvenProfile();
        var incomplete = ValidProductionAcceptance() with { ReconnectRegressionPassed = false };

        Assert.Throws<InvalidOperationException>(() =>
            MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(
                reportProven,
                Identity(),
                incomplete));
    }

    [Fact]
    public void AllEvidenceAndG26RegressionGates_AllowProductionEligibleForSameIdentityOnly()
    {
        var reportProven = BuildInformationReportProvenProfile();

        var production = MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(
            reportProven,
            Identity(),
            ValidProductionAcceptance());

        Assert.Equal(MmsDynamicReportQualificationState.ProductionEligible, production.State);
        Assert.True(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
            production,
            Identity(),
            out var reason));
        Assert.Contains("ProductionEligible", reason, StringComparison.OrdinalIgnoreCase);

        Assert.False(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
            production,
            Identity() with { ModelFingerprint = "different-model-hash" },
            out _));
    }

    [Fact]
    public void UnsupportedProfileSchema_FailsClosedForProductionPlanning()
    {
        var production = MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(
            BuildInformationReportProvenProfile(),
            Identity(),
            ValidProductionAcceptance());
        var future = production with { SchemaVersion = 99 };

        Assert.False(MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
            future,
            Identity(),
            out var reason));
        Assert.Contains("schema", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsDynamicReportQualificationProfile BuildEnvelopeProfile()
    {
        var fixture = BuildEnvelopeFixture();
        return MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            fixture.Envelope,
            fixture.Assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 2,
                ObservedFreeUrcbSlots = 30,
                ObservedAtUtc = Time(10),
                EvidenceId = "capacity-1"
            },
            "field-qualification-1",
            Time(11));
    }

    private static MmsDynamicReportQualificationProfile BuildInformationReportProvenProfile()
    {
        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            BuildEnvelopeProfile(),
            Identity(),
            ValidRcbProof());
        return MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
            activated,
            Identity(),
            ValidInformationReportProof());
    }

    private static (MmsDynamicDataSetQualificationAssessment Assessment, MmsDynamicDataSetQualifiedEnvelope Envelope) BuildEnvelopeFixture()
    {
        var refs1 = MemberReferences(1);
        var refs8 = MemberReferences(8);
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            Attempt("q1", refs1, 96, Time(1)),
            Attempt("q8", refs8, 384, Time(2))
        ]);
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "q8");
        return (assessment, envelope);
    }

    private static MmsDynamicDataSetQualificationAttemptEvidence Attempt(
        string id,
        IReadOnlyList<string> members,
        int requestBytes,
        DateTimeOffset time)
        => new()
        {
            AttemptId = id,
            ObservedAtUtc = time,
            DataSetReference = "LD0/LLN0.AR_G2Q",
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

    private static MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:station-a:q0",
            ModelFingerprint = "sha256:model-profile-001",
            Manufacturer = "Siemens",
            Model = "SIPROTEC",
            FirmwareRevision = "1.2.3",
            ProfileRevision = "cfg-42"
        };

    private static MmsDynamicRcbActivationProof ValidRcbProof()
        => new()
        {
            EvidenceId = "rcb-activation-1",
            ObservedAtUtc = Time(20),
            RcbReference = "LD0/LLN0.RP.Unbuffer02",
            DataSetReference = "LD0/LLN0.AR_G2Q",
            MemberReferences = MemberReferences(4),
            FreshRcbAvailabilityVerified = true,
            DataSetReadbackVerified = true,
            RcbDataSetBindingAccepted = true,
            RptEnaAccepted = true,
            AssociationHealthyAfterActivation = true
        };

    private static MmsDynamicInformationReportProof ValidInformationReportProof()
        => new()
        {
            EvidenceId = "information-report-1",
            ObservedAtUtc = Time(21),
            RcbReference = "LD0/LLN0.RP.Unbuffer02",
            DataSetReference = "LD0/LLN0.AR_G2Q",
            MemberReferences = MemberReferences(4),
            Kind = MmsDynamicInformationReportKind.DataChange,
            ActualInformationReportReceived = true,
            ReportIdentityVerified = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            ReportAuthoritativePointCount = 4
        };

    private static MmsDynamicReportProductionAcceptance ValidProductionAcceptance()
        => new()
        {
            FieldEvidenceId = "g2.6-field-regression-1",
            ObservedAtUtc = Time(30),
            ControlRegressionPassed = true,
            StaticReportingRegressionPassed = true,
            DynamicInformationReportRegressionPassed = true,
            PollingAuthorityGuardPassed = true,
            ReconnectRegressionPassed = true,
            QualityRegressionPassed = true,
            NoRepeatedMutationLoopPassed = true
        };

    private static string[] MemberReferences(int count)
        => Enumerable.Range(1, count)
            .Select(index => $"LD0/GGIO1$ST$Ind{index}$stVal")
            .ToArray();

    private static DateTimeOffset Time(int minutes)
        => DateTimeOffset.Parse("2026-08-19T10:00:00Z").AddMinutes(minutes);
}
