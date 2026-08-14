using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsHybridReportAcquisitionContractP22Tests
{
    [Fact]
    public void DefaultOptions_AreBoundedAndFailClosed()
    {
        var options = new MmsHybridReportAcquisitionOptions();

        Assert.Equal(64, options.MaxStaticReportPlans);
        Assert.Equal(8, options.MaxDynamicReportPlans);
        Assert.Equal(64, options.MaxDynamicMembersPerReport);
        Assert.True(options.RequireExactAvailabilityEvidence);
        Assert.True(options.AllowCallerOwnedReports);
        Assert.True(options.AllowStaticBrcb);
        Assert.True(options.AllowStaticUrcb);
        Assert.True(options.AllowDynamicBrcb);
        Assert.True(options.AllowDynamicUrcb);
        Assert.True(options.AllowPollingFallback);
    }

    [Fact]
    public void EmptyRequest_IsIncompleteAndCannotClaimCoverage()
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            IedName = "IED",
            Source = "P2.2 contract regression"
        };
        var inventory = new MmsReportInventory();
        var availability = new MmsRcbAvailabilityResult();
        var liveDirectory = new MmsIedModelDirectory(Array.Empty<MmsFcResolvedPoint>());

        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            Array.Empty<Iec61850SignalDescriptor>(),
            inventory,
            availability,
            liveDirectory);

        Assert.Equal("iec61850-hybrid-acquisition-v1", plan.SchemaVersion);
        Assert.Equal(MmsHybridAcquisitionPlanStatus.Incomplete, plan.Status);
        Assert.Equal(0, plan.RequestedSignalCount);
        Assert.Equal(0, plan.ReportCoveredSignalCount);
        Assert.False(plan.HasFullReportCoverage);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("No requested signal", StringComparison.OrdinalIgnoreCase));
    }
}