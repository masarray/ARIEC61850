using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesPublisherProfile
{
    private SampledValuesPublisherProfile(
        SclSampledValuesStream stream,
        ushort appId,
        MacAddress destination,
        VlanTag? vlan)
    {
        Stream = stream;
        AppId = appId;
        Destination = destination;
        Vlan = vlan;
        PayloadLayout = SampledValuesPayloadLayout.FromDataSet(stream.Entries);
    }

    public SclSampledValuesStream Stream { get; }
    public ushort AppId { get; }
    public MacAddress Destination { get; }
    public VlanTag? Vlan { get; }
    public SampledValuesPayloadLayout PayloadLayout { get; }
    public IReadOnlyList<SclDataSetEntry> Entries => Stream.Entries;

    public static IReadOnlyList<SampledValuesPublisherProfile> CreateMany(SclDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.SampledValuesStreams.Select(Create).ToArray();
    }

    public static SampledValuesPublisherProfile FromScl(SclDocument document, string? controlBlockReference = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = string.IsNullOrWhiteSpace(controlBlockReference)
            ? document.SampledValuesStreams.FirstOrDefault()
            : document.SampledValuesStreams.FirstOrDefault(s => string.Equals(s.ControlBlockReference, controlBlockReference, StringComparison.OrdinalIgnoreCase));

        if (stream is null)
            throw new SclProfileException("No matching SampledValueControl stream was found in the SCL document.");

        return Create(stream);
    }

    public SampledValuesFrame CreateFrame(
        MacAddress source,
        ushort sampleCount,
        ReadOnlySpan<byte> samplePayload,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2)
    {
        return new SampledValuesFrame
        {
            Destination = Destination,
            Source = source,
            Vlan = Vlan,
            AppId = AppId,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = Stream.SvId,
                        DataSetReference = Stream.DataSetReference,
                        SampleCount = sampleCount,
                        ConfigurationRevision = Stream.ConfigurationRevision,
                        ReferenceTime = referenceTime,
                        SampleSynchronization = sampleSynchronization,
                        SampleRate = Stream.SampleRate == 0 ? null : Stream.SampleRate,
                        SampleMode = TryMapSampleMode(Stream.SampleMode),
                        SamplePayload = samplePayload.ToArray()
                    }
                ]
            }
        };
    }

    public byte[] BuildEthernetFrame(
        MacAddress source,
        ushort sampleCount,
        ReadOnlySpan<byte> samplePayload,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2)
    {
        return SampledValuesFrameBuilder.BuildEthernetFrame(CreateFrame(source, sampleCount, samplePayload, referenceTime, sampleSynchronization));
    }

    public byte[] BuildPayload(IReadOnlyList<MmsDataValue> values)
        => SampledValuesPayloadBuilder.BuildPayload(PayloadLayout, values);

    public byte[] BuildDefaultPayload(Iec61850UtcTime? timestamp = null)
        => SampledValuesPayloadBuilder.BuildDefaultPayload(PayloadLayout, timestamp);

    public byte[] BuildDemoPayload(
        long sampleIndex,
        double sampleRateHz,
        double nominalHz,
        Iec61850UtcTime? timestamp = null)
        => SampledValuesPayloadBuilder.BuildDemoPayload(PayloadLayout, sampleIndex, sampleRateHz, nominalHz, timestamp);

    public ushort? ResolveSampleCounterWrap(double nominalFrequencyHz)
    {
        var mode = TryMapSampleMode(Stream.SampleMode);
        if (Stream.SampleRate == 0)
            return null;

        var samplesPerSecond = mode switch
        {
            0 => Stream.SampleRate * nominalFrequencyHz,
            1 => Stream.SampleRate,
            _ => 0
        };

        if (samplesPerSecond <= 0 || samplesPerSecond > ushort.MaxValue)
            return null;

        return (ushort)Math.Round(samplesPerSecond);
    }

    private static SampledValuesPublisherProfile Create(SclSampledValuesStream stream)
    {
        if (!stream.Address.AppId.HasValue)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no valid APPID in SCL Communication/SMV.");

        if (!stream.Address.DestinationMac.HasValue)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no valid destination MAC in SCL Communication/SMV.");

        if (string.IsNullOrWhiteSpace(stream.SvId))
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no svID/smvID.");

        if (string.IsNullOrWhiteSpace(stream.DataSetReference) || stream.Entries.Count == 0)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no resolved DataSet entries.");

        if (stream.NoAsdu != 1)
            throw new SclProfileException($"SV {stream.ControlBlockReference} declares nofASDU={stream.NoAsdu}. This publisher currently supports exactly one ASDU per frame.");

        return new SampledValuesPublisherProfile(stream, stream.Address.AppId.Value, stream.Address.DestinationMac.Value, stream.Address.ToVlanTag());
    }

    private static ushort? TryMapSampleMode(string sampleMode)
    {
        if (string.IsNullOrWhiteSpace(sampleMode))
            return null;

        return sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };
    }
}
