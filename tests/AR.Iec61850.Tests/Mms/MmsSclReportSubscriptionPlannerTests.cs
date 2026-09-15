using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSclReportSubscriptionPlannerTests
{
    [Fact]
    public void BuildStaticPlan_SelectsFreeConcreteInstanceFromIndexedSclFamily()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateCandidate("Buffer01", enabled: "true"));
        inventory.ReportControls.Add(CreateCandidate("Buffer02", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory()],
            CreateSclReportControl());

        Assert.True(result.RcbResolution.IsSuccess);
        Assert.Equal(MmsSclRcbFamilyResolutionKind.IndexedFamily, result.RcbResolution.Kind);
        Assert.True(result.Plan.IsReady);
        Assert.Equal("LD0/LLN0.BR.Buffer02", result.Plan.ReportControl?.Reference);
        Assert.Contains(result.Plan.Steps, step => step.Contains("resolved to 2 concrete live MMS RCB", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Plan.Steps, step => step.Contains("GI=true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_BlocksWhenDeclaredFamilyHasNoLiveInstance()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateCandidate("Different01", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory()],
            CreateSclReportControl());

        Assert.False(result.IsReady);
        Assert.Equal(MmsReportSubscriptionPlanStatus.Blocked, result.Plan.Status);
        Assert.Null(result.Plan.ReportControl);
        Assert.Contains(result.Plan.Blockers, blocker => blocker.Contains("No report-control write is permitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_DoesNotEscapeResolvedFamilyEvenWhenAnotherRcbIsFree()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateCandidate("Buffer01", enabled: "true"));
        inventory.ReportControls.Add(CreateCandidate("Other01", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory()],
            CreateSclReportControl());

        Assert.False(result.Plan.IsReady);
        Assert.Null(result.Plan.ReportControl);
    }

    private static SclReportControl CreateSclReportControl()
        => new()
        {
            LogicalNodePath = "LLN0",
            Name = "Buffer",
            Buffered = true,
            Indexed = true,
            ControlBlockReference = "LD0/LLN0$BR$Buffer",
            DataSetReference = "LD0/LLN0.DataSet"
        };

    private static MmsReportControlCandidate CreateCandidate(string name, string enabled)
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = name,
            Reference = $"LD0/LLN0.BR.{name}",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet",
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "fixture: live DatSet read succeeded",
            EnabledState = enabled,
            ReservationTimeSeconds = "0",
            ReportId = $"LD0/LLN0$BR${name}",
            ConfRev = "1",
            Attributes = ["RptEna", "DatSet", "GI", "ResvTms"],
            Status = "Attribute-probed"
        };

    private static MmsDataSetDirectoryResult CreateDataSetDirectory()
        => new()
        {
            IsSuccess = true,
            DataSetReference = "LD0/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    MmsItemName = "PTOC1$ST$Str$stVal",
                    UserReference = "LD0/PTOC1.Str.stVal",
                    FunctionalConstraint = "ST"
                }
            ]
        };
}
