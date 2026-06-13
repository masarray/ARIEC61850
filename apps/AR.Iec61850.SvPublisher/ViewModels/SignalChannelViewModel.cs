using AR.Iec61850.SvPublisher.Models;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SignalChannelViewModel : ObservableObject
{
    private bool _isEnabled = true;
    private double _magnitude;
    private double _angleDegrees;

    public SignalChannelViewModel(string key, string name, string kind, string unit, double magnitude, double angleDegrees)
    {
        Key = key;
        Name = name;
        Kind = kind;
        Unit = unit;
        _magnitude = magnitude;
        _angleDegrees = angleDegrees;
    }

    public string Key { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Unit { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

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

    public SignalChannelSnapshot ToSnapshot()
        => new()
        {
            Key = Key,
            IsEnabled = IsEnabled,
            Magnitude = Magnitude,
            AngleDegrees = AngleDegrees
        };
}
