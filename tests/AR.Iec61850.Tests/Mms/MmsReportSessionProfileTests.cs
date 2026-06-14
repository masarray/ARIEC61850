using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportSessionProfileTests
{
    [Fact]
    public void FromPlanPreservesRcbAndDatasetSelection()
    {
        var rcb = new MmsReportControlCandidate
        {
            Reference = "IED1LD0/LLN0.RP.rpt01",
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            Buffered = false
        };
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            Members = new[]
            {
                new MmsDataSetDirectoryMember
                {
                    Domain = "IED1LD0",
                    MmsItemName = "XCBR1$ST$Pos$stVal",
                    UserReference = "IED1LD0/XCBR1.Pos.stVal",
                    FunctionalConstraint = "ST",
                    LogicalNode = "XCBR1",
                    DataObjectPath = "Pos.stVal",
                    Confidence = 100
                }
            },
            Steps = new[] { "Enable RptEna after receiver is ready." }
        };

        var profile = MmsReportSessionProfile.FromPlan(plan, "192.0.2.10", 102, "IED1");

        Assert.Equal("192.0.2.10", profile.Host);
        Assert.Equal("IED1LD0/LLN0.RP.rpt01", profile.ReportControlReference);
        Assert.Equal("IED1LD0/LLN0.dsStatus", profile.DataSetReference);
        Assert.Single(profile.Members);
        Assert.Equal(0, profile.Members[0].Index);
    }
}
