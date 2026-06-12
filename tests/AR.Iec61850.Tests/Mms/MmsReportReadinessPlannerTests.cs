using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportReadinessPlannerTests
{
    [Fact]
    public void Build_ClassifiesStaticDatasetCandidateAsReady()
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
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        var plan = MmsReportReadinessPlanner.Build(inventory);

        var item = Assert.Single(plan.Items);
        Assert.Equal(MmsReportReadinessKind.ReadyStaticDataSet, item.Kind);
        Assert.True(item.IsReadyForSafeSubscription);
        var safeCandidate = Assert.Single(plan.SafeCandidates);
        Assert.Same(item, safeCandidate);
        Assert.Equal(1, plan.BufferedSafeCandidateCount);
    }

    [Fact]
    public void Build_ClassifiesEnabledReportAsOccupied()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "urcbA01",
            Reference = "LD0/LLN0.RP.urcbA01",
            Buffered = false,
            DataSetReference = "LD0/LLN0.DataSet",
            EnabledState = "true",
            Status = "Attribute-probed"
        });

        var plan = MmsReportReadinessPlanner.Build(inventory);

        var item = Assert.Single(plan.Items);
        Assert.Equal(MmsReportReadinessKind.OccupiedEnabled, item.Kind);
        Assert.False(item.IsReadyForSafeSubscription);
    }

    [Fact]
    public void Build_ClassifiesProbedEmptyFreeReportAsDynamicSlotCandidate()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "RP",
            Name = "urcbB01",
            Reference = "LD0/GGIO1.RP.urcbB01",
            Buffered = false,
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "LD0/GGIO1$RP$urcbB01",
            Status = "Attribute-probed"
        });

        var plan = MmsReportReadinessPlanner.Build(inventory);

        var item = Assert.Single(plan.Items);
        Assert.Equal(MmsReportReadinessKind.EmptyDynamicSlotNeedsDataSet, item.Kind);
        Assert.False(item.IsReadyForSafeSubscription);
    }

    [Fact]
    public void Build_ClassifiesUnprobedReportAsNeedsAttributeProbe()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "RP",
            Name = "urcbC01",
            Reference = "LD0/GGIO1.RP.urcbC01",
            Buffered = false,
            Status = "Discovered"
        });

        var plan = MmsReportReadinessPlanner.Build(inventory);

        var item = Assert.Single(plan.Items);
        Assert.Equal(MmsReportReadinessKind.NeedsAttributeProbe, item.Kind);
    }
}
