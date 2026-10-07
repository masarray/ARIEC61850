using System.Runtime.CompilerServices;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.SampledValues.Profiles;
using AR.Iec61850.Transports;

namespace AR.Iec61850.SampledValues.Analysis;

public enum SvCapturedFrameProcessingStatus
{
    Accepted = 0,
    ParserRejected = 1,
    ObservationRejected = 2
}

/// <summary>
/// Explicit per-stream context resolved by the application after a captured Ethernet frame has
/// been parsed as Sampled Values. The stack does not guess an SCL publisher, CT/VT context,
/// nominal frequency, or engineering timebase from capture bytes.
/// </summary>
public sealed record SvSustainedFrameContext
{
    public SampledValuesPublisherProfile? Profile { get; init; }
    public double? NominalFrequencyHz { get; init; }
    public SvComparisonMode ComparisonMode { get; init; } = SvComparisonMode.Compatible;
    public SvStreamMeasurementContext? MeasurementContext { get; init; }
    public SvEngineeringWindowEvidence? EngineeringWindowEvidence { get; init; }
}

/// <summary>
/// Compact result for one captured Ethernet frame. Raw frame bytes are deliberately not retained.
/// ParserRejected means only that the project-owned SV Ethernet parser did not accept the bytes;
/// it does not by itself classify the packet as malformed traffic.
/// </summary>
public sealed record SvCapturedFrameProcessingResult
{
    public DateTimeOffset Timestamp { get; init; }
    public int CapturedLength { get; init; }
    public SvCapturedFrameProcessingStatus Status { get; init; }
    public SvSustainedObservationResult? Observation { get; init; }
}

/// <summary>
/// Source-neutral bridge from raw captured Ethernet frames into the established Sampled Values
/// parser and <see cref="SvSustainedObservationSession"/>. The processor is sequential and owns no
/// queue; buffering and overload policy remain the responsibility of the selected
/// <see cref="IProcessBusFrameSource"/> implementation.
/// </summary>
public sealed class SvSustainedCaptureProcessor
{
    private readonly SvSustainedObservationSession _session;

    public SvSustainedCaptureProcessor(SvSustainedObservationSession? session = null)
    {
        _session = session ?? new SvSustainedObservationSession();
    }

    public SvSustainedObservationSession Session => _session;

    public SvCapturedFrameProcessingResult ProcessCapturedFrame(
        ProcessBusCapturedFrame captured,
        SvObservationInputKind inputKind,
        SvSustainedFrameContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(captured);

        if (!SampledValuesFrameParser.TryParseEthernetFrame(captured.Frame, out var frame))
        {
            return new SvCapturedFrameProcessingResult
            {
                Timestamp = captured.Timestamp,
                CapturedLength = captured.Frame.Length,
                Status = SvCapturedFrameProcessingStatus.ParserRejected
            };
        }

        context ??= new SvSustainedFrameContext();
        if (!_session.TryObserve(
                captured.Timestamp,
                frame,
                inputKind,
                out var observation,
                profile: context.Profile,
                nominalFrequencyHz: context.NominalFrequencyHz,
                comparisonMode: context.ComparisonMode,
                measurementContext: context.MeasurementContext,
                engineeringWindowEvidence: context.EngineeringWindowEvidence))
        {
            return new SvCapturedFrameProcessingResult
            {
                Timestamp = captured.Timestamp,
                CapturedLength = captured.Frame.Length,
                Status = SvCapturedFrameProcessingStatus.ObservationRejected
            };
        }

        return new SvCapturedFrameProcessingResult
        {
            Timestamp = captured.Timestamp,
            CapturedLength = captured.Frame.Length,
            Status = SvCapturedFrameProcessingStatus.Accepted,
            Observation = observation
        };
    }

    public async IAsyncEnumerable<SvCapturedFrameProcessingResult> ProcessAsync(
        IProcessBusFrameSource source,
        ProcessBusCaptureOptions captureOptions,
        SvObservationInputKind inputKind,
        Func<SampledValuesFrame, SvSustainedFrameContext?>? contextResolver = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(captureOptions);

        await foreach (var captured in source.CaptureAsync(captureOptions, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (contextResolver is null)
            {
                yield return ProcessCapturedFrame(captured, inputKind);
                continue;
            }

            if (!SampledValuesFrameParser.TryParseEthernetFrame(captured.Frame, out var parsed))
            {
                yield return new SvCapturedFrameProcessingResult
                {
                    Timestamp = captured.Timestamp,
                    CapturedLength = captured.Frame.Length,
                    Status = SvCapturedFrameProcessingStatus.ParserRejected
                };
                continue;
            }

            var context = contextResolver(parsed);
            context ??= new SvSustainedFrameContext();
            if (!_session.TryObserve(
                    captured.Timestamp,
                    parsed,
                    inputKind,
                    out var observation,
                    profile: context.Profile,
                    nominalFrequencyHz: context.NominalFrequencyHz,
                    comparisonMode: context.ComparisonMode,
                    measurementContext: context.MeasurementContext,
                    engineeringWindowEvidence: context.EngineeringWindowEvidence))
            {
                yield return new SvCapturedFrameProcessingResult
                {
                    Timestamp = captured.Timestamp,
                    CapturedLength = captured.Frame.Length,
                    Status = SvCapturedFrameProcessingStatus.ObservationRejected
                };
                continue;
            }

            yield return new SvCapturedFrameProcessingResult
            {
                Timestamp = captured.Timestamp,
                CapturedLength = captured.Frame.Length,
                Status = SvCapturedFrameProcessingStatus.Accepted,
                Observation = observation
            };
        }
    }
}
