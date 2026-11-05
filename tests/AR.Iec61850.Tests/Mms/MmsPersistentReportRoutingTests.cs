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

    [Theory]
    [InlineData("LLN0$DS_B")]
    [InlineData("OTHERLN$DS_A")]
    [InlineData("DS_B")]
    public void Selector_QuarantinesContradictoryAbbreviatedDataSetEvenWithExactRptId(string advertised)
    {
        var a = CreateSession("BrcbA01", "RPT_A", "IEDLD0/LLN0.DS_A");
        var b = CreateSession("BrcbB01", "RPT_B", "IEDLD0/LLN0.DS_B");
        var result = MmsClientSession.SelectPersistentReportMonitor(
            [a, b], new MmsReportHeader { ReportId = "RPT_A", DataSetReference = advertised },
            out var evidence);
        Assert.Null(result);
        Assert.Contains("RptID/DatSet conflict", evidence);
    }

    [Theory]
    [InlineData("LLN0$DS_A")]
    [InlineData("DS_A")]
    [InlineData("IEDLD0/LLN0$DS_A")]
    public void Selector_StillAcceptsConsistentShortAndFullDataSetNames(string advertised)
    {
        var a = CreateSession("BrcbA01", "RPT_A", "IEDLD0/LLN0.DS_A");
        var b = CreateSession("BrcbB01", "RPT_B", "IEDLD0/LLN0.DS_B");
        var result = MmsClientSession.SelectPersistentReportMonitor(
            [b, a], new MmsReportHeader { ReportId = "RPT_A", DataSetReference = advertised },
            out var evidence);
        Assert.Same(a, result);
        Assert.Equal("exact RptID", evidence);
    }

    [Fact]
    public void Selector_FourIndependentSg7ConfiguredRcbsRemainDeterministicAcrossInputOrder()
    {
        var sessions = new[]
        {
            CreateSession("BrcbD01", "RPT_D", "SG7_BCUMaster/LLN0.TT3DataSet01"),
            CreateSession("BrcbI01", "RPT_I", "SG7_BCUMaster/LLN0.TT6DataSet4"),
            CreateSession("BrcbB01", "RPT_B", "SG7_BCUMaster/LLN0.TT3DataSet02"),
            CreateSession("BrcbA01", "RPT_A", "SG7_BCUMaster/LLN0.TT6DataSet3")
        };
        var reversed = sessions.Reverse().ToArray();
        foreach (var expected in sessions)
        {
            var header = new MmsReportHeader
            {
                ReportId = expected.ReportControl.ReportId,
                DataSetReference = expected.Plan.DataSetReference.Replace('.', '$')
            };
            Assert.Same(expected, MmsClientSession.SelectPersistentReportMonitor(
                sessions, header, out _));
            Assert.Same(expected, MmsClientSession.SelectPersistentReportMonitor(
                reversed, header, out _));
        }
        Assert.Null(MmsClientSession.SelectPersistentReportMonitor(
            sessions,
            new MmsReportHeader { ReportId = "RPT_I", DataSetReference = "LLN0$TT3DataSet02" },
            out var mismatch));
        Assert.Contains("quarantined", mismatch);
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

    [Fact]
    public void Selector_ExactReportIdDoesNotAllocateCandidateArraysPerFrame()
    {
        var candidates = new[]
        {
            CreateSession("BrcbA01", "RPT_A", "SG7_BCUMaster/LLN0.TT6DataSet3"),
            CreateSession("BrcbB01", "RPT_B", "SG7_BCUMaster/LLN0.TT3DataSet02"),
            CreateSession("BrcbI01", "RPT_I", "SG7_BCUMaster/LLN0.TT6DataSet4"),
            CreateSession("BrcbD01", "RPT_D", "SG7_BCUMaster/LLN0.TT3DataSet01")
        };
        var header = new MmsReportHeader { ReportId = "RPT_A" };
        for (var i = 0; i < 256; i++)
            MmsClientSession.SelectPersistentReportMonitor(candidates, header, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8192; i++)
        {
            var result = MmsClientSession.SelectPersistentReportMonitor(candidates, header, out _);
            if (!ReferenceEquals(candidates[0], result))
                throw new InvalidOperationException("Exact Report ID routing changed in the measured dispatch path.");
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(bytes, 0, 512);
    }

    [Fact]
    public void Selector_StoppedRcbCannotShadowLiveRptIdOrDataSetIdentity()
    {
        var retired = CreateSession("BrcbA01", "DUPLICATE", "IEDLD0/LLN0.DS_A");
        var live = CreateSession("BrcbB01", "DUPLICATE", "IEDLD0/LLN0.DS_A");
        retired.IsStopped = true;
        var selected = MmsClientSession.SelectPersistentReportMonitor(
            [retired, live],
            new MmsReportHeader { ReportId = "DUPLICATE", DataSetReference = "IEDLD0/LLN0$DS_A" },
            out var evidence);
        Assert.Same(live, selected);
        Assert.Equal("exact RptID", evidence);
    }

    [Fact]
    public void Selector_ConflictingAndAmbiguousCandidatesFailClosedAcrossOrdering()
    {
        var a = CreateSession("BrcbA01", "SAME", "IEDLD0/LLN0.DS_A");
        var b = CreateSession("BrcbB01", "SAME", "IEDLD0/LLN0.DS_B");
        var third = CreateSession("BrcbC01", "DIFFERENT", "IEDLD0/LLN0.DS_A");
        foreach (var candidates in new[]
        {
            new[] { a, b, third },
            new[] { third, b, a },
            new[] { b, a, third }
        })
        {
            // Duplicate RptID cannot substitute for a unique identity.
            Assert.Null(MmsClientSession.SelectPersistentReportMonitor(
                candidates, new MmsReportHeader { ReportId = "SAME" }, out var conflict));
            Assert.Contains("ambiguous", conflict);

            // Exact RptID on a different target with conflicting DatSet
            // remains quarantined even though a DataSet-only target exists.
            Assert.Null(MmsClientSession.SelectPersistentReportMonitor(
                candidates,
                new MmsReportHeader { ReportId = "DIFFERENT", DataSetReference = "IEDLD0/LLN0$DS_B" },
                out var mismatch));
            Assert.Contains("quarantined", mismatch);
        }
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
