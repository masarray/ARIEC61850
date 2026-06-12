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
                return new MmsReceiveRouteResult
                {
                    Action = MmsReceiveRouteAction.QueuedInformationReport,
                    Envelope = envelope,
                    Message = "Queued MMS InformationReport."
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

    public void Clear()
    {
        lock (_sync)
        {
            _confirmedByInvoke.Clear();
            _informationReports.Clear();
            _unconfirmed.Clear();
            _unmatched.Clear();
        }
    }
}
