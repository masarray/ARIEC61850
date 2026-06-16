using AR.Iec61850.Scl;
using AR.Iec61850.SvPublisher.Models;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SvPublisherSlotViewModel : ObservableObject
{
    private bool _isEnabled;
    private SvStreamChoice? _selectedStream;
    private string _streamId = string.Empty;
    private string _streamControlBlock = string.Empty;
    private string _dataSetReference = string.Empty;
    private string _appIdText = string.Empty;
    private string _destinationMac = string.Empty;
    private string _sourceMac = "02:00:00:00:20:01";
    private bool _useVlan;
    private int _vlanId;
    private int _vlanPriority = 4;
    private double _sampleRateHz = 4000;
    private double _nominalFrequencyHz = 50;
    private double _currentDlsb = 0.001;
    private double _voltageDlsb = 0.01;
    private string _manualSetMode = "Direct";
    private string _sampleRatePresetKey = "9-2LE-80-50";
    private int _dataSetEntryCount;
    private int _mappedSignalCount;
    private int _payloadBytes;
    private IReadOnlyList<SignalChannelSnapshot> _channels = Array.Empty<SignalChannelSnapshot>();

    public SvPublisherSlotViewModel(int index)
    {
        Index = index;
        Name = $"Publisher {index}";
        SourceMac = $"02:00:00:00:20:{index:00}";
        IsEnabled = index == 1;
    }

    public int Index { get; }
    public string Name { get; }

    public string Header => $"IED / MU {Index}";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public SvStreamChoice? SelectedStream
    {
        get => _selectedStream;
        set
        {
            if (SetProperty(ref _selectedStream, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string StreamId
    {
        get => _streamId;
        set
        {
            if (SetProperty(ref _streamId, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string StreamControlBlock
    {
        get => _streamControlBlock;
        set
        {
            if (SetProperty(ref _streamControlBlock, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string DataSetReference
    {
        get => _dataSetReference;
        set => SetProperty(ref _dataSetReference, value);
    }

    public string AppIdText
    {
        get => _appIdText;
        set
        {
            if (SetProperty(ref _appIdText, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string DestinationMac
    {
        get => _destinationMac;
        set
        {
            if (SetProperty(ref _destinationMac, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string SourceMac
    {
        get => _sourceMac;
        set => SetProperty(ref _sourceMac, value);
    }

    public bool UseVlan
    {
        get => _useVlan;
        set
        {
            if (SetProperty(ref _useVlan, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public int VlanId
    {
        get => _vlanId;
        set
        {
            if (SetProperty(ref _vlanId, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public int VlanPriority
    {
        get => _vlanPriority;
        set => SetProperty(ref _vlanPriority, value);
    }

    public double SampleRateHz
    {
        get => _sampleRateHz;
        set
        {
            if (SetProperty(ref _sampleRateHz, value))
                OnPropertyChanged(nameof(SummaryText));
        }
    }

    public double NominalFrequencyHz
    {
        get => _nominalFrequencyHz;
        set => SetProperty(ref _nominalFrequencyHz, value);
    }

    public double CurrentDlsb
    {
        get => _currentDlsb;
        set => SetProperty(ref _currentDlsb, value);
    }

    public double VoltageDlsb
    {
        get => _voltageDlsb;
        set => SetProperty(ref _voltageDlsb, value);
    }

    public string ManualSetMode
    {
        get => _manualSetMode;
        set => SetProperty(ref _manualSetMode, value);
    }

    public string SampleRatePresetKey
    {
        get => _sampleRatePresetKey;
        set => SetProperty(ref _sampleRatePresetKey, value);
    }

    public int DataSetEntryCount
    {
        get => _dataSetEntryCount;
        set => SetProperty(ref _dataSetEntryCount, value);
    }

    public int MappedSignalCount
    {
        get => _mappedSignalCount;
        set => SetProperty(ref _mappedSignalCount, value);
    }

    public int PayloadBytes
    {
        get => _payloadBytes;
        set => SetProperty(ref _payloadBytes, value);
    }

    public IReadOnlyList<SignalChannelSnapshot> Channels
    {
        get => _channels;
        set => _channels = value ?? Array.Empty<SignalChannelSnapshot>();
    }

    public string StatusText => !IsEnabled
        ? "disabled"
        : SelectedStream is null ? "needs stream" : "enabled";

    public string SummaryText
    {
        get
        {
            if (!IsEnabled)
                return "Disabled";

            var stream = string.IsNullOrWhiteSpace(StreamControlBlock) ? "No stream" : StreamControlBlock;
            var vlan = UseVlan ? $" VLAN {VlanId}" : " untagged";
            var appId = string.IsNullOrWhiteSpace(AppIdText) ? "APPID -" : AppIdText;
            return $"{stream}  {appId}{vlan}  {SampleRateHz:0.#} fps";
        }
    }
}

public sealed class SampleRatePreset
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required double SampleRateHz { get; init; }
    public required double NominalFrequencyHz { get; init; }
    public required int SamplesPerCycle { get; init; }

    public override string ToString() => Label;
}
