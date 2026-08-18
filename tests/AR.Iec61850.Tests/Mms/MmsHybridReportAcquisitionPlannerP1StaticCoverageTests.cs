using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsHybridReportAcquisitionPlannerP1StaticCoverageTests
{
    [Fact]
    public void StaticCoveredSignal_IsNeverReassignedToDynamicOrPolling()
    {
        var staticSignal = Signal(
            "LD0/GGIO1.Ind1.stVal",
            "LD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "LD0/LLN0.dsStatic",
            "LD0/GGIO1.Ind1");
        var dynamicSignal = Signal(
            "LD0/GGIO1.Ind2.stVal",
            "LD0/GGIO1$ST$Ind2$stVal",
            "ST");

        var inventory = Inventory(
            Rcb("LD0/LLN0.BR.Static01", buffered: true, "LD0/LLN0.dsStatic"),
            Rcb("LD0/LLN0.RP.Dynamic01", buffered: false, string.Empty));
        var availability = Availability(
            StaticAvailable(
                "LD0/LLN0.BR.Static01",
                buffered: true,
                "LD0/LLN0.dsStatic",
                Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST")),
            DynamicEmpty("LD0/LLN0.RP.Dynamic01", buffered: false));
        var directory = Directory(
            Point("LD0", "GGIO1", "ST", "Ind1.stVal", "GGIO1$ST$Ind1$stVal"),
            Point("LD0", "GGIO1", "ST", "Ind2.stVal", "GGIO1$ST$Ind2$stVal"));

        var plan = Build([staticSignal, dynamicSignal], inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        Assert.Equal(2, plan.Assignments.Count);

        var staticAssignment = Assert.Single(
            plan.Assignments,
            assignment => assignment.SignalReference == staticSignal.EffectiveMmsReference);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, staticAssignment.Kind);
        Assert.Equal("LD0/LLN0.BR.Static01", staticAssignment.ReportControlReference);

        var dynamicAssignment = Assert.Single(
            plan.Assignments,
            assignment => assignment.SignalReference == dynamicSignal.EffectiveMmsReference);
        Assert.Equal(MmsHybridAcquisitionKind.DynamicUrcb, dynamicAssignment.Kind);

        var dynamicSegment = Assert.Single(
            plan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
        Assert.DoesNotContain(dynamicSegment.Signals, signal => ReferenceEquals(signal, staticSignal));
        Assert.DoesNotContain(
            plan.Segments.Where(segment => segment.Kind == MmsHybridAcquisitionKind.MmsPollingFallback)
                .SelectMany(segment => segment.Signals),
            signal => ReferenceEquals(signal, staticSignal));
    }

    [Fact]
    public void OverlappingStaticRcbs_GiveSignalOneDeterministicOwner()
    {
        var signal = Signal(
            "LD0/GGIO1.Ind1.stVal",
            "LD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "LD0/LLN0.dsShared",
            "LD0/GGIO1.Ind1");

        var inventory = Inventory(
            Rcb("LD0/LLN0.RP.U01", buffered: false, "LD0/LLN0.dsShared"),
            Rcb("LD0/LLN0.BR.B01", buffered: true, "LD0/LLN0.dsShared"));
        var member = Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST");
        var availability = Availability(
            StaticAvailable("LD0/LLN0.RP.U01", buffered: false, "LD0/LLN0.dsShared", member),
            StaticAvailable("LD0/LLN0.BR.B01", buffered: true, "LD0/LLN0.dsShared", member));

        var plan = Build([signal], inventory, availability, EmptyDirectory());

        var assignment = Assert.Single(plan.Assignments);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, assignment.Kind);
        Assert.Equal("LD0/LLN0.BR.B01", assignment.ReportControlReference);

        var reportSegments = plan.Segments.Where(segment => segment.IsReportBacked).ToArray();
        var segment = Assert.Single(reportSegments);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, segment.Kind);
        Assert.Single(segment.Signals);
        Assert.Equal(1, plan.ReportCoveredSignalCount);
    }

    [Fact]
    public void StaticCoveredSignal_IsExcludedFromPollingResidual()
    {
        var staticSignal = Signal(
            "LD0/GGIO1.Ind1.stVal",
            "LD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "LD0/LLN0.dsStatic",
            "LD0/GGIO1.Ind1");
        var residual = Signal(
            "LD0/GGIO1.Ind99.stVal",
            "LD0/GGIO1$ST$Ind99$stVal",
            "ST");

        var inventory = Inventory(
            Rcb("LD0/LLN0.BR.Static01", buffered: true, "LD0/LLN0.dsStatic"));
        var availability = Availability(
            StaticAvailable(
                "LD0/LLN0.BR.Static01",
                buffered: true,
                "LD0/LLN0.dsStatic",
                Member("LD0", "GGIO1$ST$Ind1", "LD0/GGIO1.Ind1", "ST")));

        var plan = Build([staticSignal, residual], inventory, availability, EmptyDirectory());

        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, plan.Status);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb,
            Assert.Single(plan.Assignments, assignment => assignment.SignalReference == staticSignal.EffectiveMmsReference).Kind);
        Assert.Equal(MmsHybridAcquisitionKind.MmsPollingFallback,
            Assert.Single(plan.Assignments, assignment => assignment.SignalReference == residual.EffectiveMmsReference).Kind);

        var polling = Assert.Single(
            plan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.MmsPollingFallback);
        Assert.Single(polling.Signals);
        Assert.Same(residual, polling.Signals[0]);
    }

    private static MmsHybridReportAcquisitionPlan Build(
        IReadOnlyList<Iec61850SignalDescriptor> signals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory directory)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P1 static coverage invariant regression",
            Signals = signals
        };

        return MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            signals,
            inventory,
            availability,
            directory,
            new MmsHybridReportAcquisitionOptions
            {
                AllowStaticBrcb = true,
                AllowStaticUrcb = true,
                AllowDynamicBrcb = true,
                AllowDynamicUrcb = true,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            });
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

    private static MmsReportControlCandidate Rcb(
        string reference,
        bool buffered,
        string dataSetReference)
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
            DataSetProbeMessage = "P1 synthetic fixture: live DatSet read succeeded.",
            EnabledState = "false",
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
            DataSetProbeMessage = "P1 synthetic fixture: populated DatSet confirmed.",
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
            Reason = "P1 synthetic exact available static RCB."
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
            DataSetProbeMessage = "P1 synthetic fixture: empty DatSet confirmed.",
            ReportId = reference,
            ConfRev = "1",
            EnabledState = "false",
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "P1 synthetic exact verified-empty dynamic RCB slot."
        };
    }

    private static MmsDataSetDirectoryMember Member(
        string domain,
        string item,
        string userReference,
        string fc)
        => new()
        {
            Domain = domain,
            MmsItemName = item,
            UserReference = userReference,
            FunctionalConstraint = fc,
            LogicalNode = item.Split('$', StringSplitOptions.RemoveEmptyEntries)[0]
        };

    private static MmsFcResolvedPoint Point(
        string domain,
        string logicalNode,
        string fc,
        string dataObjectPath,
        string item)
        => new()
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = fc,
            DataObjectPath = dataObjectPath,
            MmsItemName = item,
            Source = "P1 synthetic live directory",
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
