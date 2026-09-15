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

    public CanonicalRuntimeValueFields Fields
    {
        get
        {
            var fields = CanonicalRuntimeValueFields.None;
            if (HasValue)
                fields |= CanonicalRuntimeValueFields.Value;
            if (HasQuality)
                fields |= CanonicalRuntimeValueFields.Quality;
            if (HasTimestamp)
                fields |= CanonicalRuntimeValueFields.Timestamp;
            if (HasReason)
                fields |= CanonicalRuntimeValueFields.Reason;
            return fields;
        }
    }
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
    public bool HasAnyRuntimeValue => Fields != CanonicalRuntimeValueFields.None;
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
/// Dense live-value overlay for one immutable canonical model generation. The topology,
/// types and signal identity stay in CanonicalIedModel; runtime value/quality/timestamp/
/// reason state is stored in primitive arrays indexed by canonical SignalId.
/// </summary>
public sealed class CanonicalRuntimeValuePlane
{
    public const int MaximumCompanionFanout = 256;
    private const int StripeCount = 256;

    private readonly CanonicalPublishedSnapshot _snapshot;
    private readonly RuntimeStringPool _strings = new();
    private readonly object[] _stripes;
    private readonly int[] _valueIds;
    private readonly int[] _qualityIds;
    private readonly int[] _timestampIds;
    private readonly int[] _reasonIds;
    private readonly int[] _sourceIds;
    private readonly int[] _fields;
    private readonly long[] _updatedAtUtcTicks;
    private readonly long[] _versions;
    private long _valueGeneration;

    public CanonicalRuntimeValuePlane(CanonicalPublishedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;

        var count = snapshot.Model.Signals.Length;
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            if (snapshot.Model.Signals[ordinal].Id != ordinal)
            {
                throw new InvalidOperationException(
                    $"Canonical runtime value plane requires dense SignalId ordinals; row {ordinal} has SignalId={snapshot.Model.Signals[ordinal].Id}.");
            }
        }

        _valueIds = new int[count];
        _qualityIds = new int[count];
        _timestampIds = new int[count];
        _reasonIds = new int[count];
        _sourceIds = new int[count];
        _fields = new int[count];
        _updatedAtUtcTicks = new long[count];
        _versions = new long[count];
        _stripes = Enumerable.Range(0, StripeCount).Select(_ => new object()).ToArray();
    }

    public CanonicalPublishedSnapshot ModelSnapshot => _snapshot;
    public long ModelGeneration => _snapshot.Generation;
    public long ValueGeneration => Volatile.Read(ref _valueGeneration);
    public int SignalCount => _snapshot.Model.Signals.Length;
    public int RuntimeStringCount => _strings.Count;

    /// <summary>
    /// Primitive-array payload only. This deliberately excludes the canonical model,
    /// runtime string pool objects and lock objects so scale tests can assert the fixed
    /// per-signal overlay cost deterministically.
    /// </summary>
    public long ApproximateBackingBytes => (long)SignalCount *
        ((6 * sizeof(int)) + (2 * sizeof(long)));

    public CanonicalRuntimeApplyResult Apply(CanonicalRuntimeValueUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var reference = (update.Reference ?? string.Empty).Trim();
        var fc = (update.FunctionalConstraint ?? string.Empty).Trim();
        if (reference.Length == 0 || update.Fields == CanonicalRuntimeValueFields.None)
        {
            return new CanonicalRuntimeApplyResult
            {
                Status = CanonicalRuntimeApplyStatus.InvalidUpdate,
                Message = "Runtime value update requires a reference and at least one value/quality/timestamp/reason field."
            };
        }

        var exactSignalId = ResolveExactSignalId(reference, fc);
        if (exactSignalId >= 0)
        {
            ApplySignalId(exactSignalId, update);
            return new CanonicalRuntimeApplyResult
            {
                Status = CanonicalRuntimeApplyStatus.Applied,
                AppliedSignalCount = 1,
                SignalIds = [exactSignalId],
                Message = $"Applied runtime update to SignalId={exactSignalId}."
            };
        }

        // Report projectors may legitimately emit q/t-only companion updates at the
        // DataObject reference rather than at one value leaf. If no exact signal exists,
        // enrich existing value-bearing children without inventing a new signal identity.
        if (!update.HasValue && (update.HasQuality || update.HasTimestamp || update.HasReason))
        {
            var companionTargets = ResolveCompanionTargets(reference, fc, out var exceeded);
            if (exceeded)
            {
                return new CanonicalRuntimeApplyResult
                {
                    Status = CanonicalRuntimeApplyStatus.FanoutLimitExceeded,
                    Message = $"Companion update '{reference}' exceeds the bounded fan-out limit of {MaximumCompanionFanout}; no runtime state was changed."
                };
            }

            if (companionTargets.Length > 0)
            {
                foreach (var signalId in companionTargets)
                    ApplySignalId(signalId, update);

                return new CanonicalRuntimeApplyResult
                {
                    Status = CanonicalRuntimeApplyStatus.AppliedToCompanionChildren,
                    AppliedSignalCount = companionTargets.Length,
                    SignalIds = companionTargets,
                    Message = $"Applied companion-only runtime update to {companionTargets.Length} canonical child signal(s)."
                };
            }
        }

        return new CanonicalRuntimeApplyResult
        {
            Status = CanonicalRuntimeApplyStatus.UnresolvedReference,
            Message = $"Runtime reference '{reference}' [{fc}] does not resolve to the bound canonical model generation {_snapshot.Generation}."
        };
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
                _updatedAtUtcTicks[signalId] == 0
                    ? default
                    : new DateTimeOffset(_updatedAtUtcTicks[signalId], TimeSpan.Zero),
                _versions[signalId]);
        }
    }

    public CanonicalRuntimeQueryResult Query(CanonicalSignalQuery? query = null)
    {
        var metadata = _snapshot.QueryIndex.Query(query, _snapshot.Generation);
        var valueGeneration = ValueGeneration;
        var rows = new CanonicalRuntimeSignalProjection[metadata.Rows.Count];
        for (var index = 0; index < metadata.Rows.Count; index++)
        {
            var signal = metadata.Rows[index];
            var runtime = Read(signal.SignalId);
            rows[index] = new CanonicalRuntimeSignalProjection(
                signal.SignalId,
                signal.DataObjectId,
                signal.Reference,
                signal.AttributePath,
                signal.FunctionalConstraint,
                signal.BasicType,
                signal.MmsType,
                signal.MmsTypeSignature,
                signal.Cdc,
                signal.Provenance,
                runtime.Fields,
                runtime.Value,
                runtime.Quality,
                runtime.Timestamp,
                runtime.Reason,
                runtime.Source,
                runtime.UpdatedAtUtc,
                runtime.Version);
        }

        return new CanonicalRuntimeQueryResult
        {
            ModelGeneration = _snapshot.Generation,
            ValueGeneration = valueGeneration,
            Offset = metadata.Offset,
            Limit = metadata.Limit,
            HasMore = metadata.HasMore,
            Rows = rows
        };
    }

    private void ApplySignalId(int signalId, CanonicalRuntimeValueUpdate update)
    {
        ValidateSignalId(signalId);
        var stripe = _stripes[signalId & (StripeCount - 1)];
        lock (stripe)
        {
            var fields = update.Fields;
            if ((fields & CanonicalRuntimeValueFields.Value) != 0)
                _valueIds[signalId] = _strings.Intern(update.Value);
            if ((fields & CanonicalRuntimeValueFields.Quality) != 0)
                _qualityIds[signalId] = _strings.Intern(update.Quality);
            if ((fields & CanonicalRuntimeValueFields.Timestamp) != 0)
                _timestampIds[signalId] = _strings.Intern(update.Timestamp);
            if ((fields & CanonicalRuntimeValueFields.Reason) != 0)
                _reasonIds[signalId] = _strings.Intern(update.Reason);

            _sourceIds[signalId] = _strings.Intern(update.Source);
            _fields[signalId] |= (int)fields;
            var updatedAt = update.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : update.UpdatedAtUtc.ToUniversalTime();
            _updatedAtUtcTicks[signalId] = updatedAt.UtcDateTime.Ticks;
            var version = Interlocked.Increment(ref _valueGeneration);
            _versions[signalId] = version;
        }
    }

    private int ResolveExactSignalId(string reference, string functionalConstraint)
    {
        var result = _snapshot.QueryIndex.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = reference,
            FunctionalConstraint = functionalConstraint,
            Limit = 16
        }, _snapshot.Generation);

        foreach (var row in result.Rows)
        {
            if (string.Equals(row.Reference, reference, StringComparison.Ordinal) &&
                (functionalConstraint.Length == 0 || string.Equals(row.FunctionalConstraint, functionalConstraint, StringComparison.OrdinalIgnoreCase)))
            {
                return row.SignalId;
            }
        }

        return -1;
    }

    private int[] ResolveCompanionTargets(string reference, string functionalConstraint, out bool exceeded)
    {
        var prefix = reference.EndsWith('.', StringComparison.Ordinal) ? reference : reference + ".";
        var result = _snapshot.QueryIndex.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = prefix,
            FunctionalConstraint = functionalConstraint,
            Limit = MaximumCompanionFanout + 1
        }, _snapshot.Generation);

        var ids = result.Rows
            .Where(row => !IsQualityOrTimestampCarrier(row.AttributePath))
            .Select(row => row.SignalId)
            .Distinct()
            .ToArray();

        exceeded = result.HasMore || ids.Length > MaximumCompanionFanout;
        return exceeded ? Array.Empty<int>() : ids;
    }

    private static bool IsQualityOrTimestampCarrier(string attributePath)
    {
        var path = (attributePath ?? string.Empty).Trim();
        return string.Equals(path, "q", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, "t", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".t", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateSignalId(int signalId)
    {
        if ((uint)signalId >= (uint)SignalCount)
            throw new ArgumentOutOfRangeException(nameof(signalId), signalId, "SignalId is outside the bound canonical model.");
    }

    private sealed class RuntimeStringPool
    {
        private readonly object _sync = new();
        private readonly List<string> _values = [string.Empty];
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal)
        {
            [string.Empty] = 0
        };

        public int Count
        {
            get
            {
                lock (_sync)
                    return _values.Count;
            }
        }

        public int Intern(string? value)
        {
            var normalized = value ?? string.Empty;
            lock (_sync)
            {
                if (_ids.TryGetValue(normalized, out var existing))
                    return existing;

                var id = _values.Count;
                _values.Add(normalized);
                _ids.Add(normalized, id);
                return id;
            }
        }

        public string Resolve(int id)
        {
            lock (_sync)
                return id >= 0 && id < _values.Count ? _values[id] : string.Empty;
        }
    }
}
