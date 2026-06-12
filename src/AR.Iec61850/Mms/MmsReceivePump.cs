namespace AR.Iec61850.Mms;

public sealed class MmsPendingConfirmedOperation : IDisposable
{
    private readonly MmsReceivePump _owner;
    private readonly TaskCompletionSource<MmsPduEnvelope> _completion;
    private bool _disposed;

    internal MmsPendingConfirmedOperation(
        MmsReceivePump owner,
        int invokeId,
        TaskCompletionSource<MmsPduEnvelope> completion)
    {
        _owner = owner;
        InvokeId = invokeId;
        _completion = completion;
    }

    public int InvokeId { get; }
    public Task<MmsPduEnvelope> Task => _completion.Task;

    public Task<MmsPduEnvelope> WaitAsync(CancellationToken cancellationToken = default)
        => _completion.Task.WaitAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _owner.RemovePendingOperation(InvokeId, _completion);
    }
}

public sealed class MmsReceivePump
{
    private readonly MmsReceiveRouter _router;
    private readonly Func<CancellationToken, Task<byte[]>> _receiveAsync;
    private readonly object _sync = new();
    private readonly Dictionary<int, TaskCompletionSource<MmsPduEnvelope>> _pending = new();
    private CancellationTokenSource? _stopSource;
    private Task? _runTask;

    public MmsReceivePump(MmsReceiveRouter router, Func<CancellationToken, Task<byte[]>> receiveAsync)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _receiveAsync = receiveAsync ?? throw new ArgumentNullException(nameof(receiveAsync));
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _runTask is { IsCompleted: false };
        }
    }

    public int PendingOperationCount
    {
        get
        {
            lock (_sync)
                return _pending.Count;
        }
    }

    public int RoutedPduCount { get; private set; }
    public int CompletedConfirmedCount { get; private set; }
    public int QueuedInformationReportCount => _router.QueuedInformationReportCount;
    public string LastRouteMessage { get; private set; } = string.Empty;
    public string LastFaultMessage { get; private set; } = string.Empty;

    public MmsPendingConfirmedOperation RegisterConfirmedOperation(int invokeId)
    {
        if (invokeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId), "MMS invokeID must be positive.");

        var completion = new TaskCompletionSource<MmsPduEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_router.TryDequeueConfirmedResult(invokeId, out var queued))
        {
            completion.TrySetResult(queued);
            return new MmsPendingConfirmedOperation(this, invokeId, completion);
        }

        lock (_sync)
        {
            if (_pending.ContainsKey(invokeId))
                throw new InvalidOperationException($"A confirmed MMS operation for invokeID={invokeId} is already pending.");

            _pending.Add(invokeId, completion);
        }

        return new MmsPendingConfirmedOperation(this, invokeId, completion);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
                return;

            LastFaultMessage = string.Empty;
            _stopSource?.Dispose();
            _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = Task.Run(() => RunAsync(_stopSource.Token), CancellationToken.None);
        }
    }

    public async ValueTask StopAsync()
    {
        Task? task;
        CancellationTokenSource? source;
        lock (_sync)
        {
            task = _runTask;
            source = _stopSource;
        }

        if (source != null && !source.IsCancellationRequested)
            source.Cancel();

        if (task != null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        CancelPendingOperations("MMS receive pump stopped.");

        lock (_sync)
        {
            if (ReferenceEquals(task, _runTask))
                _runTask = null;

            if (source is not null && ReferenceEquals(source, _stopSource))
            {
                source.Dispose();
                _stopSource = null;
            }
        }
    }

    internal void RemovePendingOperation(int invokeId, TaskCompletionSource<MmsPduEnvelope> completion)
    {
        lock (_sync)
        {
            if (_pending.TryGetValue(invokeId, out var current) && ReferenceEquals(current, completion))
                _pending.Remove(invokeId);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await _receiveAsync(cancellationToken).ConfigureAwait(false);
                var route = _router.Route(payload);
                RoutedPduCount++;
                LastRouteMessage = route.Message;

                if (route.Action == MmsReceiveRouteAction.QueuedConfirmedResult &&
                    route.Envelope.InvokeId.HasValue &&
                    TryCompletePendingOperation(route.Envelope.InvokeId.Value, route.Envelope))
                {
                    _router.TryDequeueConfirmedResult(route.Envelope.InvokeId.Value, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastFaultMessage = $"MMS receive pump fault: {ex.GetType().Name}: {ex.Message}";
            FaultPendingOperations(ex);
        }
    }

    private bool TryCompletePendingOperation(int invokeId, MmsPduEnvelope envelope)
    {
        TaskCompletionSource<MmsPduEnvelope>? completion;
        lock (_sync)
        {
            if (!_pending.Remove(invokeId, out completion))
                return false;
        }

        CompletedConfirmedCount++;
        completion.TrySetResult(envelope);
        return true;
    }

    private void FaultPendingOperations(Exception exception)
    {
        TaskCompletionSource<MmsPduEnvelope>[] pending;
        lock (_sync)
        {
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var completion in pending)
            completion.TrySetException(exception);
    }

    private void CancelPendingOperations(string message)
    {
        TaskCompletionSource<MmsPduEnvelope>[] pending;
        lock (_sync)
        {
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var completion in pending)
            completion.TrySetCanceled(new CancellationToken(canceled: true));

        if (pending.Length > 0)
            LastFaultMessage = message;
    }
}
