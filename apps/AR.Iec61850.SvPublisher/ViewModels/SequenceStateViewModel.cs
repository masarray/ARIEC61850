using AR.Iec61850.SvPublisher.Models;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SequenceStateViewModel : ObservableObject
{
    private string _name;
    private double _durationSeconds;
    private double _currentScale;
    private double _voltageScale;
    private double _angleShiftDegrees;
    private double _frequencyHz;

    public SequenceStateViewModel(
        string name,
        double durationSeconds,
        double currentScale,
        double voltageScale,
        double angleShiftDegrees,
        double frequencyHz)
    {
        _name = name;
        _durationSeconds = durationSeconds;
        _currentScale = currentScale;
        _voltageScale = voltageScale;
        _angleShiftDegrees = angleShiftDegrees;
        _frequencyHz = frequencyHz;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, value);
    }

    public double CurrentScale
    {
        get => _currentScale;
        set => SetProperty(ref _currentScale, value);
    }

    public double VoltageScale
    {
        get => _voltageScale;
        set => SetProperty(ref _voltageScale, value);
    }

    public double AngleShiftDegrees
    {
        get => _angleShiftDegrees;
        set => SetProperty(ref _angleShiftDegrees, value);
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set => SetProperty(ref _frequencyHz, value);
    }

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
