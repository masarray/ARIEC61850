using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclPublisherProfileTests
{
    [Fact]
    public void SampledValues_Profile_FromScl_Builds_Parseable_Frame()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document, "MU01LD0/LLN0$SV$MSVCB01");
        var source = MacAddress.Parse("02:00:00:00:10:01");
        var referenceTime = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 11, 0, 0, TimeSpan.Zero), Quality: 0);
        var payload = Convert.FromHexString("0000006400000001000000C800000003");

        var bytes = profile.BuildEthernetFrame(source, sampleCount: 44, payload, referenceTime);

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(bytes, out var parsed));
        Assert.Equal("01:0C:CD:04:00:01", parsed.Destination.ToString());
        Assert.Equal("02:00:00:00:10:01", parsed.Source.ToString());
        Assert.Equal(0x4001, parsed.AppId);
        Assert.Equal(new VlanTag(priorityCodePoint: 4, vlanId: 200), parsed.Vlan);
        Assert.Single(parsed.Pdu.Asdus);

        var asdu = parsed.Pdu.Asdus[0];
        Assert.Equal("MU01LD0/LLN0$MSVCB01", asdu.SvId);
        Assert.Equal("MU01LD0/LLN0$dsSV", asdu.DataSetReference);
        Assert.Equal((ushort)44, asdu.SampleCount);
        Assert.Equal(3U, asdu.ConfigurationRevision);
        Assert.Equal((ushort)4000, asdu.SampleRate);
        Assert.Equal((ushort)1, asdu.SampleMode);
        Assert.Equal(payload, asdu.SamplePayload);
        Assert.Equal(referenceTime, asdu.ReferenceTime);
    }

    [Fact]
    public void Goose_Profile_FromScl_Builds_Parseable_Frame()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = GoosePublisherProfile.FromScl(document, "MU01LD0/LLN0$GO$GCB01");
        var source = MacAddress.Parse("02:00:00:00:10:02");
        var timestamp = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 12, 11, 1, 0, TimeSpan.Zero), Quality: 0);
        var values = new[]
        {
            MmsDataValue.Boolean(true),
            MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }),
            MmsDataValue.UtcTime(timestamp)
        };

        var bytes = profile.BuildEthernetFrame(source, values, timestamp, stateNumber: 2, sequenceNumber: 5);

        Assert.True(GooseFrameParser.TryParseEthernetFrame(bytes, out var parsed));
        Assert.Equal("01:0C:CD:01:00:01", parsed.Destination.ToString());
        Assert.Equal("02:00:00:00:10:02", parsed.Source.ToString());
        Assert.Equal(0x1001, parsed.AppId);
        Assert.Equal(new VlanTag(priorityCodePoint: 4, vlanId: 100), parsed.Vlan);
        Assert.Equal("MU01LD0/LLN0$GO$GCB01", parsed.Pdu.GoCbRef);
        Assert.Equal("MU01LD0/LLN0$dsGO", parsed.Pdu.DataSetReference);
        Assert.Equal("trip-goose", parsed.Pdu.GoId);
        Assert.Equal(2U, parsed.Pdu.StateNumber);
        Assert.Equal(5U, parsed.Pdu.SequenceNumber);
        Assert.Equal(2U, parsed.Pdu.ConfigurationRevision);
        Assert.Equal(1000U, parsed.Pdu.TimeAllowedToLiveMilliseconds);
        Assert.Equal(3, parsed.Pdu.Values.Count);
        Assert.Equal(true, parsed.Pdu.Values[0].Value);
        Assert.Equal(MmsDataKind.BitString, parsed.Pdu.Values[1].Kind);
        Assert.Equal(timestamp, parsed.Pdu.Values[2].Value);
    }

    [Fact]
    public void Goose_Profile_Rejects_Value_Count_Mismatch()
    {
        var document = SclParserTests.LoadMinimalStation();
        var profile = GoosePublisherProfile.FromScl(document);
        var source = MacAddress.Parse("02:00:00:00:10:02");
        var timestamp = new Iec61850UtcTime(DateTimeOffset.UnixEpoch, Quality: 0);

        Assert.Throws<AR.Iec61850.Scl.SclProfileException>(() =>
            profile.CreateFrame(source, [MmsDataValue.Boolean(true)], timestamp));
    }
}
