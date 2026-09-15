using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportLifecycleStateMachineTests
{
    [Fact]
    public void Dynamic_Brcb_Lifecycle_Tracks_Owned_DataSet_Enable_Replay_And_Cleanup()
    {
        var plan = BuildPlan(buffered: true, dynamic: true);
        var state = new MmsReportLifecycleStateMachine(plan);

        state.ObserveStartResult(new MmsPersistentReportMonitorStartResult
        {
            IsSuccess = true,
            WriteSteps =
            [
                Step("DefineNamedVariableList", plan.DataSetReference, true),
                Step("DatSet", plan.ReportControl!.Reference, true),
                Step("TrgOps", plan.ReportControl.Reference, true),
                Step("OptFlds", plan.ReportControl.Reference, true),
                Step("RptEna", plan.ReportControl.Reference, true),
                Step("GI", plan.ReportControl.Reference, true)
            ],
            RcbSnapshots =
            [
                new MmsReportRcbSnapshot
                {
                    Stage = "after-enable",
                    IsSuccess = true,
                    Reference = plan.ReportControl.Reference,
                    DataSetReference = plan.DataSetReference,
                    EnabledState = "true"
                }
            ],
            DataSetSnapshots =
            [
                new MmsReportDataSetSnapshot
                {
                    Stage = "after-create",
                    IsSuccess = true,
                    Exists = true,
                    DataSetReference = plan.DataSetReference,
                    MemberCount = plan.Members.Count
                }
            ]
        });

        state.ObserveReceiveResult(new MmsPersistentReportMonitorReceiveResult
        {
            Reports =
            [
                Frame(entryId: "0000000000000001", sequence: 1, time: "2026-09-15T01:00:00Z", reason: "general-interrogation"),
                Frame(entryId: "0000000000000001", sequence: 1, time: "2026-09-15T01:00:00Z", reason: "general-interrogation"),
                Frame(entryId: "0000000000000003", sequence: 3, time: "2026-09-15T01:00:02Z", reason: "integrity")
            ]
        });

        var active = state.Snapshot();
        Assert.Equal(MmsReportLifecyclePhase.Monitoring, active.Phase);
        Assert.Equal(MmsReportDataSetOwnership.CreatedBySession, active.DataSetOwnership);
        Assert.True(active.IsEnabled);
        Assert.True(active.GeneralInterrogationRequested);
        Assert.Equal(2, active.Replay.AcceptedReportCount);
        Assert.Equal(1, active.Replay.DuplicateReportCount);
        Assert.Equal(1, active.Replay.SequenceGapCount);
        Assert.Equal(1, active.Replay.GeneralInterrogationReportCount);
        Assert.Equal(1, active.Replay.IntegrityReportCount);
        Assert.Equal("0000000000000003", active.Replay.LastEntryIdHex);

        state.BeginStop();
        state.ObserveStopResult(new MmsPersistentReportMonitorStopResult
        {
            IsSuccess = true,
            WriteSteps =
            [
                Step("RptEna", plan.ReportControl.Reference, true),
                Step("DatSet", plan.ReportControl.Reference, true),
                Step("DeleteNamedVariableList", plan.DataSetReference, true)
            ]
        });

        var stopped = state.Snapshot();
        Assert.Equal(MmsReportLifecyclePhase.Cleaned, stopped.Phase);
        Assert.False(stopped.IsEnabled);
        Assert.False(stopped.CleanupHasResidue);
        Assert.Equal(MmsReportDataSetOwnership.External, stopped.DataSetOwnership);
    }

    [Fact]
    public void Urcb_Lifecycle_Requires_Reservation_Release_And_Surfaces_Failed_Cleanup()
    {
        var plan = BuildPlan(buffered: false, dynamic: false);
        var state = new MmsReportLifecycleStateMachine(plan);

        state.ObserveStartResult(new MmsPersistentReportMonitorStartResult
        {
            IsSuccess = true,
            WriteSteps =
            [
                Step("Resv", plan.ReportControl!.Reference, true),
                Step("RptEna", plan.ReportControl.Reference, true)
            ]
        });

        Assert.True(state.Snapshot().IsReserved);
        state.BeginStop();
        state.ObserveStopResult(new MmsPersistentReportMonitorStopResult
        {
            IsSuccess = false,
            WriteSteps =
            [
                Step("RptEna", plan.ReportControl!.Reference, true),
                Step("Resv", plan.ReportControl.Reference, false, "reservation release rejected")
            ]
        });

        var stopped = state.Snapshot();
        Assert.Equal(MmsReportLifecyclePhase.Faulted, stopped.Phase);
        Assert.True(stopped.CleanupHasResidue);
        Assert.True(stopped.IsReserved);
        Assert.Contains(stopped.Events, x => x.Kind == MmsReportLifecycleEventKind.CleanupResidue);
    }

    [Fact]
    public void Brcb_Sequence_Reset_Is_Not_Classified_As_Loss_When_EntryId_Changes()
    {
        var state = new MmsReportLifecycleStateMachine(BuildPlan(buffered: true, dynamic: false));
        state.ObserveReceiveResult(new MmsPersistentReportMonitorReceiveResult
        {
            Reports =
            [
                Frame(entryId: "10", sequence: 250, time: "2026-09-15T01:00:00Z", reason: "data-change"),
                Frame(entryId: "11", sequence: 1, time: "2026-09-15T01:00:01Z", reason: "data-change")
            ]
        });

        var replay = state.Snapshot().Replay;
        Assert.Equal(1, replay.SequenceResetCount);
        Assert.Equal(0, replay.SequenceGapCount);
        Assert.Equal(0, replay.DuplicateReportCount);
    }

    private static MmsReportSubscriptionPlan BuildPlan(bool buffered, bool dynamic)
    {
        var rcb = new MmsReportControlCandidate
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            Name = buffered ? "brcb01" : "urcb01",
            Reference = buffered ? "IED1LD0/LLN0.BR.brcb01" : "IED1LD0/LLN0.RP.urcb01",
            Buffered = buffered,
            DataSetReference = "IED1LD0/LLN0.Events",
            ReportId = buffered ? "IED1LD0/LLN0$BR$brcb01" : "IED1LD0/LLN0$RP$urcb01"
        };

        return new MmsReportSubscriptionPlan
        {
            Mode = dynamic ? MmsReportSubscriptionPlanMode.DynamicDataSet : MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = dynamic ? "IED1LD0/LLN0.Runtime" : rcb.DataSetReference,
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "IED1LD0",
                    MmsItemName = "MMXU1$MX$TotW$mag$f",
                    UserReference = "IED1LD0/MMXU1.TotW.mag.f",
                    FunctionalConstraint = "MX"
                }
            ]
        };
    }

    private static MmsReportAttributeWriteStep Step(string attribute, string reference, bool success, string message = "ok")
        => new()
        {
            Attribute = attribute,
            Reference = reference,
            Attempted = true,
            IsSuccess = success,
            Message = message
        };

    private static MmsReportFrame Frame(string entryId, ulong sequence, string time, string reason)
        => new()
        {
            ReceivedAt = DateTimeOffset.Parse(time),
            Header = new MmsReportHeader
            {
                ReportId = "IED1LD0/LLN0$BR$brcb01",
                DataSetReference = "IED1LD0/LLN0$Events",
                ConfRev = 1,
                EntryIdHex = entryId,
                SequenceNumber = sequence,
                TimeOfEntry = time
            },
            IncludedDataSetIndexes = [0],
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    ReasonForInclusion = [reason]
                }
            ]
        };
}
