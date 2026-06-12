using AR.Iec61850.Capture;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Capture;

public sealed class PcapReaderTests
{
    [Fact]
    public void Reader_RoundTrips_Packets_Written_By_Writer()
    {
        var firstFrame = Convert.FromHexString("010CCD01000102000000000188B80000");
        var secondFrame = Convert.FromHexString("010CCD04000102000000000188BA0001");
        var firstTimestamp = new DateTimeOffset(2026, 6, 12, 13, 0, 0, 123, TimeSpan.Zero);
        var secondTimestamp = firstTimestamp.AddMilliseconds(1);

        using var stream = new MemoryStream();
        using (var writer = new PcapWriter(stream, leaveOpen: true))
        {
            writer.WritePacket(firstTimestamp, firstFrame);
            writer.WritePacket(secondTimestamp, secondFrame);
        }

        stream.Position = 0;
        var packets = PcapReader.ReadAll(stream);

        Assert.Equal(2, packets.Count);
        Assert.Equal(firstTimestamp, packets[0].Timestamp);
        Assert.Equal(firstFrame, packets[0].Frame);
        Assert.Equal(secondTimestamp, packets[1].Timestamp);
        Assert.Equal(secondFrame, packets[1].Frame);
    }

    [Fact]
    public void Reader_Feeds_Generated_ProcessBus_Frames_Back_To_Stack_Parsers()
    {
        var document = SclParserTests.LoadMinimalStation();
        var source = MacAddress.Parse("02:00:00:00:30:01");
        var svProfile = SampledValuesPublisherProfile.FromScl(document);
        var gooseProfile = GoosePublisherProfile.FromScl(document);
        var timestamp = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 13, 1, 0, TimeSpan.Zero), Quality: 0);
        var svFrame = svProfile.BuildEthernetFrame(source, 12, Convert.FromHexString("0000006400000000"), timestamp);
        var gooseFrame = gooseProfile.BuildEthernetFrame(
            source,
            [
                MmsDataValue.Boolean(true),
                MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }),
                MmsDataValue.UtcTime(timestamp)
            ],
            timestamp);

        using var stream = new MemoryStream();
        using (var writer = new PcapWriter(stream, leaveOpen: true))
        {
            writer.WritePacket(timestamp.Value, svFrame);
            writer.WritePacket(timestamp.Value.AddMilliseconds(1), gooseFrame);
        }

        stream.Position = 0;
        var packets = PcapReader.ReadAll(stream);

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(packets[0].Frame, out var parsedSv));
        Assert.True(GooseFrameParser.TryParseEthernetFrame(packets[1].Frame, out var parsedGoose));
        Assert.Equal("MU01LD0/LLN0$MSVCB01", parsedSv.Pdu.Asdus[0].SvId);
        Assert.Equal("MU01LD0/LLN0$GO$GCB01", parsedGoose.Pdu.GoCbRef);
    }
}
