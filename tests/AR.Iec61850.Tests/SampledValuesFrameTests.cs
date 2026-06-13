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

    [Fact]
    public void SampledValues_PayloadLayout_Maps_Dataset_Order_And_Widths()
    {
        var document = Scl.SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);

        Assert.True(profile.PayloadLayout.IsFullySupported);
        Assert.Equal(8, profile.PayloadLayout.PayloadByteLength);
        Assert.Equal(2, profile.PayloadLayout.Elements.Count);

        var value = profile.PayloadLayout.Elements[0];
        Assert.Equal(0, value.Offset);
        Assert.Equal(4, value.Width);
        Assert.Equal(SampledValuePayloadElementKind.Int32, value.Kind);

        var quality = profile.PayloadLayout.Elements[1];
        Assert.Equal(4, quality.Offset);
        Assert.Equal(4, quality.Width);
        Assert.Equal(SampledValuePayloadElementKind.Quality, quality.Kind);
    }

    [Fact]
    public void SampledValues_PayloadBuilder_Writes_Typed_Values_In_DataSet_Order()
    {
        var document = Scl.SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);

        var payload = profile.BuildPayload([
            MmsDataValue.Integer(123),
            MmsDataValue.BitString(0, [0x01, 0x02, 0x03, 0x04])
        ]);

        Assert.Equal("0000007B01020304", Convert.ToHexString(payload));
    }

    [Fact]
    public void SampledValues_PayloadDecoder_Reads_Typed_Values_In_DataSet_Order()
    {
        var document = Scl.SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);
        var payload = profile.BuildPayload([
            MmsDataValue.Integer(123),
            MmsDataValue.BitString(0, [0x01, 0x02, 0x03, 0x04])
        ]);

        var decoded = SampledValuesPayloadDecoder.Decode(profile.PayloadLayout, payload);

        Assert.True(decoded.IsComplete);
        Assert.Equal(8, decoded.ExpectedPayloadBytes);
        Assert.Equal(8, decoded.ActualPayloadBytes);
        Assert.Equal(2, decoded.Values.Count);
        Assert.Equal(MmsDataKind.Integer, decoded.Values[0].Value.Kind);
        Assert.Equal(123L, decoded.Values[0].Value.Value);
        Assert.Equal(MmsDataKind.BitString, decoded.Values[1].Value.Kind);
        Assert.Equal("0001020304", Convert.ToHexString(decoded.Values[1].Value.RawValue.ToArray()));
    }

    [Fact]
    public void SampledValues_PayloadDecoder_Reports_Short_Payload()
    {
        var document = Scl.SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);

        var decoded = SampledValuesPayloadDecoder.Decode(profile.PayloadLayout, Convert.FromHexString("0000007B"));

        Assert.False(decoded.IsComplete);
        Assert.Single(decoded.Values);
        Assert.Contains(decoded.Diagnostics, x => x.Contains("too short", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SampledValues_Profile_Resolves_SampleCounterWrap_From_SmpPerSec()
    {
        var document = Scl.SclParserTests.LoadMinimalStation();
        var profile = SampledValuesPublisherProfile.FromScl(document);

        Assert.Equal((ushort)4000, profile.ResolveSampleCounterWrap(50));
    }
}
