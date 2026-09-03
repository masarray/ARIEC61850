using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26GuardedDynamicRuntimePlannerTests
{
    [Fact]
    public void InformationReportProven_ExactProvenRcb_AuthorizesGuardedDynamicRuntime()
    {
        var result = Build(
            [Signal(1), Signal(2)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1, 2, 3, 4),
            GuardedContext());

        Assert.True(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.False(result.AutomaticDynamicActivationQuarantined);
        Assert.False(result.ProductionDynamicActivationAuthorized);
        Assert.Equal(2, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.PollingFallbackSignalCount);

        var dynamic = Assert.Single(
            result.AcquisitionPlan.Segments,
            x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Equal(ProvenRcbReference, dynamic.ReportControlReference);
        Assert.Equal(
            [Member(1), Member(2)],
            dynamic.ReportPlan!.DynamicPoints.Select(point => point.MmsReference).ToArray());
        Assert.Contains("guarded", result.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InformationReportProven_UnprovenMember_RemainsOnPolling()
    {
        var result = Build(
            [Signal(1), OutsideSignal()],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1, 2, 3, 4, 99),
            GuardedContext());

        Assert.False(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(1, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);

        var dynamic = Assert.Single(
            result.AcquisitionPlan.Segments,
            x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Single(dynamic.ReportPlan!.DynamicPoints);
        Assert.Equal(Member(1), dynamic.ReportPlan.DynamicPoints[0].MmsReference);
    }

    [Fact]
    public void InformationReportProven_IdentityMismatch_FailsClosedToPolling()
    {
        var context = GuardedContext() with
        {
            CurrentIdentity = Identity() with { ModelFingerprint = "sha256:different" }
        };

        var result = Build(
            [Signal(1)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1),
            context);

        Assert.True(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("fingerprint", result.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InformationReportProven_DifferentFreeRcb_IsNeverSubstituted()
    {
        const string otherRcb = "LD0/LLN0.RP.OtherFree01";
        var result = Build(
            [Signal(1)],
            DynamicAvailability(otherRcb),
            DynamicInventory(otherRcb),
            Directory(1),
            GuardedContext());

        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.DoesNotContain(result.AcquisitionPlan.Segments, segment =>
            segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb &&
            segment.ReportControlReference.Equals(otherRcb, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InformationReportProven_NonDataChangeProof_RemainsQuarantined()
    {
        var profile = BuildInformationReportProvenProfile();
        profile = profile with
        {
            InformationReportProof = profile.InformationReportProof! with
            {
                Kind = MmsDynamicInformationReportKind.GeneralInterrogation
            }
        };

        var result = Build(
            [Signal(1)],
            DynamicAvailability(ProvenRcbReference),
            DynamicInventory(ProvenRcbReference),
            Directory(1),
            new MmsDynamicReportGuardedRuntimePlanningContext
            {
                Profile = profile,
                CurrentIdentity = Identity()
            });

        Assert.True(result.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("data-change", result.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        IReadOnlyList<Iec61850SignalDescriptor> signals,
        MmsRcbAvailabilityResult availability,
        MmsReportInventory inventory,
        MmsIedModelDirectory directory,
        MmsDynamicReportGuardedRuntimePlanningContext context)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G26_GUARDED_IED",
            Source = "Project-owned guarded runtime fixture",
            Signals = signals
        };

        return MmsGuardedDynamicReportRuntimePlanner.Build(
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

    private static MmsDynamicReportGuardedRuntimePlanningContext GuardedContext()
        => new()
        {
            Profile = BuildInformationReportProvenProfile(),
            CurrentIdentity = Identity()
        };

    private static MmsDynamicReportQualificationProfile BuildInformationReportProvenProfile()
    {
        var members = Members(1, 2, 3, 4);
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "g26-guarded-envelope-4",
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
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(
            assessment,
            "g26-guarded-envelope-4");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 1,
                ObservedAtUtc = Time(2),
                EvidenceId = "g26-guarded-capacity"
            },
            "g26-guarded-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = "g26-guarded-activation",
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
                EvidenceId = "g26-guarded-information-report",
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
            StableIdentityKey = "ied:g26:guarded",
            ModelFingerprint = "sha256:g26-guarded-model",
            Manufacturer = "Example",
            Model = "GuardedIED",
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
                    Reason = "G2.6 guarded exact empty dynamic slot fixture",
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
            Source = "G2.6 guarded synthetic live directory",
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
        => DateTimeOffset.Parse("2026-08-26T00:00:00Z").AddMinutes(minutes);

    private const string ProvenRcbReference = "LD0/LLN0.RP.Unbuffer02";
    private const string ProvenDataSetReference = "LD0/LLN0.AR_G2Q";
}
