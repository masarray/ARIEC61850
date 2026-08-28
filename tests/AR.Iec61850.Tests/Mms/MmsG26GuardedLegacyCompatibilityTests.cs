using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26GuardedLegacyCompatibilityTests
{
    [Fact]
    public void LegacyGiProfile_WithExactPhysicalDchgEvidence_BuildsGuardedCompatibilityView()
    {
        var legacyContext = LegacyContext();
        var originalProof = legacyContext.Profile.InformationReportProof!;

        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            legacyContext,
            Evidence(),
            out var compatible,
            out var reason);

        Assert.True(accepted, reason);
        Assert.Equal(MmsDynamicInformationReportKind.GeneralInterrogation, originalProof.Kind);
        Assert.Equal(MmsDynamicInformationReportKind.DataChange, compatible.Profile.InformationReportProof!.Kind);
        Assert.StartsWith("legacy-compatibility-view:", compatible.Profile.InformationReportProof.EvidenceId, StringComparison.Ordinal);
        Assert.Contains("Persisted profile remains unchanged", reason, StringComparison.OrdinalIgnoreCase);

        var plan = BuildPlan(compatible);
        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.False(plan.ProductionDynamicActivationAuthorized);
        Assert.Equal(2, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.AcquisitionPlan.PollingFallbackSignalCount);
        var dynamic = Assert.Single(plan.AcquisitionPlan.Segments, segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Equal(ProvenRcbReference, dynamic.ReportControlReference);
        Assert.Equal(Members(), dynamic.ReportPlan!.DynamicPoints.Select(point => point.MmsReference).ToArray());
    }

    [Fact]
    public void LegacyGiProfile_WithoutCompatibilityEvidence_RemainsWithheld()
    {
        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            LegacyContext(),
            null,
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("no complete physical legacy dchg compatibility evidence", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_IdentityMismatch_RemainsWithheld()
    {
        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            LegacyContext(),
            Evidence() with { ModelFingerprint = "sha256:different" },
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("fingerprint", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_MemberMismatch_RemainsWithheld()
    {
        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            LegacyContext(),
            Evidence() with { MemberReferences = [Member(2), Member(1)] },
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("member sequence", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyGiProfile_IncompleteCleanupEvidence_RemainsWithheld()
    {
        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            LegacyContext(),
            Evidence() with { CleanupSucceeded = false },
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("no complete physical legacy dchg compatibility evidence", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StoredDataChangeProfile_DoesNotRequireLegacyEvidence()
    {
        var native = NativeContext();
        var accepted = MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
            native,
            null,
            out var compatible,
            out var reason);

        Assert.True(accepted, reason);
        Assert.Same(native.Profile, compatible.Profile);
        Assert.Contains("no legacy compatibility adaptation", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan BuildPlan(
        MmsDynamicReportGuardedRuntimePlanningContext context)
    {
        var signals = new[] { Signal(1), Signal(2) };
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G26_P15_IED",
            Source = "P1.5 legacy compatibility fixture",
            Signals = signals
        };

        return MmsGuardedDynamicReportRuntimePlanner.Build(
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
            context);
    }

    private static MmsDynamicReportGuardedRuntimePlanningContext LegacyContext()
    {
        var native = NativeContext();
        return native with
        {
            Profile = native.Profile with
            {
                InformationReportProof = native.Profile.InformationReportProof! with
                {
                    Kind = MmsDynamicInformationReportKind.GeneralInterrogation
                }
            }
        };
    }

    private static MmsDynamicReportGuardedRuntimePlanningContext NativeContext()
        => new()
        {
            Profile = BuildProfile(),
            CurrentIdentity = Identity()
        };

    private static MmsDynamicReportLegacyDataChangeCompatibilityEvidence Evidence()
        => new()
        {
            EvidenceId = "field-a3-dchg-proof",
            StableIdentityKey = Identity().StableIdentityKey,
            ModelFingerprint = Identity().ModelFingerprint,
            ProfileRevision = Identity().ProfileRevision,
            RcbReference = ProvenRcbReference,
            MemberReferences = Members(),
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            CleanupSucceeded = true
        };

    private static MmsDynamicReportQualificationProfile BuildProfile()
    {
        var members = Members();
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "p15-envelope",
                ObservedAtUtc = Time(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                DefineRequestByteCount = 200,
                NegotiatedMaxMmsPduSize = 65000,
                RequestWithinKnownNegotiatedPdu = true,
                IsSuccess = true,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
                DynamicMutationAttempted = true,
                AssociationSurvived = true,
                CleanupSucceeded = true
            }
        ]);
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "p15-envelope");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 1,
                ObservedAtUtc = Time(2),
                EvidenceId = "p15-capacity"
            },
            "p15-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = "p15-activation",
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
                EvidenceId = "p15-report",
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
            StableIdentityKey = "ied:g26:p15",
            ModelFingerprint = "sha256:g26-p15-model",
            Manufacturer = "Example",
            Model = "P15IED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-p15"
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
                    Reason = "P1.5 exact empty URCB fixture",
                    Attributes = ["DatSet", "RptEna", "TrgOps", "Resv"]
                }
            ]
        };

    private static MmsIedModelDirectory Directory()
        => new(new[] { 1, 2 }.Select(index => new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = $"Ind{index}.stVal",
            MmsItemName = $"GGIO1$ST$Ind{index}$stVal",
            Source = "P1.5 synthetic live directory",
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

    private static string[] Members() => [Member(1), Member(2)];
    private static string Member(int index) => $"LD0/GGIO1$ST$Ind{index}$stVal";
    private static DateTimeOffset Time(int minutes) => DateTimeOffset.Parse("2026-08-28T00:00:00Z").AddMinutes(minutes);

    private const string ProvenRcbReference = "LD0/LLN0.RP.Unbuffer01";
    private const string ProvenDataSetReference = "LD0/LLN0.AR_G2Q";
}
