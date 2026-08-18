using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsHybridReportAcquisitionPlannerP2DynamicCoverageTests
{
    [Fact]
    public void PrimaryValueMmsReference_UsesDynamicReportBeforePolling()
    {
        var signal = new Iec61850SignalDescriptor
        {
            DesignReference = "LD0/XCBR1.Pos",
            PrimaryValueReference = "LD0/XCBR1.Pos.stVal",
            PrimaryValueMmsReference = "LD0/XCBR1$ST$Pos$stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback
        };
        var inventory = Inventory(Rcb("LD0/LLN0.RP.D01", buffered: false));
        var availability = Availability(DynamicEmpty("LD0/LLN0.RP.D01", buffered: false));
        var directory = Directory(Point("LD0", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal"));

        var plan = Build(signal, inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        Assert.Equal(1, plan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.PollingFallbackSignalCount);
        var segment = Assert.Single(plan.Segments);
        Assert.Equal(MmsHybridAcquisitionKind.DynamicUrcb, segment.Kind);
        var dynamicPoint = Assert.Single(segment.ReportPlan?.DynamicPoints ?? Array.Empty<MmsFcResolvedPoint>());
        Assert.Equal("LD0/XCBR1$ST$Pos$stVal", dynamicPoint.MmsReference);
    }

    [Fact]
    public void PrimaryValueUserReference_UsesDynamicReportBeforePolling()
    {
        var signal = new Iec61850SignalDescriptor
        {
            DesignReference = "LD0/XCBR1.Pos",
            PrimaryValueReference = "LD0/XCBR1.Pos.stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback
        };
        var inventory = Inventory(Rcb("LD0/LLN0.RP.D01", buffered: false));
        var availability = Availability(DynamicEmpty("LD0/LLN0.RP.D01", buffered: false));
        var directory = Directory(Point("LD0", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal"));

        var plan = Build(signal, inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, plan.Status);
        Assert.Equal(1, plan.DynamicUrcbSignalCount);
        Assert.Equal(0, plan.PollingFallbackSignalCount);
    }

    [Fact]
    public void QualityDescriptor_DoesNotHijackPrimaryValueDynamicFallback()
    {
        var signal = new Iec61850SignalDescriptor
        {
            DesignReference = "LD0/XCBR1.Pos.q",
            PrimaryValueReference = "LD0/XCBR1.Pos.stVal",
            PrimaryValueMmsReference = "LD0/XCBR1$ST$Pos$stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.Quality,
            IsOperationalCandidate = false,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DataSetResolvedAttribute
        };
        var inventory = Inventory(Rcb("LD0/LLN0.RP.D01", buffered: false));
        var availability = Availability(DynamicEmpty("LD0/LLN0.RP.D01", buffered: false));
        var directory = Directory(Point("LD0", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal"));

        var plan = Build(signal, inventory, availability, directory);

        Assert.Equal(MmsHybridAcquisitionPlanStatus.PollingOnly, plan.Status);
        Assert.Equal(0, plan.ReportCoveredSignalCount);
        Assert.Equal(1, plan.PollingFallbackSignalCount);
        var assignment = Assert.Single(plan.Assignments);
        Assert.Equal(MmsHybridAcquisitionKind.MmsPollingFallback, assignment.Kind);
    }

    private static MmsHybridReportAcquisitionPlan Build(
        Iec61850SignalDescriptor signal,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory directory)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P2 dynamic coverage regression",
            Signals = [signal]
        };

        return MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            [signal],
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

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsReportControlCandidate Rcb(string reference, bool buffered)
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
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "P2 synthetic fixture: empty DatSet confirmed.",
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
            DataSetProbeMessage = "P2 synthetic fixture: empty DatSet confirmed.",
            ReportId = reference,
            ConfRev = "1",
            EnabledState = "false",
            ReservationState = buffered ? string.Empty : "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "P2 synthetic exact verified-empty dynamic RCB slot."
        };
    }

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
            Source = "P2 synthetic live directory",
            Confidence = 100
        };

    private static MmsIedModelDirectory Directory(params MmsFcResolvedPoint[] points)
        => new(points);

    private static (string Domain, string LogicalNode, string Name) ParseRcbReference(string reference)
    {
        var slash = reference.IndexOf('/');
        var domain = slash > 0 ? reference[..slash] : "LD0";
        var tail = slash >= 0 ? reference[(slash + 1)..] : reference;
        var parts = tail.Split(['.', '$'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (domain, parts.FirstOrDefault() ?? "LLN0", parts.LastOrDefault() ?? "RCB");
    }
}
