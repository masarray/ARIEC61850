namespace AR.Iec61850.SampledValues.Measurements;

public enum SvMeasurementValueDomain
{
    Unknown,
    RawCount,
    PrimaryEngineering,
    SecondaryEquivalent
}

public enum SvRatioSource
{
    Unknown,
    Scl,
    DeviceConfiguration,
    Manual,
    DeviceValidated
}

/// <summary>
/// Explicit instrument-transformer context. It is never inferred from vendor identity or nominal amplitude.
/// </summary>
public sealed record SvMeasurementRatio
{
    public double PrimaryNominal { get; init; }
    public double SecondaryNominal { get; init; }
    public string Unit { get; init; } = string.Empty;
    public SvRatioSource Source { get; init; } = SvRatioSource.Unknown;
    public string Reference { get; init; } = string.Empty;

    public bool IsValid => double.IsFinite(PrimaryNominal) &&
                           double.IsFinite(SecondaryNominal) &&
                           PrimaryNominal > 0 &&
                           SecondaryNominal > 0;

    public double Ratio => IsValid ? PrimaryNominal / SecondaryNominal : double.NaN;

    public double PrimaryToSecondary(double primaryValue)
    {
        EnsureValid();
        return primaryValue * SecondaryNominal / PrimaryNominal;
    }

    public double SecondaryToPrimary(double secondaryValue)
    {
        EnsureValid();
        return secondaryValue * PrimaryNominal / SecondaryNominal;
    }

    private void EnsureValid()
    {
        if (!IsValid)
            throw new InvalidOperationException("A positive primary and secondary nominal value is required for conversion.");
    }
}

public sealed record SvMeasurementDomainValue
{
    public double WireValue { get; init; }
    public string Unit { get; init; } = string.Empty;
    public SvMeasurementValueDomain WireDomain { get; init; } = SvMeasurementValueDomain.Unknown;
    public double? PrimaryValue { get; init; }
    public double? SecondaryEquivalentValue { get; init; }
    public SvRatioSource RatioSource { get; init; } = SvRatioSource.Unknown;
    public string RatioReference { get; init; } = string.Empty;
    public string Diagnostic { get; init; } = string.Empty;
}

/// <summary>
/// Produces primary and secondary-equivalent values without silently assuming a CT/VT ratio.
/// Installed-base 9-2LE engineering values are treated as primary only when the caller declares that domain.
/// </summary>
public static class SvMeasurementDomainResolver
{
    public static SvMeasurementDomainValue Resolve(
        double wireValue,
        string unit,
        SvMeasurementValueDomain wireDomain,
        SvMeasurementRatio? ratio)
    {
        if (!double.IsFinite(wireValue))
            throw new ArgumentOutOfRangeException(nameof(wireValue), "Measurement value must be finite.");

        if (wireDomain is SvMeasurementValueDomain.Unknown or SvMeasurementValueDomain.RawCount)
        {
            return new SvMeasurementDomainValue
            {
                WireValue = wireValue,
                Unit = unit,
                WireDomain = wireDomain,
                Diagnostic = "Primary and secondary values are unavailable because the wire measurement domain is not established."
            };
        }

        if (ratio is null || !ratio.IsValid)
        {
            return new SvMeasurementDomainValue
            {
                WireValue = wireValue,
                Unit = unit,
                WireDomain = wireDomain,
                PrimaryValue = wireDomain == SvMeasurementValueDomain.PrimaryEngineering ? wireValue : null,
                SecondaryEquivalentValue = wireDomain == SvMeasurementValueDomain.SecondaryEquivalent ? wireValue : null,
                Diagnostic = "Only the declared wire-domain value is available; no verified CT/VT ratio was supplied."
            };
        }

        return wireDomain switch
        {
            SvMeasurementValueDomain.PrimaryEngineering => new SvMeasurementDomainValue
            {
                WireValue = wireValue,
                Unit = unit,
                WireDomain = wireDomain,
                PrimaryValue = wireValue,
                SecondaryEquivalentValue = ratio.PrimaryToSecondary(wireValue),
                RatioSource = ratio.Source,
                RatioReference = ratio.Reference,
                Diagnostic = "Secondary-equivalent value was calculated from explicit ratio context."
            },
            SvMeasurementValueDomain.SecondaryEquivalent => new SvMeasurementDomainValue
            {
                WireValue = wireValue,
                Unit = unit,
                WireDomain = wireDomain,
                PrimaryValue = ratio.SecondaryToPrimary(wireValue),
                SecondaryEquivalentValue = wireValue,
                RatioSource = ratio.Source,
                RatioReference = ratio.Reference,
                Diagnostic = "Primary value was calculated from explicit ratio context."
            },
            _ => throw new ArgumentOutOfRangeException(nameof(wireDomain), wireDomain, "Unsupported measurement domain.")
        };
    }
}
