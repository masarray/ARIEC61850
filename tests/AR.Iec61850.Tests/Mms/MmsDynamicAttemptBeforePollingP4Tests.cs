using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicAttemptBeforePollingP4Tests
{
    [Fact]
    public void CapabilityQualifiedDynamicPlan_RequiresRuntimeAttemptBeforeFinalPolling()
    {
        var result = Build(SupportedDynamicServices(), DynamicEmpty());

        var evidence = Assert.Single(MmsHybridDynamicAttemptEvidenceBuilder.Build(result, Options()));
        Assert.Equal(MmsHybridAcquisitionKind.DynamicUrcb, evidence.PlannedKind);
        Assert.Equal(MmsHybridDynamicAttemptDisposition.Planned, evidence.DynamicAttemptDisposition);
        Assert.True(evidence.DynamicAttemptRequired);
        Assert.Equal(MmsHybridPollingFallbackReason.None, evidence.PollingFallbackReason);
    }

    [Fact]
    public void ExplicitlyUnsupportedDefineService_ExplainsWhyPollingMayBeFinalWithoutWriteAttempt()
    {
        var negotiated = SupportedDynamicServices() with { SupportsDefineNamedVariableList = false };
        var result = Build(negotiated, DynamicEmpty());

        var evidence = Assert.Single(MmsHybridDynamicAttemptEvidenceBuilder.Build(result, Options()));
        Assert.Equal(MmsHybridAcquisitionKind.MmsPollingFallback, evidence.PlannedKind);
        Assert.Equal(MmsHybridDynamicAttemptDisposition.Skipped, evidence.DynamicAttemptDisposition);
        Assert.Equal(MmsHybridPollingFallbackReason.DefineNamedVariableListUnsupported, evidence.PollingFallbackReason);
        Assert.True(evidence.IsExplainablePollingFallback);
    }

    [Fact]
    public void MissingCapabilityQualifiedSlot_ProducesExplicitSkipReasonInsteadOfSilentPolling()
    {
        var result = Build(SupportedDynamicServices(), DynamicEmpty(includeTriggerOptions: false));

        var evidence = Assert.Single(MmsHybridDynamicAttemptEvidenceBuilder.Build(result, Options()));
        Assert.Equal(MmsHybridAcquisitionKind.MmsPollingFallback, evidence.PlannedKind);
        Assert.Equal(MmsHybridDynamicAttemptDisposition.Skipped, evidence.DynamicAttemptDisposition);
        Assert.Equal(MmsHybridPollingFallbackReason.NoCapabilityQualifiedDynamicRcb, evidence.PollingFallbackReason);
        Assert.True(evidence.IsExplainablePollingFallback);
    }

    [Fact]
    public void RuntimeAttemptContract_ExposesAttemptFailureAndRollbackEvidence()
    {
        var result = new MmsPersistentReportMonitorAttemptResult
        {
            DynamicAttemptState = MmsDynamicReportAttemptState.AttemptedFailed,
            FailureReason = MmsReportActivationFailureReason.DynamicDataSetBindFailed,
            CleanupAttempted = true,
            CleanupSucceeded = true
        };

        Assert.True(result.DynamicAttempted);
        Assert.False(result.IsSuccess);
        Assert.Equal(MmsReportActivationFailureReason.DynamicDataSetBindFailed, result.FailureReason);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        AcseMmsNegotiatedCapabilities negotiated,
        MmsRcbAvailabilitySnapshot snapshot)
    {
        var signal = Signal();
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(Rcb());
        var availability = new MmsRcbAvailabilityResult
        {
            CheckedAtUtc = DateTimeOffset.UtcNow,
            ReportControls = [snapshot]
        };
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P4 regression",
            Signals = [signal]
        };

        return MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            [signal],
            inventory,
            availability,
            new MmsIedModelDirectory([Point()]),
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

    private static MmsRcbAvailabilitySnapshot DynamicEmpty(bool includeTriggerOptions = true)
        => new()
        {
            Reference = "LD0/LLN0.RP.D01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "D01",
            Mode = "URCB",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            ReportId = "D01",
            ConfRev = "1",
            TriggerOptions = includeTriggerOptions ? "dchg" : string.Empty,
            EnabledState = "false",
            ReservationState = "false",
            Availability = MmsRcbOperationalAvailability.NoDataSet,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            Attributes = includeTriggerOptions
                ? ["DatSet", "RptEna", "Resv", "TrgOps"]
                : ["DatSet", "RptEna", "Resv"],
            Reason = "P4 synthetic exact verified-empty/free URCB."
        };

    private static MmsFcResolvedPoint Point()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "XCBR1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Pos.stVal",
            MmsItemName = "XCBR1$ST$Pos$stVal",
            Source = "P4 synthetic live directory",
            Confidence = 100
        };
}
