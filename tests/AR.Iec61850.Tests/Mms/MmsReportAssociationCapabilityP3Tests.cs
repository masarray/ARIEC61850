using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportAssociationCapabilityP3Tests
{
    [Fact]
    public void DeterministicInitiateResponse_DecodesDynamicDataSetServices()
    {
        var response = AcseMmsAssociateResponse.BuildResponseProfiles()[0].Payload;

        var capability = AcseMmsNegotiatedCapabilitiesParser.Parse(response);

        Assert.True(capability.IsDecoded);
        Assert.Equal(65000, capability.MaxMmsPduSize);
        Assert.Equal(10, capability.MaxOutstandingCalling);
        Assert.Equal(10, capability.MaxOutstandingCalled);
        Assert.Equal(5, capability.DataStructureNestingLevel);
        Assert.True(capability.SupportsWrite);
        Assert.True(capability.SupportsDefineNamedVariableList);
        Assert.True(capability.SupportsDeleteNamedVariableList);
    }

    [Fact]
    public void ExactFreeUrcb_WithRequiredFieldEvidence_BecomesDynamicCandidateAndKeepsTrgOpsEvidence()
    {
        var signal = Signal();
        var inventory = Inventory(Rcb());
        var availability = Availability(DynamicEmpty(includeTriggerOptions: true));
        var negotiated = SupportedDynamicServices();

        var result = Build(signal, inventory, availability, negotiated);

        Assert.True(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.Equal(1, result.AssociationCapability.DynamicUrcbSlotCount);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.FullReportCoverage, result.AcquisitionPlan.Status);
        Assert.Equal(1, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.PollingFallbackSignalCount);

        var segment = Assert.Single(result.AcquisitionPlan.Segments);
        Assert.Equal(MmsHybridAcquisitionKind.DynamicUrcb, segment.Kind);
        Assert.NotNull(segment.ReportPlan?.ReportControl);
        Assert.Contains("TrgOps", segment.ReportPlan!.ReportControl!.Attributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DatSet", segment.ReportPlan.ReportControl.Attributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("RptEna", segment.ReportPlan.ReportControl.Attributes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitlyUnsupportedDefineNamedVariableList_WithholdsDynamicAndFallsBackToPolling()
    {
        var signal = Signal();
        var inventory = Inventory(Rcb());
        var availability = Availability(DynamicEmpty(includeTriggerOptions: true));
        var negotiated = new AcseMmsNegotiatedCapabilities
        {
            IsDecoded = true,
            SupportsWrite = true,
            SupportsDefineNamedVariableList = false,
            SupportsDeleteNamedVariableList = true
        };

        var result = Build(signal, inventory, availability, negotiated);

        Assert.False(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.Equal(MmsCapabilityEvidenceState.Unsupported, result.AssociationCapability.DefineNamedVariableListService);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.PollingOnly, result.AcquisitionPlan.Status);
        Assert.Equal(0, result.AcquisitionPlan.ReportCoveredSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("DefineNamedVariableList", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifiedEmptyRcb_WithoutTrgOpsEvidence_IsNotPromotedToDynamicCandidate()
    {
        var signal = Signal();
        var inventory = Inventory(Rcb());
        var availability = Availability(DynamicEmpty(includeTriggerOptions: false));

        var result = Build(signal, inventory, availability, SupportedDynamicServices());

        var control = Assert.Single(result.AssociationCapability.ReportControls);
        Assert.Equal(MmsCapabilityEvidenceState.Unknown, control.TriggerOptionsAccess);
        Assert.False(control.IsDynamicWriteAttemptCandidate);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.PollingOnly, result.AcquisitionPlan.Status);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
    }

    [Fact]
    public void MissingOptionalFieldsAndGi_DoNotBlockOtherwiseSafeDynamicCandidate()
    {
        var availability = Availability(DynamicEmpty(includeTriggerOptions: true, includeOptionalFields: false, includeGi: false));

        var capability = MmsReportAssociationCapabilityEvaluator.Evaluate(
            availability,
            SupportedDynamicServices(),
            Options());

        var control = Assert.Single(capability.ReportControls);
        Assert.True(control.IsDynamicWriteAttemptCandidate);
        Assert.Equal(MmsCapabilityEvidenceState.Unknown, control.OptionalFieldsAccess);
        Assert.Equal(MmsCapabilityEvidenceState.Unknown, control.GeneralInterrogationAccess);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalDescriptor signal,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        AcseMmsNegotiatedCapabilities negotiated)
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P3 association capability regression",
            Signals = [signal]
        };

        return MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            [signal],
            inventory,
            availability,
            Directory(Point()),
            negotiated,
            Options());
    }

    private static MmsHybridReportAcquisitionOptions Options()
        => new()
        {
            AllowStaticBrcb = true,
            AllowStaticUrcb = true,
            AllowDynamicBrcb = true,
            AllowDynamicUrcb = true,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = true,
            MaxDynamicMembersPerReport = 64
        };

    private static AcseMmsNegotiatedCapabilities SupportedDynamicServices()
        => new()
        {
            IsDecoded = true,
            MaxMmsPduSize = 65000,
            SupportsWrite = true,
            SupportsDefineNamedVariableList = true,
            SupportsDeleteNamedVariableList = true
        };

    private static Iec61850SignalDescriptor Signal()
        => new()
        {
            DesignReference = "LD0/XCBR1.Pos.stVal",
            PrimaryValueReference = "LD0/XCBR1.Pos.stVal",
            PrimaryValueMmsReference = "LD0/XCBR1$ST$Pos$stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback
        };

    private static MmsReportInventory Inventory(params MmsReportControlCandidate[] candidates)
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.AddRange(candidates);
        return inventory;
    }

    private static MmsReportControlCandidate Rcb()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "D01",
            Reference = "LD0/LLN0.RP.D01",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "D01",
            ConfRev = "1",
            Attributes = ["DatSet", "RptEna", "Resv", "TrgOps"]
        };

    private static MmsRcbAvailabilityResult Availability(params MmsRcbAvailabilitySnapshot[] snapshots)
        => new() { CheckedAtUtc = DateTimeOffset.UtcNow, ReportControls = snapshots };

    private static MmsRcbAvailabilitySnapshot DynamicEmpty(
        bool includeTriggerOptions,
        bool includeOptionalFields = true,
        bool includeGi = true)
    {
        var attributes = new List<string> { "DatSet", "RptEna", "Resv" };
        if (includeTriggerOptions)
            attributes.Add("TrgOps");
        if (includeOptionalFields)
            attributes.Add("OptFlds");
        if (includeGi)
            attributes.Add("GI");

        return new MmsRcbAvailabilitySnapshot
        {
            Reference = "LD0/LLN0.RP.D01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "D01",
            Mode = "URCB",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "P3 synthetic fixture: empty DatSet confirmed.",
            ReportId = "D01",
            ConfRev = "1",
            TriggerOptions = includeTriggerOptions ? "dchg" : string.Empty,
            OptionalFields = includeOptionalFields ? "seqNum,timeStamp,reasonForInclusion,dataSet" : string.Empty,
            EnabledState = "false",
            ReservationState = "false",
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Attributes = attributes,
            Reason = "P3 synthetic exact verified-empty/free URCB."
        };
    }

    private static MmsFcResolvedPoint Point()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "XCBR1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Pos.stVal",
            MmsItemName = "XCBR1$ST$Pos$stVal",
            Source = "P3 synthetic live directory",
            Confidence = 100
        };

    private static MmsIedModelDirectory Directory(params MmsFcResolvedPoint[] points)
        => new(points);
}
