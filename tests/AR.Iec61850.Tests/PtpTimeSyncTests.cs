using AR.Iec61850.TimeSync.Health;
using AR.Iec61850.TimeSync.Monitoring;
using AR.Iec61850.TimeSync.Ptp;
using AR.Iec61850.TimeSync.PtpRuntime;
using AR.Iec61850.Transports;

namespace AR.Iec61850.Tests;

public class PtpTimeSyncTests
{
    [Fact]
    public void AnnounceSerializerRoundTripsThroughParser()
    {
        var options = new PtpBuildOptions
        {
            DomainNumber = 0,
            SourcePortIdentity = new PtpPortIdentity(ClockIdentity.Parse("02:00:00:00:00:00:00:01"), 1),
            GrandmasterIdentity = ClockIdentity.Parse("02:00:00:00:00:00:00:01"),
            SequenceId = 7,
            ClockClass = 6,
            ClockAccuracy = PtpClockAccuracy.Within1Us,
            TimeSource = PtpTimeSource.Gps,
            Timestamp = new PtpTimestamp(1_000, 500)
        };

        var message = PtpMessageSerializer.BuildAnnounce(options);

        Assert.True(PtpPacketParser.TryParseMessage(message, out var parsed));
        Assert.Equal(PtpMessageType.Announce, parsed.Header.MessageType);
        Assert.Equal(0, parsed.Header.DomainNumber);
        Assert.Equal((ushort)7, parsed.Header.SequenceId);
        Assert.NotNull(parsed.Announce);
        Assert.Equal(PtpTimeSource.Gps, parsed.Announce!.TimeSource);
    }

    [Fact]
    public void EthernetParserHandlesVlanPtpFrame()
    {
        var options = new PtpBuildOptions
        {
            DomainNumber = 0,
            SourcePortIdentity = new PtpPortIdentity(ClockIdentity.Parse("02:00:00:00:00:00:00:02"), 1),
            SequenceId = 1,
            Timestamp = new PtpTimestamp(2_000, 42)
        };

        var sync = PtpMessageSerializer.BuildSync(options);
        var ethernet = PtpMessageSerializer.BuildEthernetFrame(
            PtpConstants.GeneralMulticastMac,
            new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x02 },
            sync,
            vlanId: 100);

        Assert.True(PtpPacketParser.TryParseEthernetFrame(ethernet, out var parsed));
        Assert.Equal(PtpMessageType.Sync, parsed.Header.MessageType);
        Assert.Equal((ushort)100, parsed.VlanId);
    }

    [Fact]
    public void PassiveMonitorAndHealthValidatorProduceGlobalSyncRecommendation()
    {
        var monitor = new PtpPassiveMonitor();
        var clock = ClockIdentity.Parse("02:00:00:00:00:00:00:03");
        var port = new PtpPortIdentity(clock, 1);
        var sourceMac = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x03 };
        var now = DateTimeOffset.UtcNow;

        var baseOptions = new PtpBuildOptions
        {
            DomainNumber = 0,
            SourcePortIdentity = port,
            GrandmasterIdentity = clock,
            Timestamp = new PtpTimestamp(3_000, 0),
            TimeSource = PtpTimeSource.Gps
        };

        monitor.ObserveEthernetFrame(PtpMessageSerializer.BuildEthernetFrame(PtpConstants.GeneralMulticastMac, sourceMac, PtpMessageSerializer.BuildAnnounce(baseOptions with { SequenceId = 1 })), now);
        monitor.ObserveEthernetFrame(PtpMessageSerializer.BuildEthernetFrame(PtpConstants.GeneralMulticastMac, sourceMac, PtpMessageSerializer.BuildSync(baseOptions with { SequenceId = 2 })), now);
        monitor.ObserveEthernetFrame(PtpMessageSerializer.BuildEthernetFrame(PtpConstants.GeneralMulticastMac, sourceMac, PtpMessageSerializer.BuildFollowUp(baseOptions with { SequenceId = 2 })), now);
        monitor.ObserveEthernetFrame(PtpMessageSerializer.BuildEthernetFrame(PtpConstants.PeerDelayMulticastMac, sourceMac, PtpMessageSerializer.BuildPdelayReq(baseOptions with { SequenceId = 3 })), now);

        var snapshot = monitor.GetSnapshot(now);
        var report = new PtpTimingHealthValidator().Evaluate(snapshot, new PtpTimingHealthOptions { ExpectedDomainNumber = 0 });

        Assert.True(report.IsHealthy);
        Assert.Equal(SmpSynchValue.GlobalSynchronized, PtpSmpSynchPolicy.Resolve(report));
    }
    [Fact]
    public async Task LabPtpPublisherRuntimeBroadcastsAnnounceSyncAndFollowUp()
    {
        var transport = new InMemoryProcessBusTransport();
        var runtime = new PtpPublisherRuntime(transport, new PtpPublisherOptions
        {
            DomainNumber = 0,
            SourceMac = new byte[] { 0x02, 0x00, 0x00, 0xFF, 0xFE, 0x01 },
            ClockIdentity = ClockIdentity.Parse("02:00:00:FF:FE:00:00:01"),
            AnnounceInterval = TimeSpan.FromSeconds(1),
            SyncInterval = TimeSpan.FromMilliseconds(100),
            FollowUpDelay = TimeSpan.Zero,
            TwoStepClock = true
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
        await runtime.RunAsync(cts.Token);

        var parsedTypes = transport.Frames
            .Where(frame => PtpPacketParser.TryParseEthernetFrame(frame, out _))
            .Select(frame =>
            {
                PtpPacketParser.TryParseEthernetFrame(frame, out var parsed);
                return parsed.Header.MessageType;
            })
            .ToArray();

        Assert.Contains(PtpMessageType.Announce, parsedTypes);
        Assert.Contains(PtpMessageType.Sync, parsedTypes);
        Assert.Contains(PtpMessageType.FollowUp, parsedTypes);
        var status = runtime.GetStatus();
        Assert.True(status.AnnounceSent >= 1);
        Assert.True(status.SyncSent >= 1);
        Assert.True(status.FollowUpSent >= 1);
    }

}
