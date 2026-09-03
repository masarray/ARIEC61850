using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26GuardedLegacySubsetRuntimeTests
{
    [Fact]
    public void LegacyGiProfile_BroaderStoredChain_ExactPhysicalDchgSubset_IsAuthorizedOnlyForSubset()
    {
        var context = LegacyContext();
        var originalReport = context.Profile.InformationReportProof!;
        var evidence = Evidence();

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            context,
            evidence,
            out var reason);

        Assert.True(valid, reason);
        Assert.Equal(MmsDynamicInformationReportKind.GeneralInterrogation, originalReport.Kind);
        Assert.Equal(4, originalReport.MemberReferences.Count);
        Assert.Equal(2, evidence.MemberReferences.Count);
        Assert.Contains("ordered subset", reason, StringComparison.OrdinalIgnoreCase);

        var plan = BuildPlan(context, evidence);
        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.False(plan.ProductionDynamicActivationAuthorized);
        Assert.Equal(2, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.AcquisitionPlan.PollingFallbackSignalCount);

        var dynamic = Assert.Single(
            plan.AcquisitionPlan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Equal(ProvenRcbReference, dynamic.ReportControlReference);
        Assert.Equal(DchgMembers(), dynamic.ReportPlan!.DynamicPoints.Select(point => point.MmsReference).ToArray());

        // P1.5b never rewrites the persisted GI-classified profile into a dchg proof.
        Assert.Equal(MmsDynamicInformationReportKind.GeneralInterrogation, context.Profile.InformationReportProof!.Kind);
        Assert.Equal(StoredMembers(), context.Profile.InformationReportProof.MemberReferences);
    }

    [Fact]
    public void LegacyGiProfile_ReversedDchgSubset_RemainsWithheld()
    {
        var evidence = Evidence() with { MemberReferences = [Member(2), Member(1)] };

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            LegacyContext(),
            evidence,
            out var reason);

        Assert.False(valid);
        Assert.Contains("ordered subset", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_DchgMemberOutsideStoredChain_RemainsWithheld()
    {
        var evidence = Evidence() with { MemberReferences = [Member(1), Member(5)] };

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            LegacyContext(),
            evidence,
            out var reason);

        Assert.False(valid);
        Assert.Contains("persisted successful report sequence", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_StoredActivationAndReportSequenceMismatch_RemainsWithheld()
    {
        var context = LegacyContext();
        var profile = context.Profile;
        var activation = profile.RcbActivationProof! with
        {
            MemberReferences = [Member(1), Member(3), Member(2), Member(4)]
        };
        context = context with { Profile = profile with { RcbActivationProof = activation } };

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            context,
            Evidence(),
            out var reason);

        Assert.False(valid);
        Assert.Contains("activation/report member sequences differ", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_IdentityMismatch_RemainsWithheld()
    {
        var evidence = Evidence() with { ProfileRevision = "other-revision" };

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            LegacyContext(),
            evidence,
            out var reason);

        Assert.False(valid);
        Assert.Contains("profile revision", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_IncompletePhysicalEvidence_RemainsWithheld()
    {
        var evidence = Evidence() with { GeneralInterrogationDisabled = false };

        var valid = MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
            LegacyContext(),
            evidence,
            out var reason);

        Assert.False(valid);
        Assert.Contains("complete physical", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacySubsetPlanner_NeverAuthorizesProductionEligible()
    {
        var plan = BuildPlan(LegacyContext(), Evidence());

        Assert.False(plan.ProductionDynamicActivationAuthorized);
        Assert.Equal(0, plan.ProductionQualifiedDynamicMemberCount);
        Assert.Equal(string.Empty, plan.ProductionQualifiedRcbReference);
        Assert.Contains("ProductionEligible certification remains separate", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan BuildPlan(
        MmsDynamicReportGuardedRuntimePlanningContext context,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence evidence)
    {
        var signals = new[] { Signal(1), Signal(2) };
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G26_P15B_IED",
            Source = "P1.5b subset fixture",
            Signals = signals
        };

        return MmsGuardedDynamicReportLegacySubsetRuntimePlanner.Build(
            catalog,
            signals,
            DynamicInventory(),
            DynamicAvailability(),
            Directory(),
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
            context,
            evidence);
    }

    private static MmsDynamicReportGuardedRuntimePlanningContext LegacyContext()
        => new()
        {
            Profile = BuildLegacyProfile(),
            CurrentIdentity = Identity()
        };

    private static MmsDynamicReportLegacyDataChangeCompatibilityEvidence Evidence()
        => new()
        {
            EvidenceId = "field-a3-dchg-subset",
            StableIdentityKey = Identity().StableIdentityKey,
            ModelFingerprint = Identity().ModelFingerprint,
            ProfileRevision = Identity().ProfileRevision,
            RcbReference = ProvenRcbReference,
            MemberReferences = DchgMembers(),
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            CleanupSucceeded = true
        };

    private static MmsDynamicReportQualificationProfile BuildLegacyProfile()
    {
        var members = StoredMembers();
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "p15b-envelope",
                ObservedAtUtc = Time(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                DefineRequestByteCount = 260,
                NegotiatedMaxMmsPduSize = 65000,
                RequestWithinKnownNegotiatedPdu = true,
                IsSuccess = true,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
                DynamicMutationAttempted = true,
                AssociationSurvived = true,
                CleanupSucceeded = true
            }
        ]);
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "p15b-envelope");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 1,
                ObservedAtUtc = Time(2),
                EvidenceId = "p15b-capacity"
            },
            "p15b-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = "p15b-activation",
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

        var report = MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
            activated,
            Identity(),
            new MmsDynamicInformationReportProof
            {
                EvidenceId = "p15b-legacy-gi-report",
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

        // Model the historical persisted profile: successful InformationReportProven chain,
        // but the stored discriminator predates the later explicit NO-GI dchg proof.
        return report with
        {
            InformationReportProof = report.InformationReportProof! with
            {
                Kind = MmsDynamicInformationReportKind.GeneralInterrogation
            }
        };
    }

    private static MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:g26:p15b",
            ModelFingerprint = "sha256:g26-p15b-model",
            Manufacturer = "Example",
            Model = "P15BIED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-p15b"
        };

    private static MmsReportInventory DynamicInventory()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "Unbuffer01",
            Reference = ProvenRcbReference,
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationState = "false",
            TriggerOptions = "dchg",
            ReportId = "Unbuffer01",
            ConfRev = "1"
        });
        return inventory;
    }

    private static MmsRcbAvailabilityResult DynamicAvailability()
        => new()
        {
            CheckedAtUtc = Time(10),
            ReportControls =
            [
                new MmsRcbAvailabilitySnapshot
                {
                    Reference = ProvenRcbReference,
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    Name = "Unbuffer01",
                    Mode = "URCB",
                    Buffered = false,
                    DataSetReference = string.Empty,
                    DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
                    ReportId = "Unbuffer01",
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
                    Reason = "P1.5b exact empty URCB fixture",
                    Attributes = ["DatSet", "RptEna", "TrgOps", "Resv"]
                }
            ]
        };

    private static MmsIedModelDirectory Directory()
        => new(Enumerable.Range(1, 4).Select(index => new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = $"Ind{index}.stVal",
            MmsItemName = $"GGIO1$ST$Ind{index}$stVal",
            Source = "P1.5b synthetic live directory",
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

    private static string[] StoredMembers() => [Member(1), Member(2), Member(3), Member(4)];
    private static string[] DchgMembers() => [Member(1), Member(2)];
    private static string Member(int index) => $"LD0/GGIO1$ST$Ind{index}$stVal";
    private static DateTimeOffset Time(int minutes) => DateTimeOffset.Parse("2026-08-28T00:00:00Z").AddMinutes(minutes);

    private const string ProvenRcbReference = "LD0/LLN0.RP.Unbuffer01";
    private const string ProvenDataSetReference = "LD0/LLN0.AR_G2Q";
}
