using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26ProductionDynamicConsumerTests
{
    [Fact]
    public void InformationReportProven_RemainsQuarantinedForAutomaticDynamicPlanning()
    {
        var profile = BuildInformationReportProvenProfile();
        var result = Build(
            [Signal(1)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1),
            new MmsDynamicReportProductionPlanningContext
            {
                Profile = profile,
                CurrentIdentity = Identity()
            });

        Assert.True(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.False(result.ProductionDynamicActivationAuthorized);
        Assert.True(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("not ProductionEligible", result.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionEligible_ExactProvenRcbAndMembers_AuthorizeDynamicUrcb()
    {
        var profile = BuildProductionEligibleProfile();
        var result = Build(
            [Signal(1), Signal(2)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1, 2, 3, 4),
            new MmsDynamicReportProductionPlanningContext
            {
                Profile = profile,
                CurrentIdentity = Identity()
            });

        Assert.True(result.ProductionDynamicActivationAuthorized);
        Assert.False(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(4, result.ProductionQualifiedDynamicMemberCount);
        Assert.Equal(ProvenRcbReference, result.ProductionQualifiedRcbReference);
        Assert.Equal(2, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, result.AcquisitionPlan.Status);

        var segment = Assert.Single(result.AcquisitionPlan.Segments.Where(x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb));
        Assert.Equal(ProvenRcbReference, segment.ReportControlReference);
        Assert.Equal(
            [Member(1), Member(2)],
            segment.ReportPlan!.DynamicPoints.Select(point => point.MmsReference).ToArray());
    }

    [Fact]
    public void ProductionEligible_UnprovenMember_RemainsOnPolling()
    {
        var result = Build(
            [Signal(1), OutsideSignal()],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1, 2, 3, 4, 99),
            ProductionContext());

        Assert.True(result.ProductionDynamicActivationAuthorized);
        Assert.Equal(1, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, result.AcquisitionPlan.Status);

        var dynamic = Assert.Single(result.AcquisitionPlan.Segments.Where(x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb));
        Assert.Single(dynamic.ReportPlan!.DynamicPoints);
        Assert.Equal(Member(1), dynamic.ReportPlan.DynamicPoints[0].MmsReference);
        Assert.DoesNotContain(dynamic.ReportPlan.DynamicPoints, point =>
            point.MmsReference.Equals(Member(99), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductionEligible_IdentityMismatch_FailsClosedToPolling()
    {
        var context = ProductionContext() with
        {
            CurrentIdentity = Identity() with { ModelFingerprint = "sha256:different-model" }
        };

        var result = Build(
            [Signal(1)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1),
            context);

        Assert.False(result.ProductionDynamicActivationAuthorized);
        Assert.True(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("fingerprint", result.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionEligible_DifferentFreeRcb_DoesNotSubstituteForProvenRcb()
    {
        const string otherRcb = "LD0/LLN0.RP.OtherFree01";
        var result = Build(
            [Signal(1)],
            DynamicAvailability(otherRcb),
            DynamicInventory(otherRcb),
            Directory(1),
            ProductionContext());

        Assert.True(result.ProductionDynamicActivationAuthorized);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.DoesNotContain(result.AcquisitionPlan.Segments, segment =>
            segment.ReportControlReference.Equals(otherRcb, StringComparison.OrdinalIgnoreCase) &&
            segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
    }

    [Fact]
    public void TamperedProductionProfile_MemberOutsideEnvelope_IsRejectedByConsumer()
    {
        var profile = BuildProductionEligibleProfile();
        var tamperedReport = profile.InformationReportProof! with
        {
            MemberReferences = [Member(1), Member(99)],
            ReportAuthoritativePointCount = 2
        };
        var tampered = profile with { InformationReportProof = tamperedReport };

        var result = Build(
            [Signal(1)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1, 99),
            new MmsDynamicReportProductionPlanningContext
            {
                Profile = tampered,
                CurrentIdentity = Identity()
            });

        Assert.False(result.ProductionDynamicActivationAuthorized);
        Assert.True(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        IReadOnlyList<Iec61850SignalDescriptor> signals,
        MmsRcbAvailabilityResult availability,
        MmsReportInventory inventory,
        MmsIedModelDirectory directory,
        MmsDynamicReportProductionPlanningContext context)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G26_SYNTHETIC_IED",
            Source = "Project-owned G2.6 production consumer fixture",
            Signals = signals
        };

        return MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            signals,
            inventory,
            availability,
            directory,
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = true,
                SupportsDefineNamedVariableList = true,
                SupportsDeleteNamedVariableList = true
            },
            new MmsHybridReportAcquisitionOptions
            {
                AllowStaticBrcb = false,
                AllowStaticUrcb = false,
                AllowDynamicBrcb = false,
                AllowDynamicUrcb = true,
                AllowCallerOwnedReports = false,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            },
            context);
    }

    private static MmsDynamicReportProductionPlanningContext ProductionContext()
        => new()
        {
            Profile = BuildProductionEligibleProfile(),
            CurrentIdentity = Identity()
        };

    private static MmsDynamicReportQualificationProfile BuildProductionEligibleProfile()
        => MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(
            BuildInformationReportProvenProfile(),
            Identity(),
            new MmsDynamicReportProductionAcceptance
            {
                FieldEvidenceId = "g2.6-synthetic-regression",
                ObservedAtUtc = Time(30),
                ControlRegressionPassed = true,
                StaticReportingRegressionPassed = true,
                DynamicInformationReportRegressionPassed = true,
                PollingAuthorityGuardPassed = true,
                ReconnectRegressionPassed = true,
                QualityRegressionPassed = true,
                NoRepeatedMutationLoopPassed = true
            });

    private static MmsDynamicReportQualificationProfile BuildInformationReportProvenProfile()
    {
        var members = Members(1, 2, 3, 4);
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "g26-envelope-4",
                ObservedAtUtc = Time(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                DefineRequestByteCount = 256,
                NegotiatedMaxMmsPduSize = 65000,
                RequestWithinKnownNegotiatedPdu = true,
                IsSuccess = true,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
                DynamicMutationAttempted = true,
                AssociationSurvived = true,
                CleanupSucceeded = true
            }
        ]);
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "g26-envelope-4");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 1,
                ObservedAtUtc = Time(2),
                EvidenceId = "g26-capacity"
            },
            "g26-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = "g26-activation",
                ObservedAtUtc = Time(4),
                RcbReference = ProvenRcbReference,
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                FreshRcbAvailabilityVerified = true,
                DataSetReadbackVerified = true,
                RcbDataSetBindingAccepted = true,
                RptEnaAccepted = true,
                AssociationHealthyAfterActivation = true
            });

        return MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
            activated,
            Identity(),
            new MmsDynamicInformationReportProof
            {
                EvidenceId = "g26-information-report",
                ObservedAtUtc = Time(5),
                RcbReference = ProvenRcbReference,
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                Kind = MmsDynamicInformationReportKind.DataChange,
                ActualInformationReportReceived = true,
                ReportIdentityVerified = true,
                ExactMemberMappingVerified = true,
                AssociationHealthyAfterReport = true,
                ReportAuthoritativePointCount = members.Length
            });
    }

    private static MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:g26:synthetic",
            ModelFingerprint = "sha256:g26-model-001",
            Manufacturer = "Example",
            Model = "SyntheticIED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-1"
        };

    private static MmsReportInventory DynamicInventory(string rcbReference)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = RcbName(rcbReference),
            Reference = rcbReference,
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationState = "false",
            TriggerOptions = "dchg",
            ReportId = RcbName(rcbReference),
            ConfRev = "1"
        });
        return inventory;
    }

    private static MmsRcbAvailabilityResult DynamicAvailability(string rcbReference)
        => new()
        {
            CheckedAtUtc = Time(40),
            ReportControls =
            [
                new MmsRcbAvailabilitySnapshot
                {
                    Reference = rcbReference,
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    Name = RcbName(rcbReference),
                    Mode = "URCB",
                    Buffered = false,
                    DataSetReference = string.Empty,
                    DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
                    ReportId = RcbName(rcbReference),
                    ConfRev = "1",
                    EnabledState = "false",
                    ReservationState = "false",
                    TriggerOptions = "dchg",
                    DataSetDirectoryRead = false,
                    DataSetDirectorySuccess = false,
                    DataSetMemberCount = 0,
                    DataSetMembers = Array.Empty<MmsDataSetDirectoryMember>(),
                    Availability = MmsRcbOperationalAvailability.NoDataSet,
                    Confidence = MmsRcbAvailabilityConfidence.Exact,
                    Reason = "G2.6 exact empty dynamic slot fixture",
                    Attributes = ["DatSet", "RptEna", "TrgOps", "Resv"]
                }
            ]
        };

    private static MmsIedModelDirectory Directory(params int[] indexes)
        => new(indexes.Select(index => new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = index == 99 ? "Outside.stVal" : $"Ind{index}.stVal",
            MmsItemName = index == 99 ? "GGIO1$ST$Outside$stVal" : $"GGIO1$ST$Ind{index}$stVal",
            Source = "G2.6 synthetic live directory",
            Confidence = 100
        }));

    private static Iec61850SignalDescriptor Signal(int index)
        => new()
        {
            DesignReference = $"LD0/GGIO1.Ind{index}.stVal",
            ObservedReference = $"LD0/GGIO1.Ind{index}.stVal",
            CanonicalMmsReference = Member(index),
            EffectiveMmsReference = Member(index),
            PrimaryValueReference = $"LD0/GGIO1.Ind{index}.stVal",
            PrimaryValueMmsReference = Member(index),
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };

    private static Iec61850SignalDescriptor OutsideSignal()
        => new()
        {
            DesignReference = "LD0/GGIO1.Outside.stVal",
            ObservedReference = "LD0/GGIO1.Outside.stVal",
            CanonicalMmsReference = Member(99),
            EffectiveMmsReference = Member(99),
            PrimaryValueReference = "LD0/GGIO1.Outside.stVal",
            PrimaryValueMmsReference = Member(99),
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };

    private static string[] Members(params int[] indexes)
        => indexes.Select(Member).ToArray();

    private static string Member(int index)
        => index == 99
            ? "LD0/GGIO1$ST$Outside$stVal"
            : $"LD0/GGIO1$ST$Ind{index}$stVal";

    private static string RcbName(string reference)
        => reference[(reference.LastIndexOf('.') + 1)..];

    private static DateTimeOffset Time(int minutes)
        => DateTimeOffset.Parse("2026-08-24T00:00:00Z").AddMinutes(minutes);

    private const string ProvenRcbReference = "LD0/LLN0.RP.Unbuffer02";
    private const string ProvenDataSetReference = "LD0/LLN0.AR_G2Q";
}
