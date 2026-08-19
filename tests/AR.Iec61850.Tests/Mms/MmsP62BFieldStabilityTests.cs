using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsP62BFieldStabilityTests
{
    [Fact]
    public void AutomaticMonitoring_DoesNotPromoteEmptyRcbToDynamic_AfterCapabilityEvidence()
    {
        var staticSignal = StaticSignal();
        var residualSignal = ResidualSignal();
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "FIELD_IED",
            Source = "P6.2-B field stability fixture",
            Signals = [staticSignal, residualSignal]
        };

        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(StaticRcb());
        inventory.ReportControls.Add(EmptyUrcb());

        var availability = new MmsRcbAvailabilityResult
        {
            CheckedAtUtc = DateTimeOffset.UtcNow,
            ReportControls = [StaticAvailable(), DynamicAvailable()]
        };

        var liveDirectory = new MmsIedModelDirectory(
        [
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "GGIO1",
                FunctionalConstraint = "ST",
                DataObjectPath = "Residual.stVal",
                MmsItemName = "GGIO1$ST$Residual$stVal",
                Source = "P6.2-B exact dynamic fixture",
                Confidence = 100
            }
        ]);

        var result = MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            [staticSignal, residualSignal],
            inventory,
            availability,
            liveDirectory,
            new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                SupportsWrite = true,
                SupportsDefineNamedVariableList = true,
                SupportsDeleteNamedVariableList = true
            },
            new MmsHybridReportAcquisitionOptions
            {
                AllowStaticBrcb = true,
                AllowStaticUrcb = true,
                AllowDynamicBrcb = true,
                AllowDynamicUrcb = true,
                AllowCallerOwnedReports = false,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            });

        // The association still reports that dynamic services may be attempted. P6.2-B
        // deliberately refuses to turn that evidence into an automatic full DataSet write
        // on the production monitoring association.
        Assert.True(result.AssociationCapability.MayAttemptDynamicReports);
        Assert.Equal(1, result.AcquisitionPlan.StaticBrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.DynamicBrcbSignalCount);
        Assert.Equal(0, result.AcquisitionPlan.DynamicUrcbSignalCount);
        Assert.Equal(1, result.AcquisitionPlan.PollingFallbackSignalCount);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.HybridReportAndPolling, result.AcquisitionPlan.Status);

        Assert.Contains(result.AcquisitionPlan.Segments, segment =>
            segment.Kind == MmsHybridAcquisitionKind.StaticBrcb &&
            segment.ReportControlReference == "LD0/LLN0.BR.Static01");
        Assert.DoesNotContain(result.AcquisitionPlan.Segments, segment =>
            segment.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb);
    }

    private static Iec61850SignalDescriptor StaticSignal()
        => new()
        {
            DesignReference = "LD0/GGIO1.Static.stVal",
            ObservedReference = "LD0/GGIO1.Static.stVal",
            CanonicalMmsReference = "LD0/GGIO1$ST$Static$stVal",
            EffectiveMmsReference = "LD0/GGIO1$ST$Static$stVal",
            PrimaryValueReference = "LD0/GGIO1.Static.stVal",
            PrimaryValueMmsReference = "LD0/GGIO1$ST$Static$stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            DataSetMemberships =
            [
                new Iec61850SignalDataSetMembership
                {
                    DataSetReference = "LD0/LLN0.dsStatic",
                    MemberIndex = 0,
                    OriginalMemberReference = "LD0/GGIO1.Static",
                    CanonicalMemberReference = "LD0/GGIO1.Static",
                    FunctionalConstraint = "ST",
                    IsPrimaryValueForMember = true
                }
            ],
            IsStaticDataSetMandatory = true,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };

    private static Iec61850SignalDescriptor ResidualSignal()
        => new()
        {
            DesignReference = "LD0/GGIO1.Residual.stVal",
            ObservedReference = "LD0/GGIO1.Residual.stVal",
            CanonicalMmsReference = "LD0/GGIO1$ST$Residual$stVal",
            EffectiveMmsReference = "LD0/GGIO1$ST$Residual$stVal",
            PrimaryValueReference = "LD0/GGIO1.Residual.stVal",
            PrimaryValueMmsReference = "LD0/GGIO1$ST$Residual$stVal",
            FunctionalConstraint = "ST",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true,
            ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
            LiveStatus = Iec61850DesignLiveStatus.Exact
        };

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
            ConfRev = "1"
        };

    private static MmsReportControlCandidate EmptyUrcb()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "Dynamic01",
            Reference = "LD0/LLN0.RP.Dynamic01",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "Dynamic01",
            ConfRev = "1"
        };

    private static MmsRcbAvailabilitySnapshot StaticAvailable()
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
            ReportId = "Static01",
            ConfRev = "1",
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            DataSetDirectoryRead = true,
            DataSetDirectorySuccess = true,
            DataSetMemberCount = 1,
            DataSetMembers =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    MmsItemName = "GGIO1$ST$Static",
                    UserReference = "LD0/GGIO1.Static",
                    FunctionalConstraint = "ST",
                    LogicalNode = "GGIO1",
                    DataObjectPath = "Static"
                }
            ],
            Availability = MmsRcbOperationalAvailability.Available,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "P6.2-B exact static fixture"
        };

    private static MmsRcbAvailabilitySnapshot DynamicAvailable()
        => new()
        {
            Reference = "LD0/LLN0.RP.Dynamic01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "Dynamic01",
            Mode = "URCB",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            ReportId = "Dynamic01",
            ConfRev = "1",
            EnabledState = "false",
            ReservationState = "false",
            DataSetDirectoryRead = false,
            DataSetDirectorySuccess = false,
            DataSetMemberCount = 0,
            DataSetMembers = Array.Empty<MmsDataSetDirectoryMember>(),
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Reason = "P6.2-B exact empty dynamic slot fixture",
            Attributes = ["DatSet", "RptEna", "TrgOps", "Resv"]
        };
}
