using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Monitoring;

public sealed class ProcessBusStreamMonitorTests
{
    [Fact]
    public void Monitor_Emits_Stream_Events_And_Summaries_For_Sv_And_Goose()
    {
        var document = SclParserTests.LoadMinimalStation();
        var source = MacAddress.Parse("02:00:00:00:40:01");
        var timestamp = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero), Quality: 0);
        var svProfile = SampledValuesPublisherProfile.FromScl(document);
        var gooseProfile = GoosePublisherProfile.FromScl(document);
        var monitor = new ProcessBusStreamMonitor(document);

        var svFrame0 = svProfile.BuildEthernetFrame(source, 0, Convert.FromHexString("0000000100000000"), timestamp);
        var svFrame1 = svProfile.BuildEthernetFrame(source, 1, Convert.FromHexString("0000000200000000"), timestamp);
        var gooseFrame = gooseProfile.BuildEthernetFrame(
            source,
            [
                MmsDataValue.Boolean(true),
                MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }),
                MmsDataValue.UtcTime(timestamp)
            ],
            timestamp,
            stateNumber: 2,
            sequenceNumber: 0);

        var svEvent0 = monitor.Observe(timestamp.Value, svFrame0);
        var svEvent1 = monitor.Observe(timestamp.Value.AddMilliseconds(1), svFrame1);
        var gooseEvent = monitor.Observe(timestamp.Value.AddMilliseconds(2), gooseFrame);

        Assert.Equal(ProcessBusEventKind.SampledValues, svEvent0.Kind);
        Assert.Equal("MU01LD0/LLN0$MSVCB01", svEvent0.StreamId);
        Assert.Equal((ushort)0, svEvent0.SampleCount);
        Assert.Equal((ushort)1, svEvent1.SampleCount);
        Assert.True(svEvent0.IsBoundToScl);
        Assert.Equal(ProcessBusSequenceStatus.First, svEvent0.SequenceStatus);
        Assert.Equal(ProcessBusSequenceStatus.InSequence, svEvent1.SequenceStatus);
        Assert.Equal(2, svEvent0.DecodedValueCount);
        Assert.Equal(ProcessBusEventKind.Goose, gooseEvent.Kind);
        Assert.Equal("MU01LD0/LLN0$GO$GCB01", gooseEvent.StreamId);
        Assert.Equal(2U, gooseEvent.StateNumber);

        var summaries = monitor.Summaries.ToArray();
        var svSummary = Assert.Single(summaries.Where(s => s.Kind == ProcessBusEventKind.SampledValues));
        var gooseSummary = Assert.Single(summaries.Where(s => s.Kind == ProcessBusEventKind.Goose));

        Assert.Equal(2, svSummary.PacketCount);
        Assert.Equal((ushort)0, svSummary.FirstSampleCount);
        Assert.Equal((ushort)1, svSummary.LastSampleCount);
        Assert.Equal(2, svSummary.LastDecodedValueCount);
        Assert.Equal(0, svSummary.SequenceGapCount);
        Assert.Equal(1, gooseSummary.PacketCount);
        Assert.Equal(2U, gooseSummary.LastStateNumber);
        Assert.Equal(0U, gooseSummary.LastSequenceNumber);
    }

    [Fact]
    public void Monitor_Tracks_Sv_SampleCounter_Wrap_From_Scl_Profile()
    {
        var document = SclParserTests.LoadMinimalStation();
        var source = MacAddress.Parse("02:00:00:00:40:01");
        var timestamp = new Iec61850UtcTime(DateTimeOffset.UtcNow, Quality: 0);
        var profile = SampledValuesPublisherProfile.FromScl(document);
        var monitor = new ProcessBusStreamMonitor(document);
        var payload = profile.BuildPayload([
            MmsDataValue.Integer(1),
            MmsDataValue.BitString(0, [0x00, 0x00, 0x00, 0x00])
        ]);

        var first = monitor.Observe(timestamp.Value, profile.BuildEthernetFrame(source, 3998, payload, timestamp));
        var second = monitor.Observe(timestamp.Value.AddTicks(1), profile.BuildEthernetFrame(source, 3999, payload, timestamp));
        var third = monitor.Observe(timestamp.Value.AddTicks(2), profile.BuildEthernetFrame(source, 0, payload, timestamp));

        Assert.Equal(ProcessBusSequenceStatus.First, first.SequenceStatus);
        Assert.Equal(ProcessBusSequenceStatus.InSequence, second.SequenceStatus);
        Assert.Equal(ProcessBusSequenceStatus.Wrapped, third.SequenceStatus);

        var summary = Assert.Single(monitor.Summaries.Where(s => s.Kind == ProcessBusEventKind.SampledValues));
        Assert.Equal((ushort)4000, summary.SampleCounterWrap);
        Assert.Equal(1, summary.WrapCount);
        Assert.Equal(0, summary.SequenceGapCount);
    }

    [Fact]
    public void Monitor_Tracks_Sv_Jump_Duplicate_And_OutOfOrder()
    {
        var document = SclParserTests.LoadMinimalStation();
        var source = MacAddress.Parse("02:00:00:00:40:01");
        var timestamp = new Iec61850UtcTime(DateTimeOffset.UtcNow, Quality: 0);
        var profile = SampledValuesPublisherProfile.FromScl(document);
        var monitor = new ProcessBusStreamMonitor(document);
        var payload = profile.BuildPayload([
            MmsDataValue.Integer(1),
            MmsDataValue.BitString(0, [0x00, 0x00, 0x00, 0x00])
        ]);

        _ = monitor.Observe(timestamp.Value, profile.BuildEthernetFrame(source, 0, payload, timestamp));
        var jump = monitor.Observe(timestamp.Value.AddTicks(1), profile.BuildEthernetFrame(source, 2, payload, timestamp));
        var duplicate = monitor.Observe(timestamp.Value.AddTicks(2), profile.BuildEthernetFrame(source, 2, payload, timestamp));
        var outOfOrder = monitor.Observe(timestamp.Value.AddTicks(3), profile.BuildEthernetFrame(source, 1, payload, timestamp));

        Assert.Equal(ProcessBusSequenceStatus.Jump, jump.SequenceStatus);
        Assert.Equal(ProcessBusSequenceStatus.Duplicate, duplicate.SequenceStatus);
        Assert.Equal(ProcessBusSequenceStatus.OutOfOrder, outOfOrder.SequenceStatus);

        var summary = Assert.Single(monitor.Summaries.Where(s => s.Kind == ProcessBusEventKind.SampledValues));
        Assert.Equal(1, summary.SequenceGapCount);
        Assert.Equal(1, summary.MissedSampleCount);
        Assert.Equal(1, summary.DuplicateSampleCount);
        Assert.Equal(1, summary.OutOfOrderSampleCount);
    }
}
