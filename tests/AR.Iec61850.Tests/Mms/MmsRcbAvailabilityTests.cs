using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRcbAvailabilityTests
{
    [Fact]
    public void Evaluate_Requires_Explicit_Free_Runtime_State_For_Green_Available()
    {
        var rcb = Candidate(buffered: false);
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, PopulatedDirectory(), callerOwned: false);

        Assert.Equal(MmsRcbOperationalAvailability.Available, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Exact, result.Confidence);
        Assert.True(result.IsSelectable);
        Assert.Equal(2, result.DataSetMemberCount);
        Assert.Equal(MmsRcbDataSetProbeState.ReadSucceeded, result.DataSetProbeState);
    }

    [Theory]
    [InlineData("true", "false", "0", "")]
    [InlineData("false", "true", "0", "")]
    [InlineData("false", "false", "30", "")]
    [InlineData("false", "false", "0", "01020304")]
    public void Evaluate_Marks_Runtime_Ownership_Evidence_InUse(
        string rptEna,
        string resv,
        string resvTms,
        string owner)
    {
        var rcb = Candidate(buffered: resvTms == "30");
        rcb.EnabledState = rptEna;
        rcb.ReservationState = resv;
        rcb.ReservationTimeSeconds = resvTms;
        rcb.Owner = owner;

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, PopulatedDirectory(), callerOwned: false);

        Assert.Equal(MmsRcbOperationalAvailability.InUse, result.Availability);
        Assert.False(result.IsSelectable);
    }

    [Fact]
    public void Evaluate_Does_Not_Claim_Edition1_Brcb_Available_When_Reservation_Is_Not_Exposed()
    {
        var rcb = Candidate(buffered: true);
        rcb.EnabledState = "false";
        rcb.ReservationTimeSeconds = string.Empty;

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, PopulatedDirectory(), callerOwned: false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Reduced, result.Confidence);
        Assert.Contains("does not expose enough reservation evidence", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Caller_Ownership_Is_Distinguished_From_Other_Client()
    {
        var rcb = Candidate(buffered: true);
        rcb.EnabledState = "true";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, PopulatedDirectory(), callerOwned: true);

        Assert.Equal(MmsRcbOperationalAvailability.UsedByCaller, result.Availability);
        Assert.True(result.IsSelectable);
    }

    [Fact]
    public void Evaluate_Only_Claims_NoDataSet_When_Live_DatSet_Read_Succeeded_Empty()
    {
        var rcb = Candidate(buffered: false);
        rcb.DataSetReference = string.Empty;
        rcb.DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded;
        rcb.DataSetProbeMessage = "DatSet item=LLN0$RP$URCB01$DatSet: OK \"\"";
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, null, false);

        Assert.Equal(MmsRcbOperationalAvailability.NoDataSet, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Exact, result.Confidence);
        Assert.Contains("read successfully and is empty", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DatSet_Read_Failure_Is_Unknown_Not_NoDataSet()
    {
        var rcb = Candidate(buffered: false);
        rcb.DataSetReference = string.Empty;
        rcb.DataSetProbeState = MmsRcbDataSetProbeState.ReadFailed;
        rcb.DataSetProbeMessage = "DatSet item=LLN0$RP$URCB01$DatSet: object-access-denied";
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, null, false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Reduced, result.Confidence);
        Assert.NotEqual(MmsRcbOperationalAvailability.NoDataSet, result.Availability);
        Assert.Contains("absence is not proven", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Unprobed_Blank_DataSet_Is_Unknown_Not_NoDataSet()
    {
        var rcb = Candidate(buffered: false);
        rcb.DataSetReference = string.Empty;
        rcb.DataSetProbeState = MmsRcbDataSetProbeState.NotAttempted;
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, null, false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Unknown, result.Confidence);
        Assert.NotEqual(MmsRcbOperationalAvailability.NoDataSet, result.Availability);
    }

    [Fact]
    public void Evaluate_RptEnaFalse_Does_Not_Upgrade_Failed_DatSet_Read_To_Exact_NoDataSet()
    {
        var rcb = Candidate(buffered: false);
        rcb.DataSetReference = string.Empty;
        rcb.DataSetProbeState = MmsRcbDataSetProbeState.ReadFailed;
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, null, false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Reduced, result.Confidence);
    }

    [Fact]
    public void Evaluate_Known_DataSet_Without_Directory_Verification_Is_Not_Green_Available()
    {
        var rcb = Candidate(buffered: false);
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, null, false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Reduced, result.Confidence);
        Assert.False(result.IsSelectable);
    }

    [Fact]
    public void Evaluate_Blocks_Empty_And_Unreadable_DataSets()
    {
        var empty = Candidate(buffered: false);
        empty.EnabledState = "false";
        empty.ReservationState = "false";
        Assert.Equal(
            MmsRcbOperationalAvailability.DataSetEmpty,
            MmsRcbAvailabilityEvaluator.Evaluate(empty, new MmsDataSetDirectoryResult
            {
                IsSuccess = true,
                DataSetReference = empty.DataSetReference,
                Members = Array.Empty<MmsDataSetDirectoryMember>()
            }, false).Availability);

        Assert.Equal(
            MmsRcbOperationalAvailability.DataSetUnreadable,
            MmsRcbAvailabilityEvaluator.Evaluate(empty, new MmsDataSetDirectoryResult
            {
                IsSuccess = false,
                DataSetReference = empty.DataSetReference,
                Message = "access denied"
            }, false).Availability);
    }

    [Fact]
    public void Evaluate_Preserved_Reference_With_Current_DatSet_Failure_Remains_Unknown()
    {
        var rcb = Candidate(buffered: false);
        rcb.DataSetProbeState = MmsRcbDataSetProbeState.ReadFailed;
        rcb.EnabledState = "false";
        rcb.ReservationState = "false";

        var result = MmsRcbAvailabilityEvaluator.Evaluate(rcb, PopulatedDirectory(), false);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, result.Availability);
        Assert.Equal(MmsRcbAvailabilityConfidence.Reduced, result.Confidence);
        Assert.Equal(2, result.DataSetMemberCount);
        Assert.Contains("cannot be reconfirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsReportControlCandidate Candidate(bool buffered)
        => new()
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = buffered ? "BR" : "RP",
            Name = buffered ? "BRCB01" : "URCB01",
            Reference = buffered ? "IED1LD0/LLN0.BR.BRCB01" : "IED1LD0/LLN0.RP.URCB01",
            Buffered = buffered,
            DataSetReference = "IED1LD0/LLN0.Events",
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "DatSet live read succeeded.",
            Status = "Attribute-probed"
        };

    private static MmsDataSetDirectoryResult PopulatedDirectory()
        => new()
        {
            IsSuccess = true,
            DataSetReference = "IED1LD0/LLN0.Events",
            Members = new[]
            {
                new MmsDataSetDirectoryMember { UserReference = "IED1LD0/XCBR1.Pos.stVal" },
                new MmsDataSetDirectoryMember { UserReference = "IED1LD0/XCBR1.Pos.q" }
            }
        };
}
