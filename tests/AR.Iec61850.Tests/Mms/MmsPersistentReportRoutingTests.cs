using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsPersistentReportRoutingTests
{
    [Fact]
    public void Selector_RoutesByExactReportIdBeforeDataSetProjection()
    {
        var first = CreateSession("URCBA01", "RPT_A", "IEDLD0/LLN0.DS_A");
        var second = CreateSession("URCBA02", "RPT_B", "IEDLD0/LLN0.DS_B");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [first, second],
            new MmsReportHeader { ReportId = "RPT_B", DataSetReference = "IEDLD0/LLN0$DS_B" },
            out var evidence);

        Assert.Same(second, selected);
        Assert.Equal("exact RptID", evidence);
    }

    [Fact]
    public void Selector_RoutesByExactDataSetWhenReportIdIsUnavailable()
    {
        var first = CreateSession("URCBA01", "", "IEDLD0/LLN0.DS_A");
        var second = CreateSession("URCBA02", "", "IEDLD0/LLN0.DS_B");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [first, second],
            new MmsReportHeader { DataSetReference = "IEDLD0/LLN0$DS_A" },
            out var evidence);

        Assert.Same(first, selected);
        Assert.Equal("exact DataSet", evidence);
    }

    [Fact]
    public void Selector_DoesNotProjectAmbiguousReportAgainstArbitraryMembers()
    {
        var first = CreateSession("URCBA01", "", "IEDLD0/LLN0.DS_A");
        var second = CreateSession("URCBA02", "", "IEDLD0/LLN0.DS_B");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [first, second],
            new MmsReportHeader(),
            out var evidence);

        Assert.Null(selected);
        Assert.Contains("ambiguous", evidence, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsPersistentReportMonitorSession CreateSession(string rcbName, string reportId, string dataSet)
    {
        var rcb = new MmsReportControlCandidate
        {
            Domain = "IEDLD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = rcbName,
            Reference = $"IEDLD0/LLN0.RP.{rcbName}",
            ReportId = reportId,
            DataSetReference = dataSet
        };
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = dataSet,
            Members = [new MmsDataSetDirectoryMember { UserReference = $"IEDLD0/GGIO1.{rcbName}.stVal", FunctionalConstraint = "ST" }]
        };

        return new MmsPersistentReportMonitorSession(
            plan,
            rcb,
            originalDataSetReference: dataSet,
            isDynamic: false,
            deleteDynamicDataSetOnStop: false,
            dataSetCreated: false,
            reservationTouched: false,
            enabledByThisClient: true);
    }
}
