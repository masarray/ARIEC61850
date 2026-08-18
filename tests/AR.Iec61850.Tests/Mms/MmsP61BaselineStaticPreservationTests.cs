using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsP61BaselineStaticPreservationTests
{
    [Fact]
    public void ConfiguredStaticRcb_RemainsVisibleToStablePlanner_WhenAssociationBitmapWouldRejectNewWrites()
    {
        var signal = Signal(1, staticMember: true);
        var member = Member(1);
        var inventory = Inventory(StaticRcb());
        var availability = Availability(StaticAvailable(member));
        var catalog = Catalog([signal]);

        var result = MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            [signal],
            inventory,
            availability,
            EmptyDirectory(),
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = false,
                SupportsDefineNamedVariableList = false,
                SupportsDeleteNamedVariableList = false
            },
            Options());

        Assert.False(result.AssociationCapability.MayAttemptStaticWrites);
        Assert.False(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.Equal(1, result.AcquisitionPlan.StaticBrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.DynamicBrcbSignalCount + result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.PollingFallbackSignalCount);

        var segment = Assert.Single(result.AcquisitionPlan.Segments);
        Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, segment.Kind);
        Assert.Equal("LD0/LLN0.BR.Static01", segment.ReportControlReference);
        Assert.Contains("P6.1 baseline-static", segment.ReportPlan!.ReportControl!.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldShape_115Requested_WithSixStaticMembers_PreservesSixStaticAndOnlyPollsResidual()
    {
        var signals = Enumerable.Range(1, 115)
            .Select(index => Signal(index, staticMember: index <= 6))
            .ToArray();
        var staticMembers = Enumerable.Range(1, 6).Select(Member).ToArray();
        var inventory = Inventory(StaticRcb());
        var availability = Availability(StaticAvailable(staticMembers));

        var result = MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            Catalog(signals),
            signals,
            inventory,
            availability,
            EmptyDirectory(),
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = true,
                SupportsDefineNamedVariableList = true,
                SupportsDeleteNamedVariableList = true
            },
            Options());

        Assert.Equal(115, result.AcquisitionPlan.RequestedSignalCount);
        Assert.Equal(6, result.AcquisitionPlan.StaticBrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.StaticUrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.DynamicBrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(109, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, result.AcquisitionPlan.Status);

        var staticSegment = Assert.Single(
            result.AcquisitionPlan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.StaticBrcb);
        Assert.Equal(6, staticSegment.SignalCount);

        var pollingSegment = Assert.Single(
            result.AcquisitionPlan.Segments,
            segment => segment.Kind == MmsHybridAcquisitionKind.MmsPollingFallback);
        Assert.Equal(109, pollingSegment.SignalCount);

        foreach (var signal in signals.Take(6))
        {
            var assignment = Assert.Single(
                result.AcquisitionPlan.Assignments,
                item => item.SignalReference == signal.EffectiveMmsReference);
            Assert.Equal(MmsHybridAcquisitionKind.StaticBrcb, assignment.Kind);
        }
    }

    private static Iec61850SignalCatalogDocument Catalog(IReadOnlyList<Iec61850SignalDescriptor> signals)
        => new()
        {
            IedName = "IED",
            Source = "P6.1 baseline-preservation regression",
            Signals = signals
        };

    private static MmsHybridReportAcquisitionOptions Options()
        => new()
        {
            AllowStaticBrcb = true,
            AllowStaticUrcb = true,
            AllowDynamicBrcb = true,
            AllowDynamicUrcb = true,
            AllowCallerOwnedReports = false,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = true
        };

    private static Iec61850SignalDescriptor Signal(int index, bool staticMember)
    {
        var dataObject = $"Ind{index:000}";
        var userReference = $"LD0/GGIO1.{dataObject}.stVal";
        var parentReference = $"LD0/GGIO1.{dataObject}";
        var mmsReference = $"LD0/GGIO1$ST${dataObject}$stVal";
        var memberships = staticMember
            ? new[]
            {
                new Iec61850SignalDataSetMembership
                {
                    DataSetReference = "LD0/LLN0.dsStatic",
                    MemberIndex = index - 1,
                    OriginalMemberReference = parentReference,
                    CanonicalMemberReference = parentReference,
                    FunctionalConstraint = "ST",
                    IsPrimaryValueForMember = true
                }
            }
            : Array.Empty<Iec61850SignalDataSetMembership>();

        return new Iec61850SignalDescriptor
        {
            DesignReference = userReference,
            ObservedReference = userReference,
            CanonicalMmsReference = mmsReference,
            EffectiveMmsReference = mmsReference,
            PrimaryValueReference = userReference,
            PrimaryValueMmsReference = mmsReference,
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            DataSetMemberships = memberships,
            IsStaticDataSetMandatory = staticMember,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };
    }

    private static MmsDataSetDirectoryMember Member(int index)
    {
        var dataObject = $"Ind{index:000}";
        return new MmsDataSetDirectoryMember
        {
            Domain = "LD0",
            MmsItemName = $"GGIO1$ST${dataObject}",
            UserReference = $"LD0/GGIO1.{dataObject}",
            FunctionalConstraint = "ST",
            LogicalNode = "GGIO1",
            DataObjectPath = dataObject
        };
    }

    private static MmsReportControlCandidate StaticRcb()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "Static01",
            Reference = "LD0/LLN0.BR.Static01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.dsStatic",
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            ReportId = "Static01",
            ConfRev = "1",
            Status = "Attribute-probed"
        };

    private static MmsRcbAvailabilitySnapshot StaticAvailable(params MmsDataSetDirectoryMember[] members)
        => new()
        {
            Reference = "LD0/LLN0.BR.Static01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "Static01",
            Mode = "BRCB",
            Buffered = true,
            DataSetReference = "LD0/LLN0.dsStatic",
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "P6.1 synthetic fixture: populated static DataSet confirmed.",
            ReportId = "Static01",
            ConfRev = "1",
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            DataSetDirectoryRead = true,
            DataSetDirectorySuccess = true,
            DataSetMemberCount = members.Length,
            DataSetMembers = members,
            Availability = MmsRcbOperationalAvailability.Available,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "P6.1 exact available configured static BRCB."
        };

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsRcbAvailabilityResult Availability(params MmsRcbAvailabilitySnapshot[] snapshots)
        => new() { CheckedAtUtc = DateTimeOffset.UtcNow, ReportControls = snapshots };

    private static MmsIedModelDirectory EmptyDirectory()
        => new(Array.Empty<MmsFcResolvedPoint>());
}
