using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRcbPoolSelectorDataSetEvidenceTests
{
    [Theory]
    [InlineData(MmsRcbDataSetProbeState.NotAttempted)]
    [InlineData(MmsRcbDataSetProbeState.ReadFailed)]
    public void DynamicSelection_DoesNotTreat_UnverifiedBlankDatSet_AsEmptySlot(MmsRcbDataSetProbeState probeState)
    {
        var inventory = new MmsReportInventory();
        var rcb = Candidate(dataSetReference: string.Empty, probeState);
        inventory.ReportControls.Add(rcb);

        var result = MmsRcbPoolSelector.BuildDynamicSelection(inventory, preferredLogicalDevice: rcb.Domain);

        Assert.True(string.IsNullOrWhiteSpace(result.SelectedRcbReference));
        var evaluation = Assert.Single(result.Candidates);
        Assert.Equal(MmsRcbAvailabilityKind.UnknownNeedsProbe, evaluation.Availability);
        Assert.Contains("DatSet", evaluation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DynamicSelection_Selects_OnlyLiveVerifiedEmptyDatSetSlot()
    {
        var inventory = new MmsReportInventory();
        var rcb = Candidate(string.Empty, MmsRcbDataSetProbeState.ReadSucceeded);
        inventory.ReportControls.Add(rcb);

        var result = MmsRcbPoolSelector.BuildDynamicSelection(inventory, preferredLogicalDevice: rcb.Domain);

        Assert.Equal(rcb.Reference, result.SelectedRcbReference);
        var evaluation = Assert.Single(result.Candidates);
        Assert.Equal(MmsRcbAvailabilityKind.AvailableDynamicEmpty, evaluation.Availability);
        Assert.Equal(MmsRcbSelectionDecision.Selected, evaluation.Decision);
    }

    [Fact]
    public void StaticSelection_DoesNotClaim_WhenCurrentDatSetReadFailed()
    {
        var inventory = new MmsReportInventory();
        var rcb = Candidate("IED1LD0/LLN0.Events", MmsRcbDataSetProbeState.ReadFailed);
        inventory.ReportControls.Add(rcb);

        var dataSetMap = new Dictionary<string, MmsDataSetDirectoryResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["IED1LD0/LLN0.Events"] = new MmsDataSetDirectoryResult
            {
                IsSuccess = true,
                DataSetReference = "IED1LD0/LLN0.Events",
                Members = new[]
                {
                    new MmsDataSetDirectoryMember { UserReference = "IED1LD0/XCBR1.Pos.stVal" }
                }
            }
        };

        var result = MmsRcbPoolSelector.BuildStaticSelection(inventory, dataSetMap);

        Assert.True(string.IsNullOrWhiteSpace(result.SelectedRcbReference));
        var evaluation = Assert.Single(result.Candidates);
        Assert.Equal(MmsRcbAvailabilityKind.UnknownNeedsProbe, evaluation.Availability);
        Assert.Contains("failed", evaluation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsReportControlCandidate Candidate(
        string dataSetReference,
        MmsRcbDataSetProbeState probeState)
        => new()
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "URCB01",
            Reference = "IED1LD0/LLN0.RP.URCB01",
            Buffered = false,
            DataSetReference = dataSetReference,
            DataSetProbeState = probeState,
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "IED1LD0/LLN0$RP$URCB01",
            Status = "Attribute-probed"
        };
}
