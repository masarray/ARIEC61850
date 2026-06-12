using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;
using System.Buffers.Binary;

namespace AR.Iec61850.Tests;

public sealed class SampledValuesFrameTests
{
    [Fact]
    public void SampledValues_Publisher_Builds_Parseable_Vlan_Ethernet_Frame()
    {
        var referenceTime = new Iec61850UtcTime(
            new DateTimeOffset(2026, 6, 12, 10, 31, 0, TimeSpan.Zero),
            Quality: 0);

        var frame = new SampledValuesFrame
        {
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            Source = MacAddress.Parse("02:00:00:00:00:02"),
            Vlan = new VlanTag(priorityCodePoint: 4, vlanId: 200),
            AppId = 0x4001,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = "MU01F1/LLN0$MSVCB01",
                        DataSetReference = "MU01F1/LLN0$PhsMeas1",
                        SampleCount = 120,
                        ConfigurationRevision = 3,
                        ReferenceTime = referenceTime,
                        SampleSynchronization = 2,
                        SampleRate = 4000,
                        SampleMode = 1,
                        SamplePayload = Convert.FromHexString("0000006400000001000000C800000003")
                    }
                ]
            }
        };

        var encoded = SampledValuesFrameBuilder.BuildEthernetFrame(frame);

        Assert.Equal("010CCD040001020000000002810080C888BA", Convert.ToHexString(encoded.AsSpan(0, 18)));
        Assert.Equal(0x4001, BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(18, 2)));
        Assert.True(BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(20, 2)) > 8);

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(encoded, out var parsed));
        Assert.Equal("01:0C:CD:04:00:01", parsed.Destination.ToString());
        Assert.Equal("02:00:00:00:00:02", parsed.Source.ToString());
        Assert.Equal(new VlanTag(priorityCodePoint: 4, vlanId: 200), parsed.Vlan);
        Assert.Equal(0x4001, parsed.AppId);
        Assert.Single(parsed.Pdu.Asdus);

        var asdu = parsed.Pdu.Asdus[0];
        Assert.Equal("MU01F1/LLN0$MSVCB01", asdu.SvId);
        Assert.Equal("MU01F1/LLN0$PhsMeas1", asdu.DataSetReference);
        Assert.Equal(120, asdu.SampleCount);
        Assert.Equal(3U, asdu.ConfigurationRevision);
        Assert.Equal(referenceTime, asdu.ReferenceTime);
        Assert.Equal(2, asdu.SampleSynchronization);
        Assert.Equal((ushort)4000, asdu.SampleRate);
        Assert.Equal((ushort)1, asdu.SampleMode);
        Assert.Equal("0000006400000001000000C800000003", Convert.ToHexString(asdu.SamplePayload));
    }

    [Fact]
    public void SampledValues_Pdu_Rejects_NoAsdu_Count_Mismatch()
    {
        var pdu = new SampledValuesPdu
        {
            Asdus =
            [
                new SampledValueAsdu
                {
                    SvId = "MU01",
                    SampleCount = 1,
                    SamplePayload = [0x00]
                }
            ]
        };

        var encoded = SampledValuesFrameBuilder.EncodePdu(pdu);
        var tampered = encoded.ToArray();
        var countTagIndex = Array.IndexOf(tampered, (byte)0x80);
        Assert.True(countTagIndex >= 0);

        tampered[countTagIndex + 2] = 0x02;

        Assert.False(SampledValuesFrameParser.TryParsePdu(tampered, out _));
    }
}
