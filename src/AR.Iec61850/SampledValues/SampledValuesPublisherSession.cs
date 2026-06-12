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
        ushort initialSampleCount = 0)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _source = source;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        NextSampleCount = initialSampleCount;
    }

    public ushort NextSampleCount { get; private set; }

    public async ValueTask<byte[]> PublishNextAsync(
        byte[] samplePayload,
        Iec61850UtcTime? referenceTime = null,
        byte sampleSynchronization = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samplePayload);

        var sampleCount = NextSampleCount;
        NextSampleCount = IncrementSampleCount(sampleCount);

        var frame = _profile.BuildEthernetFrame(_source, sampleCount, samplePayload, referenceTime, sampleSynchronization);
        await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static ushort IncrementSampleCount(ushort current)
        => current == ushort.MaxValue ? (ushort)0 : (ushort)(current + 1);
}
