using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Analysis;

/// <summary>
/// Source-neutral ingress for sustained Sampled Values observation. The session accepts an
/// already-parsed <see cref="SampledValuesFrame"/>, first routes it through the established
/// observation/SCL-comparison path, and only then advances sustained analysis.
/// </summary>
public sealed record SvSustainedObservationResult
{
    public SvStreamObservationSnapshot Observation { get; init; } = new();
    public SvSustainedStreamSnapshot Sustained { get; init; } = new();
}

/// <summary>
/// Keeps the short-window/profile observation path and sustained analysis on one parsed-frame
/// intake. It does not parse capture bytes, decode dataset payloads, or infer signal semantics.
/// Live capture and PCAP replay callers therefore share the same admission and stream-identity
/// path after successful IEC 61850-9-2 frame parsing.
/// </summary>
public sealed class SvSustainedObservationSession
{
    private readonly SvStreamObservationManager _observations;
    private readonly SvSustainedStreamAnalyzer _sustained;

    public SvSustainedObservationSession(
        SvStreamObservationManager? observations = null,
        SvSustainedStreamAnalyzer? sustained = null)
    {
        _observations = observations ?? new SvStreamObservationManager();
        _sustained = sustained ?? new SvSustainedStreamAnalyzer();
    }

    public int ObservationStreamCount => _observations.Count;
    public int SustainedStreamCount => _sustained.Count;

    public bool TryObserve(
        DateTimeOffset timestamp,
        SampledValuesFrame frame,
        SvObservationInputKind inputKind,
        out SvSustainedObservationResult result,
        SampledValuesPublisherProfile? profile = null,
        double? nominalFrequencyHz = null,
        SvComparisonMode comparisonMode = SvComparisonMode.Compatible)
    {
        ArgumentNullException.ThrowIfNull(frame);
        result = new();

        // Keep the established observation/profile path authoritative for admission.
        // If it rejects the parsed frame, sustained state must not advance.
        if (!_observations.TryObserve(
                timestamp,
                frame,
                inputKind,
                profile,
                out var observation,
                nominalFrequencyHz,
                comparisonMode))
        {
            return false;
        }

        // The observation manager only accepts a frame with at least one ASDU carrying a
        // non-empty sample payload, so a failure here would indicate an internal contract drift.
        if (!_sustained.TryObserve(timestamp, frame, out var sustained))
            throw new InvalidOperationException("Sustained SV analysis rejected a frame already accepted by the canonical observation path.");

        result = new SvSustainedObservationResult
        {
            Observation = observation,
            Sustained = sustained
        };
        return true;
    }

    public IReadOnlyList<SvStreamObservationSnapshot> SnapshotObservations()
        => _observations.SnapshotAll();

    public IReadOnlyList<SvSustainedStreamSnapshot> SnapshotSustained()
        => _sustained.SnapshotAll();

    public void Clear()
    {
        _observations.Clear();
        _sustained.Clear();
    }
}
