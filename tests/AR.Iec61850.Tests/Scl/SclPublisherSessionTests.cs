using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Transports;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclPublisherSessionTests
{
    [Fact]
    public async Task SampledValues_Session_Sends_Frames_And_Increments_SmpCnt()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);
        var transport = new InMemoryProcessBusTransport();
        var session = new SampledValuesPublisherSession(
            profile,
            MacAddress.Parse("02:00:00:00:20:01"),
            transport,
            initialSampleCount: ushort.MaxValue);
        var payload = Convert.FromHexString("0000006400000001");

        await session.PublishNextAsync(payload);
        await session.PublishNextAsync(payload);

        Assert.Equal(2, transport.Frames.Count);
        Assert.Equal((ushort)1, session.NextSampleCount);

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(transport.Frames[0], out var first));
        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(transport.Frames[1], out var second));
        Assert.Equal(ushort.MaxValue, first.Pdu.Asdus[0].SampleCount);
        Assert.Equal((ushort)0, second.Pdu.Asdus[0].SampleCount);
    }

    [Fact]
    public async Task SampledValues_Session_Uses_Configured_SampleCounterWrap()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);
        var transport = new InMemoryProcessBusTransport();
        var session = new SampledValuesPublisherSession(
            profile,
            MacAddress.Parse("02:00:00:00:20:01"),
            transport,
            initialSampleCount: 3999,
            sampleCounterWrap: 4000);
        var payload = profile.BuildDefaultPayload();

        await session.PublishNextAsync(payload);
        await session.PublishNextAsync(payload);

        Assert.Equal((ushort)1, session.NextSampleCount);
        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(transport.Frames[0], out var first));
        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(transport.Frames[1], out var second));
        Assert.Equal((ushort)3999, first.Pdu.Asdus[0].SampleCount);
        Assert.Equal((ushort)0, second.Pdu.Asdus[0].SampleCount);
    }

    [Fact]
    public async Task Goose_Session_Sends_Retransmit_And_StateChange_Sequence()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = GoosePublisherProfile.FromScl(document);
        var transport = new InMemoryProcessBusTransport();
        var session = new GoosePublisherSession(
            profile,
            MacAddress.Parse("02:00:00:00:20:02"),
            transport);
        var timestamp = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero), Quality: 0);
        var values = new[]
        {
            MmsDataValue.Boolean(false),
            MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }),
            MmsDataValue.UtcTime(timestamp)
        };

        await session.PublishAsync(values, timestamp);
        await session.PublishAsync(values, timestamp);
        await session.PublishAsync(values, timestamp, stateChanged: true);

        Assert.Equal(3, transport.Frames.Count);
        Assert.Equal(2U, session.StateNumber);
        Assert.Equal(1U, session.SequenceNumber);

        Assert.True(GooseFrameParser.TryParseEthernetFrame(transport.Frames[0], out var first));
        Assert.True(GooseFrameParser.TryParseEthernetFrame(transport.Frames[1], out var second));
        Assert.True(GooseFrameParser.TryParseEthernetFrame(transport.Frames[2], out var changed));

        Assert.Equal(1U, first.Pdu.StateNumber);
        Assert.Equal(0U, first.Pdu.SequenceNumber);
        Assert.Equal(1U, second.Pdu.StateNumber);
        Assert.Equal(1U, second.Pdu.SequenceNumber);
        Assert.Equal(2U, changed.Pdu.StateNumber);
        Assert.Equal(0U, changed.Pdu.SequenceNumber);
    }
}
