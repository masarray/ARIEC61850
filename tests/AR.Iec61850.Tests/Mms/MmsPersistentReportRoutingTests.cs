using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsPersistentReportRoutingTests
{
    // Synthetic typed MMS evidence: no external packet capture or station data.
    [Fact]
    public void ControlInformationReportsCannotEnterSingleRcbFallback()
    {
        foreach (var value in new[]
        {
            MmsDataValue.Boolean(true),
            MmsDataValue.Structure(new[] { MmsDataValue.VisibleString("LD0/CSWI1$CO$Pos$Oper"), MmsDataValue.Integer(0) })
        })
        {
            var report = new MmsInformationReport
            {
                IsSuccess = true,
                VariableReferences = new[] { "LD0/CSWI1$CO$Pos$Oper" },
                Items = new[] { new MmsInformationReportItem { Value = value } }
            };
            Assert.False(MmsClientSession.HasRcbReportHeader(report));
        }
    }

    [Fact]
    public void RcbHeaderDoesNotRequireOptionalSequenceOrDatasetFields()
    {
        var report = new MmsInformationReport
        {
            IsSuccess = true,
            Items = new[]
            {
                new MmsInformationReportItem { Value = MmsDataValue.VisibleString("RPT_A") },
                new MmsInformationReportItem { Value = MmsDataValue.BitString(6, new byte[] { 0, 0 }) },
                new MmsInformationReportItem { Value = MmsDataValue.BitString(7, new byte[] { 0x80 }) },
                new MmsInformationReportItem { Value = MmsDataValue.Boolean(true) }
            }
        };
        Assert.True(MmsClientSession.HasRcbReportHeader(report));
        Assert.False(MmsClientSession.HasRcbReportHeader(new MmsInformationReport()));
        Assert.False(MmsClientSession.HasRcbReportHeader(new MmsInformationReport
        {
            IsSuccess = true,
            Items = new[]
            {
                new MmsInformationReportItem { Value = MmsDataValue.Boolean(true) },
                new MmsInformationReportItem { Value = MmsDataValue.BitString(6, new byte[] { 0, 0 }) }
            }
        }));
    }

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
    public void Selector_RejectsContradictoryDomainQualifiedDataSetWithExactReportId()
    {
        var first = CreateSession("URCBA01", "RPT_A", "IEDLD0/LLN0.DS_A");
        var second = CreateSession("URCBA02", "RPT_B", "IEDLD0/LLN0.DS_B");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [first, second],
            new MmsReportHeader { ReportId = "RPT_A", DataSetReference = "IEDLD0/LLN0$DS_B" },
            out var evidence);

        Assert.Null(selected);
        Assert.Contains("RptID/DatSet conflict", evidence);
    }

    [Fact]
    public void Selector_RejectsUnknownFullDataSetDespiteMatchingReportId()
    {
        var first = CreateSession("URCBA01", "RPT_A", "IEDLD0/LLN0.DS_A");
        var second = CreateSession("URCBA02", "RPT_B", "IEDLD0/LLN0.DS_B");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [first, second],
            new MmsReportHeader { ReportId = "RPT_A", DataSetReference = "OTHERLD/LLN0$DS_X" },
            out var evidence);

        Assert.Null(selected);
        Assert.Contains("quarantined", evidence);
    }

    [Fact]
    public void Selector_SingleActiveMonitor_DoesNotBypassDataSetConsistency()
    {
        var session = CreateSession("URCBA01", "RPT_A", "IEDLD0/LLN0.DS_A");

        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [session],
            new MmsReportHeader { ReportId = "UNRECOGNIZED", DataSetReference = "OTHERLD/LLN0.DS_X" },
            out var evidence);

        Assert.Null(selected);
        Assert.Contains("single-monitor DatSet conflict", evidence);
    }

    [Fact]
    public void Selector_PreservesValidNoDataSetAndVendorShortDataSetCompatibility()
    {
        var session = CreateSession("URCBA01", "RPT_A", "IEDLD0/LLN0.DS_A");

        var missing = MmsClientSession.SelectPersistentReportMonitor(
            [session], new MmsReportHeader { ReportId = "RPT_A" }, out var missingEvidence);
        var shortName = MmsClientSession.SelectPersistentReportMonitor(
            [session],
            new MmsReportHeader { ReportId = "RPT_A", DataSetReference = "LLN0$DS_A" },
            out var shortEvidence);

        Assert.Same(session, missing);
        Assert.Same(session, shortName);
        Assert.Equal("exact RptID", missingEvidence);
        Assert.Equal("exact RptID", shortEvidence);
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
