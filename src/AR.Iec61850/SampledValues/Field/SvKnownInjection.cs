namespace AR.Iec61850.SampledValues.Field;

public enum SvKnownInjectionResultState
{
    Unresolved,
    Review,
    Pass,
    Fail
}

public sealed record SvKnownInjectionExpectation
{
    public string Channel { get; init; } = string.Empty;
    public double ExpectedRms { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public double? ExpectedAngleDegrees { get; init; }
    public double? ExpectedFrequencyHz { get; init; }
    public double? AmplitudeTolerancePercent { get; init; }
    public double? AngleToleranceDegrees { get; init; }
    public double? FrequencyToleranceHz { get; init; }
}

public sealed record SvKnownInjectionMeasurement
{
    public double MeasuredRms { get; init; }
    public double? MeasuredAngleDegrees { get; init; }
    public double? MeasuredFrequencyHz { get; init; }
}

public sealed record SvKnownInjectionComparison
{
    public SvKnownInjectionResultState State { get; init; }
    public double AbsoluteAmplitudeError { get; init; }
    public double? AmplitudeErrorPercent { get; init; }
    public double? AngleErrorDegrees { get; init; }
    public double? FrequencyErrorHz { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public static class SvKnownInjectionComparator
{
    public static SvKnownInjectionComparison Compare(
        SvKnownInjectionExpectation expected,
        SvKnownInjectionMeasurement measured)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(measured);
        if (string.IsNullOrWhiteSpace(expected.Channel) || !double.IsFinite(expected.ExpectedRms) || expected.ExpectedRms < 0 ||
            !double.IsFinite(measured.MeasuredRms) || measured.MeasuredRms < 0)
            return new SvKnownInjectionComparison { State = SvKnownInjectionResultState.Unresolved, Diagnostics = ["Expected and measured RMS must be finite non-negative values and channel must be identified."] };

        var absolute = measured.MeasuredRms - expected.ExpectedRms;
        double? percent = Math.Abs(expected.ExpectedRms) <= double.Epsilon
            ? null
            : absolute / expected.ExpectedRms * 100.0;
        double? angleError = expected.ExpectedAngleDegrees.HasValue && measured.MeasuredAngleDegrees.HasValue
            ? NormalizeAngle(measured.MeasuredAngleDegrees.Value - expected.ExpectedAngleDegrees.Value)
            : null;
        double? frequencyError = expected.ExpectedFrequencyHz.HasValue && measured.MeasuredFrequencyHz.HasValue
            ? measured.MeasuredFrequencyHz.Value - expected.ExpectedFrequencyHz.Value
            : null;

        var diagnostics = new List<string>();
        var toleranceSpecified = false;
        var failed = false;
        if (expected.AmplitudeTolerancePercent.HasValue)
        {
            toleranceSpecified = true;
            if (!percent.HasValue || Math.Abs(percent.Value) > expected.AmplitudeTolerancePercent.Value)
            {
                failed = true;
                diagnostics.Add("Amplitude error exceeds tolerance.");
            }
        }
        if (expected.AngleToleranceDegrees.HasValue)
        {
            toleranceSpecified = true;
            if (!angleError.HasValue || Math.Abs(angleError.Value) > expected.AngleToleranceDegrees.Value)
            {
                failed = true;
                diagnostics.Add("Angle error exceeds tolerance.");
            }
        }
        if (expected.FrequencyToleranceHz.HasValue)
        {
            toleranceSpecified = true;
            if (!frequencyError.HasValue || Math.Abs(frequencyError.Value) > expected.FrequencyToleranceHz.Value)
            {
                failed = true;
                diagnostics.Add("Frequency error exceeds tolerance.");
            }
        }

        return new SvKnownInjectionComparison
        {
            State = !toleranceSpecified ? SvKnownInjectionResultState.Review : failed ? SvKnownInjectionResultState.Fail : SvKnownInjectionResultState.Pass,
            AbsoluteAmplitudeError = absolute,
            AmplitudeErrorPercent = percent,
            AngleErrorDegrees = angleError,
            FrequencyErrorHz = frequencyError,
            Diagnostics = diagnostics
        };
    }

    private static double NormalizeAngle(double value)
    {
        while (value > 180) value -= 360;
        while (value <= -180) value += 360;
        return value;
    }
}
