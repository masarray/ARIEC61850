using System.Globalization;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class RampStateViewModel : ObservableObject
{
    public const string FromPropertyName = nameof(From);
    public const string ToPropertyName = nameof(To);
    public const string StepPropertyName = nameof(Step);
    public const string StepTimeSecondsPropertyName = nameof(StepTimeSeconds);
    public const string StepsPropertyName = nameof(Steps);
    public const string TimeSecondsPropertyName = nameof(TimeSeconds);

    private const string NumericFormat = "0.000";
    private string _name;
    private string _signalKey;
    private string _signalName;
    private string _quantity;
    private double _from;
    private double _to;
    private double _step;
    private double _stepTimeSeconds;
    private int _steps;
    private double _timeSeconds;
    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private string _stepText = string.Empty;
    private string _stepTimeText = string.Empty;
    private string _stepsText = string.Empty;
    private string _timeText = string.Empty;

    public RampStateViewModel(
        string name,
        string signalKey,
        string signalName,
        string quantity,
        double from,
        double to,
        double step,
        double stepTimeSeconds,
        int steps,
        double timeSeconds)
    {
        _name = name;
        _signalKey = signalKey;
        _signalName = signalName;
        _quantity = quantity;
        _from = CoerceMagnitude(from);
        _to = CoerceMagnitude(to);
        _step = CoerceSigned(step);
        _stepTimeSeconds = CoerceSeconds(stepTimeSeconds);
        _steps = Math.Max(1, steps);
        _timeSeconds = CoerceSeconds(timeSeconds);
        RefreshTexts();
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Comma-separated channel keys selected for this ramp, for example "Ia" or "Ia,Ib,Ic".
    /// A grouped ramp changes all listed channels together and leaves every other analog output at its base value.
    /// </summary>
    public string SignalKey
    {
        get => _signalKey;
        set
        {
            if (SetProperty(ref _signalKey, value))
            {
                OnPropertyChanged(nameof(SignalKeys));
                OnPropertyChanged(nameof(PrimarySignalKey));
                RefreshTexts();
                RaiseDerivedProperties();
            }
        }
    }

    public IReadOnlyList<string> SignalKeys =>
        SignalKey.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string PrimarySignalKey => SignalKeys.FirstOrDefault() ?? string.Empty;

    public bool AppliesToChannel(string channelKey) =>
        SignalKeys.Any(key => string.Equals(key, channelKey, StringComparison.OrdinalIgnoreCase));

    public string SignalName
    {
        get => _signalName;
        set => SetProperty(ref _signalName, value);
    }

    public string Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public double From
    {
        get => _from;
        set
        {
            var coerced = CoerceMagnitude(value);
            if (SetProperty(ref _from, coerced))
            {
                FromText = FormatElectrical(coerced);
                RaiseDerivedProperties();
            }
            else
            {
                FromText = FormatElectrical(coerced);
            }
        }
    }

    public double To
    {
        get => _to;
        set
        {
            var coerced = CoerceMagnitude(value);
            if (SetProperty(ref _to, coerced))
            {
                ToText = FormatElectrical(coerced);
                RaiseDerivedProperties();
            }
            else
            {
                ToText = FormatElectrical(coerced);
            }
        }
    }

    public double Step
    {
        get => _step;
        set
        {
            var coerced = CoerceSigned(value);
            if (SetProperty(ref _step, coerced))
            {
                StepText = FormatSignedElectrical(coerced);
                RaiseDerivedProperties();
            }
            else
            {
                StepText = FormatSignedElectrical(coerced);
            }
        }
    }

    public double StepTimeSeconds
    {
        get => _stepTimeSeconds;
        set
        {
            var coerced = CoerceSeconds(value);
            if (SetProperty(ref _stepTimeSeconds, coerced))
            {
                StepTimeText = FormatSeconds(coerced);
                TimeSeconds = coerced * Math.Max(1, Steps);
                RaiseDerivedProperties();
            }
            else
            {
                StepTimeText = FormatSeconds(coerced);
            }
        }
    }

    public int Steps
    {
        get => _steps;
        set
        {
            var coerced = Math.Max(1, value);
            if (SetProperty(ref _steps, coerced))
            {
                StepsText = coerced.ToString(CultureInfo.InvariantCulture);
                StepTimeSeconds = TimeSeconds / Math.Max(1, coerced);
                RaiseDerivedProperties();
            }
            else
            {
                StepsText = coerced.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public double TimeSeconds
    {
        get => _timeSeconds;
        set
        {
            var coerced = CoerceSeconds(value);
            if (SetProperty(ref _timeSeconds, coerced))
            {
                TimeText = FormatSeconds(coerced);
                if (Steps > 0)
                {
                    var stepTime = coerced / Math.Max(1, Steps);
                    if (Math.Abs(_stepTimeSeconds - stepTime) > 0.0000001)
                    {
                        _stepTimeSeconds = stepTime;
                        OnPropertyChanged(nameof(StepTimeSeconds));
                        StepTimeText = FormatSeconds(stepTime);
                    }
                }
                RaiseDerivedProperties();
            }
            else
            {
                TimeText = FormatSeconds(coerced);
            }
        }
    }

    public double Delta => To - From;

    public double SlopePerSecond => Math.Abs(TimeSeconds) < 0.000001 ? 0 : Delta / TimeSeconds;

    public string FromText
    {
        get => _fromText;
        set => SetProperty(ref _fromText, value);
    }

    public string ToText
    {
        get => _toText;
        set => SetProperty(ref _toText, value);
    }

    public string DeltaText => FormatSignedElectrical(Delta);

    public string StepText
    {
        get => _stepText;
        set => SetProperty(ref _stepText, value);
    }

    public string StepTimeText
    {
        get => _stepTimeText;
        set => SetProperty(ref _stepTimeText, value);
    }

    public string SlopeText => FormatSlope(SlopePerSecond);

    public string StepsText
    {
        get => _stepsText;
        set => SetProperty(ref _stepsText, value);
    }

    public string TimeText
    {
        get => _timeText;
        set => SetProperty(ref _timeText, value);
    }

    public bool CommitText(string propertyName, out string warning)
    {
        warning = string.Empty;
        return propertyName switch
        {
            FromPropertyName => CommitMagnitude(FromText, nameof(From), value => From = value, out warning),
            ToPropertyName => CommitMagnitude(ToText, nameof(To), value => To = value, out warning),
            StepPropertyName => CommitSignedStep(out warning),
            StepTimeSecondsPropertyName => CommitSeconds(StepTimeText, nameof(StepTimeSeconds), value => StepTimeSeconds = value, out warning),
            StepsPropertyName => CommitSteps(out warning),
            TimeSecondsPropertyName => CommitSeconds(TimeText, nameof(TimeSeconds), value => TimeSeconds = value, out warning),
            _ => true
        };
    }

    public void RejectText(string propertyName)
    {
        switch (propertyName)
        {
            case FromPropertyName:
                FromText = FormatElectrical(From);
                break;
            case ToPropertyName:
                ToText = FormatElectrical(To);
                break;
            case StepPropertyName:
                StepText = FormatSignedElectrical(Step);
                break;
            case StepTimeSecondsPropertyName:
                StepTimeText = FormatSeconds(StepTimeSeconds);
                break;
            case StepsPropertyName:
                StepsText = Steps.ToString(CultureInfo.InvariantCulture);
                break;
            case TimeSecondsPropertyName:
                TimeText = FormatSeconds(TimeSeconds);
                break;
        }
    }

    public void RefreshTexts()
    {
        FromText = FormatElectrical(From);
        ToText = FormatElectrical(To);
        StepText = FormatSignedElectrical(Step);
        StepTimeText = FormatSeconds(StepTimeSeconds);
        StepsText = Steps.ToString(CultureInfo.InvariantCulture);
        TimeText = FormatSeconds(TimeSeconds);
        RaiseDerivedProperties();
    }

    private bool CommitMagnitude(string text, string propertyName, Action<double> setter, out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(text, Unit, out var value) || !IsFinite(value) || value < 0 || value > 1_000_000_000)
        {
            warning = $"Invalid ramp value for {Name}. {propertyName} must be a numeric value from 0 to 1,000,000,000 {Unit}.";
            RejectText(propertyName);
            return false;
        }

        setter(value);
        return true;
    }

    private bool CommitSignedStep(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(StepText, Unit, out var value) || !IsFinite(value) || Math.Abs(value) > 1_000_000_000)
        {
            warning = $"Invalid ramp step for {Name}. Step must be a numeric value within ±1,000,000,000 {Unit}.";
            RejectText(StepPropertyName);
            return false;
        }

        Step = value;
        return true;
    }

    private bool CommitSeconds(string text, string propertyName, Action<double> setter, out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(text, "s", out var value) || !IsFinite(value) || value <= 0 || value > 86_400)
        {
            warning = $"Invalid ramp time for {Name}. Time must be a numeric value greater than 0 and not more than 86,400 s.";
            RejectText(propertyName);
            return false;
        }

        setter(value);
        return true;
    }

    private bool CommitSteps(out string warning)
    {
        warning = string.Empty;
        if (!int.TryParse(NormalizeOperatorInput(StepsText, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1 || value > 1_000_000)
        {
            warning = $"Invalid steps value for {Name}. Steps must be an integer from 1 to 1,000,000.";
            RejectText(StepsPropertyName);
            return false;
        }

        Steps = value;
        return true;
    }

    private void RaiseDerivedProperties()
    {
        OnPropertyChanged(nameof(Delta));
        OnPropertyChanged(nameof(SlopePerSecond));
        OnPropertyChanged(nameof(DeltaText));
        OnPropertyChanged(nameof(SlopeText));
    }

    private string Unit => PrimarySignalKey.StartsWith("V", StringComparison.OrdinalIgnoreCase) ? "V" : "A";

    private string FormatElectrical(double value)
        => $"{FormatNumber(value)} {Unit}";

    private string FormatSignedElectrical(double value)
        => $"{(value >= 0 ? "+" : string.Empty)}{FormatNumber(value)} {Unit}";

    private static string FormatSeconds(double value)
        => $"{FormatNumber(value)} s";

    private string FormatSlope(double value)
        => $"{FormatNumber(value)} {Unit}/s";

    private static string FormatNumber(double value)
        => value.ToString(NumericFormat, CultureInfo.InvariantCulture);

    private static bool TryParseOperatorNumber(string text, string unit, out double value)
    {
        var input = NormalizeOperatorInput(text, unit);
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return true;

        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeOperatorInput(string text, string unit)
    {
        var input = (text ?? string.Empty).Trim();
        input = input.Replace(" ", string.Empty, StringComparison.Ordinal);
        input = input.Replace("seconds", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("second", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("sec", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("Hz", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("V/s", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("A/s", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(unit))
            input = input.Replace(unit, string.Empty, StringComparison.OrdinalIgnoreCase);

        return input.Trim();
    }

    private static double CoerceMagnitude(double value)
        => IsFinite(value) ? Math.Clamp(value, 0, 1_000_000_000) : 0;

    private static double CoerceSigned(double value)
        => IsFinite(value) ? Math.Clamp(value, -1_000_000_000, 1_000_000_000) : 0;

    private static double CoerceSeconds(double value)
        => IsFinite(value) && value > 0 ? Math.Min(value, 86_400) : 0.001;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
