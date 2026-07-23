using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.Transports;

namespace AR.Iec61850.SampledValues;

public sealed class SampledValuesPublisherSession
{
    private readonly SampledValuesPublisherProfile _profile;
    private readonly MacAddress _source;
    private readonly IProcessBusTransport _transport;

    public SampledValuesPublisherSession(
        SampledValuesPublisherProfile profile,
        MacAddress source,
        IProcessBusTransport transport,
        ushort initialSampleCount = 0,
        ushort? sampleCounterWrap = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _source = source;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (sampleCounterWrap is 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCounterWrap), "SV sample counter wrap must be greater than 1 when supplied.");

        SampleCounterWrap = sampleCounterWrap;
        NextSampleCount = initialSampleCount;
    }

    public ushort NextSampleCount { get; private set; }
    public ushort? SampleCounterWrap { get; }

    public async ValueTask<byte[]> PublishNextAsync(
        byte[] samplePayload,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samplePayload);

        if (_profile.AsduPerFrame != 1)
            throw new InvalidOperationException($"SV {_profile.Stream.ControlBlockReference} declares nofASDU={_profile.AsduPerFrame}. Use PublishNextBatchAsync.");

        return await PublishNextBatchAsync(
            [samplePayload],
            referenceTime,
            sampleSynchronization,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> PublishNextBatchAsync(
        IReadOnlyList<byte[]> samplePayloads,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samplePayloads);

        var sampleCount = NextSampleCount;
        NextSampleCount = SampleCounterPolicy.Increment(sampleCount, SampleCounterWrap, samplePayloads.Count);

        var frame = _profile.BuildEthernetFrame(
            _source,
            sampleCount,
            samplePayloads,
            referenceTime,
            sampleSynchronization,
            SampleCounterWrap);
        await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }
}
