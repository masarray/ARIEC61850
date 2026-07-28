namespace AR.Iec61850.SampledValues.Field;

public enum SvSignalActivityState
{
    Unresolved,
    Quiet,
    NoiseDominated,
    Active
}

public sealed record SvSignalAnalysis
{
    public SvSignalActivityState State { get; init; }
    public int SampleCount { get; init; }
    public double Median { get; init; }
    public double RobustSigma { get; init; }
    public double AcRms { get; init; }
    public double PeakDeviation { get; init; }
    public double? FundamentalRms { get; init; }
    public double? ResidualRms { get; init; }
    public double? FundamentalSnrDb { get; init; }
    public double? QuietThreshold { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record SvSignalAnalysisOptions
{
    public double? SamplesPerCycle { get; init; }
    public double? RatedRms { get; init; }
    public double? AbsoluteQuietThreshold { get; init; }
    public double QuietRatedFraction { get; init; } = 0.001;
    public double MinimumCoherentSnrDb { get; init; } = 6.0;
    public int MaximumAnalysisSamples { get; init; } = 4096;
}

/// <summary>
/// Robust signal-state analyzer. It never modifies or clamps samples; classification affects only
/// presentation and evidence. A hard QUIET result requires an explicit rated or absolute threshold.
/// </summary>
public static class SvSignalStateAnalyzer
{
    public static SvSignalAnalysis Analyze(IEnumerable<double> samples, SvSignalAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        options ??= new SvSignalAnalysisOptions();
        if (options.QuietRatedFraction < 0 || options.MinimumCoherentSnrDb < 0 || options.MaximumAnalysisSamples < 16)
            throw new ArgumentOutOfRangeException(nameof(options), "Signal-analysis options are invalid.");

        var values = samples.Where(double.IsFinite).TakeLast(options.MaximumAnalysisSamples).ToArray();
        if (values.Length < 16)
            return new SvSignalAnalysis { State = SvSignalActivityState.Unresolved, SampleCount = values.Length, Summary = "Insufficient samples for robust signal classification" };

        var median = Median(values);
        var deviations = values.Select(value => Math.Abs(value - median)).ToArray();
        var mad = Median(deviations);
        var robustSigma = 1.4826 * mad;
        var centered = values.Select(value => value - median).ToArray();
        var acRms = Math.Sqrt(centered.Average(value => value * value));
        var peak = centered.Max(value => Math.Abs(value));
        var quietThreshold = ResolveQuietThreshold(options);

        double? fundamentalRms = null;
        double? residualRms = null;
        double? snrDb = null;
        if (options.SamplesPerCycle is >= 4 && values.Length >= options.SamplesPerCycle.Value)
        {
            var omega = 2.0 * Math.PI / options.SamplesPerCycle.Value;
            var cosine = 0.0;
            var sine = 0.0;
            for (var index = 0; index < centered.Length; index++)
            {
                cosine += centered[index] * Math.Cos(omega * index);
                sine += centered[index] * Math.Sin(omega * index);
            }

            var peakFundamental = 2.0 * Math.Sqrt((cosine * cosine) + (sine * sine)) / centered.Length;
            fundamentalRms = peakFundamental / Math.Sqrt(2.0);
            residualRms = Math.Sqrt(Math.Max(0, (acRms * acRms) - (fundamentalRms.Value * fundamentalRms.Value)));
            snrDb = residualRms.Value <= double.Epsilon
                ? double.PositiveInfinity
                : 20.0 * Math.Log10(Math.Max(fundamentalRms.Value, double.Epsilon) / residualRms.Value);
        }

        SvSignalActivityState state;
        if (quietThreshold.HasValue && acRms <= quietThreshold.Value)
        {
            state = SvSignalActivityState.Quiet;
        }
        else if (!fundamentalRms.HasValue || !snrDb.HasValue || snrDb.Value < options.MinimumCoherentSnrDb)
        {
            state = SvSignalActivityState.NoiseDominated;
        }
        else
        {
            state = SvSignalActivityState.Active;
        }

        return new SvSignalAnalysis
        {
            State = state,
            SampleCount = values.Length,
            Median = median,
            RobustSigma = robustSigma,
            AcRms = acRms,
            PeakDeviation = peak,
            FundamentalRms = fundamentalRms,
            ResidualRms = residualRms,
            FundamentalSnrDb = snrDb,
            QuietThreshold = quietThreshold,
            Summary = BuildSummary(state, acRms, fundamentalRms, snrDb, quietThreshold)
        };
    }

    private static double? ResolveQuietThreshold(SvSignalAnalysisOptions options)
    {
        var candidates = new[]
        {
            options.AbsoluteQuietThreshold,
            options.RatedRms.HasValue ? options.RatedRms.Value * options.QuietRatedFraction : null
        }.Where(value => value is > 0).Select(value => value!.Value).ToArray();
        return candidates.Length == 0 ? null : candidates.Max();
    }

    private static double Median(IReadOnlyCollection<double> source)
    {
        var ordered = source.Order().ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2.0
            : ordered[middle];
    }

    private static string BuildSummary(
        SvSignalActivityState state,
        double acRms,
        double? fundamentalRms,
        double? snrDb,
        double? quietThreshold)
    {
        var fundamental = fundamentalRms.HasValue ? $"fundamental={fundamentalRms.Value:0.###}" : "fundamental=unresolved";
        var snr = snrDb.HasValue ? $"SNR={snrDb.Value:0.##} dB" : "SNR=unresolved";
        var threshold = quietThreshold.HasValue ? $"quiet≤{quietThreshold.Value:0.###}" : "quiet threshold not configured";
        return $"{state} · AC RMS={acRms:0.###} · {fundamental} · {snr} · {threshold}";
    }
}
