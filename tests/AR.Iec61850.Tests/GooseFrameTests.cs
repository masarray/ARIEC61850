using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using System.Buffers.Binary;

namespace AR.Iec61850.Tests;

public sealed class GooseFrameTests
{
    [Fact]
    public void Goose_Publisher_Builds_Parseable_Vlan_Ethernet_Frame()
    {
        var timestamp = new Iec61850UtcTime(
            new DateTimeOffset(2026, 6, 12, 10, 30, 0, TimeSpan.Zero),
            Quality: 0);

        var frame = new GooseFrame
        {
            Destination = MacAddress.Parse("01:0C:CD:01:00:01"),
            Source = MacAddress.Parse("02:00:00:00:00:01"),
            Vlan = new VlanTag(priorityCodePoint: 4, vlanId: 100),
            AppId = 0x1001,
            Pdu = new GoosePdu
            {
                GoCbRef = "IED1LD0/LLN0$GO$gcb01",
                TimeAllowedToLiveMilliseconds = 1000,
                DataSetReference = "IED1LD0/LLN0$DS1",
                GoId = "trip-goose",
                Timestamp = timestamp,
                StateNumber = 3,
                SequenceNumber = 7,
                ConfigurationRevision = 2,
                Values =
                [
                    MmsDataValue.Boolean(true),
                    MmsDataValue.Integer(-1),
                    MmsDataValue.VisibleString("TRIP")
                ]
            }
        };

        var encoded = GooseFrameBuilder.BuildEthernetFrame(frame);

        Assert.Equal("010CCD0100010200000000018100806488B8", Convert.ToHexString(encoded.AsSpan(0, 18)));
        Assert.Equal(0x1001, BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(18, 2)));
        Assert.True(BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(20, 2)) > 8);

        Assert.True(GooseFrameParser.TryParseEthernetFrame(encoded, out var parsed));
        Assert.Equal("01:0C:CD:01:00:01", parsed.Destination.ToString());
        Assert.Equal("02:00:00:00:00:01", parsed.Source.ToString());
        Assert.Equal(new VlanTag(priorityCodePoint: 4, vlanId: 100), parsed.Vlan);
        Assert.Equal(0x1001, parsed.AppId);
        Assert.Equal("IED1LD0/LLN0$GO$gcb01", parsed.Pdu.GoCbRef);
        Assert.Equal("IED1LD0/LLN0$DS1", parsed.Pdu.DataSetReference);
        Assert.Equal("trip-goose", parsed.Pdu.GoId);
        Assert.Equal(timestamp, parsed.Pdu.Timestamp);
        Assert.Equal(3U, parsed.Pdu.StateNumber);
        Assert.Equal(7U, parsed.Pdu.SequenceNumber);
        Assert.Equal(2U, parsed.Pdu.ConfigurationRevision);

        Assert.Equal(3, parsed.Pdu.Values.Count);
        Assert.Equal(true, parsed.Pdu.Values[0].Value);
        Assert.Equal(-1L, parsed.Pdu.Values[1].Value);
        Assert.Equal("TRIP", parsed.Pdu.Values[2].Value);
    }

    [Fact]
    public void Goose_Pdu_Rejects_AllData_Count_Mismatch()
    {
        var pdu = new GoosePdu
        {
            GoCbRef = "IED1LD0/LLN0$GO$gcb01",
            TimeAllowedToLiveMilliseconds = 1000,
            DataSetReference = "IED1LD0/LLN0$DS1",
            GoId = "trip-goose",
            Timestamp = new Iec61850UtcTime(DateTimeOffset.UnixEpoch, 0),
            Values = [MmsDataValue.Boolean(true)]
        };

        var encoded = GooseFrameBuilder.EncodePdu(pdu);
        var tampered = encoded.ToArray();
        var countTagIndex = Array.IndexOf(tampered, (byte)0x8A);
        Assert.True(countTagIndex >= 0);

        tampered[countTagIndex + 2] = 0x02;

        Assert.False(GooseFrameParser.TryParsePdu(tampered, out _));
    }
}
