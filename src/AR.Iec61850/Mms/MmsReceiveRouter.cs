using System.Threading.Channels;

namespace AR.Iec61850.Mms;

public enum MmsReceiveRouteAction
{
    QueuedConfirmedResult,
    QueuedInformationReport,
    QueuedUnconfirmed,
    QueuedUnmatched
}

public sealed class MmsReceiveRouteResult
{
    public MmsReceiveRouteAction Action { get; init; }
    public MmsPduEnvelope Envelope { get; init; } = new();
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReceiveRouter
{
    private readonly object _sync = new();
    private readonly Dictionary<int, Queue<MmsPduEnvelope>> _confirmedByInvoke = new();
    private readonly Queue<MmsPduEnvelope> _informationReports = new();
    private readonly Queue<MmsPduEnvelope> _unconfirmed = new();
    private readonly Queue<MmsPduEnvelope> _unmatched = new();
    private readonly Dictionary<Guid, Channel<MmsPduEnvelope>> _informationReportSubscriptions = new();

    public int QueuedConfirmedResultCount
    {
        get
        {
            lock (_sync)
                return _confirmedByInvoke.Values.Sum(x => x.Count);
        }
    }

    public int QueuedInformationReportCount
    {
        get
        {
            lock (_sync)
                return _informationReports.Count;
        }
    }

    public int QueuedUnconfirmedCount
    {
        get
        {
            lock (_sync)
                return _unconfirmed.Count;
        }
    }

    public int QueuedUnmatchedCount
    {
        get
        {
            lock (_sync)
                return _unmatched.Count;
        }
    }

    public MmsReceiveRouteResult Route(ReadOnlyMemory<byte> presentationPayload)
    {
        var envelope = MmsPduEnvelope.Decode(presentationPayload);

        lock (_sync)
        {
            if (envelope.IsConfirmedServiceResult && envelope.InvokeId.HasValue)
            {
                if (!_confirmedByInvoke.TryGetValue(envelope.InvokeId.Value, out var queue))
                {
                    queue = new Queue<MmsPduEnvelope>();
                    _confirmedByInvoke.Add(envelope.InvokeId.Value, queue);
                }

                queue.Enqueue(envelope);
                return new MmsReceiveRouteResult
                {
                    Action = MmsReceiveRouteAction.QueuedConfirmedResult,
                    Envelope = envelope,
                    Message = $"Queued {envelope.Kind} for invokeID={envelope.InvokeId.Value}."
                };
            }

            if (envelope.IsInformationReport)
            {
                _informationReports.Enqueue(envelope);
                foreach (var subscription in _informationReportSubscriptions.Values)
                    subscription.Writer.TryWrite(envelope);

                return new MmsReceiveRouteResult
                {
                    Action = MmsReceiveRouteAction.QueuedInformationReport,
                    Envelope = envelope,
                    Message = $"Queued MMS InformationReport for {_informationReportSubscriptions.Count} subscriber(s)."
                };
            }

            if (envelope.Kind == MmsPduKind.Unconfirmed)
            {
                _unconfirmed.Enqueue(envelope);
                return new MmsReceiveRouteResult
                {
                    Action = MmsReceiveRouteAction.QueuedUnconfirmed,
                    Envelope = envelope,
                    Message = "Queued MMS unconfirmed PDU."
                };
            }

            _unmatched.Enqueue(envelope);
            return new MmsReceiveRouteResult
            {
                Action = MmsReceiveRouteAction.QueuedUnmatched,
                Envelope = envelope,
                Message = $"Queued unmatched MMS PDU kind={envelope.Kind}."
            };
        }
    }

    public bool TryDequeueConfirmedResult(int invokeId, out MmsPduEnvelope envelope)
    {
        lock (_sync)
        {
            if (_confirmedByInvoke.TryGetValue(invokeId, out var queue) && queue.Count > 0)
            {
                envelope = queue.Dequeue();
                if (queue.Count == 0)
                    _confirmedByInvoke.Remove(invokeId);
                return true;
            }
        }

        envelope = new MmsPduEnvelope();
        return false;
    }

    public bool TryDequeueInformationReport(out MmsPduEnvelope envelope)
    {
        lock (_sync)
        {
            if (_informationReports.Count > 0)
            {
                envelope = _informationReports.Dequeue();
                return true;
            }
        }

        envelope = new MmsPduEnvelope();
        return false;
    }

    internal MmsInformationReportSubscription SubscribeInformationReports(int capacity = 32)
    {
        var boundedCapacity = Math.Clamp(capacity, 1, 1024);
        var channel = Channel.CreateBounded<MmsPduEnvelope>(new BoundedChannelOptions(boundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var id = Guid.NewGuid();
        lock (_sync)
            _informationReportSubscriptions.Add(id, channel);

        return new MmsInformationReportSubscription(this, id, channel.Reader);
    }

    internal void RemoveInformationReportSubscription(Guid id)
    {
        Channel<MmsPduEnvelope>? channel = null;
        lock (_sync)
        {
            if (_informationReportSubscriptions.Remove(id, out var removed))
                channel = removed;
        }

        channel?.Writer.TryComplete();
    }

    internal void FaultInformationReportSubscriptions(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Channel<MmsPduEnvelope>[] subscriptions;
        lock (_sync)
        {
            subscriptions = _informationReportSubscriptions.Values.ToArray();
            _informationReportSubscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
            subscription.Writer.TryComplete(exception);
    }

    public void Clear()
    {
        Channel<MmsPduEnvelope>[] subscriptions;
        lock (_sync)
        {
            _confirmedByInvoke.Clear();
            _informationReports.Clear();
            _unconfirmed.Clear();
            _unmatched.Clear();
            subscriptions = _informationReportSubscriptions.Values.ToArray();
            _informationReportSubscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
            subscription.Writer.TryComplete(new IOException("MMS association receive routing was reset."));
    }
}
