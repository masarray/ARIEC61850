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
    public void Evaluate_Blocks_Missing_Empty_And_Unreadable_DataSets()
    {
        var noDataSet = Candidate(buffered: false);
        noDataSet.DataSetReference = string.Empty;
        noDataSet.EnabledState = "false";
        noDataSet.ReservationState = "false";
        Assert.Equal(
            MmsRcbOperationalAvailability.NoDataSet,
            MmsRcbAvailabilityEvaluator.Evaluate(noDataSet, null, false).Availability);

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
