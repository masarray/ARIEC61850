using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSclReportSubscriptionPlannerTests
{
    [Fact]
    public void BuildStaticPlan_SelectsFreeConcreteInstanceFromIndexedSclFamily()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateBufferedCandidate("Buffer01", enabled: "true"));
        inventory.ReportControls.Add(CreateBufferedCandidate("Buffer02", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory("LD0/LLN0.DataSet")],
            CreateSclReportControl("Buffer", buffered: true, "LD0/LLN0.DataSet"));

        Assert.True(result.RcbResolution.IsSuccess);
        Assert.Equal(MmsSclRcbFamilyResolutionKind.IndexedFamily, result.RcbResolution.Kind);
        Assert.True(result.Plan.IsReady);
        Assert.Equal("LD0/LLN0.BR.Buffer02", result.Plan.ReportControl?.Reference);
        Assert.Contains(result.Plan.Steps, step => step.Contains("resolved to 2 concrete live MMS RCB", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Plan.Steps, step => step.Contains("GI=true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_SelectsFreeConcreteUrcbFromIndexedSclFamily()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateUnbufferedCandidate("Unbuffer01", enabled: "false", reserved: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory("LD0/LLN0.Analog")],
            CreateSclReportControl("Unbuffer", buffered: false, "LD0/LLN0.Analog"),
            allowUrCbFallback: true);

        Assert.True(result.RcbResolution.IsSuccess);
        Assert.True(result.Plan.IsReady);
        Assert.NotNull(result.Plan.ReportControl);
        Assert.False(result.Plan.ReportControl!.Buffered);
        Assert.Equal("LD0/LLN0.RP.Unbuffer01", result.Plan.ReportControl.Reference);
        Assert.Contains("Resv", result.Plan.ReportControl.Attributes);
        Assert.Contains("GI", result.Plan.ReportControl.Attributes);
        Assert.Contains(result.Plan.Steps, step => step.Contains("Resv=true", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Plan.Steps, step => step.Contains("RptEna=true", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Plan.Steps, step => step.Contains("GI=true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_BlocksWhenDeclaredFamilyHasNoLiveInstance()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateBufferedCandidate("Different01", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory("LD0/LLN0.DataSet")],
            CreateSclReportControl("Buffer", buffered: true, "LD0/LLN0.DataSet"));

        Assert.False(result.IsReady);
        Assert.Equal(MmsReportSubscriptionPlanStatus.Blocked, result.Plan.Status);
        Assert.Null(result.Plan.ReportControl);
        Assert.Contains(result.Plan.Blockers, blocker => blocker.Contains("No report-control write is permitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_DoesNotEscapeResolvedFamilyEvenWhenAnotherRcbIsFree()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(CreateBufferedCandidate("Buffer01", enabled: "true"));
        inventory.ReportControls.Add(CreateBufferedCandidate("Other01", enabled: "false"));

        var result = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            [CreateDataSetDirectory("LD0/LLN0.DataSet")],
            CreateSclReportControl("Buffer", buffered: true, "LD0/LLN0.DataSet"));

        Assert.False(result.Plan.IsReady);
        Assert.Null(result.Plan.ReportControl);
    }

    private static SclReportControl CreateSclReportControl(string name, bool buffered, string dataSetReference)
        => new()
        {
            LogicalNodePath = "LLN0",
            Name = name,
            Buffered = buffered,
            Indexed = true,
            ControlBlockReference = $"LD0/LLN0${(buffered ? "BR" : "RP")}${name}",
            DataSetReference = dataSetReference
        };

    private static MmsReportControlCandidate CreateBufferedCandidate(string name, string enabled)
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

    private static MmsReportControlCandidate CreateUnbufferedCandidate(string name, string enabled, string reserved)
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = name,
            Reference = $"LD0/LLN0.RP.{name}",
            Buffered = false,
            DataSetReference = "LD0/LLN0.Analog",
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "fixture: live DatSet read succeeded",
            EnabledState = enabled,
            ReservationState = reserved,
            ReportId = $"LD0/LLN0$RP${name}",
            ConfRev = "1",
            Attributes = ["RptEna", "Resv", "DatSet", "GI"],
            Status = "Attribute-probed"
        };

    private static MmsDataSetDirectoryResult CreateDataSetDirectory(string reference)
        => new()
        {
            IsSuccess = true,
            DataSetReference = reference,
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
