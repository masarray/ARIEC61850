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
        set => SetProperty(ref _name, value);
    }

    public string Kind { get; }
    public string Unit { get; }

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
        set => SetProperty(ref _magnitude, value);
    }

    public double AngleDegrees
    {
        get => _angleDegrees;
        set => SetProperty(ref _angleDegrees, value);
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set => SetProperty(ref _frequencyHz, value);
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
