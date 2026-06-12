using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportSubscriptionPlannerTests
{
    [Fact]
    public void BuildStaticPlan_SelectsBufferedStaticCandidateWithDatasetMap()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet",
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var dataSet = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "LD0/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    MmsItemName = "PTOC1$ST$Str",
                    UserReference = "LD0/PTOC1.Str",
                    FunctionalConstraint = "ST"
                }
            ]
        };

        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(inventory, [dataSet]);

        Assert.Equal(MmsReportSubscriptionPlanMode.StaticDataSet, plan.Mode);
        Assert.Equal(MmsReportSubscriptionPlanStatus.ReadyRequiresWrite, plan.Status);
        Assert.Equal("LD0/LLN0.BR.brcbA01", plan.ReportControl?.Reference);
        Assert.Single(plan.Members);
        Assert.Contains(plan.Steps, x => x.Contains("RptEna=true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_BlocksWhenDatasetMapIsMissing()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet",
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(inventory, []);

        Assert.Equal(MmsReportSubscriptionPlanStatus.Blocked, plan.Status);
        Assert.NotEmpty(plan.Blockers);
    }


    [Fact]
    public void BuildStaticPlan_AutoSelectsStaticRcbWhenRptEnaIsNotExplicitButDataSetMapIsValid()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet",
            EnabledState = string.Empty,
            ReservationTimeSeconds = "0",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var dataSet = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "LD0/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    MmsItemName = "PTOC1$ST$Str",
                    UserReference = "LD0/PTOC1.Str",
                    FunctionalConstraint = "ST"
                }
            ]
        };

        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(inventory, [dataSet]);

        Assert.Equal(MmsReportSubscriptionPlanStatus.ReadyRequiresWrite, plan.Status);
        Assert.Equal("LD0/LLN0.BR.brcbA01", plan.ReportControl?.Reference);
        Assert.Contains(plan.Warnings, x => x.Contains("RptEna was not decoded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStaticPlan_DoesNotSelectDynamicSlotWithoutDataset()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = string.Empty,
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(inventory, []);

        Assert.Equal(MmsReportSubscriptionPlanStatus.Blocked, plan.Status);
        Assert.Null(plan.ReportControl);
    }

    [Fact]
    public void BuildDynamicPlan_SelectsFreeDynamicSlotAndResolvesPoints()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            EnabledState = "false",
            ReservationTimeSeconds = "0",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var directory = new MmsIedModelDirectory(
        [
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "PTOC1",
                FunctionalConstraint = "ST",
                DataObjectPath = "Str.stVal",
                MmsItemName = "PTOC1$ST$Str$stVal"
            },
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "MMXU1",
                FunctionalConstraint = "MX",
                DataObjectPath = "PhV.phsA.cVal.mag.f",
                MmsItemName = "MMXU1$MX$PhV$phsA$cVal$mag$f"
            }
        ]);

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(
            inventory,
            directory,
            ["LD0/PTOC1.Str.stVal", "LD0/MMXU1.PhV.phsA.cVal.mag.f"],
            dataSetName: "AR_TEST");

        Assert.Equal(MmsReportSubscriptionPlanMode.DynamicDataSet, plan.Mode);
        Assert.Equal(MmsReportSubscriptionPlanStatus.ReadyRequiresWrite, plan.Status);
        Assert.Equal("LD0/LLN0.AR_TEST", plan.DataSetReference);
        Assert.Equal(2, plan.DynamicPoints.Count);
        Assert.Contains(plan.Steps, x => x.Contains("Create dynamic DataSet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildDynamicPlan_CreatesDataSetInSameLogicalNodeAsSelectedRcb()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "RP",
            Name = "urcbA01",
            Reference = "LD0/GGIO1.RP.urcbA01",
            Buffered = false,
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "LD0/GGIO1$RP$urcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var directory = new MmsIedModelDirectory(
        [
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "GGIO1",
                FunctionalConstraint = "ST",
                DataObjectPath = "Ind1.stVal",
                MmsItemName = "GGIO1$ST$Ind1$stVal"
            }
        ]);

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(
            inventory,
            directory,
            ["LD0/GGIO1.Ind1.stVal"],
            dataSetName: "AR_TEST");

        Assert.Equal("LD0/GGIO1.AR_TEST", plan.DataSetReference);
    }
}
