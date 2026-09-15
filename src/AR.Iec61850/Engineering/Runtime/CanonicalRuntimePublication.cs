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
    private readonly LatestOnlyWorker<CanonicalIedModel> _worker;
    private readonly object _publishSync = new();
    private CanonicalRuntimePublishedSnapshot? _current;
    private long _generation;

    public CanonicalRuntimeSnapshotPublisher(
        TimeSpan? processingTimeout = null,
        Action<Exception>? onFault = null)
    {
        _worker = new LatestOnlyWorker<CanonicalIedModel>(
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
        // or poll result to bind to stale SignalIds. Once the replacement is queued,
        // consumers therefore observe either no runtime snapshot or the new generation;
        // they never observe the superseded generation as an eligible update target.
        lock (_publishSync)
        {
            var previous = Volatile.Read(ref _current);
            Volatile.Write(ref _current, null);
            if (_worker.TryPublish(model))
                return true;

            // Publication was rejected because the worker is stopping. Restore the last
            // valid generation because no replacement will be produced.
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

    private ValueTask PublishCoreAsync(CanonicalIedModel model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compacted = CanonicalModelMemoryCompactor.Compact(model);
        var index = CanonicalSignalQueryIndex.Build(compacted, cancellationToken);
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
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Producer-side ingress for the combined P1 model/value publication boundary.
/// </summary>
public static class CanonicalRuntimeIngressPublication
{
    public static bool TryPublishLiveDiscovery(
        CanonicalRuntimeSnapshotPublisher publisher,
        LiveIedModelDiscoveryDocument discovery)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(discovery);
        return publisher.TryPublish(CanonicalLiveModelAdapter.FromLiveDiscovery(discovery));
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
