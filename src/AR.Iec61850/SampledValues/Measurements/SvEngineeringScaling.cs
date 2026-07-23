namespace AR.Iec61850.SampledValues.Measurements;

/// <summary>
/// Describes how a decoded Sampled Values number is converted into an engineering value.
/// The source and confidence are deliberately explicit so unknown traffic is never labelled A or V by guesswork.
/// </summary>
public sealed record SvEngineeringScale
{
    public static SvEngineeringScale RawOnly(string reason) => new()
    {
        Multiplier = 1.0,
        Unit = "count",
        Source = SvEngineeringScaleSource.RawOnly,
        Confidence = SvEngineeringScaleConfidence.Unknown,
        Reason = reason
    };

    public double Multiplier { get; init; } = 1.0;
    public double Offset { get; init; }
    public string Unit { get; init; } = "count";
    public SvEngineeringScaleSource Source { get; init; } = SvEngineeringScaleSource.RawOnly;
    public SvEngineeringScaleConfidence Confidence { get; init; } = SvEngineeringScaleConfidence.Unknown;
    public string Reason { get; init; } = string.Empty;
    public bool HasEngineeringUnit => Source != SvEngineeringScaleSource.RawOnly;

    public double Apply(double rawValue) => (rawValue * Multiplier) + Offset;
}

public enum SvEngineeringScaleSource
{
    RawOnly,

    /// <summary>
    /// Retained for evidence-document compatibility. Generic receive-side analysis no longer activates
    /// engineering scaling from packet shape or observed rate alone.
    /// </summary>
    Legacy92LeStyleStructuralInference,

    SclBackedLegacy92LeStyle,
    ManualOverride
}

public enum SvEngineeringScaleConfidence
{
    Unknown,
    Inferred,
    SclBacked,
    DeviceValidated
}

/// <summary>
/// Evidence supplied to the scale resolver. This is intentionally vendor-neutral.
/// Product identity may be retained in an evidence report, but it is not a scaling rule.
/// </summary>
public sealed record SvEngineeringScaleEvidence
{
    public string Channel { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool IsSclBound { get; init; }
    public bool IsFixedFourCurrentFourVoltageLayout { get; init; }
    public int AnalogChannelCount { get; init; }
    public int PayloadBytesPerAsdu { get; init; }
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public double? ObservedSamplesPerSecond { get; init; }
}

/// <summary>
/// Resolves conservative engineering scaling for an explicitly SCL-bound installed-base
/// 4-current/4-voltage protection layout. Packet shape, rate, product name, APPID, MAC address,
/// svID, and amplitude are never sufficient by themselves.
/// </summary>
public static class SvEngineeringScaleResolver
{
    private const double CurrentAmperesPerCount = 0.001;
    private const double VoltageVoltsPerCount = 0.01;
    private const double RateToleranceFraction = 0.02;

    public static SvEngineeringScale Resolve(SvEngineeringScaleEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var domain = ResolveDomain(evidence.Channel, evidence.Kind);
        if (domain == SvMeasurementDomain.Unknown)
            return SvEngineeringScale.RawOnly("The SCL-derived channel semantics do not prove a current or voltage domain.");

        if (!evidence.IsSclBound)
        {
            return SvEngineeringScale.RawOnly(
                "No SCL dataset mapping is bound. Generic packet inspection preserves raw counts and does not infer engineering units.");
        }

        var fixedLayout = evidence.IsFixedFourCurrentFourVoltageLayout &&
                          evidence.AnalogChannelCount == 8 &&
                          evidence.PayloadBytesPerAsdu == 64;
        if (!fixedLayout)
        {
            return SvEngineeringScale.RawOnly(
                "The SCL-mapped payload is not the fixed 4-current/4-voltage value-quality layout required by this scaling rule.");
        }

        if (!HasProtectionRateEvidence(evidence))
        {
            return SvEngineeringScale.RawOnly(
                "The fixed SCL layout is visible, but declared or observed protection-rate evidence is insufficient for the installed-base scaling rule.");
        }

        var reason =
            "SCL dataset mapping, fixed 4-current/4-voltage structure, and protection-rate evidence support the installed-base 9-2LE-style scale.";

        return domain switch
        {
            SvMeasurementDomain.Current => new SvEngineeringScale
            {
                Multiplier = CurrentAmperesPerCount,
                Unit = "A",
                Source = SvEngineeringScaleSource.SclBackedLegacy92LeStyle,
                Confidence = SvEngineeringScaleConfidence.SclBacked,
                Reason = reason
            },
            SvMeasurementDomain.Voltage => new SvEngineeringScale
            {
                Multiplier = VoltageVoltsPerCount,
                Unit = "V",
                Source = SvEngineeringScaleSource.SclBackedLegacy92LeStyle,
                Confidence = SvEngineeringScaleConfidence.SclBacked,
                Reason = reason
            },
            _ => SvEngineeringScale.RawOnly("Unsupported measurement domain.")
        };
    }

    public static SvMeasurementDomain ResolveDomain(string channel, string kind)
    {
        var normalizedKind = kind?.Trim() ?? string.Empty;
        if (normalizedKind.Contains("voltage", StringComparison.OrdinalIgnoreCase))
            return SvMeasurementDomain.Voltage;
        if (normalizedKind.Contains("current", StringComparison.OrdinalIgnoreCase))
            return SvMeasurementDomain.Current;

        var normalizedChannel = channel?.Trim() ?? string.Empty;
        if (normalizedChannel.StartsWith("V", StringComparison.OrdinalIgnoreCase) ||
            normalizedChannel.Contains("TVTR", StringComparison.OrdinalIgnoreCase) ||
            normalizedChannel.Contains("VolSv", StringComparison.OrdinalIgnoreCase))
            return SvMeasurementDomain.Voltage;
        if (normalizedChannel.StartsWith("I", StringComparison.OrdinalIgnoreCase) ||
            normalizedChannel.Contains("TCTR", StringComparison.OrdinalIgnoreCase) ||
            normalizedChannel.Contains("AmpSv", StringComparison.OrdinalIgnoreCase))
            return SvMeasurementDomain.Current;

        return SvMeasurementDomain.Unknown;
    }

    private static bool HasProtectionRateEvidence(SvEngineeringScaleEvidence evidence)
    {
        if (evidence.DeclaredSampleMode == 0 && evidence.DeclaredSampleRate == 80)
            return true;

        if (evidence.DeclaredSampleMode == 1 &&
            IsNearOneOf(evidence.DeclaredSampleRate, 4_000, 4_800))
            return true;

        return IsNearOneOf(evidence.ObservedSamplesPerSecond, 4_000, 4_800);
    }

    private static bool IsNearOneOf(double? value, params double[] candidates)
    {
        if (!value.HasValue || value.Value <= 0)
            return false;

        return candidates.Any(candidate =>
            Math.Abs(value.Value - candidate) <= candidate * RateToleranceFraction);
    }
}

public enum SvMeasurementDomain
{
    Unknown,
    Current,
    Voltage
}
