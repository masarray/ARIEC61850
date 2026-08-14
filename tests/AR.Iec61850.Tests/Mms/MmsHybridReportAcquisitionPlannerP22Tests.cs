using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsHybridReportAcquisitionPlannerP22Tests
{
    [Fact]
    public void StaticPartialCoverage_UsesBrcbAndUrcb_ThenPollsOnlyResidual()
    {
        var a = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST", "LD0/LLN0.dsA", "LD0/GGIO1.Ind1");
        var b = Signal("LD0/GGIO1.Ind2.stVal", "LD0/GGIO1$ST$Ind2$stVal", "ST", "LD0/LLN0.dsA", "LD0/GGIO1.Ind2");
        var c = Signal("LD0/GGIO1.Ind3.stVal", "LD0/GGIO1$ST$Ind3$stVal", "ST", "LD0/LLN0.dsB", "LD0/GGIO1.Ind3");
        var d = Signal("LD0/GGIO1.Ind4.stVal", "LD0/GGIO1$ST$Ind4$stVal", "ST");
        var inventory = Inventory(
            Rcb("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsA"),
            Rcb("LD0/LLN0.RP.U01", false, "LD0/LLN0.dsB"));
        var availability = Availability(
            StaticAvailable("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsA",
                Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST"),
                Member("LD0", "GGIO1$ST$Ind2", "LD0/GGIO1.Ind2", "ST")),
            StaticAvailable("LD0/LLN0.RP.U01", false, "LD0/LLN0.dsB",
                Member("LD0", "GGIO1$ST$Ind3", "LD0/GGIO1.Ind3", "ST")));

        var plan = Build([a, b, c, d], inventory, availability, EmptyDirectory());

        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, plan.Status);
        Assert.Equal(4, plan.RequestedSignalCount);
        Assert.Equal(3, plan.ReportCoveredSignalCount);
        Assert.Equal(2, plan.StaticBrcbSignalCount);
        Assert.Equal(1, plan.StaticUrcbSignalCount);
        Assert.Equal(1, plan.PollingFallbackSignalCount);
        Assert.Equal(0, plan.UncoveredSignalCount);
        Assert.Equal(3, plan.Segments.Count);
        Assert.Contains(plan.Segments, x => x.Kind == MmsHybridAcquisitionKind.StaticBrcb && x.SignalCount == 2);
        Assert.Contains(plan.Segments, x => x.Kind == MmsHybridAcquisitionKind.StaticUrcb && x.SignalCount == 1);
        Assert.Contains(plan.Segments, x => x.Kind == MmsHybridAcquisitionKind.MmsPollingFallback && x.SignalCount == 1);
        Assert.Equal(2, plan.Capability.StaticUsableCount);
    }

    [Fact]
    public void CallerOwnedStatic_IsReusedReadOnly_WithoutConfigurationWrite()
    {
        var signal = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST", "LD0/LLN0.dsA", "LD0/GGIO1.Ind1");
        var inventory = Inventory(Rcb("LD0/LLN0.RP.U01", false, "LD0/LLN0.dsA", enabled: "true"));
        var snapshot = Copy(
            StaticAvailable(
                "LD0/LLN0.RP.U01",
                false,
                "LD0/LLN0.dsA",
                Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST")),
            availability: MmsRcbOperationalAvailability.UsedByCaller,
            enabledState: "true",
            reservationState: "true");

        var plan = Build([signal], inventory, Availability(snapshot), EmptyDirectory());

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        var segment = Assert.Single(plan.Segments);
        Assert.Equal(MmsHybridAcquisitionKind.StaticUrcb, segment.Kind);
        Assert.Equal(MmsHybridReportActivation.AlreadyActiveByCaller, segment.Activation);
        Assert.True(segment.IsAlreadyActiveByCaller);
        Assert.False(segment.RequiresWrite);
        Assert.Equal(MmsReportSubscriptionPlanStatus.ReadyReadOnly, segment.ReportPlan?.Status);
    }

    [Fact]
    public void DynamicResidual_UsesOnlyFreshVerifiedEmptyUrcb_ThenPollsUnresolvedResidual()
    {
        var a = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST", "LD0/LLN0.dsA", "LD0/GGIO1.Ind1");
        var b = Signal("LD0/GGIO1.Ind2.stVal", "LD0/GGIO1$ST$Ind2$stVal", "ST");
        var c = Signal("LD0/GGIO1.Ind3.stVal", "LD0/GGIO1$ST$Ind3$stVal", "ST");
        var inventory = Inventory(
            Rcb("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsA"),
            Rcb("LD0/LLN0.RP.U02", false, string.Empty));
        var availability = Availability(
            StaticAvailable("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsA",
                Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST")),
            DynamicEmpty("LD0/LLN0.RP.U02", false));
        var directory = Directory(Point("LD0", "GGIO1", "ST", "Ind2.stVal", "GGIO1$ST$Ind2$stVal"));

        var plan = Build([a, b, c], inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, plan.Status);
        Assert.Equal(2, plan.ReportCoveredSignalCount);
        Assert.Equal(1, plan.StaticBrcbSignalCount);
        Assert.Equal(1, plan.DynamicUrcbSignalCount);
        Assert.Equal(1, plan.PollingFallbackSignalCount);
        var dynamicSegment = Assert.Single(plan.Segments, x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.True(dynamicSegment.RequiresWrite);
        Assert.Equal(MmsHybridReportActivation.ConfigureDynamicDataSet, dynamicSegment.Activation);
        Assert.Equal("LD0/LLN0.AR_HYB_01", dynamicSegment.ReportPlan?.DataSetReference);
        Assert.Single(dynamicSegment.ReportPlan?.DynamicPoints ?? Array.Empty<MmsFcResolvedPoint>());
        Assert.Equal(1, plan.Capability.DynamicUsableCount);
    }

    [Fact]
    public void DynamicBrcb_RequiresVerifiedEmptyDatSetAndExplicitFreeResvTms()
    {
        var signal = Signal("LD0/MMXU1.Hz.mag.f", "LD0/MMXU1$MX$Hz$mag$f", "MX");
        var inventory = Inventory(Rcb("LD0/LLN0.BR.D01", true, string.Empty));
        var availability = Availability(DynamicEmpty("LD0/LLN0.BR.D01", true));
        var directory = Directory(Point("LD0", "MMXU1", "MX", "Hz.mag.f", "MMXU1$MX$Hz$mag$f"));

        var plan = Build([signal], inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        Assert.Equal(1, plan.DynamicBrcbSignalCount);
        var segment = Assert.Single(plan.Segments);
        Assert.Equal(MmsHybridAcquisitionKind.DynamicBrcb, segment.Kind);
        Assert.True(segment.RequiresWrite);
        Assert.Equal("0", segment.Availability?.ReservationTimeSeconds);
        Assert.Equal(MmsRcbDataSetProbeState.ReadSucceeded, segment.Availability?.DataSetProbeState);
    }

    [Fact]
    public void BusyAndReservationUnknownDynamicRcbs_AreNeverClaimed()
    {
        var signal = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST");
        var inventory = Inventory(
            Rcb("LD0/LLN0.RP.BUSY", false, string.Empty),
            Rcb("LD0/LLN0.RP.UNKNOWN", false, string.Empty));
        var busy = Copy(
            DynamicEmpty("LD0/LLN0.RP.BUSY", false),
            availability: MmsRcbOperationalAvailability.InUse,
            enabledState: "true",
            reservationState: "true");
        var unknownReservation = Copy(
            DynamicEmpty("LD0/LLN0.RP.UNKNOWN", false),
            reservationState: string.Empty);
        var directory = Directory(Point("LD0", "GGIO1", "ST", "Ind1.stVal", "GGIO1$ST$Ind1$stVal"));

        var plan = Build([signal], inventory, Availability(busy, unknownReservation), directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.PollingOnly, plan.Status);
        Assert.Equal(0, plan.ReportCoveredSignalCount);
        Assert.Equal(1, plan.PollingFallbackSignalCount);
        Assert.DoesNotContain(plan.Segments, x => x.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.Equal(1, plan.Capability.BusyCount);
        Assert.Equal(0, plan.Capability.DynamicUsableCount);
    }

    [Fact]
    public void StaticCoverage_RecognizesEffectiveAlternateMmsReference_WithoutChangingCanonicalIdentity()
    {
        const string canonical = "LD0/MMXU1$MX$TotW$mag$f";
        const string effective = "LD0/MMXU1$MX$TotW$instMag$f";
        var signal = new Iec61850SignalDescriptor
        {
            DesignReference = "LD0/MMXU1.TotW.mag.f",
            CanonicalMmsReference = canonical,
            EffectiveMmsReference = effective,
            ObservedMmsReference = effective,
            FunctionalConstraint = "MX",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery
        };
        var inventory = Inventory(Rcb("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsM"));
        var availability = Availability(
            StaticAvailable("LD0/LLN0.BR.B01", true, "LD0/LLN0.dsM",
                Member("LD0", "MMXU1$MX$TotW$instMag$f", "LD0/MMXU1.TotW.instMag.f", "MX")));

        var plan = Build([signal], inventory, availability, EmptyDirectory());

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        Assert.Equal(1, plan.StaticBrcbSignalCount);
        var assignment = Assert.Single(plan.Assignments);
        Assert.Equal(effective, assignment.SignalReference);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, assignment.Kind);
        Assert.Equal(canonical, signal.CanonicalMmsReference);
    }

    [Fact]
    public void PollingDisabled_LeavesResidualUncovered_AndNeverCallsItMissing()
    {
        var signal = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST");

        var plan = Build(
            [signal],
            Inventory(),
            Availability(),
            EmptyDirectory(),
            new MmsHybridReportAcquisitionOptions { AllowPollingFallback = false });

        Assert.Equal(MmsHybridAcquisitionPlanStatus.Incomplete, plan.Status);
        Assert.Equal(1, plan.UncoveredSignalCount);
        Assert.Equal(0, plan.PollingFallbackSignalCount);
        Assert.Contains(plan.Blockers, x => x.Contains("uncovered", StringComparison.OrdinalIgnoreCase));
        Assert.All(plan.Assignments, x => Assert.DoesNotContain("missing", x.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.All(plan.Assignments, x => Assert.Contains("No absence conclusion", x.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Capability_DistinguishesDiscoveredCheckedUsableBusyAndUnknownRcbs()
    {
        var signal = Signal("LD0/GGIO1.Ind1.stVal", "LD0/GGIO1$ST$Ind1$stVal", "ST");
        var inventory = Inventory(
            Rcb("LD0/LLN0.BR.S", true, "LD0/LLN0.dsA"),
            Rcb("LD0/LLN0.RP.D", false, string.Empty),
            Rcb("LD0/LLN0.RP.B", false, string.Empty),
            Rcb("LD0/LLN0.RP.U", false, string.Empty));
        var staticAvailable = StaticAvailable("LD0/LLN0.BR.S", true, "LD0/LLN0.dsA",
            Member("LD0", "GGIO1$ST$Other", "LD0/GGIO1.Other", "ST"));
        var dynamic = DynamicEmpty("LD0/LLN0.RP.D", false);
        var busy = Copy(DynamicEmpty("LD0/LLN0.RP.B", false), availability: MmsRcbOperationalAvailability.InUse, enabledState: "true");
        var unknown = Copy(DynamicEmpty("LD0/LLN0.RP.U", false), availability: MmsRcbOperationalAvailability.Unknown, reservationState: string.Empty);

        var plan = Build([signal], inventory, Availability(staticAvailable, dynamic, busy, unknown), EmptyDirectory());

        Assert.Equal(4, plan.Capability.DiscoveredRcbCount);
        Assert.Equal(4, plan.Capability.CheckedRcbCount);
        Assert.Equal(1, plan.Capability.BrcbCount);
        Assert.Equal(3, plan.Capability.UrcbCount);
        Assert.Equal(1, plan.Capability.StaticConfiguredCount);
        Assert.Equal(1, plan.Capability.StaticUsableCount);
        Assert.Equal(1, plan.Capability.DynamicUsableCount);
        Assert.Equal(1, plan.Capability.BusyCount);
        Assert.Equal(1, plan.Capability.UnknownCount);
    }

    private static MmsHybridReportAcquisitionPlan Build(
        IReadOnlyList<Iec61850SignalDescriptor> signals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory directory,
        MmsHybridReportAcquisitionOptions? options = null)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P2.2 synthetic regression",
            Signals = signals
        };
        return MmsHybridReportAcquisitionPlanner.Build(catalog, signals, inventory, availability, directory, options);
    }

    private static Iec61850SignalDescriptor Signal(
        string userReference,
        string mmsReference,
        string fc,
        string? dataSetReference = null,
        string? memberReference = null)
    {
        var memberships = string.IsNullOrWhiteSpace(dataSetReference)
            ? Array.Empty<Iec61850SignalDataSetMembership>()
            :
            [
                new Iec61850SignalDataSetMembership
                {
                    DataSetReference = dataSetReference,
                    MemberIndex = 0,
                    OriginalMemberReference = memberReference ?? userReference,
                    CanonicalMemberReference = memberReference ?? userReference,
                    FunctionalConstraint = fc,
                    IsPrimaryValueForMember = true
                }
            ];

        return new Iec61850SignalDescriptor
        {
            DesignReference = userReference,
            CanonicalMmsReference = mmsReference,
            EffectiveMmsReference = mmsReference,
            FunctionalConstraint = fc,
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            DataSetMemberships = memberships,
            IsStaticDataSetMandatory = memberships.Length > 0,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };
    }

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsReportControlCandidate Rcb(string reference, bool buffered, string dataSetReference, string enabled = "false")
    {
        var (domain, logicalNode, name) = ParseRcbReference(reference);
        return new MmsReportControlCandidate
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = buffered ? "BR" : "RP",
            Name = name,
            Reference = reference,
            Buffered = buffered,
            DataSetReference = dataSetReference,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "Synthetic P2.2 fixture: live DatSet read succeeded.",
            EnabledState = enabled,
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            ReportId = reference,
            ConfRev = "1",
            Status = "Attribute-probed"
        };
    }

    private static MmsRcbAvailabilityResult Availability(params MmsRcbAvailabilitySnapshot[] snapshots)
        => new() { ReportControls = snapshots };

    private static MmsRcbAvailabilitySnapshot StaticAvailable(
        string reference,
        bool buffered,
        string dataSetReference,
        params MmsDataSetDirectoryMember[] members)
    {
        var (domain, logicalNode, name) = ParseRcbReference(reference);
        return new MmsRcbAvailabilitySnapshot
        {
            Reference = reference,
            Domain = domain,
            LogicalNode = logicalNode,
            Name = name,
            Mode = buffered ? "BRCB" : "URCB",
            Buffered = buffered,
            DataSetReference = dataSetReference,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "Synthetic P2.2 fixture: populated DatSet confirmed.",
            ReportId = reference,
            ConfRev = "1",
            EnabledState = "false",
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            DataSetDirectoryRead = true,
            DataSetDirectorySuccess = true,
            DataSetMemberCount = members.Length,
            DataSetMembers = members,
            Availability = MmsRcbOperationalAvailability.Available,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "Synthetic exact available static RCB."
        };
    }

    private static MmsRcbAvailabilitySnapshot DynamicEmpty(string reference, bool buffered)
    {
        var (domain, logicalNode, name) = ParseRcbReference(reference);
        return new MmsRcbAvailabilitySnapshot
        {
            Reference = reference,
            Domain = domain,
            LogicalNode = logicalNode,
            Name = name,
            Mode = buffered ? "BRCB" : "URCB",
            Buffered = buffered,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "Synthetic P2.2 fixture: empty DatSet confirmed.",
            ReportId = reference,
            ConfRev = "1",
            EnabledState = "false",
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "Synthetic exact verified-empty dynamic RCB slot."
        };
    }

    private static MmsRcbAvailabilitySnapshot Copy(
        MmsRcbAvailabilitySnapshot source,
        MmsRcbOperationalAvailability? availability = null,
        string? enabledState = null,
        string? reservationState = null)
        => new()
        {
            CheckedAtUtc = source.CheckedAtUtc,
            Reference = source.Reference,
            Domain = source.Domain,
            LogicalNode = source.LogicalNode,
            Name = source.Name,
            Mode = source.Mode,
            Buffered = source.Buffered,
            DataSetReference = source.DataSetReference,
            DataSetProbeState = source.DataSetProbeState,
            DataSetProbeMessage = source.DataSetProbeMessage,
            ReportId = source.ReportId,
            ConfRev = source.ConfRev,
            BufferTimeMs = source.BufferTimeMs,
            IntegrityPeriodMs = source.IntegrityPeriodMs,
            TriggerOptions = source.TriggerOptions,
            OptionalFields = source.OptionalFields,
            EnabledState = enabledState ?? source.EnabledState,
            ReservationState = reservationState ?? source.ReservationState,
            ReservationTimeSeconds = source.ReservationTimeSeconds,
            Owner = source.Owner,
            DataSetDirectoryRead = source.DataSetDirectoryRead,
            DataSetDirectorySuccess = source.DataSetDirectorySuccess,
            DataSetIsDeletable = source.DataSetIsDeletable,
            DataSetMemberCount = source.DataSetMemberCount,
            DataSetMembers = source.DataSetMembers,
            Availability = availability ?? source.Availability,
            Confidence = source.Confidence,
            Reason = source.Reason,
            ProbeDiagnostics = source.ProbeDiagnostics
        };

    private static MmsDataSetDirectoryMember Member(string domain, string item, string userReference, string fc)
        => new()
        {
            Domain = domain,
            MmsItemName = item,
            UserReference = userReference,
            FunctionalConstraint = fc,
            LogicalNode = item.Split('$', StringSplitOptions.RemoveEmptyEntries)[0]
        };

    private static MmsFcResolvedPoint Point(string domain, string logicalNode, string fc, string dataObjectPath, string item)
        => new()
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = fc,
            DataObjectPath = dataObjectPath,
            MmsItemName = item,
            Source = "P2.2 synthetic live directory",
            Confidence = 100
        };

    private static MmsIedModelDirectory Directory(params MmsFcResolvedPoint[] points)
        => new(points);

    private static MmsIedModelDirectory EmptyDirectory()
        => Directory();

    private static (string Domain, string LogicalNode, string Name) ParseRcbReference(string reference)
    {
        var slash = reference.IndexOf('/');
        var domain = slash > 0 ? reference[..slash] : "LD0";
        var tail = slash >= 0 ? reference[(slash + 1)..] : reference;
        var parts = tail.Split(['.', '$'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (domain, parts.FirstOrDefault() ?? "LLN0", parts.LastOrDefault() ?? "RCB");
    }
}
