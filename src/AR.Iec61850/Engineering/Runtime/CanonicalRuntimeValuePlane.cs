using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Engineering.Runtime;

[Flags]
public enum CanonicalRuntimeValueFields : int
{
    None = 0,
    Value = 1 << 0,
    Quality = 1 << 1,
    Timestamp = 1 << 2,
    Reason = 1 << 3
}

public sealed class CanonicalRuntimeValueUpdate
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool HasValue { get; init; }
    public bool HasQuality { get; init; }
    public bool HasTimestamp { get; init; }
    public bool HasReason { get; init; }

    public CanonicalRuntimeValueFields Fields =>
        (HasValue ? CanonicalRuntimeValueFields.Value : 0) |
        (HasQuality ? CanonicalRuntimeValueFields.Quality : 0) |
        (HasTimestamp ? CanonicalRuntimeValueFields.Timestamp : 0) |
        (HasReason ? CanonicalRuntimeValueFields.Reason : 0);
}

public readonly record struct CanonicalRuntimeValueState(
    int SignalId,
    CanonicalRuntimeValueFields Fields,
    string Value,
    string Quality,
    string Timestamp,
    string Reason,
    string Source,
    DateTimeOffset UpdatedAtUtc,
    long Version)
{
    public bool HasValue => (Fields & CanonicalRuntimeValueFields.Value) != 0;
    public bool HasQuality => (Fields & CanonicalRuntimeValueFields.Quality) != 0;
    public bool HasTimestamp => (Fields & CanonicalRuntimeValueFields.Timestamp) != 0;
    public bool HasReason => (Fields & CanonicalRuntimeValueFields.Reason) != 0;
}

public enum CanonicalRuntimeApplyStatus
{
    Applied,
    AppliedToCompanionChildren,
    UnresolvedReference,
    InvalidUpdate,
    FanoutLimitExceeded
}

public sealed class CanonicalRuntimeApplyResult
{
    public CanonicalRuntimeApplyStatus Status { get; init; }
    public int AppliedSignalCount { get; init; }
    public IReadOnlyList<int> SignalIds { get; init; } = Array.Empty<int>();
    public string Message { get; init; } = string.Empty;
    public bool IsApplied => Status is CanonicalRuntimeApplyStatus.Applied or CanonicalRuntimeApplyStatus.AppliedToCompanionChildren;
}

public readonly record struct CanonicalRuntimeSignalProjection(
    int SignalId,
    int DataObjectId,
    string Reference,
    string AttributePath,
    string FunctionalConstraint,
    string BasicType,
    string MmsType,
    string MmsTypeSignature,
    string Cdc,
    CanonicalProvenance Provenance,
    CanonicalRuntimeValueFields RuntimeFields,
    string Value,
    string Quality,
    string Timestamp,
    string Reason,
    string Source,
    DateTimeOffset UpdatedAtUtc,
    long ValueVersion)
{
    public bool HasValue => (RuntimeFields & CanonicalRuntimeValueFields.Value) != 0;
    public bool HasQuality => (RuntimeFields & CanonicalRuntimeValueFields.Quality) != 0;
    public bool HasTimestamp => (RuntimeFields & CanonicalRuntimeValueFields.Timestamp) != 0;
    public bool HasReason => (RuntimeFields & CanonicalRuntimeValueFields.Reason) != 0;
}

public sealed class CanonicalRuntimeQueryResult
{
    public long ModelGeneration { get; init; }
    public long ValueGeneration { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
    public IReadOnlyList<CanonicalRuntimeSignalProjection> Rows { get; init; } = Array.Empty<CanonicalRuntimeSignalProjection>();
}

/// <summary>
/// Mutable runtime evidence over one immutable canonical model generation. High-cardinality
/// state is stored only in primitive arrays indexed by dense canonical SignalId values.
/// </summary>
public sealed class CanonicalRuntimeValuePlane
{
    public const int MaximumCompanionFanout = 256;
    private const int StripeCount = 256;

    private readonly CanonicalPublishedSnapshot _snapshot;
    private readonly RuntimeStringPool _strings = new();
    private readonly object[] _stripes = Enumerable.Range(0, StripeCount).Select(_ => new object()).ToArray();
    private readonly int[] _valueIds;
    private readonly int[] _qualityIds;
    private readonly int[] _timestampIds;
    private readonly int[] _reasonIds;
    private readonly int[] _sourceIds;
    private readonly int[] _fields;
    private readonly long[] _updatedAtUtcTicks;
    private readonly long[] _versions;
    private long _generation;

    public CanonicalRuntimeValuePlane(CanonicalPublishedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        var count = snapshot.Model.Signals.Length;
        for (var i = 0; i < count; i++)
        {
            if (snapshot.Model.Signals[i].Id != i)
                throw new InvalidOperationException($"Canonical runtime value plane requires dense SignalId ordinals; row {i} has SignalId={snapshot.Model.Signals[i].Id}.");
        }

        _valueIds = new int[count];
        _qualityIds = new int[count];
        _timestampIds = new int[count];
        _reasonIds = new int[count];
        _sourceIds = new int[count];
        _fields = new int[count];
        _updatedAtUtcTicks = new long[count];
        _versions = new long[count];
    }

    public CanonicalPublishedSnapshot ModelSnapshot => _snapshot;
    public long ModelGeneration => _snapshot.Generation;
    public long ValueGeneration => Volatile.Read(ref _generation);
    public int SignalCount => _snapshot.Model.Signals.Length;
    public int RuntimeStringCount => _strings.Count;
    public long ApproximateBackingBytes => (long)SignalCount * ((6 * sizeof(int)) + (2 * sizeof(long)));

    public CanonicalRuntimeApplyResult Apply(CanonicalRuntimeValueUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var reference = update.Reference.Trim();
        var fc = update.FunctionalConstraint.Trim();
        if (reference.Length == 0 || update.Fields == CanonicalRuntimeValueFields.None)
            return Result(CanonicalRuntimeApplyStatus.InvalidUpdate, "Runtime update requires a reference and at least one runtime field.");

        var exactId = ResolveExact(reference, fc);
        if (exactId >= 0)
        {
            ApplySignal(exactId, update);
            return Result(CanonicalRuntimeApplyStatus.Applied, $"Applied runtime update to SignalId={exactId}.", [exactId]);
        }

        if (!update.HasValue && (update.HasQuality || update.HasTimestamp || update.HasReason))
        {
            var targets = ResolveCompanionTargets(reference, fc, out var exceeded);
            if (exceeded)
                return Result(CanonicalRuntimeApplyStatus.FanoutLimitExceeded, $"Companion update '{reference}' exceeds fan-out limit {MaximumCompanionFanout}.");
            if (targets.Length > 0)
            {
                foreach (var id in targets)
                    ApplySignal(id, update);
                return Result(CanonicalRuntimeApplyStatus.AppliedToCompanionChildren, $"Applied companion runtime evidence to {targets.Length} child signal(s).", targets);
            }
        }

        return Result(CanonicalRuntimeApplyStatus.UnresolvedReference, $"Runtime reference '{reference}' [{fc}] does not resolve in canonical model generation {ModelGeneration}.");
    }

    public CanonicalRuntimeValueState Read(int signalId)
    {
        ValidateSignalId(signalId);
        lock (_stripes[signalId & (StripeCount - 1)])
        {
            return new CanonicalRuntimeValueState(
                signalId,
                (CanonicalRuntimeValueFields)_fields[signalId],
                _strings.Resolve(_valueIds[signalId]),
                _strings.Resolve(_qualityIds[signalId]),
                _strings.Resolve(_timestampIds[signalId]),
                _strings.Resolve(_reasonIds[signalId]),
                _strings.Resolve(_sourceIds[signalId]),
                _updatedAtUtcTicks[signalId] == 0 ? default : new DateTimeOffset(_updatedAtUtcTicks[signalId], TimeSpan.Zero),
                _versions[signalId]);
        }
    }

    public CanonicalRuntimeQueryResult Query(CanonicalSignalQuery? query = null)
    {
        if (_snapshot.QueryIndex is null)
            throw new InvalidOperationException("Canonical runtime queries require a canonical signal query index.");

        var metadata = _snapshot.QueryIndex.Query(query, ModelGeneration);
        var rows = metadata.Rows.Select(meta =>
        {
            var runtime = Read(meta.SignalId);
            return new CanonicalRuntimeSignalProjection(
                meta.SignalId,
                meta.DataObjectId,
                meta.Reference,
                meta.AttributePath,
                meta.FunctionalConstraint,
                meta.BasicType,
                meta.MmsType,
                meta.MmsTypeSignature,
                meta.Cdc,
                meta.Provenance,
                runtime.Fields,
                runtime.Value,
                runtime.Quality,
                runtime.Timestamp,
                runtime.Reason,
                runtime.Source,
                runtime.UpdatedAtUtc,
                runtime.Version);
        }).ToArray();

        return new CanonicalRuntimeQueryResult
        {
            ModelGeneration = ModelGeneration,
            ValueGeneration = ValueGeneration,
            Offset = metadata.Offset,
            Limit = metadata.Limit,
            HasMore = metadata.HasMore,
            Rows = rows
        };
    }

    private void ApplySignal(int signalId, CanonicalRuntimeValueUpdate update)
    {
        ValidateSignalId(signalId);
        lock (_stripes[signalId & (StripeCount - 1)])
        {
            if (update.HasValue)
                _valueIds[signalId] = _strings.Intern(update.Value);
            if (update.HasQuality)
                _qualityIds[signalId] = _strings.Intern(update.Quality);
            if (update.HasTimestamp)
                _timestampIds[signalId] = _strings.Intern(update.Timestamp);
            if (update.HasReason)
                _reasonIds[signalId] = _strings.Intern(update.Reason);

            _sourceIds[signalId] = _strings.Intern(update.Source);
            _fields[signalId] |= (int)update.Fields;
            var time = update.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : update.UpdatedAtUtc.ToUniversalTime();
            _updatedAtUtcTicks[signalId] = time.UtcDateTime.Ticks;
            _versions[signalId] = Interlocked.Increment(ref _generation);
        }
    }

    private int ResolveExact(string reference, string fc)
    {
        if (_snapshot.QueryIndex is null)
            return -1;
        var page = _snapshot.QueryIndex.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = reference,
            FunctionalConstraint = fc,
            Limit = 16
        }, ModelGeneration);
        return page.Rows
            .Where(row => string.Equals(row.Reference, reference, StringComparison.Ordinal))
            .Where(row => fc.Length == 0 || string.Equals(row.FunctionalConstraint, fc, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.SignalId)
            .DefaultIfEmpty(-1)
            .First();
    }

    private int[] ResolveCompanionTargets(string reference, string fc, out bool exceeded)
    {
        exceeded = false;
        if (_snapshot.QueryIndex is null)
            return Array.Empty<int>();

        var prefix = reference.EndsWith(".", StringComparison.Ordinal) ? reference : reference + ".";
        var page = _snapshot.QueryIndex.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = prefix,
            FunctionalConstraint = fc,
            Limit = MaximumCompanionFanout + 1
        }, ModelGeneration);
        var ids = page.Rows
            .Where(row => !IsQualityOrTimestamp(row.AttributePath))
            .Select(row => row.SignalId)
            .Distinct()
            .ToArray();
        exceeded = page.HasMore || ids.Length > MaximumCompanionFanout;
        return exceeded ? Array.Empty<int>() : ids;
    }

    private static bool IsQualityOrTimestamp(string path)
    {
        path = path.Trim();
        return string.Equals(path, "q", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, "t", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".t", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateSignalId(int signalId)
    {
        if ((uint)signalId >= (uint)SignalCount)
            throw new ArgumentOutOfRangeException(nameof(signalId));
    }

    private static CanonicalRuntimeApplyResult Result(CanonicalRuntimeApplyStatus status, string message, IReadOnlyList<int>? ids = null)
        => new()
        {
            Status = status,
            Message = message,
            SignalIds = ids ?? Array.Empty<int>(),
            AppliedSignalCount = ids?.Count ?? 0
        };

    private sealed class RuntimeStringPool
    {
        private readonly object _sync = new();
        private readonly List<string> _values = [string.Empty];
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal) { [string.Empty] = 0 };

        public int Count { get { lock (_sync) return _values.Count; } }

        public int Intern(string? value)
        {
            value ??= string.Empty;
            lock (_sync)
            {
                if (_ids.TryGetValue(value, out var id))
                    return id;
                id = _values.Count;
                _values.Add(value);
                _ids.Add(value, id);
                return id;
            }
        }

        public string Resolve(int id)
        {
            lock (_sync)
                return (uint)id < (uint)_values.Count ? _values[id] : string.Empty;
        }
    }
}
