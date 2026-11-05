using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

/// <summary>
/// Project-owned synthetic MMS InformationReport BER, not real station traffic.
/// Tests the shared MMS decode -> RCB selection -> typed projection -> delivery.
/// </summary>
public sealed class MmsP9MultiRcbDispatchTests
{
    [Fact]
    public async Task FourRcbBurst_RoutesShortAndExactDataSetsWithoutCrossProjection()
    {
        await using var client = new MmsClientSession();
        var sessions = FourSessions();
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        var router = new MmsReceiveRouter();
        for (var round = 0; round < 12; round++)
        {
            foreach (var session in sessions.Reverse())
            {
                var dataSet = (round % 3) switch
                {
                    0 => session.Plan.DataSetReference.Replace('.', '$'),
                    1 => "LLN0$" + session.Plan.DataSetReference.Split('.').Last(),
                    _ => session.Plan.DataSetReference.Split('.').Last()
                };
                router.Route(BuildReport(session.ReportControl.ReportId, dataSet, round % 2 == 0));
            }
        }

        Assert.Equal(48, router.QueuedInformationReportCount);
        while (router.TryDequeueInformationReport(out var envelope))
            client.RoutePersistentInformationReport(envelope.PresentationPayload);

        Assert.Equal(0, client.UnroutedPersistentReportCount);
        foreach (var session in sessions)
        {
            Assert.Equal(12, session.RoutedReportCount);
            Assert.Equal(12, session.PendingReportCount);
            Assert.Equal(MmsPersistentReportTrafficState.TrafficProven, session.TrafficState);
            Assert.Equal(0, session.PendingDeliveryOverflowCount);
            for (var round = 0; round < 12; round++)
            {
                Assert.True(session.TryDequeuePendingReport(out var frame));
                Assert.Single(frame.Values);
                Assert.Equal(session.Plan.Members[0].UserReference, frame.Values[0].Member?.UserReference);
                Assert.Equal(round % 2 == 0, (bool)frame.Values[0].Value!.Value!);
            }
            Assert.Equal(0, session.PendingReportCount);
        }
    }

    [Fact]
    public async Task ConflictingIdentity_AndZeroFrameTarget_DoNotFabricateLiveEvidence()
    {
        await using var client = new MmsClientSession();
        var sessions = FourSessions();
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        // Same association: explicit RptID A conflicts with DataSet B.
        client.RoutePersistentInformationReport(BuildReport("RPT_A", "LLN0$TT3DataSet02", true));
        // Unknown report identity with four active targets is deliberately ambiguous.
        client.RoutePersistentInformationReport(BuildReport("UNBOUND", null, true));
        client.RoutePersistentInformationReport(BuildReport("RPT_I", "TT6DataSet4", false));

        Assert.Equal(2, client.UnroutedPersistentReportCount);
        Assert.Equal(0, sessions[0].RoutedReportCount);
        Assert.Equal(0, sessions[1].RoutedReportCount);
        Assert.Equal(1, sessions[2].RoutedReportCount);
        Assert.Equal(0, sessions[3].RoutedReportCount);
        Assert.Equal(MmsPersistentReportTrafficState.ArmedAwaitingTraffic, sessions[3].TrafficState);
    }

    [Fact]
    public async Task SlowRcb_BoundedOverflowStaysExplicit_IndependentRcbsKeepReceiving()
    {
        await using var client = new MmsClientSession();
        var sessions = FourSessions(pendingCapacity: 8);
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        for (var i = 0; i < 80; i++)
            client.RoutePersistentInformationReport(BuildReport("RPT_A", "LLN0$TT6DataSet3", true));

        client.RoutePersistentInformationReport(BuildReport("RPT_B", "LLN0$TT3DataSet02", false));
        client.RoutePersistentInformationReport(BuildReport("RPT_I", "LLN0$TT6DataSet4", true));

        Assert.Equal(8, sessions[0].PendingReportCount);
        Assert.Equal(8, sessions[0].RoutedReportCount);
        Assert.Equal(72, sessions[0].PendingDeliveryOverflowCount);
        Assert.True(sessions[0].IsDeliveryContinuityUnverified);
        Assert.Contains("deliveryContinuityUnverified=True", sessions[0].Summary);
        Assert.Equal(72, client.UnroutedPersistentReportCount);
        Assert.Equal(1, sessions[1].RoutedReportCount);
        Assert.Equal(1, sessions[2].RoutedReportCount);
        Assert.Equal(MmsPersistentReportTrafficState.ArmedAwaitingTraffic, sessions[3].TrafficState);

        for (var i = 0; i < 8; i++)
            Assert.True(sessions[0].TryDequeuePendingReport(out _));
        Assert.Equal(0, sessions[0].PendingReportCount);

        // Draining memory never erases previously detected continuity failure.
        client.RoutePersistentInformationReport(BuildReport("RPT_A", "LLN0$TT6DataSet3", false));
        Assert.Equal(1, sessions[0].PendingReportCount);
        Assert.True(sessions[0].IsDeliveryContinuityUnverified);
    }

    [Fact]
    public async Task AssociationRegistry_UsesStableSnapshot_UntilMembershipChangesOrReset()
    {
        await using var client = new MmsClientSession();
        var sessions = FourSessions();
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        var snapshot = client.SnapshotPersistentReportMonitors();
        Assert.Equal(4, snapshot.Length);
        for (var i = 0; i < 1024; i++)
            Assert.Same(snapshot, client.SnapshotPersistentReportMonitors());

        // Re-registering the same target is idempotent and does not force
        // allocation or alter report ownership.
        client.RegisterPersistentReportMonitor(sessions[0]);
        Assert.Same(snapshot, client.SnapshotPersistentReportMonitors());

        client.RoutePersistentInformationReport(
            BuildReport("RPT_A", "LLN0$TT6DataSet3", true));
        Assert.Equal(1, sessions[0].PendingReportCount);

        // A reconnect must revoke every RCB of the old association, publish
        // an empty registry atomically and drain its old pending deliveries.
        client.ClearPersistentReportMonitors();
        Assert.Empty(client.SnapshotPersistentReportMonitors());
        Assert.All(sessions, session =>
        {
            Assert.True(session.IsStopped);
            Assert.Equal(0, session.PendingReportCount);
            Assert.False(session.TryQueuePendingReport(new MmsReportFrame()));
        });
        Assert.Equal(0, client.UnroutedPersistentReportCount);
    }

    internal static MmsPersistentReportMonitorSession[] FourSessions(int pendingCapacity = 2048) =>
    [
        Create("BrcbA01", "RPT_A", "TT6DataSet3", pendingCapacity),
        Create("BrcbB01", "RPT_B", "TT3DataSet02", pendingCapacity),
        Create("BrcbI01", "RPT_I", "TT6DataSet4", pendingCapacity),
        Create("BrcbD01", "RPT_D", "TT3DataSet01", pendingCapacity)
    ];

    private static MmsPersistentReportMonitorSession Create(string name, string reportId, string dataSet, int capacity)
    {
        var reference = "SG7_BCUMaster/LLN0." + dataSet;
        var rcb = new MmsReportControlCandidate
        {
            Domain = "SG7_BCUMaster",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = name,
            Reference = "SG7_BCUMaster/LLN0.BR." + name,
            ReportId = reportId,
            DataSetReference = reference,
            Buffered = true
        };
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = reference,
            Members = [new MmsDataSetDirectoryMember
            {
                UserReference = "SG7_BCUMaster/GGIO1." + name + ".stVal",
                FunctionalConstraint = "ST"
            }]
        };
        return new MmsPersistentReportMonitorSession(
            plan, rcb, reference, isDynamic: false, deleteDynamicDataSetOnStop: false,
            dataSetCreated: false, reservationTouched: false, enabledByThisClient: true,
            pendingReportCapacity: capacity);
    }

    internal static byte[] BuildReport(string reportId, string? dataSet, bool value)
    {
        var fields = new List<MmsDataValue>
        {
            MmsDataValue.VisibleString(reportId),
            // 10-bit OptFlds: bit 4 indicates DataSet name.
            MmsDataValue.BitString(6, [dataSet is null ? (byte)0x00 : (byte)0x08, 0x00])
        };
        if (dataSet is not null)
            fields.Add(MmsDataValue.VisibleString(dataSet));
        fields.Add(MmsDataValue.BitString(7, [0x80])); // one included DataSet member
        fields.Add(MmsDataValue.Boolean(value));

        var encodedResults = fields.SelectMany(MmsDataCodec.Encode).ToArray();
        var variableList = BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("SG7_BCUMaster"))
                .Concat(BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("LLN0$RPT")))
                .ToArray());
        var informationReport = BerWriter.EncodeTlv(
            0xA0, variableList.Concat(BerWriter.EncodeTlv(0xA0, encodedResults)).ToArray());
        return MmsPresentation.WrapIsoPresentationPData(
            BerWriter.EncodeTlv(0xA3, informationReport));
    }
}
