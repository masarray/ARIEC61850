using System;
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
    private bool _isSelected;

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
        set
        {
            if (SetProperty(ref _durationSeconds, value))
                OnPropertyChanged(nameof(DurationText));
        }
    }

    public double CurrentScale
    {
        get => _currentScale;
        set
        {
            if (SetProperty(ref _currentScale, value))
                OnPropertyChanged(nameof(CurrentText));
        }
    }

    public double VoltageScale
    {
        get => _voltageScale;
        set
        {
            if (SetProperty(ref _voltageScale, value))
            {
                OnPropertyChanged(nameof(VoltageText));
                OnPropertyChanged(nameof(VoltageMagnitudeText));
            }
        }
    }

    public double AngleShiftDegrees
    {
        get => _angleShiftDegrees;
        set
        {
            if (SetProperty(ref _angleShiftDegrees, value))
            {
                OnPropertyChanged(nameof(AngleText));
                OnPropertyChanged(nameof(PhaseAAngleText));
                OnPropertyChanged(nameof(PhaseBAngleText));
                OnPropertyChanged(nameof(PhaseCAngleText));
            }
        }
    }

    public double FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            if (SetProperty(ref _frequencyHz, value))
                OnPropertyChanged(nameof(FrequencyText));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DurationText => $"{DurationSeconds:0.000} s";
    public string CurrentText => $"{CurrentScale:0.000} A";
    public string VoltageText => $"{VoltageScale:0.000} pu";
    public string VoltageMagnitudeText => $"{57.735 * Math.Max(0, VoltageScale):0.000} V";
    public string AngleText => $"{AngleShiftDegrees:0.000} °";
    public string PhaseAAngleText => $"{NormalizeDegrees(AngleShiftDegrees):0.000} °";
    public string PhaseBAngleText => $"{NormalizeDegrees(AngleShiftDegrees - 120):0.000} °";
    public string PhaseCAngleText => $"{NormalizeDegrees(AngleShiftDegrees + 120):0.000} °";
    public string FrequencyText => $"{FrequencyHz:0.000} Hz";

    private static double NormalizeDegrees(double degrees)
    {
        while (degrees > 180)
            degrees -= 360;
        while (degrees <= -180)
            degrees += 360;
        return degrees;
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
