using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Engineering.Runtime;

public sealed class CanonicalRuntimePublishedSnapshot
{
    public CanonicalPublishedSnapshot ModelSnapshot { get; init; } = new();
    public CanonicalRuntimeValuePlane Values { get; init; } = null!;
    public long ModelGeneration => ModelSnapshot.Generation;
    public DateTimeOffset PublishedAtUtc => ModelSnapshot.PublishedAtUtc;
}

public interface ICanonicalRuntimeSource
{
    CanonicalRuntimePublishedSnapshot? Current { get; }
    CanonicalRuntimeQueryResult Query(CanonicalSignalQuery? query = null);
}

/// <summary>
/// P1 publication boundary. Canonical model compaction, query indexing and runtime-plane
/// allocation happen on the bounded latest-only worker. Consumers see one atomically
/// published model/value pair and never have to bind a value plane on the UI thread.
/// </summary>
public sealed class CanonicalRuntimeSnapshotPublisher : ICanonicalRuntimeSource, IAsyncDisposable
{
    private sealed record PublicationRequest(long Version, CanonicalIedModel Model);

    private readonly LatestOnlyWorker<PublicationRequest> _worker;
    private readonly object _publishSync = new();
    private CanonicalRuntimePublishedSnapshot? _current;
    private long _generation;
    private long _latestAcceptedRequestVersion;

    public CanonicalRuntimeSnapshotPublisher(
        TimeSpan? processingTimeout = null,
        Action<Exception>? onFault = null)
    {
        _worker = new LatestOnlyWorker<PublicationRequest>(
            PublishCoreAsync,
            processingTimeout ?? Timeout.InfiniteTimeSpan,
            onFault);
    }

    public CanonicalRuntimePublishedSnapshot? Current => Volatile.Read(ref _current);
    public LatestOnlyWorkerStatistics Statistics => _worker.GetStatistics();

    public bool TryPublish(CanonicalIedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Fail closed while a replacement model generation is being built. Keeping the
        // previous value plane visible after accepting a new topology allows a fast report
        // or poll result to bind to stale SignalIds. A versioned request also prevents an
        // already-processing, now-superseded model from becoming visible after a newer
        // replacement has been accepted.
        lock (_publishSync)
        {
            var previous = Volatile.Read(ref _current);
            var previousRequestVersion = Volatile.Read(ref _latestAcceptedRequestVersion);
            var requestVersion = previousRequestVersion + 1;

            Volatile.Write(ref _latestAcceptedRequestVersion, requestVersion);
            Volatile.Write(ref _current, null);
            if (_worker.TryPublish(new PublicationRequest(requestVersion, model)))
                return true;

            // Publication was rejected because the worker is stopping. Restore the last
            // valid generation and request token because no replacement will be produced.
            Volatile.Write(ref _latestAcceptedRequestVersion, previousRequestVersion);
            Volatile.Write(ref _current, previous);
            return false;
        }
    }

    public CanonicalRuntimeQueryResult Query(CanonicalSignalQuery? query = null)
    {
        var current = Current;
        if (current is not null)
            return current.Values.Query(query);

        var limit = query is { Limit: > 0 } ? query.Limit : 500;
        return new CanonicalRuntimeQueryResult
        {
            Offset = Math.Max(0, query?.Offset ?? 0),
            Limit = Math.Clamp(limit, 1, CanonicalSignalQueryIndex.MaximumPageSize)
        };
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
        => _worker.StopAsync(cancellationToken);

    public ValueTask DisposeAsync()
        => _worker.DisposeAsync();

    private ValueTask PublishCoreAsync(PublicationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compacted = CanonicalModelMemoryCompactor.Compact(request.Model);
        var index = CanonicalSignalQueryIndex.Build(compacted, cancellationToken);

        lock (_publishSync)
        {
            // The latest-only worker may already be processing request N when request N+1
            // arrives. N is allowed to finish its expensive compaction/indexing work, but
            // it must not become an observable runtime generation after N+1 was accepted.
            if (request.Version != Volatile.Read(ref _latestAcceptedRequestVersion))
                return ValueTask.CompletedTask;

            var modelSnapshot = new CanonicalPublishedSnapshot
            {
                Generation = Interlocked.Increment(ref _generation),
                PublishedAtUtc = DateTimeOffset.UtcNow,
                Model = compacted,
                QueryIndex = index
            };
            var values = new CanonicalRuntimeValuePlane(modelSnapshot);
            Volatile.Write(ref _current, new CanonicalRuntimePublishedSnapshot
            {
                ModelSnapshot = modelSnapshot,
                Values = values
            });
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Producer-side ingress for the combined P1 model/value publication boundary.
/// </summary>
public static class CanonicalRuntimeIngressPublication
{
    private sealed class LiveIngressPendingState
    {
        public object Sync { get; } = new();
        public WeakReference<LiveIedModelDiscoveryDocument>? Document { get; set; }
    }

    private static readonly ConditionalWeakTable<CanonicalRuntimeSnapshotPublisher, LiveIngressPendingState> LiveIngressPending = new();

    public static bool TryPublishLiveDiscovery(
        CanonicalRuntimeSnapshotPublisher publisher,
        LiveIedModelDiscoveryDocument discovery)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(discovery);

        // Application readiness checks may run while a large canonical model is still
        // being compacted/indexed. Re-projecting the exact same discovery object on every
        // timer tick would continuously supersede that in-flight publication and could
        // starve a very large model. Coalesce only the same discovery object while Current
        // is intentionally null; a different discovery object is always a real replacement.
        var pending = LiveIngressPending.GetOrCreateValue(publisher);
        lock (pending.Sync)
        {
            if (publisher.Current is null &&
                pending.Document is not null &&
                pending.Document.TryGetTarget(out var existing) &&
                ReferenceEquals(existing, discovery))
            {
                return true;
            }

            if (publisher.Current is not null)
                pending.Document = null;

            pending.Document = new WeakReference<LiveIedModelDiscoveryDocument>(discovery);
            var accepted = publisher.TryPublish(CanonicalLiveModelAdapter.FromLiveDiscovery(discovery));
            if (!accepted)
                pending.Document = null;
            return accepted;
        }
    }

    public static bool TryPublishScl(
        CanonicalRuntimeSnapshotPublisher publisher,
        XDocument document,
        SclCanonicalImportOptions? options,
        out SclCanonicalImportResult import)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(document);
        import = SclCanonicalImporter.Import(document, options);
        return import.IsSuccess && import.Model is not null && publisher.TryPublish(import.Model);
    }

    public static bool TryPublishSclFile(
        CanonicalRuntimeSnapshotPublisher publisher,
        string filePath,
        SclCanonicalImportOptions? options,
        out SclCanonicalImportResult import)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        import = SclCanonicalImporter.Load(filePath, options);
        return import.IsSuccess && import.Model is not null && publisher.TryPublish(import.Model);
    }
}
