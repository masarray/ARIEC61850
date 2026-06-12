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
        var monitor = new ProcessBusStreamMonitor();

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
        Assert.Equal(ProcessBusEventKind.Goose, gooseEvent.Kind);
        Assert.Equal("MU01LD0/LLN0$GO$GCB01", gooseEvent.StreamId);
        Assert.Equal(2U, gooseEvent.StateNumber);

        var summaries = monitor.Summaries.ToArray();
        var svSummary = Assert.Single(summaries.Where(s => s.Kind == ProcessBusEventKind.SampledValues));
        var gooseSummary = Assert.Single(summaries.Where(s => s.Kind == ProcessBusEventKind.Goose));

        Assert.Equal(2, svSummary.PacketCount);
        Assert.Equal((ushort)0, svSummary.FirstSampleCount);
        Assert.Equal((ushort)1, svSummary.LastSampleCount);
        Assert.Equal(1, gooseSummary.PacketCount);
        Assert.Equal(2U, gooseSummary.LastStateNumber);
        Assert.Equal(0U, gooseSummary.LastSequenceNumber);
    }
}
