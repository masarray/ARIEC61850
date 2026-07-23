using AR.Iec61850.Ethernet;
using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues;

public sealed record SampledValuesFramePreview(
    string ControlBlockReference,
    string SvId,
    string DataSetReference,
    ushort AppId,
    MacAddress Destination,
    VlanTag? Vlan,
    ushort NoAsdu,
    int PayloadBytesPerAsdu,
    double SampleRateHz,
    double PublicationRateHz,
    int EstimatedEthernetBytes,
    double EstimatedBandwidthBitsPerSecond)
{
    public string Summary =>
        $"{ControlBlockReference}: svID={SvId}, APPID=0x{AppId:X4}, dst={Destination}, " +
        $"{(Vlan is null ? "untagged" : $"VID={Vlan.Value.VlanId}/PCP={Vlan.Value.PriorityCodePoint}")}, " +
        $"nofASDU={NoAsdu}, sample={SampleRateHz:0.###} fps, publish={PublicationRateHz:0.###} fps, " +
        $"payload={PayloadBytesPerAsdu} B/ASDU, frame≈{EstimatedEthernetBytes} B, bw≈{EstimatedBandwidthBitsPerSecond / 1000.0:0.###} kbps";

    public static SampledValuesFramePreview FromStream(SclSampledValuesStream stream, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var profile = SampledValuesPublisherProfile.Create(stream);
        return FromProfile(profile, sampleRateHz);
    }

    public static SampledValuesFramePreview FromProfile(SampledValuesPublisherProfile profile, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var noAsdu = profile.AsduPerFrame;
        var publicationRateHz = SampledValuesPublisherProfile.ResolvePublicationRate(sampleRateHz, noAsdu);
        var estimatedBytes = EstimateEthernetFrameBytes(profile, noAsdu);
        return new SampledValuesFramePreview(
            profile.Stream.ControlBlockReference,
            profile.Stream.SvId,
            profile.Stream.DataSetReference,
            profile.AppId,
            profile.Destination,
            profile.Vlan,
            noAsdu,
            profile.PayloadLayout.PayloadByteLength,
            sampleRateHz,
            publicationRateHz,
            estimatedBytes,
            estimatedBytes * 8.0 * publicationRateHz);
    }

    public static int EstimateEthernetFrameBytes(SampledValuesPublisherProfile profile, ushort? noAsduOverride = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var noAsdu = noAsduOverride ?? profile.AsduPerFrame;
        var payloads = Enumerable.Range(0, noAsdu)
            .Select(_ => new byte[profile.PayloadLayout.PayloadByteLength])
            .ToArray();
        var frame = profile.BuildEthernetFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            samplePayloads: payloads,
            referenceTime: null,
            sampleSynchronization: 2);
        return frame.Length;
    }
}
