using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Analysis;

public sealed record SvEngineeringWindowAnalysisOptions
{
    private const long MaximumBufferedDoubles = 8L * 1024L * 1024L;

    public int MaximumStreams { get; init; } = 256;
    public int MaximumChannelsPerStream { get; init; } = 32;
    public int MaximumSamplesPerCycle { get; init; } = 512;
    public double SamplesPerCycleTolerance { get; init; } = 0.01;

    public void Validate()
    {
        if (MaximumStreams < 1 || MaximumStreams > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaximumStreams));
        if (MaximumChannelsPerStream < 1 || MaximumChannelsPerStream > 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumChannelsPerStream));
        if (MaximumSamplesPerCycle < 4 || MaximumSamplesPerCycle > 8192)
            throw new ArgumentOutOfRangeException(nameof(MaximumSamplesPerCycle));
        if (!double.IsFinite(SamplesPerCycleTolerance) || SamplesPerCycleTolerance < 0 || SamplesPerCycleTolerance > 0.5)
            throw new ArgumentOutOfRangeException(nameof(SamplesPerCycleTolerance));

        var worstCaseBufferedDoubles =
            (long)MaximumStreams * MaximumChannelsPerStream * MaximumSamplesPerCycle;
        if (worstCaseBufferedDoubles > MaximumBufferedDoubles)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSamplesPerCycle),
                "Configured engineering-window bounds could retain more than 64 MiB of sample buffers.");
        }
    }
}

/// <summary>
/// Explicit evidence required before an engineering waveform may be windowed for RMS/phasor analysis.
/// Unknown or profile-inferred rate/frequency values are deliberately rejected so 50/60 Hz and the
/// samples-per-cycle count are never guessed from payload shape or amplitude.
/// </summary>
public sealed record SvEngineeringWindowEvidence
{
    public double SampleRateHz { get; init; }
    public SvFactSource SampleRateSource { get; init; } = SvFactSource.Unknown;
    public double FundamentalFrequencyHz { get; init; }
    public SvFactSource FundamentalFrequencySource { get; init; } = SvFactSource.Unknown;
    public ushort? SampleCounterWrap { get; init; }
    public SvFactSource SampleCounterWrapSource { get; init; } = SvFactSource.Unknown;

    public bool TryResolveSamplesPerCycle(
        SvEngineeringWindowAnalysisOptions options,
        out int samplesPerCycle,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(options);
        samplesPerCycle = 0;

        if (!double.IsFinite(SampleRateHz) || SampleRateHz <= 0)
        {
            reason = "Engineering-window analysis requires a finite positive sample rate.";
            return false;
        }
        if (!IsExplicitEvidence(SampleRateSource))
        {
            reason = "Engineering-window analysis requires an explicit sample-rate evidence source; unknown/profile-inferred values are rejected.";
            return false;
        }
        if (!double.IsFinite(FundamentalFrequencyHz) || FundamentalFrequencyHz <= 0)
        {
            reason = "Engineering-window analysis requires a finite positive fundamental frequency.";
            return false;
        }
        if (!IsExplicitEvidence(FundamentalFrequencySource))
        {
            reason = "Engineering-window analysis requires an explicit fundamental-frequency evidence source; 50/60 Hz is never guessed.";
            return false;
        }
        if (SampleCounterWrap is <= 1)
        {
            reason = "Configured smpCnt wrap must be greater than one.";
            return false;
        }
        if (SampleCounterWrap.HasValue && !IsExplicitEvidence(SampleCounterWrapSource))
        {
            reason = "A configured smpCnt wrap requires an explicit evidence source.";
            return false;
        }

        var exact = SampleRateHz / FundamentalFrequencyHz;
        var rounded = Math.Round(exact, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(exact) || rounded < 4 || rounded > options.MaximumSamplesPerCycle)
        {
            reason = $"Resolved samples-per-cycle {exact:G17} is outside the supported bounded window range 4..{options.MaximumSamplesPerCycle}.";
            return false;
        }
        if (Math.Abs(exact - rounded) > options.SamplesPerCycleTolerance)
        {
            reason = $"Sample rate {SampleRateHz:G17} Hz and fundamental {FundamentalFrequencyHz:G17} Hz do not resolve to an integral one-cycle window within tolerance.";
            return false;
        }

        samplesPerCycle = checked((int)rounded);
        reason = string.Empty;
        return true;
    }

    private static bool IsExplicitEvidence(SvFactSource source)
        => source is not SvFactSource.Unknown and not SvFactSource.ProfileInferred;
}

public sealed record SvEngineeringWindowResult
{
    public SvObservedStreamKey StreamKey { get; init; } = new();
    public int ElementIndex { get; init; }
    public string SignalReference { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string EngineeringUnit { get; init; } = string.Empty;
    public SvEngineeringScaleSource ScaleSource { get; init; }
    public SvEngineeringScaleConfidence ScaleConfidence { get; init; }
    public ushort FirstSampleCount { get; init; }
    public ushort LastSampleCount { get; init; }
    public int SamplesPerCycle { get; init; }
    public double SampleRateHz { get; init; }
    public SvFactSource SampleRateSource { get; init; }
    public double FundamentalFrequencyHz { get; init; }
    public SvFactSource FundamentalFrequencySource { get; init; }
    public SvPhasorEstimate Estimate { get; init; } = new();
}

/// <summary>
/// Bounded per-stream/per-channel engineering waveform windowing. Input must already come from the
/// fail-closed SCL-bound projector. Raw-only values are ignored. A partial cycle is discarded whenever
/// smpCnt continuity or channel metadata is broken, so RMS/phasor results never bridge an observed gap,
/// duplicate, out-of-order transition, restart, mapping change, or evidence/timebase change.
/// </summary>
public sealed class SvEngineeringWindowAnalyzer
{
    private sealed record WindowConfiguration(
        int SamplesPerCycle,
        double SampleRateHz,
        SvFactSource SampleRateSource,
        double FundamentalFrequencyHz,
        SvFactSource FundamentalFrequencySource,
        ushort? SampleCounterWrap,
        SvFactSource SampleCounterWrapSource);

    private sealed class ChannelState
    {
        private readonly SvSampleCounterTracker _counter = new();
        private double[] _samples;
        private int _count;
        private ushort _firstSampleCount;
        private ushort _lastSampleCount;

        public ChannelState(SvProjectedMeasurementSample sample, int samplesPerCycle)
        {
            _samples = new double[samplesPerCycle];
            SetMetadata(sample);
        }

        public string SignalReference { get; private set; } = string.Empty;
        public string Cdc { get; private set; } = string.Empty;
        public string EngineeringUnit { get; private set; } = string.Empty;
        public SvEngineeringScaleSource ScaleSource { get; private set; }
        public SvEngineeringScaleConfidence ScaleConfidence { get; private set; }

        public bool MetadataMatches(SvProjectedMeasurementSample sample)
            => string.Equals(SignalReference, sample.SignalReference, StringComparison.Ordinal) &&
               string.Equals(Cdc, sample.Cdc, StringComparison.Ordinal) &&
               string.Equals(EngineeringUnit, sample.Scale.Unit, StringComparison.Ordinal) &&
               ScaleSource == sample.Scale.Source &&
               ScaleConfidence == sample.Scale.Confidence;

        public void ResetForMetadata(SvProjectedMeasurementSample sample, int samplesPerCycle)
        {
            if (_samples.Length != samplesPerCycle)
                _samples = new double[samplesPerCycle];
            _count = 0;
            _counter.Reset();
            SetMetadata(sample);
        }

        public void ResetForConfiguration(int samplesPerCycle)
        {
            if (_samples.Length != samplesPerCycle)
                _samples = new double[samplesPerCycle];
            _count = 0;
            _counter.Reset();
        }

        public SvEngineeringWindowResult? Observe(
            SvObservedStreamKey streamKey,
            SvProjectedMeasurementSample sample,
            WindowConfiguration configuration)
        {
            var transition = _counter.Observe(sample.SampleCount, configuration.SampleCounterWrap);
            switch (transition.Kind)
            {
                case SvSampleCounterTransitionKind.Duplicate:
                    ResetPartialWindow();
                    return null;
                case SvSampleCounterTransitionKind.Gap:
                case SvSampleCounterTransitionKind.OutOfOrder:
                case SvSampleCounterTransitionKind.Restart:
                    ResetPartialWindow();
                    break;
            }

            var value = sample.EngineeringValue!.Value;
            if (_count == 0)
                _firstSampleCount = sample.SampleCount;
            _lastSampleCount = sample.SampleCount;
            _samples[_count++] = value;

            if (_count < configuration.SamplesPerCycle)
                return null;

            var estimate = SvSignalWindowAnalyzer.AnalyzeFundamental(_samples);
            var result = new SvEngineeringWindowResult
            {
                StreamKey = streamKey,
                ElementIndex = sample.ElementIndex,
                SignalReference = SignalReference,
                Cdc = Cdc,
                EngineeringUnit = EngineeringUnit,
                ScaleSource = ScaleSource,
                ScaleConfidence = ScaleConfidence,
                FirstSampleCount = _firstSampleCount,
                LastSampleCount = _lastSampleCount,
                SamplesPerCycle = configuration.SamplesPerCycle,
                SampleRateHz = configuration.SampleRateHz,
                SampleRateSource = configuration.SampleRateSource,
                FundamentalFrequencyHz = configuration.FundamentalFrequencyHz,
                FundamentalFrequencySource = configuration.FundamentalFrequencySource,
                Estimate = estimate
            };
            ResetPartialWindow();
            return result;
        }

        private void SetMetadata(SvProjectedMeasurementSample sample)
        {
            SignalReference = sample.SignalReference;
            Cdc = sample.Cdc;
            EngineeringUnit = sample.Scale.Unit;
            ScaleSource = sample.Scale.Source;
            ScaleConfidence = sample.Scale.Confidence;
        }

        private void ResetPartialWindow() => _count = 0;
    }

    private sealed class StreamState
    {
        public Dictionary<int, ChannelState> Channels { get; } = new();
        public WindowConfiguration? Configuration { get; set; }
        public long LastUseSequence { get; set; }

        public void ResetForConfiguration(WindowConfiguration configuration)
        {
            Configuration = configuration;
            foreach (var channel in Channels.Values)
                channel.ResetForConfiguration(configuration.SamplesPerCycle);
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SvObservedStreamKey, StreamState> _streams = new();
    private readonly SvEngineeringWindowAnalysisOptions _options;
    private long _useSequence;

    public SvEngineeringWindowAnalyzer(SvEngineeringWindowAnalysisOptions? options = null)
    {
        _options = options ?? new SvEngineeringWindowAnalysisOptions();
        _options.Validate();
    }

    public int ActiveStreamCount
    {
        get { lock (_gate) return _streams.Count; }
    }

    public int ActiveChannelCount
    {
        get { lock (_gate) return _streams.Values.Sum(stream => stream.Channels.Count); }
    }

    public bool TryObserve(
        SvSclBoundMeasurementProjection projection,
        SvEngineeringWindowEvidence evidence,
        out IReadOnlyList<SvEngineeringWindowResult> completed,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(evidence);
        completed = Array.Empty<SvEngineeringWindowResult>();

        if (!projection.IsBoundToScl)
        {
            reason = "Engineering-window analysis requires a projection that is explicitly bound to SCL.";
            return false;
        }
        if (!evidence.TryResolveSamplesPerCycle(_options, out var samplesPerCycle, out reason))
            return false;

        var invalidEngineeringSample = projection.Samples.FirstOrDefault(sample =>
            sample.EngineeringValue.HasValue &&
            (!double.IsFinite(sample.EngineeringValue.Value) || !sample.Scale.HasEngineeringUnit));
        if (invalidEngineeringSample is not null)
        {
            reason = "Projection contains an invalid engineering sample or a value without evidence-backed engineering units.";
            return false;
        }

        var engineeringSamples = projection.Samples
            .Where(sample => sample.EngineeringValue.HasValue &&
                             sample.Scale.HasEngineeringUnit &&
                             !string.IsNullOrWhiteSpace(sample.SignalReference) &&
                             !string.IsNullOrWhiteSpace(sample.Scale.Unit))
            .ToArray();
        if (engineeringSamples.Length == 0)
        {
            reason = "Projection contains no evidence-backed engineering samples; raw-only values were intentionally ignored.";
            return true;
        }

        foreach (var group in engineeringSamples.GroupBy(sample => sample.ElementIndex))
        {
            var first = group.First();
            if (group.Any(sample =>
                    !string.Equals(sample.SignalReference, first.SignalReference, StringComparison.Ordinal) ||
                    !string.Equals(sample.Cdc, first.Cdc, StringComparison.Ordinal) ||
                    !string.Equals(sample.Scale.Unit, first.Scale.Unit, StringComparison.Ordinal) ||
                    sample.Scale.Source != first.Scale.Source ||
                    sample.Scale.Confidence != first.Scale.Confidence))
            {
                reason = $"Element {group.Key} changed engineering identity inside one projection; analysis failed closed.";
                return false;
            }
        }

        var configuration = new WindowConfiguration(
            samplesPerCycle,
            evidence.SampleRateHz,
            evidence.SampleRateSource,
            evidence.FundamentalFrequencyHz,
            evidence.FundamentalFrequencySource,
            evidence.SampleCounterWrap,
            evidence.SampleCounterWrapSource);

        lock (_gate)
        {
            if (!_streams.TryGetValue(projection.StreamKey, out var stream))
            {
                EnsureStreamCapacity();
                stream = new StreamState();
                _streams.Add(projection.StreamKey, stream);
            }

            var incomingElements = engineeringSamples.Select(sample => sample.ElementIndex).Distinct().ToArray();
            var newChannelCount = incomingElements.Count(element => !stream.Channels.ContainsKey(element));
            if (stream.Channels.Count + newChannelCount > _options.MaximumChannelsPerStream)
            {
                reason = $"Engineering channel bound {_options.MaximumChannelsPerStream} would be exceeded for stream {projection.StreamKey.Id}.";
                return false;
            }

            stream.LastUseSequence = ++_useSequence;
            if (stream.Configuration != configuration)
                stream.ResetForConfiguration(configuration);

            var results = new List<SvEngineeringWindowResult>();
            foreach (var sample in engineeringSamples)
            {
                if (!stream.Channels.TryGetValue(sample.ElementIndex, out var channel))
                {
                    channel = new ChannelState(sample, samplesPerCycle);
                    stream.Channels.Add(sample.ElementIndex, channel);
                }
                else if (!channel.MetadataMatches(sample))
                {
                    channel.ResetForMetadata(sample, samplesPerCycle);
                }

                var result = channel.Observe(projection.StreamKey, sample, configuration);
                if (result is not null)
                    results.Add(result);
            }

            completed = results;
            reason = string.Empty;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _streams.Clear();
            _useSequence = 0;
        }
    }

    private void EnsureStreamCapacity()
    {
        while (_streams.Count >= _options.MaximumStreams)
        {
            var oldest = _streams
                .OrderBy(pair => pair.Value.LastUseSequence)
                .ThenBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .First();
            _streams.Remove(oldest.Key);
        }
    }
}
