using System.Threading.Channels;

namespace AR.Iec61850.Mms;

internal sealed class MmsInformationReportSubscription : IAsyncDisposable
{
    private readonly MmsReceiveRouter _router;
    private readonly Guid _id;
    private int _disposed;

    internal MmsInformationReportSubscription(
        MmsReceiveRouter router,
        Guid id,
        ChannelReader<MmsPduEnvelope> reader)
    {
        _router = router;
        _id = id;
        Reader = reader;
    }

    public ChannelReader<MmsPduEnvelope> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _router.RemoveInformationReportSubscription(_id);

        return ValueTask.CompletedTask;
    }
}
