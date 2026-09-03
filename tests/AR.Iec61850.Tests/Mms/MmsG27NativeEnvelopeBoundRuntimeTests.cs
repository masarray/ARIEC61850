using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG27NativeEnvelopeBoundRuntimeTests
{
    [Fact]
    public void NativeRuntime_GenericLimitAbovePhysicalEnvelope_UsesMoreBoundedGroupsInsteadOfOversizedDataSet()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var profile = NativeProfile();
        var context = new MmsDynamicReportGuardedRuntimePlanningContext
        {
            Profile = profile,
            CurrentIdentity = Identity()
        };

        var plan = MmsGuardedDynamicReportNativeFieldCapabilityEnvelopeBoundRuntimePlanner.Build(
            new Iec61850SignalCatalogDocument
            {
                IedName = "G27_BOUND_IED",
                Source = "P1.7 envelope-bound runtime fixture",
                Signals = signals
            },
            signals,
            DynamicInventory(1, 2, 3),
            DynamicAvailability(1, 2, 3),
            Directory(1, 2, 3, 4, 5),
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = true,
                SupportsDefineNamedVariableList = true,
                SupportsDeleteNamedVariableList = true
            },
            new MmsHybridReportAcquisitionOptions
            {
                MaxDynamicReportPlans = 8,
                MaxDynamicMembersPerReport = 64,
                AllowStaticBrcb = false,
                AllowStaticUrcb = false,
                AllowDynamicBrcb = false,
                AllowDynamicUrcb = true,
                AllowCallerOwnedReports = false,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            },
            context,
            NativeEvidence());

        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.Equal(5, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.AcquisitionPlan.PollingFallbackSignalCount);

        var groups = plan.AcquisitionPlan.Segments
            .Where(segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();

        Assert.Equal(3, groups.Length);
        Assert.Equal(new[] { 2, 2, 1 }, groups.Select(group => group.Signals.Count).ToArray());
        Assert.All(groups, group => Assert.True(group.Signals.Count <= profile.ProvenSafeMemberCount));
        Assert.All(groups, group => Assert.Contains("AR_HYB_", group.DataSetReference, StringComparison.OrdinalIgnoreCase));
    }

    private static MmsDynamicReportQualificationProfile NativeProfile()
    {
        var members = new[] { Member(1), Member(2) };
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "g27-bound-envelope",
                ObservedAtUtc = Time(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                DefineRequestByteCount = 220,
                NegotiatedMaxMmsPduSize = 65000,
                RequestWithinKnownNegotiatedPdu = true,
                IsSuccess = true,
                FailureStage = MmsDynamicDataSetQualificationFailureStage.None,
                DynamicMutationAttempted = true,
                AssociationSurvived = true,
                CleanupSucceeded = true
            }
        ]);
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "g27-bound-envelope");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(), envelope, assessment, null, "g27-bound-envelope-evidence", Time(2));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = ActivationEvidenceId,
                ObservedAtUtc = Time(3),
                RcbReference = DynamicRcb(1),
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
                EvidenceId = ReportEvidenceId,
                ObservedAtUtc = Time(4),
                RcbReference = DynamicRcb(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                Kind = MmsDynamicInformationReportKind.DataChange,
                ActualInformationReportReceived = true,
                ReportIdentityVerified = true,
                ExactMemberMappingVerified = true,
                AssociationHealthyAfterReport = true,
                ReportAuthoritativePointCount = 1
            });
    }

    private static MmsDynamicReportNativeFieldCapabilityEvidence NativeEvidence()
        => new()
        {
            EvidenceId = "g27-bound-native-witness",
            ObservedAtUtc = Time(5),
            StableIdentityKey = Identity().StableIdentityKey,
            ModelFingerprint = Identity().ModelFingerprint,
            ProfileRevision = Identity().ProfileRevision,
            RcbReference = DynamicRcb(1),
            DataSetReference = ProvenDataSetReference,
            RcbActivationEvidenceId = ActivationEvidenceId,
            InformationReportEvidenceId = ReportEvidenceId,
            IncludedMemberReferences = [Member(1)],
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            MonitorCleanupSucceeded = true,
            ProofFieldRestoreSucceeded = true,
            FreshCleanupClosureSucceeded = true
        };

    private static MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:g27:bound",
            ModelFingerprint = "sha256:g27-bound-model",
            Manufacturer = "Example",
            Model = "BoundIED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-bound"
        };

    private static MmsReportInventory DynamicInventory(params int[] indexes)
    {
        var inventory = new MmsReportInventory();
        foreach (var index in indexes)
        {
            inventory.ReportControls.Add(new MmsReportControlCandidate
            {
                Domain = "LD0",
                LogicalNode = "LLN0",
                FunctionalConstraint = "RP",
                Name = $"Unbuffer{index:00}",
                Reference = DynamicRcb(index),
                Buffered = false,
                DataSetReference = string.Empty,
                DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
                EnabledState = "false",
                ReservationState = "false",
                TriggerOptions = "dchg",
                ReportId = $"Unbuffer{index:00}",
                ConfRev = "1"
            });
        }
        return inventory;
    }

    private static MmsRcbAvailabilityResult DynamicAvailability(params int[] indexes)
        => new()
        {
            CheckedAtUtc = Time(10),
            ReportControls = indexes.Select(index => new MmsRcbAvailabilitySnapshot
            {
                Reference = DynamicRcb(index),
                Domain = "LD0",
                LogicalNode = "LLN0",
                Name = $"Unbuffer{index:00}",
                Mode = "URCB",
                Buffered = false,
                DataSetReference = string.Empty,
                DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
                ReportId = $"Unbuffer{index:00}",
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
                Reason = "P1.7 exact empty URCB fixture",
                Attributes = ["DatSet", "RptEna", "TrgOps", "OptFlds", "Resv"]
            }).ToArray()
        };

    private static MmsIedModelDirectory Directory(params int[] indexes)
        => new(indexes.Select(index => new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = $"Ind{index}.stVal",
            MmsItemName = $"GGIO1$ST$Ind{index}$stVal",
            Source = "P1.7 synthetic live directory",
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

    private static string DynamicRcb(int index) => $"LD0/LLN0.RP.Unbuffer{index:00}";
    private static string Member(int index) => $"LD0/GGIO1$ST$Ind{index}$stVal";
    private static DateTimeOffset Time(int minutes) => DateTimeOffset.Parse("2026-09-03T00:00:00Z").AddMinutes(minutes);

    private const string ProvenDataSetReference = "LD0/LLN0.AR_G27_BOUND";
    private const string ActivationEvidenceId = "g27-bound-activation";
    private const string ReportEvidenceId = "g27-bound-dchg-report";
}
