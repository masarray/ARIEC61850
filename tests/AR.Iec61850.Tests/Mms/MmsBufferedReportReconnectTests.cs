using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsBufferedReportReconnectTests
{
    [Fact]
    public void Brcb_Reconnect_Prefers_Validated_EntryId_And_Never_SqNum_As_Resume_Authority()
    {
        var rcb = BuildBrcb(["RptEna", "EntryID", "PurgeBuf"]);
        var lifecycle = BuildLifecycle(rcb, "00:00:00:00:00:00:00:38", 1);

        var plan = MmsBufferedReportReconnectPlanner.Build(rcb, lifecycle);

        Assert.Equal(MmsBufferedReportReconnectStrategy.ResumeAfterEntryId, plan.Strategy);
        Assert.Equal("0000000000000038", plan.EntryIdHex);
        Assert.Equal(8, plan.EntryIdBytes.Length);
        Assert.False(plan.SequenceNumberIsResumeAuthority);
    }

    [Fact]
    public void Brcb_Reconnect_Blocks_Malformed_EntryId_Instead_Of_Stripping_Unknown_Characters()
    {
        var rcb = BuildBrcb(["EntryID"]);
        var lifecycle = BuildLifecycle(rcb, "ZZ11", 5);

        var plan = MmsBufferedReportReconnectPlanner.Build(rcb, lifecycle);

        Assert.Equal(MmsBufferedReportReconnectStrategy.BlockedInvalidCursor, plan.Strategy);
        Assert.Empty(plan.EntryIdBytes);
    }

    [Fact]
    public async Task PurgeBuf_Requires_Explicit_Event_Loss_Acknowledgement_Before_Association_Is_Needed()
    {
        await using var session = new MmsClientSession();
        var rcb = BuildBrcb(["PurgeBuf"]);

        var result = await session.PurgeBufferedReportAsync(rcb, acknowledgeBufferedEventLoss: false);

        Assert.False(result.IsSuccess);
        Assert.Contains("not explicitly acknowledged", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Write);
    }

    [Fact]
    public async Task PurgeBuf_Rejects_Urcb_Locally_Without_Wire_Access()
    {
        await using var session = new MmsClientSession();
        var rcb = new MmsReportControlCandidate
        {
            Buffered = false,
            Attributes = ["PurgeBuf"]
        };

        var result = await session.PurgeBufferedReportAsync(rcb, acknowledgeBufferedEventLoss: true);

        Assert.False(result.IsSuccess);
        Assert.Contains("only to buffered", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Write);
    }

    private static MmsReportControlCandidate BuildBrcb(IEnumerable<string> attributes)
        => new()
        {
            Buffered = true,
            Reference = "IED1LD0/LLN0.BR.Buffer01",
            DataSetReference = "IED1LD0/LLN0.Events",
            Attributes = attributes.ToList()
        };

    private static MmsReportLifecycleSnapshot BuildLifecycle(
        MmsReportControlCandidate rcb,
        string entryId,
        ulong sequence)
    {
        var state = new MmsReportLifecycleStateMachine(new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = rcb.DataSetReference
        });
        state.ObserveReceiveResult(new MmsPersistentReportMonitorReceiveResult
        {
            Reports =
            [
                new MmsReportFrame
                {
                    Header = new MmsReportHeader
                    {
                        ReportId = "IED1LD0/LLN0$BR$Buffer01",
                        DataSetReference = "IED1LD0/LLN0$Events",
                        EntryIdHex = entryId,
                        SequenceNumber = sequence,
                        TimeOfEntry = "2026-09-15T01:00:00Z"
                    }
                }
            ]
        });
        return state.Snapshot();
    }
}
