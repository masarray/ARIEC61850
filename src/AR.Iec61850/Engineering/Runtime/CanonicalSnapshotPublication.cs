using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Engineering.Runtime;

public sealed class CanonicalSignalQuery
{
    public string ReferencePrefix { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Limit { get; init; } = 500;
}

/// <summary>
/// A bounded consumer projection. All string values are references to the immutable
/// canonical string table; query execution does not manufacture a second model graph.
/// </summary>
public readonly record struct CanonicalSignalProjection(
    int SignalId,
    int DataObjectId,
    string Reference,
    string AttributePath,
    string FunctionalConstraint,
    string BasicType,
    string MmsType,
    string MmsTypeSignature,
    string Cdc,
    CanonicalProvenance Provenance);

public sealed class CanonicalSignalQueryResult
{
    public long Generation { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
    public IReadOnlyList<CanonicalSignalProjection> Rows { get; init; } = Array.Empty<CanonicalSignalProjection>();
}

/// <summary>
/// Immutable, allocation-bounded query index. It stores only signal ordinals (4 bytes
/// each) in reference sort order. Signal strings and semantics remain owned by the
/// canonical snapshot and are projected only for the requested page.
/// </summary>
public sealed class CanonicalSignalQueryIndex
{
    public const int MaximumPageSize = 10_000;

    private readonly CanonicalIedModel _model;
    private readonly int[] _signalOrdinalsByReference;

    private CanonicalSignalQueryIndex(CanonicalIedModel model, int[] signalOrdinalsByReference)
    {
        _model = model;
        _signalOrdinalsByReference = signalOrdinalsByReference;
    }

    public int SignalCount => _model.Signals.Length;
    public long ApproximateIndexBytes => (long)_signalOrdinalsByReference.Length * sizeof(int);

    public static CanonicalSignalQueryIndex Build(
        CanonicalIedModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var ordinals = new int[model.Signals.Length];
        for (var index = 0; index < ordinals.Length; index++)
            ordinals[index] = index;

        Array.Sort(ordinals, (left, right) =>
        {
            var leftReference = model.Strings.Resolve(model.Signals[left].ObjectReference);
            var rightReference = model.Strings.Resolve(model.Signals[right].ObjectReference);
            var compare = string.CompareOrdinal(leftReference, rightReference);
            return compare != 0 ? compare : left.CompareTo(right);
        });

        cancellationToken.ThrowIfCancellationRequested();
        return new CanonicalSignalQueryIndex(model, ordinals);
    }

    public CanonicalSignalQueryResult Query(CanonicalSignalQuery? query = null, long generation = 0)
    {
        query ??= new CanonicalSignalQuery();
        var prefix = query.ReferencePrefix?.Trim() ?? string.Empty;
        var functionalConstraint = query.FunctionalConstraint?.Trim() ?? string.Empty;
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit <= 0 ? 500 : query.Limit, 1, MaximumPageSize);
        var rows = new List<CanonicalSignalProjection>(Math.Min(limit, _model.Signals.Length));

        var ordinalIndex = prefix.Length == 0 ? 0 : LowerBound(prefix);
        var matched = 0;
        var hasMore = false;

        for (; ordinalIndex < _signalOrdinalsByReference.Length; ordinalIndex++)
        {
            var signalOrdinal = _signalOrdinalsByReference[ordinalIndex];
            var signal = _model.Signals[signalOrdinal];
            var reference = _model.Strings.Resolve(signal.ObjectReference);

            if (prefix.Length > 0 && !reference.StartsWith(prefix, StringComparison.Ordinal))
                break;

            var fc = _model.Strings.Resolve(signal.FunctionalConstraint);
            if (functionalConstraint.Length > 0 &&
                !string.Equals(fc, functionalConstraint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched++ < offset)
                continue;

            if (rows.Count >= limit)
            {
                hasMore = true;
                break;
            }

            rows.Add(Project(signal, reference, fc));
        }

        return new CanonicalSignalQueryResult
        {
            Generation = generation,
            Offset = offset,
            Limit = limit,
            HasMore = hasMore,
            Rows = rows.ToArray()
        };
    }

    private int LowerBound(string referencePrefix)
    {
        var low = 0;
        var high = _signalOrdinalsByReference.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var ordinal = _signalOrdinalsByReference[middle];
            var candidate = _model.Strings.Resolve(_model.Signals[ordinal].ObjectReference);
            if (string.CompareOrdinal(candidate, referencePrefix) < 0)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private CanonicalSignalProjection Project(
        CanonicalSignalRow signal,
        string reference,
        string functionalConstraint)
    {
        var cdc = string.Empty;
        if (signal.DataObjectId >= 0 && signal.DataObjectId < _model.DataObjects.Length)
            cdc = _model.Strings.Resolve(_model.DataObjects[signal.DataObjectId].Cdc);

        return new CanonicalSignalProjection(
            signal.Id,
            signal.DataObjectId,
            reference,
            _model.Strings.Resolve(signal.AttributePath),
            functionalConstraint,
            _model.Strings.Resolve(signal.BasicType),
            _model.Strings.Resolve(signal.MmsType),
            _model.Strings.Resolve(signal.MmsTypeSignature),
            cdc,
            signal.Provenance);
    }
}

public sealed class CanonicalPublishedSnapshot
{
    public long Generation { get; init; }
    public DateTimeOffset PublishedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public CanonicalIedModel Model { get; init; } = new();
    public CanonicalSignalQueryIndex QueryIndex { get; init; } = null!;
}

public interface ICanonicalSnapshotSource
{
    CanonicalPublishedSnapshot? Current { get; }
    CanonicalSignalQueryResult Query(CanonicalSignalQuery? query = null);
}

/// <summary>
/// Publication boundary for protocol/SCL producers. Publication is non-blocking and
/// latest-only: one model may be processed while at most one newer model waits. Before a
/// snapshot becomes visible it is metadata-compacted and indexed. UI/CLI/exporter code
/// reads only the immutable published snapshot; it never owns the producer queue.
/// </summary>
public sealed class CanonicalSnapshotPublisher : ICanonicalSnapshotSource, IAsyncDisposable
{
    private readonly LatestOnlyWorker<CanonicalIedModel> _worker;
    private CanonicalPublishedSnapshot? _current;
    private long _generation;

    public CanonicalSnapshotPublisher(
        TimeSpan? processingTimeout = null,
        Action<Exception>? onFault = null)
    {
        var timeout = processingTimeout ?? Timeout.InfiniteTimeSpan;
        _worker = new LatestOnlyWorker<CanonicalIedModel>(PublishCoreAsync, timeout, onFault);
    }

    public CanonicalPublishedSnapshot? Current => Volatile.Read(ref _current);

    public LatestOnlyWorkerStatistics Statistics => _worker.GetStatistics();

    public bool TryPublish(CanonicalIedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _worker.TryPublish(model);
    }

    public CanonicalSignalQueryResult Query(CanonicalSignalQuery? query = null)
    {
        var current = Current;
        return current is null
            ? new CanonicalSignalQueryResult
            {
                Offset = Math.Max(0, query?.Offset ?? 0),
                Limit = Math.Clamp(query?.Limit is > 0 ? query.Limit : 500, 1, CanonicalSignalQueryIndex.MaximumPageSize)
            }
            : current.QueryIndex.Query(query, current.Generation);
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
        var snapshot = new CanonicalPublishedSnapshot
        {
            Generation = Interlocked.Increment(ref _generation),
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Model = compacted,
            QueryIndex = index
        };

        Volatile.Write(ref _current, snapshot);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Shared consumer facade. UI and CLI get bounded pages; exporters use the same index via
/// explicit paging rather than materializing a second million-row object graph.
/// </summary>
public static class CanonicalConsumerProjection
{
    public static CanonicalSignalQueryResult ForUi(
        ICanonicalSnapshotSource source,
        string referencePrefix = "",
        string functionalConstraint = "",
        int offset = 0,
        int limit = 500)
        => Query(source, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, limit), 1_000));

    public static CanonicalSignalQueryResult ForCli(
        ICanonicalSnapshotSource source,
        string referencePrefix = "",
        string functionalConstraint = "",
        int offset = 0,
        int limit = 2_000)
        => Query(source, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, limit), 5_000));

    public static CanonicalSignalQueryResult ForExporter(
        ICanonicalSnapshotSource source,
        int offset,
        int pageSize = CanonicalSignalQueryIndex.MaximumPageSize,
        string referencePrefix = "",
        string functionalConstraint = "")
        => Query(source, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, pageSize), CanonicalSignalQueryIndex.MaximumPageSize));

    private static CanonicalSignalQueryResult Query(
        ICanonicalSnapshotSource source,
        string referencePrefix,
        string functionalConstraint,
        int offset,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = referencePrefix,
            FunctionalConstraint = functionalConstraint,
            Offset = offset,
            Limit = limit
        });
    }
}
