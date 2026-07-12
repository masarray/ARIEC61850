using AR.Iec61850.Mms;
using System.Threading.Channels;

namespace AR.Iec61850.Control;

internal interface IIec61850ControlTransport
{
    object AssociationIdentity { get; }
    bool IsAssociated { get; }
    string LastRequestHex { get; }
    string LastResponseHex { get; }

    Task<MmsReadResult> ReadAsync(MmsObjectReference reference, CancellationToken cancellationToken);
    Task<MmsWriteResult> WriteControlAsync(MmsObjectReference reference, MmsDataValue value, CancellationToken cancellationToken);
    Task<MmsVariableAccessAttributesResult> GetVariableSpecificationAsync(MmsObjectReference reference, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariablesAsync(CancellationToken cancellationToken);
    IAsyncDisposable SubscribeInformationReports(out ChannelReader<MmsPduEnvelope> reader, int capacity = 32);
}

internal sealed class MmsClientControlTransport : IIec61850ControlTransport
{
    private readonly MmsClientSession _session;

    public MmsClientControlTransport(MmsClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public object AssociationIdentity => _session;
    public bool IsAssociated => _session.IsMmsInitiated;
    public string LastRequestHex => _session.LastReadRequestHex;
    public string LastResponseHex => _session.LastReadResponseHex;

    public Task<MmsReadResult> ReadAsync(MmsObjectReference reference, CancellationToken cancellationToken)
        => _session.ReadSingleVariableAsync(reference, cancellationToken);

    public Task<MmsWriteResult> WriteControlAsync(MmsObjectReference reference, MmsDataValue value, CancellationToken cancellationToken)
        => _session.WriteControlVariableAsync(reference, value, cancellationToken);

    public Task<MmsVariableAccessAttributesResult> GetVariableSpecificationAsync(MmsObjectReference reference, CancellationToken cancellationToken)
        => _session.GetVariableAccessAttributesAsync(reference, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariablesAsync(CancellationToken cancellationToken)
        => _session.DiscoverDomainVariableNamesAsync(cancellationToken);

    public IAsyncDisposable SubscribeInformationReports(out ChannelReader<MmsPduEnvelope> reader, int capacity = 32)
    {
        var subscription = _session.SubscribeInformationReports(capacity);
        reader = subscription.Reader;
        return subscription;
    }
}
