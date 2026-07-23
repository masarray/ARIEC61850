using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesPublisherProfile
{
    public const ushort MaxAsduPerFrame = 8;

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
    public ushort AsduPerFrame => ResolveAsduPerFrame(Stream);

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
        byte sampleSynchronization = 2,
        ushort? sampleCounterWrap = null)
    {
        if (AsduPerFrame != 1)
            throw new InvalidOperationException($"SV {Stream.ControlBlockReference} declares nofASDU={AsduPerFrame}. Use the multi-ASDU CreateFrame overload.");

        return CreateFrame(
            source,
            sampleCount,
            new[] { samplePayload.ToArray() },
            referenceTime,
            sampleSynchronization,
            sampleCounterWrap);
    }

    public SampledValuesFrame CreateFrame(
        MacAddress source,
        ushort sampleCount,
        IReadOnlyList<byte[]> samplePayloads,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        ushort? sampleCounterWrap = null)
    {
        ArgumentNullException.ThrowIfNull(samplePayloads);
        ValidateAsduPayloadBatch(samplePayloads);

        if (sampleCounterWrap is 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCounterWrap), "SV sample counter wrap must be greater than 1 when supplied.");

        var asdus = new List<SampledValueAsdu>(samplePayloads.Count);
        for (var index = 0; index < samplePayloads.Count; index++)
        {
            asdus.Add(new SampledValueAsdu
            {
                SvId = Stream.SvId,
                DataSetReference = Stream.DataSetReference,
                SampleCount = SampleCounterPolicy.Increment(sampleCount, sampleCounterWrap, index),
                ConfigurationRevision = Stream.ConfigurationRevision,
                ReferenceTime = referenceTime,
                SampleSynchronization = sampleSynchronization,
                SampleRate = Stream.SampleRate == 0 ? null : Stream.SampleRate,
                SampleMode = TryMapSampleMode(Stream.SampleMode),
                SamplePayload = samplePayloads[index].ToArray()
            });
        }

        return new SampledValuesFrame
        {
            Destination = Destination,
            Source = source,
            Vlan = Vlan,
            AppId = AppId,
            Pdu = new SampledValuesPdu { Asdus = asdus }
        };
    }

    public byte[] BuildEthernetFrame(
        MacAddress source,
        ushort sampleCount,
        ReadOnlySpan<byte> samplePayload,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        ushort? sampleCounterWrap = null)
        => SampledValuesFrameBuilder.BuildEthernetFrame(
            CreateFrame(source, sampleCount, samplePayload, referenceTime, sampleSynchronization, sampleCounterWrap));

    public byte[] BuildEthernetFrame(
        MacAddress source,
        ushort sampleCount,
        IReadOnlyList<byte[]> samplePayloads,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        ushort? sampleCounterWrap = null)
        => SampledValuesFrameBuilder.BuildEthernetFrame(
            CreateFrame(source, sampleCount, samplePayloads, referenceTime, sampleSynchronization, sampleCounterWrap));

    public byte[] BuildPayload(IReadOnlyList<MmsDataValue> values)
        => SampledValuesPayloadBuilder.BuildPayload(PayloadLayout, values);

    public byte[] BuildDefaultPayload(Iec61850UtcTime? timestamp = null)
        => SampledValuesPayloadBuilder.BuildDefaultPayload(PayloadLayout, timestamp);

    public byte[] BuildDemoPayload(
        long sampleIndex,
        double sampleRateHz,
        double nominalHz,
        Iec61850UtcTime? timestamp = null,
        SampledValueQuality? quality = null)
        => SampledValuesPayloadBuilder.BuildDemoPayload(PayloadLayout, sampleIndex, sampleRateHz, nominalHz, timestamp, quality);

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

    public static SampledValuesPublisherProfile Create(SclSampledValuesStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.Address.AppId.HasValue)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no valid APPID in SCL Communication/SMV.");
        if (!stream.Address.DestinationMac.HasValue)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no valid destination MAC in SCL Communication/SMV.");
        if (string.IsNullOrWhiteSpace(stream.SvId))
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no svID/smvID.");
        if (string.IsNullOrWhiteSpace(stream.DataSetReference) || stream.Entries.Count == 0)
            throw new SclProfileException($"SV {stream.ControlBlockReference} has no resolved DataSet entries.");

        var noAsdu = ResolveAsduPerFrame(stream);
        if (noAsdu > MaxAsduPerFrame)
            throw new SclProfileException($"SV {stream.ControlBlockReference} declares nofASDU={stream.NoAsdu}. This publisher supports up to {MaxAsduPerFrame} ASDUs per frame.");

        return new SampledValuesPublisherProfile(stream, stream.Address.AppId.Value, stream.Address.DestinationMac.Value, stream.Address.ToVlanTag());
    }

    public static ushort ResolveAsduPerFrame(SclSampledValuesStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.NoAsdu == 0 ? (ushort)1 : stream.NoAsdu;
    }

    public static double ResolvePublicationRate(double sampleRateHz, ushort noAsdu)
    {
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be greater than 0 and finite.");
        if (noAsdu == 0)
            throw new ArgumentOutOfRangeException(nameof(noAsdu), "nofASDU must be greater than 0.");

        return sampleRateHz / noAsdu;
    }

    private void ValidateAsduPayloadBatch(IReadOnlyList<byte[]> samplePayloads)
    {
        var expected = AsduPerFrame;
        if (samplePayloads.Count != expected)
            throw new ArgumentException($"SV {Stream.ControlBlockReference} expects nofASDU={expected}, got {samplePayloads.Count} payload(s).", nameof(samplePayloads));

        for (var index = 0; index < samplePayloads.Count; index++)
        {
            if (samplePayloads[index].Length != PayloadLayout.PayloadByteLength)
                throw new ArgumentException($"SV ASDU payload {index} has {samplePayloads[index].Length} byte(s), expected {PayloadLayout.PayloadByteLength}.", nameof(samplePayloads));
        }
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
