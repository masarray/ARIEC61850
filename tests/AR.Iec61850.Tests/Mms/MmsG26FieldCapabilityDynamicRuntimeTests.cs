using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26FieldCapabilityDynamicRuntimeTests
{
    [Fact]
    public void PhysicalWitness_ProvesCapability_NotMemberScope_AllExactResidualSignalsBecomeDynamic()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var plan = BuildPlan(signals, Evidence(), DynamicAvailability(1, 2), maxMembers: 3);

        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.False(plan.ProductionDynamicActivationAuthorized);
        Assert.Equal(5, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.AcquisitionPlan.PollingFallbackSignalCount);

        var dynamicSegments = plan.AcquisitionPlan.Segments
            .Where(segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();
        Assert.Equal(2, dynamicSegments.Length);
        Assert.All(dynamicSegments, segment => Assert.InRange(segment.ReportPlan!.DynamicPoints.Count, 1, 3));

        var dynamicMembers = dynamicSegments
            .SelectMany(segment => segment.ReportPlan!.DynamicPoints)
            .Select(point => point.MmsReference)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(5, dynamicMembers.Count);
        Assert.Contains(Member(5), dynamicMembers);

        // The physical witness contains only members 1-2. Member 5 becoming dynamic is the
        // regression proof that P1.6 treats Q0/A3 as capability evidence rather than scope.
        Assert.DoesNotContain(Member(5), Evidence().MemberReferences);
        Assert.Contains("field-capability", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhysicalWitnessIdentityMismatch_FailsClosedToPolling()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var evidence = Evidence() with { ProfileRevision = "different-profile" };
        var plan = BuildPlan(signals, evidence, DynamicAvailability(1, 2), maxMembers: 3);

        Assert.True(plan.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(5, plan.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("profile revision", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhysicalWitnessWithoutCleanup_FailsClosedToPolling()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var evidence = Evidence() with { CleanupSucceeded = false };
        var plan = BuildPlan(signals, evidence, DynamicAvailability(1, 2), maxMembers: 3);

        Assert.True(plan.AutomaticDynamicActivationQuarantined);
        Assert.Equal(0, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(5, plan.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains("physical", plan.ProductionDynamicAuthorizationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshAvailabilityBoundsRuntime_OnlyVerifiedFreeSlotsAreUsed()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var availability = DynamicAvailability(1, 2);
        availability.ReportControls[1] = availability.ReportControls[1] with
        {
            Availability = MmsRcbOperationalAvailability.InUse,
            EnabledState = "true",
            Reason = "busy fixture"
        };

        var plan = BuildPlan(signals, Evidence(), availability, maxMembers: 3);

        Assert.False(plan.AutomaticDynamicActivationQuarantined);
        Assert.Equal(3, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(2, plan.AcquisitionPlan.PollingFallbackSignalCount);
        var dynamic = Assert.Single(plan.AcquisitionPlan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Equal(DynamicRcb(1), dynamic.ReportControlReference);
    }

    [Fact]
    public void RequestedMemberMissingFromLiveDirectory_RemainsPollingResidual()
    {
        var signals = Enumerable.Range(1, 5).Select(Signal).ToArray();
        var plan = BuildPlan(
            signals,
            Evidence(),
            DynamicAvailability(1, 2),
            maxMembers: 3,
            directory: Directory(1, 2, 3, 4));

        Assert.Equal(4, plan.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, plan.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains(plan.AcquisitionPlan.Assignments,
            assignment => assignment.Kind == MmsHybridAcquisitionKind.MmsPollingFallback &&
                          assignment.SignalReference.Contains("Ind5", StringComparison.OrdinalIgnoreCase));
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan BuildPlan(
        IReadOnlyCollection<Iec61850SignalDescriptor> signals,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence evidence,
        MmsRcbAvailabilityResult availability,
        int maxMembers,
        MmsIedModelDirectory? directory = null)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "G26_P16_IED",
            Source = "P1.6 field capability fixture",
            Signals = signals.ToArray()
        };

        return MmsGuardedDynamicReportFieldCapabilityRuntimePlanner.Build(
            catalog,
            signals,
            DynamicInventory(1, 2),
            availability,
            directory ?? Directory(1, 2, 3, 4, 5),
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
            LegacyContext(),
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
            EvidenceId = "field-a3-dchg-capability-witness",
            StableIdentityKey = Identity().StableIdentityKey,
            ModelFingerprint = Identity().ModelFingerprint,
            ProfileRevision = Identity().ProfileRevision,
            RcbReference = DynamicRcb(1),
            MemberReferences = [Member(1), Member(2)],
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            CleanupSucceeded = true
        };

    private static MmsDynamicReportQualificationProfile BuildLegacyProfile()
    {
        var members = new[] { Member(1), Member(2), Member(3), Member(4) };
        var assessment = MmsDynamicDataSetQualificationLadder.Assess(
        [
            new MmsDynamicDataSetQualificationAttemptEvidence
            {
                AttemptId = "p16-envelope",
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
        var envelope = MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "p16-envelope");
        var profile = MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            Identity(),
            envelope,
            assessment,
            new MmsDynamicReportCapacityEvidence
            {
                ObservedFreeBrcbSlots = 0,
                ObservedFreeUrcbSlots = 2,
                ObservedAtUtc = Time(2),
                EvidenceId = "p16-capacity"
            },
            "p16-envelope-evidence",
            Time(3));

        var activated = MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
            profile,
            Identity(),
            new MmsDynamicRcbActivationProof
            {
                EvidenceId = "p16-activation",
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

        var report = MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
            activated,
            Identity(),
            new MmsDynamicInformationReportProof
            {
                EvidenceId = "p16-legacy-gi-report",
                ObservedAtUtc = Time(5),
                RcbReference = DynamicRcb(1),
                DataSetReference = ProvenDataSetReference,
                MemberReferences = members,
                Kind = MmsDynamicInformationReportKind.DataChange,
                ActualInformationReportReceived = true,
                ReportIdentityVerified = true,
                ExactMemberMappingVerified = true,
                AssociationHealthyAfterReport = true,
                ReportAuthoritativePointCount = members.Length
            });

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
            StableIdentityKey = "ied:g26:p16",
            ModelFingerprint = "sha256:g26-p16-model",
            Manufacturer = "Example",
            Model = "P16IED",
            FirmwareRevision = "1.0.0",
            ProfileRevision = "cfg-p16"
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
                Reason = "P1.6 exact empty URCB fixture",
                Attributes = ["DatSet", "RptEna", "TrgOps", "Resv"]
            }).ToList()
        };

    private static MmsIedModelDirectory Directory(params int[] indexes)
        => new(indexes.Select(index => new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = $"Ind{index}.stVal",
            MmsItemName = $"GGIO1$ST$Ind{index}$stVal",
            Source = "P1.6 synthetic live directory",
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
    private static DateTimeOffset Time(int minutes) => DateTimeOffset.Parse("2026-08-28T00:00:00Z").AddMinutes(minutes);

    private const string ProvenDataSetReference = "LD0/LLN0.AR_G2Q";
}
