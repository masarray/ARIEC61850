using AR.Iec61850.SvPublisher.Models;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SignalChannelViewModel : ObservableObject
{
    private bool _isEnabled = true;
    private string _name;
    private double _magnitude;
    private double _angleDegrees;
    private double _frequencyHz;

    public SignalChannelViewModel(string key, string name, string kind, string unit, double magnitude, double angleDegrees, double frequencyHz = 50)
    {
        Key = key;
        _name = name;
        Kind = kind;
        Unit = unit;
        _magnitude = magnitude;
        _angleDegrees = angleDegrees;
        _frequencyHz = frequencyHz;
    }

    public string Key { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Kind { get; }
    public string Unit { get; }

    public string DisplayName => Name;

    public string MagnitudeText => $"{Magnitude:0.000} {Unit}";

    public string AngleDegreesText => $"{AngleDegrees:0.000} °";

    public string FrequencyHzText => $"{FrequencyHz:0.000} Hz";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>
    /// RMS phasor magnitude shown to the operator. The SV payload builder converts this value
    /// to instantaneous peak counts before encoding samples.
    /// </summary>
    public double Magnitude
    {
        get => _magnitude;
        set
        {
            if (SetProperty(ref _magnitude, value))
                OnPropertyChanged(nameof(MagnitudeText));
        }
    }

    public double AngleDegrees
    {
        get => _angleDegrees;
        set
        {
            if (SetProperty(ref _angleDegrees, value))
                OnPropertyChanged(nameof(AngleDegreesText));
        }
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            if (SetProperty(ref _frequencyHz, value))
                OnPropertyChanged(nameof(FrequencyHzText));
        }
    }

    public SignalChannelSnapshot ToSnapshot()
        => new()
        {
            Key = Key,
            IsEnabled = IsEnabled,
            Magnitude = Magnitude,
            AngleDegrees = AngleDegrees,
            FrequencyHz = FrequencyHz
        };
}
