using System.Numerics;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Analysis;

public enum SvCaptureTimestampEvidence
{
    Unknown,
    HostSoftwareTimestamp,
    HardwareTimestamp
}

public sealed record SvSustainedAnalysisOptions
{
    public int MaximumStreams { get; init; } = 256;
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public ushort? SampleCounterWrap { get; init; }
    public SvCaptureTimestampEvidence TimestampEvidence { get; init; } = SvCaptureTimestampEvidence.HostSoftwareTimestamp;
    public double DropoutIntervalMultiplier { get; init; } = 2.5;

    public void Validate()
    {
        if (MaximumStreams < 1 || MaximumStreams > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaximumStreams));
        if (StreamIdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StreamIdleTimeout));
        if (DropoutIntervalMultiplier <= 1.0 || !double.IsFinite(DropoutIntervalMultiplier))
            throw new ArgumentOutOfRangeException(nameof(DropoutIntervalMultiplier));
    }
}

public sealed record SvContinuityEvidence
{
    public long ContinuousTransitions { get; init; }
    public long NormalWraps { get; init; }
    public long GapTransitions { get; init; }
    public long MissingSamples { get; init; }
    public long DuplicateTransitions { get; init; }
    public long OutOfOrderTransitions { get; init; }
}

public sealed record SvTimingEvidence
{
    public SvCaptureTimestampEvidence TimestampEvidence { get; init; }
    public long IntervalCount { get; init; }
    public double? MeanFrameIntervalMicroseconds { get; init; }
    public double? FrameRateHz { get; init; }
    public double? EstimatedSampleRateHz { get; init; }
    public double? IntervalJitterStdDevMicroseconds { get; init; }
    public double? MaximumAbsoluteJitterMicroseconds { get; init; }
    public long DropoutIntervals { get; init; }
    public bool IsStrongTimingEvidence => TimestampEvidence == SvCaptureTimestampEvidence.HardwareTimestamp;
    public string ClaimBoundary => IsStrongTimingEvidence
        ? "Timing statistics use caller-declared hardware timestamp evidence; clock accuracy and synchronization still require separate validation."
        : "Timing statistics are ordinary capture-arrival evidence only and must not be presented as process-bus time-accuracy or synchronization proof.";
}

public sealed record SvSynchronizationEvidence
{
    public long SampleSynchronization0 { get; init; }
    public long SampleSynchronization1 { get; init; }
    public long SampleSynchronization2 { get; init; }
    public long OtherSampleSynchronization { get; init; }
}

public sealed record SvSustainedStreamSnapshot
{
    public SvObservedStreamKey Key { get; init; } = new();
    public long FrameCount { get; init; }
    public long AsduCount { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public int StablePayloadBytesPerAsdu { get; init; }
    public bool PayloadLayoutChanged { get; init; }
    public SvContinuityEvidence Continuity { get; init; } = new();
    public SvTimingEvidence Timing { get; init; } = new();
    public SvSynchronizationEvidence Synchronization { get; init; } = new();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Bounded, sustained Sampled Values stream registry. It consumes already-decoded SV frames,
/// keeps incremental evidence only, and never retains packet payload history. This class is
/// deliberately separate from the wire parser and from application presentation state.
/// </summary>
public sealed class SvSustainedStreamAnalyzer
{
    private sealed class StreamState
    {
        private readonly SvSampleCounterTracker _counter = new();
        private long _intervalCount;
        private double _intervalMeanTicks;
        private double _intervalM2Ticks;
        private double _maxAbsoluteJitterTicks;
        private TimeSpan? _lastInterval;
        private int _firstPayloadLength;
        private readonly Queue<string> _diagnostics = new();

        public StreamState(SvObservedStreamKey key, DateTimeOffset timestamp)
        {
            Key = key;
            FirstSeen = timestamp;
            LastSeen = timestamp;
        }

        public SvObservedStreamKey Key { get; }
        public long FrameCount { get; private set; }
        public long AsduCount { get; private set; }
        public DateTimeOffset FirstSeen { get; }
        public DateTimeOffset LastSeen { get; private set; }
        public bool PayloadLayoutChanged { get; private set; }
        public long Continuous { get; private set; }
        public long Wraps { get; private set; }
        public long Gaps { get; private set; }
        public long Missing { get; private set; }
        public long Duplicates { get; private set; }
        public long OutOfOrder { get; private set; }
        public long Dropouts { get; private set; }
        public long Sync0 { get; private set; }
        public long Sync1 { get; private set; }
        public long Sync2 { get; private set; }
        public long SyncOther { get; private set; }
        public double AsduPerFrameMean { get; private set; }

        public void Observe(DateTimeOffset timestamp, SampledValuesFrame frame, SvSustainedAnalysisOptions options)
        {
            if (FrameCount > 0)
            {
                var interval = timestamp - LastSeen;
                if (interval > TimeSpan.Zero)
                    ObserveInterval(interval, options.DropoutIntervalMultiplier);
                else
                    AddDiagnostic("A non-increasing capture timestamp was observed; that interval was excluded from rate/jitter statistics.");
            }

            LastSeen = timestamp > LastSeen ? timestamp : LastSeen;
            FrameCount++;
            var asdus = frame.Pdu.Asdus;
            AsduCount += asdus.Count;
            AsduPerFrameMean += (asdus.Count - AsduPerFrameMean) / FrameCount;

            foreach (var asdu in asdus)
            {
                if (_firstPayloadLength == 0)
                    _firstPayloadLength = asdu.SamplePayload.Length;
                else if (asdu.SamplePayload.Length != _firstPayloadLength)
                    PayloadLayoutChanged = true;

                switch (asdu.SampleSynchronization)
                {
                    case 0: Sync0++; break;
                    case 1: Sync1++; break;
                    case 2: Sync2++; break;
                    default: SyncOther++; break;
                }

                var transition = _counter.Observe(asdu.SampleCount, options.SampleCounterWrap);
                switch (transition.Kind)
                {
                    case SvSampleCounterTransitionKind.Continuous: Continuous++; break;
                    case SvSampleCounterTransitionKind.NormalWrap: Wraps++; break;
                    case SvSampleCounterTransitionKind.Gap:
                        Gaps++;
                        Missing += transition.MissingSamples;
                        break;
                    case SvSampleCounterTransitionKind.Duplicate: Duplicates++; break;
                    case SvSampleCounterTransitionKind.OutOfOrder: OutOfOrder++; break;
                }
            }
        }

        private void ObserveInterval(TimeSpan interval, double dropoutMultiplier)
        {
            var ticks = (double)interval.Ticks;
            _intervalCount++;
            var delta = ticks - _intervalMeanTicks;
            _intervalMeanTicks += delta / _intervalCount;
            _intervalM2Ticks += delta * (ticks - _intervalMeanTicks);

            if (_intervalCount > 1)
            {
                var jitter = Math.Abs(ticks - _intervalMeanTicks);
                _maxAbsoluteJitterTicks = Math.Max(_maxAbsoluteJitterTicks, jitter);
            }

            if (_lastInterval.HasValue && ticks > _intervalMeanTicks * dropoutMultiplier)
                Dropouts++;
            _lastInterval = interval;
        }

        private void AddDiagnostic(string diagnostic)
        {
            if (_diagnostics.Contains(diagnostic, StringComparer.Ordinal))
                return;
            _diagnostics.Enqueue(diagnostic);
            while (_diagnostics.Count > 16)
                _diagnostics.Dequeue();
        }

        public SvSustainedStreamSnapshot Snapshot(SvSustainedAnalysisOptions options)
        {
            double? meanUs = _intervalCount > 0 ? TimeSpan.FromTicks((long)_intervalMeanTicks).TotalMicroseconds : null;
            double? frameRate = _intervalMeanTicks > 0 ? TimeSpan.TicksPerSecond / _intervalMeanTicks : null;
            double? sampleRate = frameRate.HasValue ? frameRate.Value * AsduPerFrameMean : null;
            double? stdUs = _intervalCount > 1
                ? Math.Sqrt(_intervalM2Ticks / (_intervalCount - 1)) / TimeSpan.TicksPerMicrosecond
                : null;

            var diagnostics = _diagnostics.ToList();
            if (PayloadLayoutChanged)
                diagnostics.Add("SV sample payload length changed inside one stream identity.");
            if (Gaps > 0)
                diagnostics.Add($"Observed {Gaps} smpCnt gap transition(s) representing {Missing} missing sample(s) under the configured counter modulus.");
            if (Dropouts > 0)
                diagnostics.Add($"Observed {Dropouts} capture interval(s) above the sustained dropout threshold; this is arrival-time evidence, not proof of network loss.");

            return new SvSustainedStreamSnapshot
            {
                Key = Key,
                FrameCount = FrameCount,
                AsduCount = AsduCount,
                FirstSeen = FirstSeen,
                LastSeen = LastSeen,
                StablePayloadBytesPerAsdu = PayloadLayoutChanged ? 0 : _firstPayloadLength,
                PayloadLayoutChanged = PayloadLayoutChanged,
                Continuity = new SvContinuityEvidence
                {
                    ContinuousTransitions = Continuous,
                    NormalWraps = Wraps,
                    GapTransitions = Gaps,
                    MissingSamples = Missing,
                    DuplicateTransitions = Duplicates,
                    OutOfOrderTransitions = OutOfOrder
                },
                Timing = new SvTimingEvidence
                {
                    TimestampEvidence = options.TimestampEvidence,
                    IntervalCount = _intervalCount,
                    MeanFrameIntervalMicroseconds = meanUs,
                    FrameRateHz = frameRate,
                    EstimatedSampleRateHz = sampleRate,
                    IntervalJitterStdDevMicroseconds = stdUs,
                    MaximumAbsoluteJitterMicroseconds = _maxAbsoluteJitterTicks / TimeSpan.TicksPerMicrosecond,
                    DropoutIntervals = Dropouts
                },
                Synchronization = new SvSynchronizationEvidence
                {
                    SampleSynchronization0 = Sync0,
                    SampleSynchronization1 = Sync1,
                    SampleSynchronization2 = Sync2,
                    OtherSampleSynchronization = SyncOther
                },
                Diagnostics = diagnostics
            };
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SvObservedStreamKey, StreamState> _streams = new();
    private readonly SvSustainedAnalysisOptions _options;

    public SvSustainedStreamAnalyzer(SvSustainedAnalysisOptions? options = null)
    {
        _options = options ?? new SvSustainedAnalysisOptions();
        _options.Validate();
    }

    public int Count { get { lock (_gate) return _streams.Count; } }

    public bool TryObserve(DateTimeOffset timestamp, SampledValuesFrame frame, out SvSustainedStreamSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(frame);
        snapshot = new();
        if (frame.Pdu.Asdus.Count == 0)
            return false;

        var key = SvObservedStreamKey.FromFrame(frame);
        lock (_gate)
        {
            EvictIdle(timestamp);
            if (!_streams.TryGetValue(key, out var state))
            {
                EnsureCapacity();
                state = new StreamState(key, timestamp);
                _streams.Add(key, state);
            }
            state.Observe(timestamp, frame, _options);
            snapshot = state.Snapshot(_options);
            return true;
        }
    }

    public IReadOnlyList<SvSustainedStreamSnapshot> SnapshotAll()
    {
        lock (_gate)
            return _streams.Values.Select(state => state.Snapshot(_options))
                .OrderBy(snapshot => snapshot.Key.AppId)
                .ThenBy(snapshot => snapshot.Key.SvId, StringComparer.Ordinal)
                .ToArray();
    }

    public void Clear()
    {
        lock (_gate)
            _streams.Clear();
    }

    private void EvictIdle(DateTimeOffset now)
    {
        foreach (var key in _streams.Where(pair => now - pair.Value.LastSeen > _options.StreamIdleTimeout).Select(pair => pair.Key).ToArray())
            _streams.Remove(key);
    }

    private void EnsureCapacity()
    {
        while (_streams.Count >= _options.MaximumStreams)
        {
            var oldest = _streams.OrderBy(pair => pair.Value.LastSeen).ThenBy(pair => pair.Key.Id, StringComparer.Ordinal).First();
            _streams.Remove(oldest.Key);
        }
    }
}

public sealed record SvPhasorEstimate
{
    public double Rms { get; init; }
    public double MagnitudeRms { get; init; }
    public double AngleDegrees { get; init; }
    public int SampleCount { get; init; }
}

/// <summary>
/// Deterministic bounded-window measurement primitive. The caller must supply samples that
/// already have an explicit channel mapping/scaling context; this class never guesses payload semantics.
/// </summary>
public static class SvSignalWindowAnalyzer
{
    public static SvPhasorEstimate AnalyzeFundamental(IReadOnlyList<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 4)
            throw new ArgumentException("At least four samples are required for RMS/phasor analysis.", nameof(samples));
        if (samples.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Samples must be finite.", nameof(samples));

        var sumSquares = 0.0;
        var fundamental = Complex.Zero;
        for (var index = 0; index < samples.Count; index++)
        {
            var value = samples[index];
            sumSquares += value * value;
            var angle = -2.0 * Math.PI * index / samples.Count;
            fundamental += value * Complex.FromPolarCoordinates(1.0, angle);
        }

        var rms = Math.Sqrt(sumSquares / samples.Count);
        var rmsPhasor = fundamental * (Math.Sqrt(2.0) / samples.Count);
        return new SvPhasorEstimate
        {
            Rms = rms,
            MagnitudeRms = rmsPhasor.Magnitude,
            AngleDegrees = Math.Atan2(rmsPhasor.Imaginary, rmsPhasor.Real) * 180.0 / Math.PI,
            SampleCount = samples.Count
        };
    }
}
