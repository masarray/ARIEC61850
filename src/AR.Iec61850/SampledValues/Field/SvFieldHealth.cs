using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Field;

public enum SvFieldHealthState
{
    Unknown,
    Good,
    Quiet,
    Warning,
    Bad
}

public sealed record SvFieldHealthAxis(
    string Name,
    SvFieldHealthState State,
    string Summary,
    IReadOnlyList<string> Evidence)
{
    public static SvFieldHealthAxis Unknown(string name, string summary)
        => new(name, SvFieldHealthState.Unknown, summary, Array.Empty<string>());
}

public sealed record SvFieldHealthReport
{
    public SvFieldHealthAxis Capture { get; init; } = SvFieldHealthAxis.Unknown("CAPTURE", "No capture evidence");
    public SvFieldHealthAxis Protocol { get; init; } = SvFieldHealthAxis.Unknown("PROTOCOL", "No protocol evidence");
    public SvFieldHealthAxis Stream { get; init; } = SvFieldHealthAxis.Unknown("STREAM", "No continuity evidence");
    public SvFieldHealthAxis Configuration { get; init; } = SvFieldHealthAxis.Unknown("CONFIGURATION", "No SCL context");
    public SvFieldHealthAxis Measurement { get; init; } = SvFieldHealthAxis.Unknown("MEASUREMENT", "Measurement semantics unresolved");

    /// <summary>Operational status uses only capture, protocol, and stream continuity.</summary>
    public SvFieldHealthState OperationalState => Worst(Capture.State, Protocol.State, Stream.State);

    /// <summary>Review status describes configuration and measurement confidence without declaring the stream broken.</summary>
    public SvFieldHealthState ReviewState => Worst(Configuration.State, Measurement.State);

    private static SvFieldHealthState Worst(params SvFieldHealthState[] states)
        => states.OrderByDescending(Rank).FirstOrDefault();

    private static int Rank(SvFieldHealthState state) => state switch
    {
        SvFieldHealthState.Bad => 5,
        SvFieldHealthState.Warning => 4,
        SvFieldHealthState.Quiet => 3,
        SvFieldHealthState.Good => 2,
        _ => 1
    };
}

public sealed record SvFieldHealthInput
{
    public long RawFrameCount { get; init; }
    public long SvFrameCount { get; init; }
    public long ParseErrorCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int DuplicateCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int PayloadIssueCount { get; init; }
    public SvConfigurationComparisonResult? ConfigurationComparison { get; init; }
    public bool IsSclBound { get; init; }
    public bool HasSemanticMapping { get; init; }
    public bool HasEngineeringScaling { get; init; }
    public bool IsScalingValidated { get; init; }
    public SvSignalAnalysis? Signal { get; init; }
}

public static class SvFieldHealthEvaluator
{
    public static SvFieldHealthReport Evaluate(SvFieldHealthInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var capture = EvaluateCapture(input);
        var protocol = EvaluateProtocol(input);
        var stream = EvaluateStream(input);
        var configuration = EvaluateConfiguration(input);
        var measurement = EvaluateMeasurement(input);
        return new SvFieldHealthReport
        {
            Capture = capture,
            Protocol = protocol,
            Stream = stream,
            Configuration = configuration,
            Measurement = measurement
        };
    }

    private static SvFieldHealthAxis EvaluateCapture(SvFieldHealthInput input)
    {
        if (input.RawFrameCount <= 0)
            return SvFieldHealthAxis.Unknown("CAPTURE", "No frames observed");
        if (input.SvFrameCount <= 0)
            return new("CAPTURE", SvFieldHealthState.Warning, "Frames received, no SV decoded", [$"raw={input.RawFrameCount:N0}"]);
        return new("CAPTURE", SvFieldHealthState.Good, $"{input.SvFrameCount:N0} SV frame(s) available", [$"raw={input.RawFrameCount:N0}"]);
    }

    private static SvFieldHealthAxis EvaluateProtocol(SvFieldHealthInput input)
    {
        if (input.SvFrameCount <= 0 && input.ParseErrorCount <= 0)
            return SvFieldHealthAxis.Unknown("PROTOCOL", "No SV protocol evidence");
        if (input.SvFrameCount <= 0 && input.ParseErrorCount > 0)
            return new("PROTOCOL", SvFieldHealthState.Bad, "SV frames could not be decoded", [$"parse errors={input.ParseErrorCount:N0}"]);
        if (input.ParseErrorCount > 0)
            return new("PROTOCOL", SvFieldHealthState.Warning, "Decoded SV with parse errors also present", [$"parse errors={input.ParseErrorCount:N0}"]);
        return new("PROTOCOL", SvFieldHealthState.Good, "Ethernet and SV APDU decode cleanly", Array.Empty<string>());
    }

    private static SvFieldHealthAxis EvaluateStream(SvFieldHealthInput input)
    {
        if (input.SvFrameCount <= 0)
            return SvFieldHealthAxis.Unknown("STREAM", "No continuity window");
        if (input.OutOfOrderCount > 0 || input.PayloadIssueCount > 0)
            return new("STREAM", SvFieldHealthState.Bad, "Continuity or payload integrity failed",
                [$"out-of-order={input.OutOfOrderCount}", $"payload={input.PayloadIssueCount}"]);
        if (input.SequenceGapCount > 0 || input.DuplicateCount > 0)
            return new("STREAM", SvFieldHealthState.Warning, "Continuity requires review",
                [$"gaps={input.SequenceGapCount}", $"duplicates={input.DuplicateCount}"]);
        return new("STREAM", SvFieldHealthState.Good, "Sample continuity is healthy", Array.Empty<string>());
    }

    private static SvFieldHealthAxis EvaluateConfiguration(SvFieldHealthInput input)
    {
        if (!input.IsSclBound || input.ConfigurationComparison is null)
            return SvFieldHealthAxis.Unknown("CONFIGURATION", "No SCL/CID binding");

        var comparison = input.ConfigurationComparison;
        if (comparison.ErrorCount > 0)
            return new("CONFIGURATION", SvFieldHealthState.Bad, comparison.Summary,
                comparison.Findings.Select(finding => $"{finding.Code}: {finding.Message}").ToArray());
        if (comparison.WarningCount > 0)
            return new("CONFIGURATION", SvFieldHealthState.Warning, comparison.Summary,
                comparison.Findings.Select(finding => $"{finding.Code}: {finding.Message}").ToArray());
        return new("CONFIGURATION", SvFieldHealthState.Good, "Observed stream matches bound SCL", Array.Empty<string>());
    }

    private static SvFieldHealthAxis EvaluateMeasurement(SvFieldHealthInput input)
    {
        if (!input.HasSemanticMapping)
            return SvFieldHealthAxis.Unknown("MEASUREMENT", "Raw values available; channel semantics unresolved");
        if (!input.HasEngineeringScaling)
            return new("MEASUREMENT", SvFieldHealthState.Warning, "Channels mapped; engineering scaling unresolved", Array.Empty<string>());

        if (input.Signal?.State is SvSignalActivityState.Quiet or SvSignalActivityState.NoiseDominated)
            return new("MEASUREMENT", SvFieldHealthState.Quiet, "QUIET / NOISE FLOOR", [input.Signal.Summary]);
        if (!input.IsScalingValidated)
            return new("MEASUREMENT", SvFieldHealthState.Warning, "Engineering values are provisional", Array.Empty<string>());
        return new("MEASUREMENT", SvFieldHealthState.Good, "Engineering interpretation validated", Array.Empty<string>());
    }
}
