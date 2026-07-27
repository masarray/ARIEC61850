namespace AR.Iec61850.SampledValues.Measurements;

public sealed record SvTimebaseEvidence
{
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public double? ObservedSamplesPerSecond { get; init; }
    public bool IsFixedLegacyProtectionLayout { get; init; }
    public double? TrustedNominalFrequencyHz { get; init; }
}

public sealed record SvTimebaseResolution
{
    public double? NominalFrequencyHz { get; init; }
    public int? SamplesPerCycle { get; init; }
    public ushort? SampleCounterWrap { get; init; }
    public SvTimebaseSource Source { get; init; } = SvTimebaseSource.Unknown;
    public string Reason { get; init; } = string.Empty;
    public bool IsResolved => NominalFrequencyHz.HasValue && SamplesPerCycle.HasValue;
}

public enum SvTimebaseSource
{
    Unknown,
    TrustedContext,
    DeclaredSamplesPerPeriod,
    DeclaredSamplesPerSecond,
    ObservedLegacyProtectionRate
}

/// <summary>
/// Resolves frequency, samples-per-cycle and sample-counter wrap without silently assuming 50 Hz.
/// </summary>
public static class SvTimebaseResolver
{
    private static readonly double[] NominalFrequencyCandidates = [50.0, 60.0];
    private const double FrequencyToleranceHz = 1.0;
    private const double RateToleranceFraction = 0.02;

    public static SvTimebaseResolution Resolve(SvTimebaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.TrustedNominalFrequencyHz is > 0 &&
            TryResolveSamplesPerCycle(evidence, evidence.TrustedNominalFrequencyHz.Value, out var trustedSamplesPerCycle, out var trustedWrap))
        {
            return new SvTimebaseResolution
            {
                NominalFrequencyHz = evidence.TrustedNominalFrequencyHz,
                SamplesPerCycle = trustedSamplesPerCycle,
                SampleCounterWrap = trustedWrap,
                Source = SvTimebaseSource.TrustedContext,
                Reason = "Nominal frequency was supplied by trusted configuration context."
            };
        }

        if (evidence.DeclaredSampleMode == 0 && evidence.DeclaredSampleRate is > 0)
        {
            var samplesPerCycle = evidence.DeclaredSampleRate.Value;
            var estimatedFrequency = evidence.ObservedSamplesPerSecond.HasValue
                ? SnapNominalFrequency(evidence.ObservedSamplesPerSecond.Value / samplesPerCycle)
                : null;
            var wrap = estimatedFrequency.HasValue
                ? ToCounterWrap(samplesPerCycle * estimatedFrequency.Value)
                : null;

            return new SvTimebaseResolution
            {
                NominalFrequencyHz = estimatedFrequency,
                SamplesPerCycle = samplesPerCycle,
                SampleCounterWrap = wrap,
                Source = SvTimebaseSource.DeclaredSamplesPerPeriod,
                Reason = estimatedFrequency.HasValue
                    ? "Samples-per-period was declared and nominal frequency was confirmed by the observed rate."
                    : "Samples-per-period was declared; nominal frequency remains unknown until rate evidence is available."
            };
        }

        if (evidence.DeclaredSampleMode == 1 && evidence.DeclaredSampleRate is > 0)
        {
            var samplesPerSecond = evidence.DeclaredSampleRate.Value;
            var legacy = ResolveLegacyProtectionRate(samplesPerSecond, evidence.IsFixedLegacyProtectionLayout);
            if (legacy is not null)
                return legacy with { Source = SvTimebaseSource.DeclaredSamplesPerSecond };

            return new SvTimebaseResolution
            {
                SampleCounterWrap = ToCounterWrap(samplesPerSecond),
                Source = SvTimebaseSource.DeclaredSamplesPerSecond,
                Reason = "Samples-per-second was declared, but samples-per-cycle cannot be inferred safely for an unknown profile."
            };
        }

        if (evidence.ObservedSamplesPerSecond is > 0)
        {
            var legacy = ResolveLegacyProtectionRate(evidence.ObservedSamplesPerSecond.Value, evidence.IsFixedLegacyProtectionLayout);
            if (legacy is not null)
                return legacy with { Source = SvTimebaseSource.ObservedLegacyProtectionRate };
        }

        return new SvTimebaseResolution
        {
            Source = SvTimebaseSource.Unknown,
            Reason = "No trustworthy timebase could be resolved without making a hidden 50/60 Hz assumption."
        };
    }

    private static SvTimebaseResolution? ResolveLegacyProtectionRate(double samplesPerSecond, bool fixedLegacyLayout)
    {
        if (!fixedLegacyLayout)
            return null;

        foreach (var nominal in NominalFrequencyCandidates)
        {
            const int samplesPerCycle = 80;
            var expected = nominal * samplesPerCycle;
            if (Math.Abs(samplesPerSecond - expected) > expected * RateToleranceFraction)
                continue;

            return new SvTimebaseResolution
            {
                NominalFrequencyHz = nominal,
                SamplesPerCycle = samplesPerCycle,
                SampleCounterWrap = ToCounterWrap(expected),
                Reason = $"The fixed protection layout and observed/declared rate match {samplesPerCycle} samples/cycle at {nominal:0} Hz."
            };
        }

        return null;
    }

    private static bool TryResolveSamplesPerCycle(
        SvTimebaseEvidence evidence,
        double nominalFrequencyHz,
        out int samplesPerCycle,
        out ushort? wrap)
    {
        samplesPerCycle = 0;
        wrap = null;

        if (evidence.DeclaredSampleMode == 0 && evidence.DeclaredSampleRate is > 0)
        {
            samplesPerCycle = evidence.DeclaredSampleRate.Value;
            wrap = ToCounterWrap(samplesPerCycle * nominalFrequencyHz);
            return true;
        }

        var samplesPerSecond = evidence.DeclaredSampleMode == 1 && evidence.DeclaredSampleRate is > 0
            ? evidence.DeclaredSampleRate.Value
            : evidence.ObservedSamplesPerSecond;
        if (!samplesPerSecond.HasValue || samplesPerSecond.Value <= 0)
            return false;

        var calculated = samplesPerSecond.Value / nominalFrequencyHz;
        var rounded = (int)Math.Round(calculated);
        if (rounded <= 0 || Math.Abs(calculated - rounded) > 0.05)
            return false;

        samplesPerCycle = rounded;
        wrap = ToCounterWrap(samplesPerSecond.Value);
        return true;
    }

    private static double? SnapNominalFrequency(double measured)
    {
        foreach (var nominal in NominalFrequencyCandidates)
        {
            if (Math.Abs(measured - nominal) <= FrequencyToleranceHz)
                return nominal;
        }

        return null;
    }

    private static ushort? ToCounterWrap(double samplesPerSecond)
    {
        if (samplesPerSecond <= 1 || samplesPerSecond > ushort.MaxValue)
            return null;
        return (ushort)Math.Round(samplesPerSecond);
    }
}
