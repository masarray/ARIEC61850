using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG27NativeFieldCapabilityRuntimeTests
{
    [Fact]
    public void NativeDataChangeWitness_WithCompleteCleanup_UnlocksGeneralDynamicCoverage()
    {
        var signals = Enumerable.Range(1, 3).Select(Signal).ToArray();
        var plan = BuildPlan(signals, NativeEvidence(), maxMembers: 2);

        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.False(plan.ProductionDynamicActivationAuthorized);
        Assert.Equal(3, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.AcquisitionPlan.PollingFallbackSignalCount);

        var dynamicSegments = plan.AcquisitionPlan.Segments
            .Where(segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();
        Assert.Equal(2, dynamicSegments.Length);
        Assert.Contains(dynamicSegments, segment => segment.ReportPlan!.DynamicPoints.Any(point =>
            point.MmsReference.Equals(Member(3), StringComparison.OrdinalIgnoreCase)));

        var dataSets = dynamicSegments
            .Select(segment => segment.DataSetReference)
            .ToArray();
        Assert.All(dataSets, reference => Assert.Contains("AR_HYB_", reference, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(dataSets.Length, dataSets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("native per-IED field-capability", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeDataChangeWitness_MissingFreshCleanupClosure_FailsClosedToPolling()
    {
        var signals = Enumerable.Range(1, 3).Select(Signal).ToArray();
        var evidence = NativeEvidence() with { FreshCleanupClosureSucceeded = false };
        var plan = BuildPlan(signals, evidence, maxMembers: 2);

        Assert.True(plan.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(3, plan.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("complete native physical", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeDataChangeWitness_FromDifferentDataSet_FailsClosed()
    {
        var context = new MmsDynamicReportGuardedRuntimePlanningContext
        {
            Profile = NativeProfile(),
            CurrentIdentity = Identity()
        };
        var evidence = NativeEvidence() with { DataSetReference = "LD0/LLN0.AR_OTHER" };

        var accepted = MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
            context,
            evidence,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("DataSet", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeDataChangeProfile_WithoutSeparateCleanupWitness_IsNotEnough()
    {
        var context = new MmsDynamicReportGuardedRuntimePlanningContext
        {
            Profile = NativeProfile(),
            CurrentIdentity = Identity()
        };

        var accepted = MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
            context,
            null,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("dchg + cleanup", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan BuildPlan(
        IReadOnlyCollection<Iec61850SignalDescriptor> signals,
        MmsDynamicReportNativeFieldCapabilityEvidence evidence,
        int maxMembers)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G27_NATIVE_IED",
            Source = "P1.7 native field-capability fixture",
            Signals = signals.ToArray()
        };

        return MmsGuardedDynamicReportNativeFieldCapabilityStableRuntimePlanner.Build(
            catalog,
            signals,
            DynamicInventory(1, 2),
            DynamicAvailability(1, 2),
            Directory(1, 2, 3),
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = true,
                SupportsDefineNamedVariableList = true,
                SupportsDeleteNamedVariableList = true
            },
            new MmsHybridReportAcquisitionOptions
            {
                MaxDynamicReportPlans = 4,
                MaxDynamicMembersPerReport = maxMembers,
                AllowStaticBrcb = false,
                AllowStaticUrcb = false,
                AllowDynamicBrcb = false,
                AllowDynamicUrcb = true,
                AllowCallerOwnedReports = false,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            },
            new MmsDynamicReportGuardedRuntimePlanningContext
            {
                Profile = NativeProfile(),
                CurrentIdentity = Identity()
            },
            evidence);
    }

    private static MmsDynamicReportNativeFieldCapabilityEvidence NativeEvidence()
        => new()
        {
            EvidenceId = "g27-native-capability-witness",
            ObservedAtUtc = Time(6),
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

    private static MmsDynamicReportQualificationProfile NativeProfile()
    {
        var members = new[] { Member(1), Member(2) };
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "g27-envelope",
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
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "g27-envelope");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 2,
                ObservedAtUtc = Time(2),
                EvidenceId = "g27-capacity"
            },
            "g27-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = ActivationEvidenceId,
                ObservedAtUtc = Time(4),
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
                ObservedAtUtc = Time(5),
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

    private static MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:g27:native",
            ModelFingerprint = "sha256:g27-native-model",
            Manufacturer = "Example",
            Model = "NativeIED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-g27"
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
            ReportControls = indexes.Select(DynamicSnapshot).ToArray()
        };

    private static MmsRcbAvailabilitySnapshot DynamicSnapshot(int index)
        => new()
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
    private static DateTimeOffset Time(int minutes) => DateTimeOffset.Parse("2026-09-01T00:00:00Z").AddMinutes(minutes);

    private const string ProvenDataSetReference = "LD0/LLN0.AR_G27_BOOT";
    private const string ActivationEvidenceId = "g27-native-activation";
    private const string ReportEvidenceId = "g27-native-dchg-report";
}
