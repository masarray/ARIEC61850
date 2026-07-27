namespace AR.Iec61850.SampledValues.Profiles;

public sealed record SvFrameObservation
{
    public DateTimeOffset Timestamp { get; init; }
    public ushort EtherType { get; init; }
    public ushort AppId { get; init; }
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint ConfigurationRevision { get; init; }
    public int PayloadBytesPerAsdu { get; init; }
    public IReadOnlyList<ushort> SampleCounts { get; init; } = Array.Empty<ushort>();
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public double? NominalFrequencyHz { get; init; }
    public IReadOnlyList<SvDatasetElementSignature> DataSetSignature { get; init; }
        = Array.Empty<SvDatasetElementSignature>();

    public int AsduPerFrame => SampleCounts.Count;

    public void Validate()
    {
        if (SampleCounts.Count == 0)
            throw new ArgumentException("An SV frame observation requires at least one ASDU sample counter.");
        if (PayloadBytesPerAsdu <= 0)
            throw new ArgumentOutOfRangeException(nameof(PayloadBytesPerAsdu));
    }
}

public sealed class SvObservationAccumulator
{
    public const int DefaultMaximumObservations = 4096;
    public static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Queue<SvFrameObservation> _observations = new();
    private readonly int _maximumObservations;
    private readonly TimeSpan _maximumAge;

    public SvObservationAccumulator(int maximumObservations = DefaultMaximumObservations, TimeSpan? maximumAge = null)
    {
        if (maximumObservations < 2)
            throw new ArgumentOutOfRangeException(nameof(maximumObservations), "At least two observations are required for rate analysis.");
        _maximumAge = maximumAge ?? DefaultMaximumAge;
        if (_maximumAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAge), "Observation age must be greater than zero.");
        _maximumObservations = maximumObservations;
    }

    public int Count { get { lock (_gate) return _observations.Count; } }
    public int MaximumObservations => _maximumObservations;
    public TimeSpan MaximumAge => _maximumAge;

    public void Add(SvFrameObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        lock (_gate)
        {
            _observations.Enqueue(observation);
            TrimWindow(observation.Timestamp);
        }
    }

    public void Clear() { lock (_gate) _observations.Clear(); }

    public SvObservedStreamFacts BuildFacts()
    {
        SvFrameObservation[] snapshot;
        lock (_gate) snapshot = _observations.ToArray();
        if (snapshot.Length == 0)
            return new SvObservedStreamFacts();

        var ordered = snapshot.OrderBy(item => item.Timestamp).ToArray();
        var diagnostics = new List<string>();
        var first = ordered[0];
        var last = ordered[^1];
        var durationSeconds = (last.Timestamp - first.Timestamp).TotalSeconds;
        double? framesPerSecond = null;
        double? samplesPerSecond = null;
        if (ordered.Length >= 2 && durationSeconds > 0)
        {
            framesPerSecond = (ordered.Length - 1) / durationSeconds;
            samplesPerSecond = framesPerSecond * ordered.Average(item => item.AsduPerFrame);
        }
        else
        {
            diagnostics.Add("At least two timestamped frames are required to estimate stream rate.");
        }

        var counter = AnalyzeCounters(ordered, diagnostics);
        return new SvObservedStreamFacts
        {
            EtherType = StableValue(ordered.Select(item => item.EtherType), "EtherType", diagnostics),
            AppId = StableValue(ordered.Select(item => item.AppId), "APPID", diagnostics),
            DestinationMac = StableString(ordered.Select(item => item.DestinationMac), "destination MAC", diagnostics),
            VlanId = StableNullableValue(ordered.Select(item => item.VlanId), "VLAN ID", diagnostics),
            VlanPriority = StableNullableValue(ordered.Select(item => item.VlanPriority), "VLAN priority", diagnostics),
            SvId = StableString(ordered.Select(item => item.SvId), "svID", diagnostics),
            DataSetReference = StableString(ordered.Select(item => item.DataSetReference), "dataset reference", diagnostics),
            ConfigurationRevision = StableValue(ordered.Select(item => item.ConfigurationRevision), "confRev", diagnostics),
            AsduPerFrame = StableValue(ordered.Select(item => item.AsduPerFrame), "ASDU count", diagnostics),
            PayloadBytesPerAsdu = StableValue(ordered.Select(item => item.PayloadBytesPerAsdu), "payload length", diagnostics),
            ObservedFramesPerSecond = framesPerSecond,
            ObservedSamplesPerSecond = samplesPerSecond,
            ObservedCounterWrap = counter.Wrap,
            CounterTransitions = counter.Summary,
            DeclaredSampleRate = StableNullableValue(ordered.Select(item => item.DeclaredSampleRate), "declared sample rate", diagnostics),
            DeclaredSampleMode = StableNullableValue(ordered.Select(item => item.DeclaredSampleMode), "declared sample mode", diagnostics),
            NominalFrequencyHz = StableNullableValue(ordered.Select(item => item.NominalFrequencyHz), "nominal frequency", diagnostics),
            DataSetSignature = StableSignature(ordered.Select(item => item.DataSetSignature), diagnostics),
            Provenance = BuildProvenance(),
            ObservationCount = ordered.Length,
            FirstTimestamp = first.Timestamp,
            LastTimestamp = last.Timestamp,
            Diagnostics = diagnostics
        };
    }

    private void TrimWindow(DateTimeOffset newestTimestamp)
    {
        while (_observations.Count > _maximumObservations) _observations.Dequeue();
        var oldestAllowed = newestTimestamp - _maximumAge;
        while (_observations.Count > 0 && _observations.Peek().Timestamp < oldestAllowed) _observations.Dequeue();
    }

    private static (int? Wrap, SvCounterTransitionSummary Summary) AnalyzeCounters(IReadOnlyList<SvFrameObservation> observations, List<string> diagnostics)
    {
        var sampleCounts = observations.SelectMany(item => item.SampleCounts).ToArray();
        if (sampleCounts.Length < 2)
            return (null, new SvCounterTransitionSummary());

        var sequential = 0; var gaps = 0; var duplicates = 0; var outOfOrderOrReset = 0; var confirmedWraps = 0;
        var wrapCandidates = new HashSet<int>();
        for (var index = 1; index < sampleCounts.Length; index++)
        {
            var previous = sampleCounts[index - 1];
            var current = sampleCounts[index];
            if (current == previous) { duplicates++; continue; }
            if (current == previous + 1) { sequential++; continue; }
            if (current > previous + 1) { gaps++; continue; }
            var recovery = index + 1 < sampleCounts.Length && sampleCounts[index + 1] == current + 1;
            if (current == 0 && recovery && previous > 1) { confirmedWraps++; wrapCandidates.Add(previous + 1); continue; }
            outOfOrderOrReset++;
        }

        int? wrap = wrapCandidates.Count == 1 ? wrapCandidates.Single() : null;
        if (wrapCandidates.Count > 1) diagnostics.Add($"Multiple sample-counter wrap candidates were observed: {string.Join(", ", wrapCandidates.Order())}.");
        if (gaps > 0) diagnostics.Add($"Observed {gaps} forward sample-counter gap transition(s).");
        if (duplicates > 0) diagnostics.Add($"Observed {duplicates} duplicate sample-counter transition(s).");
        if (outOfOrderOrReset > 0) diagnostics.Add($"Observed {outOfOrderOrReset} out-of-order or reset transition(s); these were not classified as wraps.");
        return (wrap, new SvCounterTransitionSummary
        {
            SequentialCount = sequential,
            GapCount = gaps,
            DuplicateCount = duplicates,
            OutOfOrderOrResetCount = outOfOrderOrReset,
            ConfirmedWrapCount = confirmedWraps
        });
    }

    private static IReadOnlyDictionary<string, SvFactSource> BuildProvenance()
        => new Dictionary<string, SvFactSource>(StringComparer.Ordinal)
        {
            [nameof(SvObservedStreamFacts.EtherType)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.AppId)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.DestinationMac)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.VlanId)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.VlanPriority)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.SvId)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.DataSetReference)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.ConfigurationRevision)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.AsduPerFrame)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.PayloadBytesPerAsdu)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.DeclaredSampleRate)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.DeclaredSampleMode)] = SvFactSource.WireObserved,
            [nameof(SvObservedStreamFacts.ObservedFramesPerSecond)] = SvFactSource.CaptureCalculated,
            [nameof(SvObservedStreamFacts.ObservedSamplesPerSecond)] = SvFactSource.CaptureCalculated,
            [nameof(SvObservedStreamFacts.ObservedCounterWrap)] = SvFactSource.CaptureCalculated,
            [nameof(SvObservedStreamFacts.CounterTransitions)] = SvFactSource.CaptureCalculated,
            [nameof(SvObservedStreamFacts.NominalFrequencyHz)] = SvFactSource.TrustedContext,
            [nameof(SvObservedStreamFacts.DataSetSignature)] = SvFactSource.SclDerived
        };

    private static T? StableValue<T>(IEnumerable<T> values, string field, List<string> diagnostics) where T : struct
    {
        var distinct = values.Distinct().ToArray();
        if (distinct.Length == 1) return distinct[0];
        diagnostics.Add($"Observed {field} changed within the analysis window.");
        return null;
    }

    private static T? StableNullableValue<T>(IEnumerable<T?> values, string field, List<string> diagnostics) where T : struct
    {
        var distinct = values.Distinct().ToArray();
        if (distinct.Length == 1) return distinct[0];
        diagnostics.Add($"Observed {field} changed within the analysis window.");
        return null;
    }

    private static string StableString(IEnumerable<string> values, string field, List<string> diagnostics)
    {
        var distinct = values.Select(value => value ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 1) return distinct[0];
        diagnostics.Add($"Observed {field} changed within the analysis window.");
        return string.Empty;
    }

    private static IReadOnlyList<SvDatasetElementSignature> StableSignature(IEnumerable<IReadOnlyList<SvDatasetElementSignature>> signatures, List<string> diagnostics)
    {
        var materialized = signatures.ToArray();
        var normalized = materialized.Select(signature => signature.Select(ToKey).ToArray()).ToArray();
        if (normalized.Length == 0 || normalized.All(signature => signature.Length == 0)) return Array.Empty<SvDatasetElementSignature>();
        var first = normalized[0];
        if (normalized.All(signature => signature.SequenceEqual(first, StringComparer.Ordinal))) return materialized[0].ToArray();
        diagnostics.Add("Observed dataset signature changed within the analysis window.");
        return Array.Empty<SvDatasetElementSignature>();
    }

    private static string ToKey(SvDatasetElementSignature element)
        => $"{element.NormalizedBType}|{element.NormalizedCdc}|{element.IsQuality}|{element.IsTimestamp}";
}
