namespace AR.Iec61850.Engineering.Runtime;

public readonly record struct LatestOnlyWorkerStatistics(
    long Published,
    long Processed,
    long Coalesced,
    long Faulted,
    long TimedOut,
    bool IsAccepting,
    bool HasPending);

/// <summary>
/// Single-consumer, latest-only background worker for expensive snapshot processing.
/// At most one item waits behind the currently executing handler. Publishing never waits
/// for the handler: a newer pending item replaces an older pending item and increments the
/// coalesced counter. This keeps producer latency and retained memory bounded under bursts.
/// </summary>
public sealed class LatestOnlyWorker<T> : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Func<T, CancellationToken, ValueTask> _handler;
    private readonly Action<Exception>? _onFault;
    private readonly TimeSpan _handlerTimeout;
    private readonly Task _workerTask;

    private T _pending = default!;
    private bool _hasPending;
    private bool _signalPending;
    private bool _accepting = true;
    private long _published;
    private long _processed;
    private long _coalesced;
    private long _faulted;
    private long _timedOut;
    private int _disposeStarted;

    public LatestOnlyWorker(
        Func<T, CancellationToken, ValueTask> handler,
        Action<Exception>? onFault = null)
        : this(handler, Timeout.InfiniteTimeSpan, onFault)
    {
    }

    /// <summary>
    /// Creates a latest-only worker with an optional cooperative execution deadline.
    /// Handlers must observe the supplied cancellation token for the deadline to bound
    /// execution. No thread abort or detached orphan task is used.
    /// </summary>
    public LatestOnlyWorker(
        Func<T, CancellationToken, ValueTask> handler,
        TimeSpan handlerTimeout,
        Action<Exception>? onFault = null)
    {
        if (handlerTimeout != Timeout.InfiniteTimeSpan && handlerTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(handlerTimeout), "Handler timeout must be positive or Timeout.InfiniteTimeSpan.");

        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _handlerTimeout = handlerTimeout;
        _onFault = onFault;
        _workerTask = Task.Run(RunAsync);
    }

    /// <summary>
    /// Publishes without blocking. Returns false after graceful shutdown has begun.
    /// If one item is already pending, it is released immediately and replaced by item.
    /// </summary>
    public bool TryPublish(T item)
    {
        var releaseSignal = false;
        lock (_gate)
        {
            if (!_accepting)
                return false;

            _published++;
            if (_hasPending)
            {
                _coalesced++;
                _pending = default!;
            }

            _pending = item;
            _hasPending = true;

            if (!_signalPending)
            {
                _signalPending = true;
                releaseSignal = true;
            }
        }

        if (releaseSignal)
            _signal.Release();

        return true;
    }

    public LatestOnlyWorkerStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new LatestOnlyWorkerStatistics(
                _published,
                _processed,
                _coalesced,
                _faulted,
                _timedOut,
                _accepting,
                _hasPending);
        }
    }

    /// <summary>
    /// Stops accepting new work, drains the latest pending item if one exists, and waits
    /// for the worker to finish. The caller's token bounds only the wait; it does not turn
    /// graceful shutdown into an implicit destructive cancellation of the active handler.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        RequestStop();
        await _workerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            await _workerTask.ConfigureAwait(false);
            return;
        }

        RequestStop();
        await _workerTask.ConfigureAwait(false);
        _signal.Dispose();
    }

    private void RequestStop()
    {
        var releaseSignal = false;
        lock (_gate)
        {
            if (!_accepting)
                return;

            _accepting = false;
            if (!_signalPending)
            {
                _signalPending = true;
                releaseSignal = true;
            }
        }

        if (releaseSignal)
            _signal.Release();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await _signal.WaitAsync().ConfigureAwait(false);

            T item = default!;
            var hasItem = false;
            lock (_gate)
            {
                _signalPending = false;
                if (_hasPending)
                {
                    item = _pending;
                    _pending = default!;
                    _hasPending = false;
                    hasItem = true;
                }
                else if (!_accepting)
                {
                    return;
                }
            }

            if (!hasItem)
                continue;

            CancellationTokenSource? handlerDeadline = null;
            try
            {
                var handlerToken = CancellationToken.None;
                if (_handlerTimeout != Timeout.InfiniteTimeSpan)
                {
                    handlerDeadline = new CancellationTokenSource(_handlerTimeout);
                    handlerToken = handlerDeadline.Token;
                }

                await _handler(item, handlerToken).ConfigureAwait(false);
                lock (_gate)
                    _processed++;
            }
            catch (OperationCanceledException) when (handlerDeadline?.IsCancellationRequested == true)
            {
                var timeout = new TimeoutException($"Latest-only worker handler exceeded the cooperative deadline of {_handlerTimeout.TotalMilliseconds:0} ms.");
                lock (_gate)
                    _timedOut++;
                NotifyFault(timeout);
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _faulted++;
                NotifyFault(ex);
            }
            finally
            {
                handlerDeadline?.Dispose();

                // Drop the local reference before the next wait so a large processed
                // snapshot cannot be retained solely by this worker frame.
                item = default!;
            }

            lock (_gate)
            {
                if (!_accepting && !_hasPending)
                    return;
            }
        }
    }

    private void NotifyFault(Exception exception)
    {
        if (_onFault is null)
            return;

        try
        {
            _onFault(exception);
        }
        catch
        {
            // Diagnostic callbacks must never terminate the worker.
        }
    }
}
