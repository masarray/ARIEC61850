using System.Globalization;
using AR.Iec61850.SvPublisher.Models;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SequenceStateViewModel : ObservableObject
{
    public const string DurationSecondsPropertyName = nameof(DurationSeconds);
    public const string CurrentMagnitudePropertyName = "CurrentMagnitude";
    public const string VoltageMagnitudePropertyName = "VoltageMagnitude";
    public const string PhaseAAnglePropertyName = "PhaseAAngle";
    public const string PhaseBAnglePropertyName = "PhaseBAngle";
    public const string PhaseCAnglePropertyName = "PhaseCAngle";
    public const string FrequencyHzPropertyName = nameof(FrequencyHz);

    private const string NumericFormat = "0.000";
    private const double NominalVoltageLn = 57.735;

    private string _name;
    private double _durationSeconds;
    private double _currentScale;
    private double _voltageScale;
    private double _angleShiftDegrees;
    private double _frequencyHz;
    private bool _isSelected;
    private string _durationText = string.Empty;
    private string _currentText = string.Empty;
    private string _voltageMagnitudeText = string.Empty;
    private string _angleText = string.Empty;
    private string _phaseAAngleText = string.Empty;
    private string _phaseBAngleText = string.Empty;
    private string _phaseCAngleText = string.Empty;
    private string _frequencyText = string.Empty;

    public SequenceStateViewModel(
        string name,
        double durationSeconds,
        double currentScale,
        double voltageScale,
        double angleShiftDegrees,
        double frequencyHz)
    {
        _name = name;
        _durationSeconds = CoerceSeconds(durationSeconds);
        _currentScale = CoerceMagnitude(currentScale);
        _voltageScale = CoerceMagnitude(voltageScale);
        _angleShiftDegrees = NormalizeDegrees(angleShiftDegrees);
        _frequencyHz = CoerceFrequency(frequencyHz);
        RefreshTexts();
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            var coerced = CoerceSeconds(value);
            if (SetProperty(ref _durationSeconds, coerced))
                DurationText = FormatSeconds(coerced);
            else
                DurationText = FormatSeconds(coerced);
        }
    }

    /// <summary>
    /// Absolute RMS current magnitude for the state. The property name is retained for compatibility with
    /// saved snapshots, but the operator-facing workflow treats it as an ampere value, not a UI scale field.
    /// </summary>
    public double CurrentScale
    {
        get => _currentScale;
        set
        {
            var coerced = CoerceMagnitude(value);
            if (SetProperty(ref _currentScale, coerced))
                CurrentText = FormatAmpere(coerced);
            else
                CurrentText = FormatAmpere(coerced);
        }
    }

    /// <summary>
    /// Voltage is stored as per-unit of 57.735 V L-N for snapshot compatibility. The UI exposes the
    /// resulting absolute RMS voltage with a V unit so the state table behaves like a test-set output table.
    /// </summary>
    public double VoltageScale
    {
        get => _voltageScale;
        set
        {
            var coerced = CoerceMagnitude(value);
            if (SetProperty(ref _voltageScale, coerced))
            {
                VoltageMagnitudeText = FormatVolt(VoltageMagnitude);
                OnPropertyChanged(nameof(VoltageText));
                OnPropertyChanged(nameof(VoltageMagnitude));
            }
            else
            {
                VoltageMagnitudeText = FormatVolt(VoltageMagnitude);
            }
        }
    }

    public double VoltageMagnitude => NominalVoltageLn * Math.Max(0, VoltageScale);

    public double AngleShiftDegrees
    {
        get => _angleShiftDegrees;
        set
        {
            var coerced = NormalizeDegrees(value);
            if (SetProperty(ref _angleShiftDegrees, coerced))
                RefreshAngleTexts();
            else
                RefreshAngleTexts();
        }
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            var coerced = CoerceFrequency(value);
            if (SetProperty(ref _frequencyHz, coerced))
                FrequencyText = FormatHertz(coerced);
            else
                FrequencyText = FormatHertz(coerced);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value);
    }

    public string CurrentText
    {
        get => _currentText;
        set => SetProperty(ref _currentText, value);
    }

    public string VoltageText => $"{VoltageScale:0.000} pu";

    public string VoltageMagnitudeText
    {
        get => _voltageMagnitudeText;
        set => SetProperty(ref _voltageMagnitudeText, value);
    }

    public string AngleText
    {
        get => _angleText;
        set => SetProperty(ref _angleText, value);
    }

    public string PhaseAAngleText
    {
        get => _phaseAAngleText;
        set => SetProperty(ref _phaseAAngleText, value);
    }

    public string PhaseBAngleText
    {
        get => _phaseBAngleText;
        set => SetProperty(ref _phaseBAngleText, value);
    }

    public string PhaseCAngleText
    {
        get => _phaseCAngleText;
        set => SetProperty(ref _phaseCAngleText, value);
    }

    public string FrequencyText
    {
        get => _frequencyText;
        set => SetProperty(ref _frequencyText, value);
    }

    public bool CommitText(string propertyName, out string warning)
    {
        warning = string.Empty;
        return propertyName switch
        {
            DurationSecondsPropertyName => CommitSeconds(out warning),
            CurrentMagnitudePropertyName => CommitCurrent(out warning),
            VoltageMagnitudePropertyName => CommitVoltage(out warning),
            PhaseAAnglePropertyName => CommitAngle(PhaseAAngleText, 0, out warning),
            PhaseBAnglePropertyName => CommitAngle(PhaseBAngleText, 120, out warning),
            PhaseCAnglePropertyName => CommitAngle(PhaseCAngleText, -120, out warning),
            FrequencyHzPropertyName => CommitFrequency(out warning),
            _ => true
        };
    }

    public void RejectText(string propertyName)
    {
        switch (propertyName)
        {
            case DurationSecondsPropertyName:
                DurationText = FormatSeconds(DurationSeconds);
                break;
            case CurrentMagnitudePropertyName:
                CurrentText = FormatAmpere(CurrentScale);
                break;
            case VoltageMagnitudePropertyName:
                VoltageMagnitudeText = FormatVolt(VoltageMagnitude);
                break;
            case PhaseAAnglePropertyName:
            case PhaseBAnglePropertyName:
            case PhaseCAnglePropertyName:
                RefreshAngleTexts();
                break;
            case FrequencyHzPropertyName:
                FrequencyText = FormatHertz(FrequencyHz);
                break;
        }
    }

    public void RefreshTexts()
    {
        DurationText = FormatSeconds(DurationSeconds);
        CurrentText = FormatAmpere(CurrentScale);
        VoltageMagnitudeText = FormatVolt(VoltageMagnitude);
        FrequencyText = FormatHertz(FrequencyHz);
        RefreshAngleTexts();
        OnPropertyChanged(nameof(VoltageText));
        OnPropertyChanged(nameof(VoltageMagnitude));
    }

    private bool CommitSeconds(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(DurationText, "s", out var value) || !IsFinite(value) || value <= 0 || value > 86_400)
        {
            warning = $"Invalid duration for {Name}. Duration must be greater than 0 and not more than 86,400 s.";
            RejectText(DurationSecondsPropertyName);
            return false;
        }

        DurationSeconds = value;
        return true;
    }

    private bool CommitCurrent(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(CurrentText, "A", out var value) || !IsFinite(value) || value < 0 || value > 1_000_000_000)
        {
            warning = $"Invalid current for {Name}. Current must be a numeric value from 0 to 1,000,000,000 A.";
            RejectText(CurrentMagnitudePropertyName);
            return false;
        }

        CurrentScale = value;
        return true;
    }

    private bool CommitVoltage(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(VoltageMagnitudeText, "V", out var value) || !IsFinite(value) || value < 0 || value > 1_000_000_000)
        {
            warning = $"Invalid voltage for {Name}. Voltage must be a numeric value from 0 to 1,000,000,000 V.";
            RejectText(VoltageMagnitudePropertyName);
            return false;
        }

        VoltageScale = value / NominalVoltageLn;
        OnPropertyChanged(nameof(VoltageText));
        OnPropertyChanged(nameof(VoltageMagnitude));
        return true;
    }

    private bool CommitAngle(string text, double anchorCompensationDegrees, out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(text, "°", out var value) || !IsFinite(value) || Math.Abs(value) > 360_000)
        {
            warning = $"Invalid angle for {Name}. Angle must be a numeric value within ±360,000 degrees.";
            RejectText(PhaseAAnglePropertyName);
            return false;
        }

        AngleShiftDegrees = NormalizeDegrees(value + anchorCompensationDegrees);
        return true;
    }

    private bool CommitFrequency(out string warning)
    {
        warning = string.Empty;
        if (!TryParseOperatorNumber(FrequencyText, "Hz", out var value) || !IsFinite(value) || value < 0 || value > 5_000)
        {
            warning = $"Invalid frequency for {Name}. Frequency must be a numeric value from 0 to 5000 Hz. Use 0 Hz for DC.";
            RejectText(FrequencyHzPropertyName);
            return false;
        }

        FrequencyHz = value;
        return true;
    }

    private void RefreshAngleTexts()
    {
        AngleText = FormatDegrees(AngleShiftDegrees);
        PhaseAAngleText = FormatDegrees(NormalizeDegrees(AngleShiftDegrees));
        PhaseBAngleText = FormatDegrees(NormalizeDegrees(AngleShiftDegrees - 120));
        PhaseCAngleText = FormatDegrees(NormalizeDegrees(AngleShiftDegrees + 120));
    }

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
        input = input.Replace("deg", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("degree", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("degrees", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("seconds", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("second", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("sec", string.Empty, StringComparison.OrdinalIgnoreCase);
        input = input.Replace("°", string.Empty, StringComparison.Ordinal);
        input = input.Replace("Hz", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(unit))
            input = input.Replace(unit, string.Empty, StringComparison.OrdinalIgnoreCase);

        return input.Trim();
    }

    private static double CoerceMagnitude(double value)
        => IsFinite(value) ? Math.Clamp(value, 0, 1_000_000_000) : 0;

    private static double CoerceFrequency(double value)
        => IsFinite(value) && value >= 0 ? Math.Min(value, 5_000) : 50;

    private static double CoerceSeconds(double value)
        => IsFinite(value) && value > 0 ? Math.Min(value, 86_400) : 0.001;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

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

    private static string FormatSeconds(double value)
        => $"{FormatNumber(value)} s";

    private static string FormatAmpere(double value)
        => $"{FormatNumber(value)} A";

    private static string FormatVolt(double value)
        => $"{FormatNumber(value)} V";

    private static string FormatHertz(double value)
        => $"{FormatNumber(value)} Hz";

    private static string FormatDegrees(double value)
        => $"{FormatNumber(value)} °";

    public SequenceStateSnapshot ToSnapshot()
        => new()
        {
            Name = Name,
            DurationSeconds = DurationSeconds,
            CurrentScale = CurrentScale,
            VoltageScale = VoltageScale,
            AngleShiftDegrees = AngleShiftDegrees,
            FrequencyHz = FrequencyHz
        };
}
