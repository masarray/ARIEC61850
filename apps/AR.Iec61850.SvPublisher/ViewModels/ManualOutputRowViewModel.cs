using System.Globalization;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class ManualOutputRowViewModel : ObservableObject
{
    public const string MagnitudePropertyName = nameof(Magnitude);
    public const string AngleDegreesPropertyName = nameof(AngleDegrees);
    public const string FrequencyHzPropertyName = nameof(FrequencyHz);

    private const string NumericFormat = "0.###";
    private readonly Action<ManualOutputRowViewModel, string> _onChanged;
    private bool _isEnabled = true;
    private string _name;
    private double _magnitude;
    private double _angleDegrees;
    private double _frequencyHz;
    private string _magnitudeText = string.Empty;
    private string _angleDegreesText = string.Empty;
    private string _frequencyHzText = string.Empty;

    public ManualOutputRowViewModel(
        string key,
        string name,
        string kind,
        string unit,
        double magnitude,
        double angleDegrees,
        double frequencyHz,
        bool isEnabled,
        Action<ManualOutputRowViewModel, string> onChanged)
    {
        Key = key;
        _name = name;
        Kind = kind;
        Unit = unit;
        _magnitude = CoerceMagnitude(magnitude);
        _angleDegrees = NormalizeDegrees(angleDegrees);
        _frequencyHz = CoerceFrequency(frequencyHz);
        _isEnabled = isEnabled;
        _onChanged = onChanged;
        RefreshTexts();
    }

    public string Key { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Kind { get; }
    public string Unit { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                _onChanged(this, nameof(IsEnabled));
        }
    }

    public double Magnitude
    {
        get => _magnitude;
        set
        {
            var coerced = CoerceMagnitude(value);
            if (SetProperty(ref _magnitude, coerced))
            {
                MagnitudeText = FormatNumber(coerced);
                _onChanged(this, nameof(Magnitude));
            }
            else
            {
                MagnitudeText = FormatNumber(coerced);
            }
        }
    }

    public double AngleDegrees
    {
        get => _angleDegrees;
        set
        {
            var coerced = NormalizeDegrees(value);
            if (SetProperty(ref _angleDegrees, coerced))
            {
                AngleDegreesText = FormatNumber(coerced);
                _onChanged(this, nameof(AngleDegrees));
            }
            else
            {
                AngleDegreesText = FormatNumber(coerced);
            }
        }
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            var coerced = CoerceFrequency(value);
            if (SetProperty(ref _frequencyHz, coerced))
            {
                FrequencyHzText = FormatNumber(coerced);
                _onChanged(this, nameof(FrequencyHz));
            }
            else
            {
                FrequencyHzText = FormatNumber(coerced);
            }
        }
    }

    public string MagnitudeText
    {
        get => _magnitudeText;
        set => SetProperty(ref _magnitudeText, value);
    }

    public string AngleDegreesText
    {
        get => _angleDegreesText;
        set => SetProperty(ref _angleDegreesText, value);
    }

    public string FrequencyHzText
    {
        get => _frequencyHzText;
        set => SetProperty(ref _frequencyHzText, value);
    }

    public bool CommitText(string propertyName, out string warning)
    {
        warning = string.Empty;
        return propertyName switch
        {
            MagnitudePropertyName => CommitMagnitude(out warning),
            AngleDegreesPropertyName => CommitAngle(out warning),
            FrequencyHzPropertyName => CommitFrequency(out warning),
            _ => true
        };
    }

    public void RejectText(string propertyName)
    {
        switch (propertyName)
        {
            case MagnitudePropertyName:
                MagnitudeText = FormatNumber(Magnitude);
                break;
            case AngleDegreesPropertyName:
                AngleDegreesText = FormatNumber(AngleDegrees);
                break;
            case FrequencyHzPropertyName:
                FrequencyHzText = FormatNumber(FrequencyHz);
                break;
        }
    }

    public void RefreshTexts()
    {
        MagnitudeText = FormatNumber(Magnitude);
        AngleDegreesText = FormatNumber(AngleDegrees);
        FrequencyHzText = FormatNumber(FrequencyHz);
    }

    private bool CommitMagnitude(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(MagnitudeText, out var value) || !IsFinite(value) || value < 0 || value > 1_000_000_000)
        {
            warning = $"Invalid value for {Name}. Magnitude must be a numeric value from 0 to 1,000,000,000.";
            RejectText(MagnitudePropertyName);
            return false;
        }

        Magnitude = value;
        return true;
    }

    private bool CommitAngle(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(AngleDegreesText, out var value) || !IsFinite(value) || Math.Abs(value) > 360_000)
        {
            warning = $"Invalid angle for {Name}. Angle must be a numeric value within ±360,000 degrees.";
            RejectText(AngleDegreesPropertyName);
            return false;
        }

        AngleDegrees = value;
        return true;
    }

    private bool CommitFrequency(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(FrequencyHzText, out var value) || !IsFinite(value) || value < 0 || value > 5_000)
        {
            warning = $"Invalid frequency for {Name}. Frequency must be a numeric value from 0 to 5000 Hz. Use 0 Hz for DC.";
            RejectText(FrequencyHzPropertyName);
            return false;
        }

        FrequencyHz = value;
        return true;
    }

    private static bool TryParseOperatorNumber(string text, out double value)
    {
        var input = (text ?? string.Empty).Trim();
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return true;

        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double CoerceMagnitude(double value)
        => IsFinite(value) ? Math.Clamp(value, 0, 1_000_000_000) : 0;

    private static double CoerceFrequency(double value)
        => IsFinite(value) && value >= 0 ? Math.Min(value, 5_000) : 50;

    private static double NormalizeDegrees(double degrees)
    {
        if (!IsFinite(degrees))
            return 0;

        while (degrees > 180)
            degrees -= 360;
        while (degrees <= -180)
            degrees += 360;
        return Math.Round(degrees, 6);
    }

    private static string FormatNumber(double value)
        => value.ToString(NumericFormat, CultureInfo.InvariantCulture);
}
