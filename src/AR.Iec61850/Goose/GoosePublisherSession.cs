using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.Transports;

namespace AR.Iec61850.Goose;

public sealed class GoosePublisherSession
{
    private readonly GoosePublisherProfile _profile;
    private readonly MacAddress _source;
    private readonly IProcessBusTransport _transport;

    public GoosePublisherSession(
        GoosePublisherProfile profile,
        MacAddress source,
        IProcessBusTransport transport,
        uint initialStateNumber = 1,
        uint initialSequenceNumber = 0)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _source = source;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        StateNumber = initialStateNumber == 0 ? 1 : initialStateNumber;
        SequenceNumber = initialSequenceNumber;
    }

    public uint StateNumber { get; private set; }
    public uint SequenceNumber { get; private set; }

    public async ValueTask<byte[]> PublishAsync(
        IReadOnlyList<MmsDataValue> values,
        Iec61850UtcTime timestamp,
        bool stateChanged = false,
        bool test = false,
        bool needsCommissioning = false,
        CancellationToken cancellationToken = default)
    {
        if (stateChanged)
        {
            StateNumber = IncrementStateNumber(StateNumber);
            SequenceNumber = 0;
        }

        var frame = _profile.BuildEthernetFrame(
            _source,
            values,
            timestamp,
            StateNumber,
            SequenceNumber,
            test,
            needsCommissioning);

        await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        SequenceNumber = IncrementSequenceNumber(SequenceNumber);
        return frame;
    }

    private static uint IncrementStateNumber(uint current)
        => current == uint.MaxValue ? 1 : current + 1;

    private static uint IncrementSequenceNumber(uint current)
        => current == uint.MaxValue ? 0 : current + 1;
}
