using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.SvPublisher.Models;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;
using AR.Iec61850.TimeSync.Health;
using AR.Iec61850.TimeSync.Monitoring;
using AR.Iec61850.TimeSync.Ptp;
using AR.Iec61850.TimeSync.PtpRuntime;
using Microsoft.Win32;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SvPublisherViewModel : ObservableObject
{
    private const string DirectSetMode = "Direct";
    private const string LineLineSetMode = "Line-Line";
    private const string SymmetricalSetMode = "Symmetrical components";
    private const string PtpCaptureFilter = "ether proto 0x88f7";
    private const double NominalVoltageLn = 57.735;
    private const double NominalVoltageLl = 100.0;
    private const double NominalCurrent = 1.0;

    private readonly List<string> _eventLines = new();
    private SvStreamChoice? _selectedStream;
    private AdapterChoice? _selectedAdapter;
    private SignalChannelViewModel? _selectedRampChannel;
    private RampSignalChoice? _selectedRampSignalChoice;
    private bool _isSyncingRampSignalChoice;
    private RampStateViewModel? _selectedRampState;
    private SequenceStateViewModel? _selectedSequenceState;
    private CancellationTokenSource? _publisherStop;
    private string _sclPath = string.Empty;
    private string _sclSummary = "Open an SCL file to resolve SV streams.";
    private string _statusText = "Idle";
    private string _publishText = "No active publisher.";
    private string _evidenceText = string.Empty;
    private string _liveApplyText = "Auto apply ready.";
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
    private double _durationSeconds = 1;
    private bool _continuous = true;
    private bool _loopSequence = true;
    private bool _isLiveArmed;
    private bool _isPublishing;
    private bool _autoApplyWhileRunning = true;
    private bool _linkFrequencies = true;
    private SvSyncPolicyMode _syncPolicyMode = SvSyncPolicyMode.AutoPtp;
    private int _expectedPtpDomain;
    private bool _ptpAllowLocalFallback = true;
    private PtpPublisherMode _ptpPublisherMode = AR.Iec61850.SvPublisher.Models.PtpPublisherMode.MonitorOnly;
    private string _ptpClockIdentityText = "02:00:00:FF:FE:00:00:01";
    private int _ptpAnnounceIntervalMs = 1000;
    private int _ptpSyncIntervalMs = 250;
    private bool _ptpRespondToPeerDelay = true;
    private string _ptpPublisherStatusText = "PTP TX: off";
    private string _ptpStatusText = "PTP: idle";
    private string _smpSynchStatusText = "smpSynch: Auto PTP";
    private bool _isUpdatingManualRows;
    private ManualOutputRowViewModel? _contextManualRow;
    private string _contextColumnHeader = string.Empty;
    private string _signalNamingScheme = "L1L2L3E";
    private string _manualSetMode = DirectSetMode;
    private InjectionMode _mode;
    private double _rampTargetMagnitude = 5;
    private double _rampDurationSeconds = 1;
    private int _dataSetEntryCount;
    private int _mappedSignalCount;
    private int _payloadBytes;

    public SvPublisherViewModel()
    {
        Channels =
        [
            new SignalChannelViewModel("Ia", "I L1", "I", "A", NominalCurrent, 0, _nominalFrequencyHz),
            new SignalChannelViewModel("Ib", "I L2", "I", "A", NominalCurrent, -120, _nominalFrequencyHz),
            new SignalChannelViewModel("Ic", "I L3", "I", "A", NominalCurrent, 120, _nominalFrequencyHz),
            new SignalChannelViewModel("In", "I N", "I", "A", 0.000, 0, _nominalFrequencyHz) { IsEnabled = false },
            new SignalChannelViewModel("Va", "V L1-E", "V", "V", NominalVoltageLn, 0, _nominalFrequencyHz),
            new SignalChannelViewModel("Vb", "V L2-E", "V", "V", NominalVoltageLn, -120, _nominalFrequencyHz),
            new SignalChannelViewModel("Vc", "V L3-E", "V", "V", NominalVoltageLn, 120, _nominalFrequencyHz),
            new SignalChannelViewModel("Vn", "V N", "V", "V", 0.000, 0, _nominalFrequencyHz) { IsEnabled = false }
        ];

        ManualRows = new ObservableCollection<ManualOutputRowViewModel>();
        RampPreviewChannels = CreatePreviewChannels();
        SequencePreviewChannels = CreatePreviewChannels();
        RampStates =
        [
            new RampStateViewModel("Ramp 1", "Ia", "I L1", "Magnitude", 1.000, 5.000, 0.200, 0.100, 21, 2.100),
            new RampStateViewModel("Ramp 2", "Ia", "I L1", "Magnitude", 5.000, 4.000, -0.050, 0.100, 21, 2.100)
        ];

        SequenceStates =
        [
            new SequenceStateViewModel("Prefault", 1.000, 1.0, 1.0, 0, 50),
            new SequenceStateViewModel("Fault", 0.200, 4.0, 0.25, 0, 50),
            new SequenceStateViewModel("Recovery", 1.000, 1.0, 1.0, 0, 50)
        ];

        foreach (var rampState in RampStates)
            AttachRampState(rampState);
        foreach (var sequenceState in SequenceStates)
            AttachSequenceState(sequenceState);

        SelectedRampSignalChoice = RampSignalChoices.FirstOrDefault(choice => string.Equals(choice.KeyCsv, "Ia", StringComparison.OrdinalIgnoreCase));
        SelectedRampChannel = Channels.FirstOrDefault(c => c.Key == "Ia");
        SelectedRampState = RampStates.FirstOrDefault();
        SelectedSequenceState = SequenceStates.FirstOrDefault();

        OpenSclCommand = new AsyncRelayCommand(OpenSclAsync, () => !IsPublishing);
        RefreshAdaptersCommand = new RelayCommand(RefreshAdapters, () => !IsPublishing);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => !IsPublishing);
        RunDryCommand = new AsyncRelayCommand(() => RunPublishAsync(live: false), () => !IsPublishing);
        RunLiveCommand = new AsyncRelayCommand(() => RunPublishAsync(live: true), () => !IsPublishing);
        StopCommand = new RelayCommand(StopPublisher, () => IsPublishing);
        ApplyBalancedDefaultsCommand = new RelayCommand(ApplyBalancedDefaults);
        AddSequenceStateCommand = new RelayCommand(AddSequenceState, () => !IsPublishing);
        RemoveSequenceStateCommand = new RelayCommand(RemoveLastSequenceState, () => !IsPublishing && SequenceStates.Count > 0);
        SelectSequenceStateCommand = new ParameterRelayCommand(parameter => SelectSequenceState(parameter as SequenceStateViewModel));
        AddRampStateCommand = new RelayCommand(AddRampState, () => !IsPublishing);
        RemoveRampStateCommand = new RelayCommand(RemoveSelectedRampState, () => !IsPublishing && RampStates.Count > 0);
        ApplyNominalCommand = new RelayCommand(ApplyNominalValues);
        ZeroOutputCommand = new RelayCommand(ZeroOutputs);
        EqualMagnitudesCommand = new RelayCommand(EqualMagnitudes);
        HundredPercentLoadCommand = new RelayCommand(ApplyHundredPercentLoad);
        FiftyPercentLoadCommand = new RelayCommand(ApplyFiftyPercentLoad);
        UnloadCommand = new RelayCommand(ApplyUnload);
        BalanceAnglesCommand = new RelayCommand(BalanceAngles);
        NominalValueFromContextCommand = new RelayCommand(NominalValueFromContext);
        ZeroFromContextCommand = new RelayCommand(ZeroFromContext);
        EqualMagnitudesFromContextCommand = new RelayCommand(EqualMagnitudesFromContext);
        LineAngleFromContextCommand = new RelayCommand(LineAngleFromContext);
        BalanceAnglesFromContextCommand = new RelayCommand(BalanceAnglesFromContext);
        ReverseRotationFromContextCommand = new RelayCommand(ReverseRotationFromContext);
        NominalFrequencyFromContextCommand = new RelayCommand(NominalFrequencyFromContext);
        DcFrequencyFromContextCommand = new RelayCommand(DcFrequencyFromContext);
        EqualFrequenciesFromContextCommand = new RelayCommand(EqualFrequenciesFromContext);
        ReverseRotationCommand = new RelayCommand(ReverseRotation);
        SetSignalNamingCommand = new ParameterRelayCommand(parameter => ApplySignalNaming(parameter?.ToString() ?? "L1L2L3E"));
        CopyTableCommand = new RelayCommand(CopyManualTable);
        PasteTableCommand = new RelayCommand(PasteManualTable);

        ApplyChannelNaming();
        RebuildManualRowsFromChannels();
        UpdateRampPreview();
        UpdateSequencePreview();
        RefreshAdapters();
    }

    public ObservableCollection<SignalChannelViewModel> Channels { get; }
    public ObservableCollection<ManualOutputRowViewModel> ManualRows { get; }
    public ObservableCollection<SignalChannelViewModel> RampPreviewChannels { get; }
    public ObservableCollection<SignalChannelViewModel> SequencePreviewChannels { get; }
    public ObservableCollection<RampStateViewModel> RampStates { get; }
    public ObservableCollection<SequenceStateViewModel> SequenceStates { get; }

    public IReadOnlyList<RampSignalChoice> RampSignalChoices { get; } =
    [
        new RampSignalChoice { KeyCsv = "Va", Name = "V L1-E", Unit = "V" },
        new RampSignalChoice { KeyCsv = "Vb", Name = "V L2-E", Unit = "V" },
        new RampSignalChoice { KeyCsv = "Vc", Name = "V L3-E", Unit = "V" },
        new RampSignalChoice { KeyCsv = "Ia", Name = "I L1", Unit = "A" },
        new RampSignalChoice { KeyCsv = "Ib", Name = "I L2", Unit = "A" },
        new RampSignalChoice { KeyCsv = "Ic", Name = "I L3", Unit = "A" },
        new RampSignalChoice { KeyCsv = "Va,Vb,Vc", Name = "V L1-E, L2-E, L3-E", Unit = "V" },
        new RampSignalChoice { KeyCsv = "Ia,Ib,Ic", Name = "I L1, L2, L3", Unit = "A" }
    ];

    public IReadOnlyList<string> RampQuantities { get; } =
    [
        "Magnitude"
    ];

    public double RampTotalTimeSeconds => RampStates.Sum(state => Math.Max(0.001, state.TimeSeconds));

    public string RampTotalTimeText => $"{RampTotalTimeSeconds:0.000} s";
    public ObservableCollection<SvStreamChoice> Streams { get; } = new();
    public ObservableCollection<AdapterChoice> Adapters { get; } = new();

    public IReadOnlyList<string> ManualSetModes { get; } =
    [
        DirectSetMode,
        LineLineSetMode,
        SymmetricalSetMode
    ];

    public IReadOnlyList<SvSyncPolicyMode> SyncPolicyModes { get; } =
    [
        SvSyncPolicyMode.AutoPtp,
        SvSyncPolicyMode.ForceUnsynchronized,
        SvSyncPolicyMode.ForceLocal,
        SvSyncPolicyMode.ForceGlobal
    ];

    public IReadOnlyList<PtpPublisherMode> PtpPublisherModes { get; } =
    [
        AR.Iec61850.SvPublisher.Models.PtpPublisherMode.MonitorOnly,
        AR.Iec61850.SvPublisher.Models.PtpPublisherMode.LabPublisher
    ];

    public IReadOnlyList<InjectionMode> Modes { get; } =
    [
        InjectionMode.Manual,
        InjectionMode.Ramp,
        InjectionMode.Sequencer
    ];

    public ICommand OpenSclCommand { get; }
    public ICommand RefreshAdaptersCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand RunDryCommand { get; }
    public ICommand RunLiveCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ApplyBalancedDefaultsCommand { get; }
    public ICommand AddSequenceStateCommand { get; }
    public ICommand RemoveSequenceStateCommand { get; }
    public ICommand SelectSequenceStateCommand { get; }
    public ICommand AddRampStateCommand { get; }
    public ICommand RemoveRampStateCommand { get; }
    public ICommand ApplyNominalCommand { get; }
    public ICommand ZeroOutputCommand { get; }
    public ICommand EqualMagnitudesCommand { get; }
    public ICommand HundredPercentLoadCommand { get; }
    public ICommand FiftyPercentLoadCommand { get; }
    public ICommand UnloadCommand { get; }
    public ICommand BalanceAnglesCommand { get; }
    public ICommand NominalValueFromContextCommand { get; }
    public ICommand ZeroFromContextCommand { get; }
    public ICommand EqualMagnitudesFromContextCommand { get; }
    public ICommand LineAngleFromContextCommand { get; }
    public ICommand BalanceAnglesFromContextCommand { get; }
    public ICommand ReverseRotationFromContextCommand { get; }
    public ICommand NominalFrequencyFromContextCommand { get; }
    public ICommand DcFrequencyFromContextCommand { get; }
    public ICommand EqualFrequenciesFromContextCommand { get; }
    public ICommand ReverseRotationCommand { get; }
    public ICommand SetSignalNamingCommand { get; }
    public ICommand CopyTableCommand { get; }
    public ICommand PasteTableCommand { get; }

    public string SclPath
    {
        get => _sclPath;
        private set => SetProperty(ref _sclPath, value);
    }

    public string SclSummary
    {
        get => _sclSummary;
        private set => SetProperty(ref _sclSummary, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PublishText
    {
        get => _publishText;
        private set => SetProperty(ref _publishText, value);
    }

    public string AdapterStatusText => SelectedAdapter is null
        ? "Adapter: not selected"
        : $"Adapter: {SelectedAdapter.DisplayName}";

    public string PtpStatusText
    {
        get => _ptpStatusText;
        private set
        {
            if (SetProperty(ref _ptpStatusText, value))
                OnPropertyChanged(nameof(SyncStatusBarText));
        }
    }

    public string SmpSynchStatusText
    {
        get => _smpSynchStatusText;
        private set
        {
            if (SetProperty(ref _smpSynchStatusText, value))
                OnPropertyChanged(nameof(SyncStatusBarText));
        }
    }

    public string PtpPublisherStatusText
    {
        get => _ptpPublisherStatusText;
        private set
        {
            if (SetProperty(ref _ptpPublisherStatusText, value))
                OnPropertyChanged(nameof(SyncStatusBarText));
        }
    }

    public string SyncStatusBarText => $"{PtpStatusText}  |  {PtpPublisherStatusText}  |  {SmpSynchStatusText}";

    public string EvidenceText
    {
        get => _evidenceText;
        private set => SetProperty(ref _evidenceText, value);
    }

    public string LiveApplyText
    {
        get => _liveApplyText;
        private set => SetProperty(ref _liveApplyText, value);
    }

    public SvStreamChoice? SelectedStream
    {
        get => _selectedStream;
        set
        {
            if (SetProperty(ref _selectedStream, value))
                ApplySelectedStream(value);
        }
    }

    public AdapterChoice? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (!SetProperty(ref _selectedAdapter, value))
                return;

            OnPropertyChanged(nameof(AdapterStatusText));

            if (value is not null && !string.IsNullOrWhiteSpace(value.MacAddress))
                SourceMac = value.MacAddress;
        }
    }

    public SignalChannelViewModel? SelectedRampChannel
    {
        get => _selectedRampChannel;
        set
        {
            if (!SetProperty(ref _selectedRampChannel, value) || value is null || SelectedRampState is null)
                return;

            if (_isSyncingRampSignalChoice)
                return;

            var choice = RampSignalChoices.FirstOrDefault(candidate => string.Equals(candidate.KeyCsv, value.Key, StringComparison.OrdinalIgnoreCase));
            if (choice is not null)
                SelectedRampSignalChoice = choice;
        }
    }

    public RampSignalChoice? SelectedRampSignalChoice
    {
        get => _selectedRampSignalChoice;
        set
        {
            if (!SetProperty(ref _selectedRampSignalChoice, value) || value is null)
                return;

            ApplyRampSignalChoiceToSelectedState(value, resetFromBase: true);
        }
    }

    public RampStateViewModel? SelectedRampState
    {
        get => _selectedRampState;
        set
        {
            if (!SetProperty(ref _selectedRampState, value))
                return;

            if (value is not null)
            {
                SyncRampSignalChoiceFromState(value);
                RampTargetMagnitude = value.To;
                RampDurationSeconds = value.TimeSeconds;
            }

            UpdateRampPreview();
        }
    }

    public SequenceStateViewModel? SelectedSequenceState
    {
        get => _selectedSequenceState;
        set
        {
            if (!SetProperty(ref _selectedSequenceState, value))
                return;

            foreach (var state in SequenceStates)
                state.IsSelected = ReferenceEquals(state, value);

            UpdateSequencePreview();
        }
    }

    public string StreamId
    {
        get => _streamId;
        set => SetProperty(ref _streamId, value);
    }

    public string StreamControlBlock
    {
        get => _streamControlBlock;
        private set => SetProperty(ref _streamControlBlock, value);
    }

    public string DataSetReference
    {
        get => _dataSetReference;
        set => SetProperty(ref _dataSetReference, value);
    }

    public string AppIdText
    {
        get => _appIdText;
        set => SetProperty(ref _appIdText, value);
    }

    public string DestinationMac
    {
        get => _destinationMac;
        set => SetProperty(ref _destinationMac, value);
    }

    public string SourceMac
    {
        get => _sourceMac;
        set => SetProperty(ref _sourceMac, value);
    }

    public bool UseVlan
    {
        get => _useVlan;
        set => SetProperty(ref _useVlan, value);
    }

    public int VlanId
    {
        get => _vlanId;
        set => SetProperty(ref _vlanId, value);
    }

    public int VlanPriority
    {
        get => _vlanPriority;
        set => SetProperty(ref _vlanPriority, value);
    }

    public double SampleRateHz
    {
        get => _sampleRateHz;
        set => SetProperty(ref _sampleRateHz, value);
    }

    public double NominalFrequencyHz
    {
        get => _nominalFrequencyHz;
        set
        {
            if (!SetProperty(ref _nominalFrequencyHz, value))
                return;

            if (LinkFrequencies)
                SetAllManualFrequencies(value);
        }
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

    public double DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, value);
    }

    public bool Continuous
    {
        get => _continuous;
        set => SetProperty(ref _continuous, value);
    }

    public bool LoopSequence
    {
        get => _loopSequence;
        set => SetProperty(ref _loopSequence, value);
    }

    public bool IsLiveArmed
    {
        get => _isLiveArmed;
        set => SetProperty(ref _isLiveArmed, value);
    }

    public bool AutoApplyWhileRunning
    {
        get => _autoApplyWhileRunning;
        set
        {
            if (SetProperty(ref _autoApplyWhileRunning, value))
                LiveApplyText = value ? "Auto apply ready." : "Auto apply paused. Edits remain visible but publisher keeps previous setpoints.";
        }
    }

    public bool LinkFrequencies
    {
        get => _linkFrequencies;
        set
        {
            if (SetProperty(ref _linkFrequencies, value) && value)
            {
                SetAllManualFrequencies(NominalFrequencyHz);
                AppendEvent("Frequencies linked to nominal frequency.");
            }
        }
    }

    public SvSyncPolicyMode SyncPolicyMode
    {
        get => _syncPolicyMode;
        set
        {
            if (SetProperty(ref _syncPolicyMode, value))
            {
                SmpSynchStatusText = value switch
                {
                    SvSyncPolicyMode.ForceUnsynchronized => "smpSynch=0 forced",
                    SvSyncPolicyMode.ForceLocal => "smpSynch=1 forced",
                    SvSyncPolicyMode.ForceGlobal => "smpSynch=2 forced",
                    _ => "smpSynch: Auto PTP"
                };
                AppendEvent($"Sync policy changed to {value}.");
            }
        }
    }

    public int ExpectedPtpDomain
    {
        get => _expectedPtpDomain;
        set => SetProperty(ref _expectedPtpDomain, Math.Clamp(value, 0, 255));
    }

    public bool PtpAllowLocalFallback
    {
        get => _ptpAllowLocalFallback;
        set => SetProperty(ref _ptpAllowLocalFallback, value);
    }

    public PtpPublisherMode PtpPublisherMode
    {
        get => _ptpPublisherMode;
        set
        {
            if (SetProperty(ref _ptpPublisherMode, value))
            {
                PtpPublisherStatusText = value == AR.Iec61850.SvPublisher.Models.PtpPublisherMode.LabPublisher ? "PTP TX: lab publisher armed" : "PTP TX: off";
                AppendEvent($"PTP mode changed to {value}.");
            }
        }
    }

    public string PtpClockIdentityText
    {
        get => _ptpClockIdentityText;
        set => SetProperty(ref _ptpClockIdentityText, value);
    }

    public int PtpAnnounceIntervalMs
    {
        get => _ptpAnnounceIntervalMs;
        set => SetProperty(ref _ptpAnnounceIntervalMs, Math.Clamp(value, 100, 10000));
    }

    public int PtpSyncIntervalMs
    {
        get => _ptpSyncIntervalMs;
        set => SetProperty(ref _ptpSyncIntervalMs, Math.Clamp(value, 20, 5000));
    }

    public bool PtpRespondToPeerDelay
    {
        get => _ptpRespondToPeerDelay;
        set => SetProperty(ref _ptpRespondToPeerDelay, value);
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            if (SetProperty(ref _isPublishing, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsContextValueColumn
        => string.Equals(_contextColumnHeader, "Value", StringComparison.OrdinalIgnoreCase);

    public bool IsContextAngleColumn
        => string.Equals(_contextColumnHeader, "Angle", StringComparison.OrdinalIgnoreCase);

    public bool IsContextFrequencyColumn
        => string.Equals(_contextColumnHeader, "Freq", StringComparison.OrdinalIgnoreCase);

    public bool IsContextSignalColumn
        => string.Equals(_contextColumnHeader, "Signal", StringComparison.OrdinalIgnoreCase);

    public string SignalNamingScheme
    {
        get => _signalNamingScheme;
        private set => SetProperty(ref _signalNamingScheme, value);
    }

    public InjectionMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;

            OnPropertyChanged(nameof(IsManualWorkspaceVisible));
            OnPropertyChanged(nameof(IsRampWorkspaceVisible));
            OnPropertyChanged(nameof(IsSequencerWorkspaceVisible));
            OnPropertyChanged(nameof(WorkspaceTitle));
            OnPropertyChanged(nameof(WorkspaceSubtitle));
            AppendEvent($"Workspace changed to {value}.");
        }
    }

    public bool IsManualWorkspaceVisible => Mode == InjectionMode.Manual;

    public bool IsRampWorkspaceVisible => Mode == InjectionMode.Ramp;

    public bool IsSequencerWorkspaceVisible => Mode == InjectionMode.Sequencer;

    public string WorkspaceTitle => Mode switch
    {
        InjectionMode.Ramp => "Ramping",
        InjectionMode.Sequencer => "State Sequencer",
        _ => "Quick Manual"
    };

    public string WorkspaceSubtitle => Mode switch
    {
        InjectionMode.Ramp => "Step ramp profile, analog-output detail, and time-signal preview.",
        InjectionMode.Sequencer => "Horizontal state table with selected-state analog detail, phasor, and time-signal preview.",
        _ => "Fast manual SV injection with live numeric commit and unit formatting."
    };

    public string ManualSetMode
    {
        get => _manualSetMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (SetProperty(ref _manualSetMode, value))
            {
                RebuildManualRowsFromChannels();
                AppendEvent($"Manual set mode changed to {value}.");
            }
        }
    }

    public double RampTargetMagnitude
    {
        get => _rampTargetMagnitude;
        set
        {
            if (SetProperty(ref _rampTargetMagnitude, value) && SelectedRampState is not null)
            {
                SelectedRampState.To = value;
                UpdateRampPreview();
            }
        }
    }

    public double RampDurationSeconds
    {
        get => _rampDurationSeconds;
        set
        {
            var coerced = Math.Max(0.001, value);
            if (SetProperty(ref _rampDurationSeconds, coerced) && SelectedRampState is not null)
            {
                SelectedRampState.TimeSeconds = coerced;
                UpdateRampPreview();
            }
        }
    }

    public int DataSetEntryCount
    {
        get => _dataSetEntryCount;
        private set => SetProperty(ref _dataSetEntryCount, value);
    }

    public int MappedSignalCount
    {
        get => _mappedSignalCount;
        private set => SetProperty(ref _mappedSignalCount, value);
    }

    public int PayloadBytes
    {
        get => _payloadBytes;
        private set => SetProperty(ref _payloadBytes, value);
    }


    private static ObservableCollection<SignalChannelViewModel> CreatePreviewChannels()
        =>
        [
            new SignalChannelViewModel("Va", "V L1-E", "V", "V", NominalVoltageLn, 0, 50),
            new SignalChannelViewModel("Vb", "V L2-E", "V", "V", NominalVoltageLn, -120, 50),
            new SignalChannelViewModel("Vc", "V L3-E", "V", "V", NominalVoltageLn, 120, 50),
            new SignalChannelViewModel("Ia", "I L1", "I", "A", NominalCurrent, 0, 50),
            new SignalChannelViewModel("Ib", "I L2", "I", "A", NominalCurrent, -120, 50),
            new SignalChannelViewModel("Ic", "I L3", "I", "A", NominalCurrent, 120, 50)
        ];

    private void AttachRampState(RampStateViewModel state)
        => state.PropertyChanged += RampState_PropertyChanged;

    private void AttachSequenceState(SequenceStateViewModel state)
        => state.PropertyChanged += SequenceState_PropertyChanged;

    private void DetachRampState(RampStateViewModel state)
        => state.PropertyChanged -= RampState_PropertyChanged;

    private void DetachSequenceState(SequenceStateViewModel state)
        => state.PropertyChanged -= SequenceState_PropertyChanged;

    private void RampState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RampTotalTimeSeconds));
        OnPropertyChanged(nameof(RampTotalTimeText));

        if (ReferenceEquals(sender, SelectedRampState))
        {
            if (SelectedRampState is not null)
            {
                if (string.Equals(e.PropertyName, nameof(RampStateViewModel.SignalKey), StringComparison.Ordinal))
                    SyncRampSignalChoiceFromState(SelectedRampState);

                _rampTargetMagnitude = SelectedRampState.To;
                _rampDurationSeconds = SelectedRampState.TimeSeconds;
                OnPropertyChanged(nameof(RampTargetMagnitude));
                OnPropertyChanged(nameof(RampDurationSeconds));
            }

            UpdateRampPreview();
        }
    }

    private void SequenceState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedSequenceState))
            UpdateSequencePreview();
    }

    private void SelectSequenceState(SequenceStateViewModel? state)
    {
        if (state is not null)
            SelectedSequenceState = state;
    }

    private void SyncRampSignalChoiceFromState(RampStateViewModel state)
    {
        var choice = RampSignalChoices.FirstOrDefault(candidate => string.Equals(candidate.KeyCsv, state.SignalKey, StringComparison.OrdinalIgnoreCase));
        if (choice is null)
            return;

        _isSyncingRampSignalChoice = true;
        try
        {
            _selectedRampSignalChoice = choice;
            OnPropertyChanged(nameof(SelectedRampSignalChoice));
            _selectedRampChannel = Channels.FirstOrDefault(channel => string.Equals(channel.Key, choice.Keys.FirstOrDefault(), StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(SelectedRampChannel));
        }
        finally
        {
            _isSyncingRampSignalChoice = false;
        }
    }

    private void ApplyRampSignalChoiceToSelectedState(RampSignalChoice choice, bool resetFromBase)
    {
        if (SelectedRampState is not { } state)
            return;

        state.SignalKey = choice.KeyCsv;
        state.SignalName = choice.Name;
        state.Quantity = choice.Quantity;

        _isSyncingRampSignalChoice = true;
        try
        {
            SelectedRampChannel = Channels.FirstOrDefault(channel => string.Equals(channel.Key, choice.Keys.FirstOrDefault(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isSyncingRampSignalChoice = false;
        }

        if (resetFromBase && ResolveFirstRampBaseChannel(choice) is { } baseChannel)
        {
            state.From = baseChannel.Magnitude;
            if (Math.Abs(state.To - state.From) < 0.000001)
            {
                var bump = baseChannel.Kind == "I" ? 1.0 : 10.0;
                state.To = state.From + bump;
            }
        }

        RampTargetMagnitude = state.To;
        RampDurationSeconds = state.TimeSeconds;
        UpdateRampPreview();
    }

    private SignalChannelViewModel? ResolveFirstRampBaseChannel(RampSignalChoice choice)
        => choice.Keys
            .Select(key => Channels.FirstOrDefault(channel => string.Equals(channel.Key, key, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(channel => channel is not null);

    private void UpdateRampPreview()
    {
        if (RampPreviewChannels.Count == 0)
            return;

        CopyManualPreview(Channels, RampPreviewChannels);

        if (SelectedRampState is { } state)
        {
            foreach (var channel in RampPreviewChannels.Where(c => state.AppliesToChannel(c.Key)))
            {
                channel.Magnitude = Math.Max(0, state.To);
                channel.IsEnabled = state.To > 0;
            }
        }
    }

    private void UpdateSequencePreview()
    {
        if (SequencePreviewChannels.Count == 0)
            return;

        var state = SelectedSequenceState;
        var voltage = state is null ? NominalVoltageLn : NominalVoltageLn * Math.Max(0, state.VoltageScale);
        var current = state is null ? NominalCurrent : Math.Max(0, state.CurrentScale);
        var shift = state?.AngleShiftDegrees ?? 0;
        var frequency = state?.FrequencyHz ?? NominalFrequencyHz;

        SetPreviewChannel(SequencePreviewChannels, "Va", DisplaySignalName("Va", "V L1-E"), voltage, shift, voltage > 0, frequency);
        SetPreviewChannel(SequencePreviewChannels, "Vb", DisplaySignalName("Vb", "V L2-E"), voltage, shift - 120, voltage > 0, frequency);
        SetPreviewChannel(SequencePreviewChannels, "Vc", DisplaySignalName("Vc", "V L3-E"), voltage, shift + 120, voltage > 0, frequency);
        SetPreviewChannel(SequencePreviewChannels, "Ia", DisplaySignalName("Ia", "I L1"), current, shift, current > 0, frequency);
        SetPreviewChannel(SequencePreviewChannels, "Ib", DisplaySignalName("Ib", "I L2"), current, shift - 120, current > 0, frequency);
        SetPreviewChannel(SequencePreviewChannels, "Ic", DisplaySignalName("Ic", "I L3"), current, shift + 120, current > 0, frequency);
    }

    private static void CopyManualPreview(IEnumerable<SignalChannelViewModel> source, ObservableCollection<SignalChannelViewModel> target)
    {
        foreach (var destination in target)
        {
            var sourceChannel = source.FirstOrDefault(c => string.Equals(c.Key, destination.Key, StringComparison.OrdinalIgnoreCase));
            if (sourceChannel is null)
                continue;

            destination.Name = sourceChannel.Name;
            destination.Magnitude = sourceChannel.Magnitude;
            destination.AngleDegrees = sourceChannel.AngleDegrees;
            destination.FrequencyHz = sourceChannel.FrequencyHz;
            destination.IsEnabled = sourceChannel.IsEnabled;
        }
    }

    private static void SetPreviewChannel(
        ObservableCollection<SignalChannelViewModel> channels,
        string key,
        string name,
        double magnitude,
        double angle,
        bool enabled,
        double frequency)
    {
        var channel = channels.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
            return;

        channel.Name = name;
        channel.Magnitude = magnitude;
        channel.AngleDegrees = NormalizeDegrees(angle);
        channel.FrequencyHz = frequency;
        channel.IsEnabled = enabled;
    }

    private async Task OpenSclAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open IEC 61850 SCL",
            Filter = "SCL files (*.scd;*.cid;*.icd;*.iid;*.xml)|*.scd;*.cid;*.icd;*.iid;*.xml|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var document = await Task.Run(() => new SclParser().Load(dialog.FileName)).ConfigureAwait(true);
            SclPath = dialog.FileName;
            Streams.Clear();

            for (var i = 0; i < document.SampledValuesStreams.Count; i++)
                Streams.Add(new SvStreamChoice { Index = i + 1, Stream = document.SampledValuesStreams[i] });

            SelectedStream = Streams.FirstOrDefault();
            SclSummary = $"IED={document.Ieds.Count}  DataSets={document.DataSets.Count}  SV={document.SampledValuesStreams.Count}  Warnings={document.Warnings.Count}";
            StatusText = document.SampledValuesStreams.Count == 0 ? "SCL opened, no SV streams found." : "SCL opened.";
            AppendEvent($"Opened SCL: {Path.GetFileName(dialog.FileName)}");

            foreach (var warning in document.Warnings.Take(6))
                AppendEvent($"SCL warning: {warning}");

            foreach (var conflict in document.Conflicts.Take(6))
                AppendEvent($"SCL conflict: {conflict.Description}");
        }
        catch (Exception ex)
        {
            StatusText = "Open SCL failed.";
            AppendEvent(ex.Message);
        }
    }

    private void RefreshAdapters()
    {
        try
        {
            Adapters.Clear();
            foreach (var adapter in NpcapAdapterCatalog.ListAdapters())
            {
                var mac = adapter.MacAddress?.ToString() ?? string.Empty;
                var description = string.IsNullOrWhiteSpace(adapter.Description) ? adapter.Name : adapter.Description;
                Adapters.Add(new AdapterChoice
                {
                    Selector = adapter.Index.ToString(CultureInfo.InvariantCulture),
                    MacAddress = mac,
                    DisplayName = $"[{adapter.Index}] {(string.IsNullOrWhiteSpace(mac) ? "MAC -" : mac)}  {description}"
                });
            }

            SelectedAdapter ??= Adapters.FirstOrDefault();
            AppendEvent(Adapters.Count == 0 ? "No Npcap adapters found." : $"Adapters found: {Adapters.Count}");
        }
        catch (Exception ex)
        {
            Adapters.Clear();
            AppendEvent($"Adapter list unavailable: {ex.Message}");
        }
    }

    private async Task SaveProfileAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save SV Publisher Plan",
            Filter = "SV publisher plan (*.svpub.json)|*.svpub.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "sv-publisher-plan.svpub.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var snapshot = CreateSnapshot();
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(true);
            StatusText = "Plan saved.";
            AppendEvent($"Saved plan: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            StatusText = "Save failed.";
            AppendEvent(ex.Message);
        }
    }

    private async Task RunPublishAsync(bool live)
    {
        try
        {
            ValidateBeforeRun(live);

            using var stop = new CancellationTokenSource();
            _publisherStop = stop;
            IsPublishing = true;
            StatusText = live ? "START INJECTION - live NIC." : "START INJECTION - dry run.";
            AutoApplyWhileRunning = true;
            Continuous = true;
            LiveApplyText = "RUN: table edits are applied to the next SV frames.";
            AppendEvent(live ? "Start Injection: live NIC publisher started." : "Start Injection: dry-run publisher started.");

            await Task.Run(async () => await PublishLoopAsync(live, stop.Token).ConfigureAwait(false)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "STOP INJECTION.";
            AppendEvent("Stop Injection requested by operator.");
        }
        catch (Exception ex)
        {
            StatusText = "Publisher failed.";
            AppendEvent(ex.Message);
        }
        finally
        {
            _publisherStop?.Dispose();
            _publisherStop = null;
            IsPublishing = false;
            IsLiveArmed = false;
            if (!PublishText.StartsWith("Complete", StringComparison.OrdinalIgnoreCase))
                PublishText = "Publisher stopped.";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task PublishLoopAsync(bool live, CancellationToken cancellationToken)
    {
        var selectedStream = SelectedStream?.Stream ?? throw new InvalidOperationException("Select an SV stream first.");
        var source = MacAddress.Parse(SourceMac);
        var destination = MacAddress.Parse(DestinationMac);
        var appId = ParseAppId(AppIdText);
        var vlan = ResolveVlanTag();
        var sampleRateHz = SampleRateHz;
        var runDurationSeconds = Mode == InjectionMode.Ramp ? RampTotalTimeSeconds : DurationSeconds;
        var frameLimit = Continuous ? (long?)null : Math.Max(1, (long)Math.Round(sampleRateHz * runDurationSeconds));
        var startedTicks = Stopwatch.GetTimestamp();
        var startedAt = DateTimeOffset.UtcNow;
        var nextUiTicks = startedTicks;
        var sampleCounterWrap = ResolveSampleCounterWrap(selectedStream, sampleRateHz, NominalFrequencyHz);
        var frozenChannels = CaptureEffectiveChannels(0);
        var oscillatorStates = frozenChannels.ToDictionary(
            x => x.Key,
            x => new OscillatorState
            {
                PhaseRadians = x.Value.AngleDegrees * Math.PI / 180.0,
                LastAngleDegrees = x.Value.AngleDegrees
            },
            StringComparer.OrdinalIgnoreCase);

        var ptpMonitor = new PtpPassiveMonitor();
        var ptpValidator = new PtpTimingHealthValidator();
        var ptpOptions = BuildPtpHealthOptions();
        PtpTimingHealthReport? latestPtpReport = null;

        IProcessBusTransport transport;
        IProcessBusFrameSource? frameSource = null;
        if (live)
        {
            var duplex = new NpcapProcessBusDuplexTransport(SelectedAdapter?.Selector ?? string.Empty);
            transport = duplex;
            frameSource = duplex;
        }
        else
        {
            transport = new InMemoryProcessBusTransport();
        }

        IDisposable? disposableTransport = transport as IDisposable;
        using var ptpCaptureStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        PtpPublisherRuntime? labPtpPublisher = live && PtpPublisherMode == AR.Iec61850.SvPublisher.Models.PtpPublisherMode.LabPublisher
            ? new PtpPublisherRuntime(transport, BuildLabPtpOptions(source, vlan))
            : null;
        var labPtpTask = labPtpPublisher is not null
            ? Task.Run(async () => await labPtpPublisher.RunAsync(ptpCaptureStop.Token).ConfigureAwait(false), CancellationToken.None)
            : Task.CompletedTask;
        var ptpCaptureTask = live && frameSource is not null
            ? Task.Run(async () => await CapturePtpAsync(frameSource, ptpMonitor, labPtpPublisher, ptpCaptureStop.Token).ConfigureAwait(false), CancellationToken.None)
            : Task.CompletedTask;

        long sent = 0;
        ushort sampleCount = 0;
        var lastFrameBytes = 0;

        try
        {
            Dispatch(() =>
            {
                PtpStatusText = live ? $"PTP: listening domain {ExpectedPtpDomain}" : "PTP: dry-run monitor inactive";
                PtpPublisherStatusText = labPtpPublisher is null ? "PTP TX: off" : "PTP TX: starting lab publisher";
                SmpSynchStatusText = live ? "smpSynch: waiting for policy" : FormatSmpSynchStatus(ResolveSampleSynchronization(null));
            });

            while (!frameLimit.HasValue || sent < frameLimit.Value)
            {
                await DelayUntilSampleAsync(startedTicks, sent, sampleRateHz, cancellationToken).ConfigureAwait(false);

                var elapsedSeconds = sent / sampleRateHz;
                var timestamp = startedAt.AddTicks((long)Math.Round(sent * TimeSpan.TicksPerSecond / sampleRateHz));
                var sampleTime = new Iec61850UtcTime(timestamp, Quality: 0);
                var channels = AutoApplyWhileRunning
                    ? CaptureEffectiveChannels(elapsedSeconds)
                    : frozenChannels;
                var phasedChannels = ApplyOscillatorPhases(channels, oscillatorStates, sampleRateHz);
                var payload = BuildSamplePayload(selectedStream, sampleTime, phasedChannels);
                var frame = SampledValuesFrameBuilder.BuildEthernetFrame(new SampledValuesFrame
                {
                    Destination = destination,
                    Source = source,
                    Vlan = vlan,
                    AppId = appId,
                    Pdu = new SampledValuesPdu
                    {
                        Asdus =
                        [
                            new SampledValueAsdu
                            {
                                SvId = StreamId.Trim(),
                                DataSetReference = DataSetReference.Trim(),
                                SampleCount = sampleCount,
                                ConfigurationRevision = selectedStream.ConfigurationRevision,
                                ReferenceTime = sampleTime,
                                SampleSynchronization = (byte)ResolveSampleSynchronization(latestPtpReport),
                                SampleRate = ToSampleRate(sampleRateHz),
                                SampleMode = MapSampleMode(selectedStream.SampleMode),
                                SamplePayload = payload
                            }
                        ]
                    }
                });

                await transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
                lastFrameBytes = frame.Length;
                sampleCount = IncrementSampleCount(sampleCount, sampleCounterWrap);
                sent++;

                var nowTicks = Stopwatch.GetTimestamp();
                if (nowTicks >= nextUiTicks)
                {
                    latestPtpReport = ptpValidator.Evaluate(ptpMonitor.GetSnapshot(), ptpOptions);
                    var smpSynch = ResolveSampleSynchronization(latestPtpReport);
                    var elapsed = Stopwatch.GetElapsedTime(startedTicks);
                    var rate = sent / Math.Max(elapsed.TotalSeconds, 0.001);
                    var progress = frameLimit.HasValue ? $"{sent}/{frameLimit.Value}" : sent.ToString(CultureInfo.InvariantCulture);
                    var message = $"{(live ? "LIVE" : "DRY")} frames={progress} rate={rate:0.0} fps smpCnt={sampleCount} smpSynch={(byte)smpSynch} payload={payload.Length}B frame={lastFrameBytes}B autoApply=ON";
                    Dispatch(() =>
                    {
                        PayloadBytes = payload.Length;
                        PublishText = message;
                        UpdatePtpStatus(latestPtpReport, smpSynch, live);
                        UpdatePtpPublisherStatus(labPtpPublisher);
                    });
                    nextUiTicks = nowTicks + (long)Math.Round(0.25 * Stopwatch.Frequency);
                }
            }
        }
        finally
        {
            ptpCaptureStop.Cancel();
            try
            {
                await Task.WhenAll(ptpCaptureTask, labPtpTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during publisher shutdown.
            }
            catch (Exception ex)
            {
                Dispatch(() => AppendEvent($"PTP runtime stopped: {ex.Message}"));
            }

            disposableTransport?.Dispose();
        }

        var totalElapsed = Stopwatch.GetElapsedTime(startedTicks);
        var effectiveRate = sent / Math.Max(totalElapsed.TotalSeconds, 0.001);
        Dispatch(() =>
        {
            PublishText = $"Complete frames={sent} elapsed={totalElapsed.TotalSeconds:0.000}s rate={effectiveRate:0.0} fps lastFrame={lastFrameBytes}B";
            StatusText = "Publisher complete.";
            AppendEvent(PublishText);
        });
    }

private static async Task CapturePtpAsync(
    IProcessBusFrameSource frameSource,
    PtpPassiveMonitor monitor,
    PtpPublisherRuntime? labPtpPublisher,
    CancellationToken cancellationToken)
{
    var options = new ProcessBusCaptureOptions
    {
        Filter = PtpCaptureFilter,
        BufferCapacity = 2048,
        ReadTimeoutMilliseconds = 500
    };

    await foreach (var captured in frameSource.CaptureAsync(options, cancellationToken).ConfigureAwait(false))
    {
        var frameBytes = captured.Frame.ToArray();
        monitor.ObserveEthernetFrame(frameBytes, captured.Timestamp);

        if (labPtpPublisher is not null &&
            PtpPacketParser.TryParseEthernetFrame(frameBytes, out var ptpFrame) &&
            ptpFrame.Header.MessageType == PtpMessageType.PdelayReq)
        {
            await labPtpPublisher.RespondToPeerDelayRequestAsync(ptpFrame, cancellationToken).ConfigureAwait(false);
        }
    }
}

private PtpPublisherOptions BuildLabPtpOptions(MacAddress sourceMac, VlanTag? vlan)
    {
        var clockIdentity = ClockIdentity.TryParse(PtpClockIdentityText, out var parsedIdentity)
            ? parsedIdentity
            : DeriveClockIdentity(sourceMac);

        return new PtpPublisherOptions
        {
            DomainNumber = (byte)Math.Clamp(ExpectedPtpDomain, 0, 255),
            SourceMac = sourceMac.ToArray(),
            VlanId = vlan?.VlanId,
            VlanPriority = vlan?.PriorityCodePoint ?? (byte)Math.Clamp(VlanPriority, 0, 7),
            ClockIdentity = clockIdentity,
            PortNumber = 1,
            AnnounceInterval = TimeSpan.FromMilliseconds(PtpAnnounceIntervalMs),
            SyncInterval = TimeSpan.FromMilliseconds(PtpSyncIntervalMs),
            FollowUpDelay = TimeSpan.FromMilliseconds(2),
            RespondToPeerDelay = PtpRespondToPeerDelay,
            TwoStepClock = true
        };
    }

    private static ClockIdentity DeriveClockIdentity(MacAddress sourceMac)
    {
        var mac = sourceMac.ToArray();
        return new ClockIdentity(new byte[]
        {
            mac[0], mac[1], mac[2], 0xFF, 0xFE, mac[3], mac[4], mac[5]
        });
    }

    private PtpTimingHealthOptions BuildPtpHealthOptions()
    => new()
    {
        ExpectedDomainNumber = (byte)Math.Clamp(ExpectedPtpDomain, 0, 255),
        SourceTimeout = TimeSpan.FromSeconds(3),
        RequireAnnounce = true,
        RequireSync = true,
        RequireFollowUpForTwoStep = true,
        RequirePeerDelayActivity = true,
        MaximumSequenceAnomalies = 0
    };

private SmpSynchValue ResolveSampleSynchronization(PtpTimingHealthReport? report)
    => SyncPolicyMode switch
    {
        SvSyncPolicyMode.ForceUnsynchronized => SmpSynchValue.NotSynchronized,
        SvSyncPolicyMode.ForceLocal => SmpSynchValue.LocalSynchronized,
        SvSyncPolicyMode.ForceGlobal => SmpSynchValue.GlobalSynchronized,
        _ => PtpPublisherMode == AR.Iec61850.SvPublisher.Models.PtpPublisherMode.LabPublisher
            ? SmpSynchValue.GlobalSynchronized
            : report is null
                ? SmpSynchValue.NotSynchronized
                : PtpSmpSynchPolicy.Resolve(report, PtpAllowLocalFallback)
    };

private void UpdatePtpStatus(PtpTimingHealthReport? report, SmpSynchValue smpSynch, bool live)
{
    if (!live)
    {
        PtpStatusText = "PTP: dry-run";
        SmpSynchStatusText = FormatSmpSynchStatus(smpSynch);
        return;
    }

    if (report is null)
    {
        PtpStatusText = "PTP: waiting";
        SmpSynchStatusText = FormatSmpSynchStatus(smpSynch);
        return;
    }

    var snapshot = report.Snapshot;
    var source = snapshot.Sources
        .Where(s => s.DomainNumber == Math.Clamp(ExpectedPtpDomain, 0, 255))
        .OrderByDescending(s => s.LastSeenAt)
        .FirstOrDefault()
        ?? snapshot.Sources.OrderByDescending(s => s.LastSeenAt).FirstOrDefault();

    if (source is null)
    {
        PtpStatusText = report.Severity == PtpHealthSeverity.Fail
            ? "PTP: not detected"
            : $"PTP: {report.Severity}";
    }
    else
    {
        var age = Math.Max(0, (snapshot.CapturedAt - source.LastSeenAt).TotalSeconds);
        PtpStatusText = $"PTP: {report.Severity} d={source.DomainNumber} src={source.SourcePortIdentity.ClockIdentity} age={age:0.0}s";
    }

    SmpSynchStatusText = FormatSmpSynchStatus(smpSynch);
}

private void UpdatePtpPublisherStatus(PtpPublisherRuntime? publisher)
{
    if (publisher is null)
    {
        PtpPublisherStatusText = "PTP TX: off";
        return;
    }

    var status = publisher.GetStatus();
    if (!string.IsNullOrWhiteSpace(status.LastError))
    {
        PtpPublisherStatusText = $"PTP TX: error {status.LastError}";
        return;
    }

    PtpPublisherStatusText = $"PTP TX: {(status.IsRunning ? "lab" : "stopped")} A={status.AnnounceSent} S={status.SyncSent} FU={status.FollowUpSent} PD={status.PeerDelayResponsesSent}";
}

private static string FormatSmpSynchStatus(SmpSynchValue value)
    => value switch
    {
        SmpSynchValue.GlobalSynchronized => "smpSynch=2 global",
        SmpSynchValue.LocalSynchronized => "smpSynch=1 local",
        _ => "smpSynch=0 unsync"
    };

    private byte[] BuildSamplePayload(
        SclSampledValuesStream stream,
        Iec61850UtcTime timestamp,
        IReadOnlyDictionary<string, EffectiveChannel> channels)
    {
        var layout = SampledValuesPayloadLayout.FromDataSet(stream.Entries);
        if (!layout.IsFullySupported)
            throw new InvalidOperationException("Unsupported SV payload layout: " + string.Join("; ", layout.UnsupportedElements.Select(x => $"{x.SignalReference} bType={x.BType}")));

        var entriesByIndex = stream.Entries.ToDictionary(x => x.Index);
        var values = new List<MmsDataValue>(layout.Elements.Count);
        foreach (var element in layout.Elements)
        {
            if (!entriesByIndex.TryGetValue(element.Index, out var entry))
                throw new InvalidOperationException($"SV payload layout entry {element.Index} has no matching DataSet entry.");

            if (element.Kind == SampledValuePayloadElementKind.Quality ||
                element.Kind == SampledValuePayloadElementKind.BitString ||
                element.Kind == SampledValuePayloadElementKind.EntryTime)
            {
                values.Add(MmsDataValue.BitString(0, new byte[element.Width]));
                continue;
            }

            if (element.Kind == SampledValuePayloadElementKind.Timestamp)
            {
                values.Add(MmsDataValue.UtcTime(timestamp));
                continue;
            }

            values.Add(BuildChannelValue(entry, element, channels));
        }

        return SampledValuesPayloadBuilder.BuildPayload(layout, values);
    }

    private MmsDataValue BuildChannelValue(
        SclDataSetEntry entry,
        SampledValuePayloadElement element,
        IReadOnlyDictionary<string, EffectiveChannel> channels)
    {
        var key = ResolveSignalKey(entry);
        if (key is null || !channels.TryGetValue(key, out var effective) || !effective.IsEnabled)
            return ZeroValue(element);

        var dlsb = effective.Kind == "I" ? CurrentDlsb : VoltageDlsb;
        if (dlsb <= 0)
            throw new InvalidOperationException("dLSB must be greater than 0.");

        // Operator values are RMS phasors. IEC 61850-9-2 Sampled Values carry instantaneous samples,
        // therefore the RMS setpoint is converted to peak before dLSB scaling.
        var counts = effective.MagnitudeRms * Math.Sqrt(2.0) / dlsb;
        var sample = counts * Math.Sin(effective.PhaseRadians);
        return element.Kind switch
        {
            SampledValuePayloadElementKind.Boolean => MmsDataValue.Boolean(Math.Abs(sample) >= 0.5),
            SampledValuePayloadElementKind.UInt8 or
            SampledValuePayloadElementKind.UInt16 or
            SampledValuePayloadElementKind.UInt24 or
            SampledValuePayloadElementKind.UInt32 or
            SampledValuePayloadElementKind.UInt64 => MmsDataValue.Unsigned((ulong)Math.Max(0, Math.Round(sample))),
            SampledValuePayloadElementKind.Float32 or
            SampledValuePayloadElementKind.Float64 => MmsDataValue.FloatingPoint((float)sample),
            _ => MmsDataValue.Integer((long)Math.Clamp(Math.Round(sample), long.MinValue, long.MaxValue))
        };
    }

    private static MmsDataValue ZeroValue(SampledValuePayloadElement element)
        => element.Kind switch
        {
            SampledValuePayloadElementKind.Boolean => MmsDataValue.Boolean(false),
            SampledValuePayloadElementKind.UInt8 or
            SampledValuePayloadElementKind.UInt16 or
            SampledValuePayloadElementKind.UInt24 or
            SampledValuePayloadElementKind.UInt32 or
            SampledValuePayloadElementKind.UInt64 => MmsDataValue.Unsigned(0),
            SampledValuePayloadElementKind.Float32 or
            SampledValuePayloadElementKind.Float64 => MmsDataValue.FloatingPoint(0),
            _ => MmsDataValue.Integer(0)
        };

    private IReadOnlyDictionary<string, EffectiveChannel> CaptureEffectiveChannels(double elapsedSeconds)
    {
        var channels = new Dictionary<string, EffectiveChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in Channels)
            channels[channel.Key] = ResolveEffectiveChannel(channel, elapsedSeconds);

        return channels;
    }

    private EffectiveChannel ResolveEffectiveChannel(
        SignalChannelViewModel channel,
        double elapsedSeconds)
    {
        var magnitude = channel.Magnitude;
        var angle = channel.AngleDegrees;
        var frequency = channel.FrequencyHz >= 0 ? channel.FrequencyHz : NominalFrequencyHz;

        if (Mode == InjectionMode.Ramp && ResolveRampState(elapsedSeconds) is { State: var ramp, LocalElapsedSeconds: var localElapsed } &&
            ramp.AppliesToChannel(channel.Key))
        {
            var duration = Math.Max(0.001, ramp.TimeSeconds);
            var position = Math.Clamp(localElapsed / duration, 0.0, 1.0);
            magnitude = ramp.From + ((ramp.To - ramp.From) * position);
        }
        else if (Mode == InjectionMode.Sequencer && ResolveSequenceState(elapsedSeconds) is { } state)
        {
            magnitude = string.Equals(channel.Kind, "I", StringComparison.OrdinalIgnoreCase)
                ? state.CurrentScale
                : NominalVoltageLn * Math.Max(0, state.VoltageScale);
            angle = state.AngleShiftDegrees + PhaseOffsetForChannel(channel.Key);
            frequency = state.FrequencyHz > 0 ? state.FrequencyHz : frequency;
        }

        return new EffectiveChannel(channel.Kind, channel.IsEnabled, magnitude, angle, frequency, angle * Math.PI / 180.0);
    }

    private (RampStateViewModel State, double LocalElapsedSeconds)? ResolveRampState(double elapsedSeconds)
    {
        var states = RampStates.Where(s => s.TimeSeconds > 0).ToArray();
        if (states.Length == 0)
            return null;

        var total = states.Sum(s => Math.Max(0.001, s.TimeSeconds));
        var cursor = Math.Min(Math.Max(0, elapsedSeconds), Math.Max(0, total - 0.000001));

        foreach (var state in states)
        {
            var duration = Math.Max(0.001, state.TimeSeconds);
            if (cursor <= duration)
                return (state, cursor);

            cursor -= duration;
        }

        return (states[^1], Math.Max(0.001, states[^1].TimeSeconds));
    }

    private static double PhaseOffsetForChannel(string channelKey)
    {
        if (string.Equals(channelKey, "Vb", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(channelKey, "Ib", StringComparison.OrdinalIgnoreCase))
            return -120;

        if (string.Equals(channelKey, "Vc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(channelKey, "Ic", StringComparison.OrdinalIgnoreCase))
            return 120;

        return 0;
    }

    private SequenceStateViewModel? ResolveSequenceState(double elapsedSeconds)
    {
        var states = SequenceStates.Where(s => s.DurationSeconds > 0).ToArray();
        if (states.Length == 0)
            return null;

        var total = states.Sum(s => s.DurationSeconds);
        var cursor = LoopSequence ? elapsedSeconds % total : Math.Min(elapsedSeconds, Math.Max(0, total - 0.000001));

        foreach (var state in states)
        {
            if (cursor <= state.DurationSeconds)
                return state;

            cursor -= state.DurationSeconds;
        }

        return states[^1];
    }

    private void ValidateBeforeRun(bool live)
    {
        if (SelectedStream is null)
            throw new InvalidOperationException("Open an SCL file and select an SV stream first.");

        if (SampleRateHz <= 0)
            throw new InvalidOperationException("Sample rate must be greater than 0.");

        if (!Continuous && DurationSeconds <= 0)
            throw new InvalidOperationException("Duration must be greater than 0 for finite publish.");

        if (NominalFrequencyHz <= 0)
            throw new InvalidOperationException("Frequency must be greater than 0.");

        if (CurrentDlsb <= 0 || VoltageDlsb <= 0)
            throw new InvalidOperationException("Current and voltage dLSB must be greater than 0.");

        if (SelectedStream.Stream.NoAsdu != 1)
            throw new InvalidOperationException($"SV stream declares nofASDU={SelectedStream.Stream.NoAsdu}. This publisher currently supports exactly one ASDU per frame.");

        var layout = SampledValuesPayloadLayout.FromDataSet(SelectedStream.Stream.Entries);
        if (!layout.IsFullySupported)
            throw new InvalidOperationException("Unsupported SV payload layout: " + string.Join("; ", layout.UnsupportedElements.Select(x => $"{x.SignalReference} bType={x.BType}")));

        if (!MacAddress.TryParse(SourceMac, out _))
            throw new InvalidOperationException("Source MAC is invalid.");

        if (!MacAddress.TryParse(DestinationMac, out _))
            throw new InvalidOperationException("Destination MAC is invalid.");

        _ = ParseAppId(AppIdText);
        _ = ResolveVlanTag();

        if (live && SelectedAdapter is null)
            throw new InvalidOperationException("Select a NIC adapter before live publishing.");
    }

    private void ApplySelectedStream(SvStreamChoice? choice)
    {
        if (choice is null)
        {
            StreamId = string.Empty;
            StreamControlBlock = string.Empty;
            DataSetReference = string.Empty;
            DataSetEntryCount = 0;
            MappedSignalCount = 0;
            return;
        }

        var stream = choice.Stream;
        StreamControlBlock = stream.ControlBlockReference;
        StreamId = stream.SvId;
        DataSetReference = stream.DataSetReference;
        AppIdText = stream.Address.AppId.HasValue ? $"0x{stream.Address.AppId.Value:X4}" : stream.Address.AppIdText;
        DestinationMac = stream.Address.DestinationMac?.ToString() ?? stream.Address.DestinationMacText;
        UseVlan = stream.Address.VlanId.HasValue;
        VlanId = stream.Address.VlanId ?? 0;
        VlanPriority = stream.Address.VlanPriority ?? 4;
        SampleRateHz = stream.SampleRate == 0 ? SampleRateHz : stream.SampleRate;
        DataSetEntryCount = stream.Entries.Count;
        MappedSignalCount = stream.Entries.Count(e => !e.IsQuality && !e.IsTimestamp && ResolveSignalKey(e) is not null);
        PayloadBytes = EstimatePayloadBytes(stream.Entries);
        AppendEvent($"Selected SV stream #{choice.Index}: {stream.ControlBlockReference}");
        AppendEvent($"DataSet entries={DataSetEntryCount}, mapped injection signals={MappedSignalCount}, payload={PayloadBytes} bytes.");
    }

    private VlanTag? ResolveVlanTag()
    {
        if (!UseVlan)
            return null;

        if (VlanId is < 0 or > 4094)
            throw new InvalidOperationException("VLAN ID must be 0..4094.");

        if (VlanPriority is < 0 or > 7)
            throw new InvalidOperationException("VLAN priority must be 0..7.");

        return new VlanTag((byte)VlanPriority, (ushort)VlanId);
    }

    private static ushort ParseAppId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("APPID is required.");

        var value = text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ushort.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex;
        }

        if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;

        if (ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var implicitHex))
            return implicitHex;

        throw new InvalidOperationException("APPID must be a 16-bit decimal value or hex value like 0x4000.");
    }

    private SignalChannelViewModel? ResolveChannel(SclDataSetEntry entry)
    {
        var key = ResolveSignalKey(entry);
        return key is null ? null : Channels.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveSignalKey(SclDataSetEntry entry)
    {
        if (!int.TryParse(entry.LnInst, NumberStyles.Integer, CultureInfo.InvariantCulture, out var instance))
            return null;

        return entry.LnClass.ToUpperInvariant() switch
        {
            "TCTR" => instance switch
            {
                1 => "Ia",
                2 => "Ib",
                3 => "Ic",
                4 => "In",
                _ => null
            },
            "TVTR" => instance switch
            {
                1 => "Va",
                2 => "Vb",
                3 => "Vc",
                4 => "Vn",
                _ => null
            },
            _ => null
        };
    }

    private static int EstimatePayloadBytes(IEnumerable<SclDataSetEntry> entries)
        => SampledValuesPayloadLayout.FromDataSet(entries.ToArray()).PayloadByteLength;

    private static ushort? ToSampleRate(double sampleRateHz)
    {
        if (sampleRateHz <= 0 || sampleRateHz > ushort.MaxValue)
            return null;

        return (ushort)Math.Round(sampleRateHz);
    }

    private static ushort? MapSampleMode(string sampleMode)
        => sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };

    private static ushort? ResolveSampleCounterWrap(SclSampledValuesStream stream, double sampleRateHz, double nominalFrequencyHz)
    {
        var mode = MapSampleMode(stream.SampleMode);
        var samplesPerSecond = mode switch
        {
            0 when stream.SampleRate > 0 && nominalFrequencyHz > 0 => stream.SampleRate * nominalFrequencyHz,
            1 when sampleRateHz > 0 => sampleRateHz,
            _ => 0
        };

        if (samplesPerSecond <= 0 || samplesPerSecond > ushort.MaxValue)
            return null;

        return (ushort)Math.Round(samplesPerSecond);
    }

    private static ushort IncrementSampleCount(ushort current, ushort? wrap)
    {
        if (wrap is > 1)
            return current + 1 >= wrap.Value ? (ushort)0 : (ushort)(current + 1);

        return current == ushort.MaxValue ? (ushort)0 : (ushort)(current + 1);
    }

    private static async Task DelayUntilSampleAsync(long startedTicks, long sampleIndex, double sampleRateHz, CancellationToken cancellationToken)
    {
        var targetTicks = startedTicks + (long)Math.Round(sampleIndex * Stopwatch.Frequency / sampleRateHz);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return;

            var remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 2)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remainingMs - 1, 10)), cancellationToken).ConfigureAwait(false);
            else
                Thread.SpinWait(64);
        }
    }

    public void SetManualContext(ManualOutputRowViewModel? row, string columnHeader)
    {
        _contextManualRow = row;
        _contextColumnHeader = columnHeader ?? string.Empty;
        OnPropertyChanged(nameof(IsContextValueColumn));
        OnPropertyChanged(nameof(IsContextAngleColumn));
        OnPropertyChanged(nameof(IsContextFrequencyColumn));
        OnPropertyChanged(nameof(IsContextSignalColumn));
    }

    public bool CommitManualRowText(ManualOutputRowViewModel row, string propertyName, out string warning)
    {
        if (row is null)
            throw new ArgumentNullException(nameof(row));

        var committed = row.CommitText(propertyName, out warning);
        if (!committed)
        {
            LiveApplyText = warning;
            return false;
        }

        return true;
    }

    private void StopPublisher()
        => _publisherStop?.Cancel();

    private void RebuildManualRowsFromChannels()
    {
        _isUpdatingManualRows = true;
        try
        {
            ManualRows.Clear();
            if (ManualSetMode == SymmetricalSetMode)
                AddSymmetricalRowsFromChannels();
            else if (ManualSetMode == LineLineSetMode)
                AddLineLineRowsFromChannels();
            else
                AddDirectRowsFromChannels();
        }
        finally
        {
            _isUpdatingManualRows = false;
        }

        ProjectManualRowsToChannels($"Mode={ManualSetMode}");
    }

    private void AddDirectRowsFromChannels()
    {
        AddManualRow("Va", "V L1-E", "V", "V", Channel("Va"));
        AddManualRow("Vb", "V L2-E", "V", "V", Channel("Vb"));
        AddManualRow("Vc", "V L3-E", "V", "V", Channel("Vc"));
        AddManualRow("Vn", "V N", "V", "V", Channel("Vn"));
        AddManualRow("Ia", "I L1", "I", "A", Channel("Ia"));
        AddManualRow("Ib", "I L2", "I", "A", Channel("Ib"));
        AddManualRow("Ic", "I L3", "I", "A", Channel("Ic"));
        AddManualRow("In", "I N", "I", "A", Channel("In"));
    }

    private void AddSymmetricalRowsFromChannels()
    {
        var v = ToSymmetricalComponents(ChannelPhasor("Va"), ChannelPhasor("Vb"), ChannelPhasor("Vc"));
        var i = ToSymmetricalComponents(ChannelPhasor("Ia"), ChannelPhasor("Ib"), ChannelPhasor("Ic"));
        var voltageFrequency = Channel("Va")?.FrequencyHz ?? NominalFrequencyHz;
        var currentFrequency = Channel("Ia")?.FrequencyHz ?? NominalFrequencyHz;

        AddManualRow("V1", "V 1", "V", "V", v.Positive, voltageFrequency, v.Positive.Magnitude > 0.000001);
        AddManualRow("V2", "V 2", "V", "V", v.Negative, voltageFrequency, v.Negative.Magnitude > 0.000001);
        AddManualRow("V0", "V 0", "V", "V", v.Zero, voltageFrequency, v.Zero.Magnitude > 0.000001);
        AddManualRow("I1", "I 1", "I", "A", i.Positive, currentFrequency, i.Positive.Magnitude > 0.000001);
        AddManualRow("I2", "I 2", "I", "A", i.Negative, currentFrequency, i.Negative.Magnitude > 0.000001);
        AddManualRow("I0", "I 0", "I", "A", i.Zero, currentFrequency, i.Zero.Magnitude > 0.000001);
    }

    private void AddLineLineRowsFromChannels()
    {
        AddManualRow("Vab", "V L1-L2", "V", "V", ChannelPhasor("Va") - ChannelPhasor("Vb"), Channel("Va")?.FrequencyHz ?? NominalFrequencyHz, true);
        AddManualRow("Vbc", "V L2-L3", "V", "V", ChannelPhasor("Vb") - ChannelPhasor("Vc"), Channel("Vb")?.FrequencyHz ?? NominalFrequencyHz, true);
        AddManualRow("Vca", "V L3-L1", "V", "V", ChannelPhasor("Vc") - ChannelPhasor("Va"), Channel("Vc")?.FrequencyHz ?? NominalFrequencyHz, true);
        AddManualRow("Ia", "I L1", "I", "A", Channel("Ia"));
        AddManualRow("Ib", "I L2", "I", "A", Channel("Ib"));
        AddManualRow("Ic", "I L3", "I", "A", Channel("Ic"));
        AddManualRow("In", "I N", "I", "A", Channel("In"));
    }

    private void AddManualRow(string key, string name, string kind, string unit, SignalChannelViewModel? channel)
        => ManualRows.Add(new ManualOutputRowViewModel(
            key,
            DisplaySignalName(key, name),
            kind,
            unit,
            channel?.Magnitude ?? 0,
            channel?.AngleDegrees ?? 0,
            channel?.FrequencyHz ?? NominalFrequencyHz,
            channel?.IsEnabled ?? false,
            ManualRowChanged));

    private void AddManualRow(string key, string name, string kind, string unit, Complex phasor, double frequencyHz, bool isEnabled)
        => ManualRows.Add(new ManualOutputRowViewModel(
            key,
            DisplaySignalName(key, name),
            kind,
            unit,
            phasor.Magnitude,
            NormalizeDegrees(phasor.Phase * 180.0 / Math.PI),
            frequencyHz,
            isEnabled,
            ManualRowChanged));

    private void ManualRowChanged(ManualOutputRowViewModel row, string propertyName)
    {
        if (_isUpdatingManualRows)
            return;

        if (LinkFrequencies && propertyName == nameof(ManualOutputRowViewModel.FrequencyHz) && row.FrequencyHz > 0)
            NominalFrequencyHz = row.FrequencyHz;
        else
            ProjectManualRowsToChannels($"Edited {row.Name}");
    }

    private void ProjectManualRowsToChannels(string reason)
    {
        if (_isUpdatingManualRows)
            return;

        if (ManualSetMode == SymmetricalSetMode)
            ProjectSymmetricalRowsToChannels();
        else if (ManualSetMode == LineLineSetMode)
            ProjectLineLineRowsToChannels();
        else
            ProjectDirectRowsToChannels();

        UpdateRampPreview();

        LiveApplyText = IsPublishing
            ? $"RUN auto-applied: {reason}"
            : $"Ready: {reason}";
    }

    private void ProjectDirectRowsToChannels()
    {
        foreach (var row in ManualRows)
        {
            if (Channel(row.Key) is { } channel)
                SetChannel(channel.Key, row.Magnitude, row.AngleDegrees, row.IsEnabled, row.FrequencyHz);
        }
    }

    private void ProjectSymmetricalRowsToChannels()
    {
        var a = Complex.FromPolarCoordinates(1, 120.0 * Math.PI / 180.0);
        var a2 = a * a;
        var v0 = RowPhasor("V0");
        var v1 = RowPhasor("V1");
        var v2 = RowPhasor("V2");
        var i0 = RowPhasor("I0");
        var i1 = RowPhasor("I1");
        var i2 = RowPhasor("I2");
        var voltageFrequency = RowFrequency("V1", "V2", "V0");
        var currentFrequency = RowFrequency("I1", "I2", "I0");
        var voltageEnabled = IsAnyRowEnabled("V1", "V2", "V0");
        var currentEnabled = IsAnyRowEnabled("I1", "I2", "I0");

        SetChannelFromPhasor("Va", v0 + v1 + v2, voltageEnabled, voltageFrequency);
        SetChannelFromPhasor("Vb", v0 + (a2 * v1) + (a * v2), voltageEnabled, voltageFrequency);
        SetChannelFromPhasor("Vc", v0 + (a * v1) + (a2 * v2), voltageEnabled, voltageFrequency);
        SetChannel("Vn", 0, 0, false, voltageFrequency);
        SetChannelFromPhasor("Ia", i0 + i1 + i2, currentEnabled, currentFrequency);
        SetChannelFromPhasor("Ib", i0 + (a2 * i1) + (a * i2), currentEnabled, currentFrequency);
        SetChannelFromPhasor("Ic", i0 + (a * i1) + (a2 * i2), currentEnabled, currentFrequency);
        SetChannel("In", 0, 0, false, currentFrequency);
    }

    private void ProjectLineLineRowsToChannels()
    {
        var vab = RowPhasor("Vab");
        var vbc = RowPhasor("Vbc");
        var vca = RowPhasor("Vca");
        var voltageEnabled = IsAnyRowEnabled("Vab", "Vbc", "Vca");
        var voltageFrequency = RowFrequency("Vab", "Vbc", "Vca");

        SetChannelFromPhasor("Va", (vab - vca) / 3.0, voltageEnabled, voltageFrequency);
        SetChannelFromPhasor("Vb", (vbc - vab) / 3.0, voltageEnabled, voltageFrequency);
        SetChannelFromPhasor("Vc", (vca - vbc) / 3.0, voltageEnabled, voltageFrequency);
        SetChannel("Vn", 0, 0, false, voltageFrequency);

        foreach (var key in new[] { "Ia", "Ib", "Ic", "In" })
        {
            if (Row(key) is { } row)
                SetChannel(key, row.Magnitude, row.AngleDegrees, row.IsEnabled, row.FrequencyHz);
        }
    }

    private void ApplySignalNaming(string scheme)
    {
        var normalized = scheme.Trim().ToUpperInvariant() switch
        {
            "ABC" => "ABC",
            "RSTN" => "RSTN",
            "RAW" => "RAW",
            _ => "L1L2L3E"
        };

        SignalNamingScheme = normalized;
        ApplyChannelNaming();
        foreach (var row in ManualRows)
            row.Name = DisplaySignalName(row.Key, row.Name);

        LiveApplyText = $"Signal naming changed to {normalized}.";
        AppendEvent(LiveApplyText);
    }

    private void ApplyChannelNaming()
    {
        foreach (var channel in Channels)
            channel.Name = DisplaySignalName(channel.Key, channel.Name);

        UpdateRampPreview();
        UpdateSequencePreview();
    }

    private string DisplaySignalName(string key, string fallback)
        => SignalNamingScheme switch
        {
            "ABC" => key switch
            {
                "Va" => "V A-E",
                "Vb" => "V B-E",
                "Vc" => "V C-E",
                "Vn" => "V N",
                "Ia" => "I A",
                "Ib" => "I B",
                "Ic" => "I C",
                "In" => "I N",
                "Vab" => "V A-B",
                "Vbc" => "V B-C",
                "Vca" => "V C-A",
                _ => key
            },
            "RSTN" => key switch
            {
                "Va" => "V R-N",
                "Vb" => "V S-N",
                "Vc" => "V T-N",
                "Vn" => "V N",
                "Ia" => "I R",
                "Ib" => "I S",
                "Ic" => "I T",
                "In" => "I N",
                "Vab" => "V R-S",
                "Vbc" => "V S-T",
                "Vca" => "V T-R",
                _ => key
            },
            "RAW" => key,
            _ => key switch
            {
                "Va" => "V L1-E",
                "Vb" => "V L2-E",
                "Vc" => "V L3-E",
                "Vn" => "V N",
                "Ia" => "I L1",
                "Ib" => "I L2",
                "Ic" => "I L3",
                "In" => "I N",
                "Vab" => "V L1-L2",
                "Vbc" => "V L2-L3",
                "Vca" => "V L3-L1",
                _ => fallback
            }
        };

    private void NominalValueFromContext()
    {
        if (_contextManualRow is not { } row)
            return;

        SetRowsInBatch(() => row.Magnitude = ResolveNominalMagnitude(row), $"Nominal value applied to {row.Name}");
        AppendEvent($"Nominal value applied to {row.Name}.");
    }

    private void ZeroFromContext()
    {
        if (_contextManualRow is not { } row)
            return;

        SetRowsInBatch(() =>
        {
            if (IsContextAngleColumn)
                row.AngleDegrees = 0;
            else if (IsContextFrequencyColumn)
                row.FrequencyHz = 0;
            else
                row.Magnitude = 0;
        }, $"Zero applied to {row.Name}");
        AppendEvent($"Zero applied to {row.Name} {_contextColumnHeader}.");
    }

    private void EqualMagnitudesFromContext()
    {
        if (_contextManualRow is not { } anchor)
            return;

        if (!TryResolveMagnitudeGroup(anchor.Key, anchor.Kind, out var keys))
        {
            LiveApplyText = "Equal Magnitudes is available only for compatible voltage/current rows.";
            return;
        }

        var value = anchor.Magnitude;
        SetRowsInBatch(() =>
        {
            foreach (var key in keys)
            {
                if (Row(key) is { } row)
                    row.Magnitude = value;
            }
        }, $"Equal magnitudes from {anchor.Name}");
        AppendEvent($"Equal magnitudes applied using {anchor.Name}={value:0.000} {anchor.Unit} as reference.");
    }

    private void LineAngleFromContext()
    {
        if (_contextManualRow is not { } row)
            return;

        SetRowsInBatch(() => row.AngleDegrees = ResolveLineAngle(row.Key), $"Line angle applied to {row.Name}");
        AppendEvent($"Line angle applied to {row.Name}.");
    }

    private void BalanceAnglesFromContext()
    {
        if (_contextManualRow is not { } anchor)
        {
            LiveApplyText = "Right-click an angle cell first.";
            return;
        }

        if (!TryResolveBalanceGroup(anchor.Key, reverse: false, out var keys, out var offsets))
        {
            LiveApplyText = "Balance Angles is available only for three-phase voltage/current angle rows.";
            return;
        }

        var anchorIndex = Array.FindIndex(keys, key => string.Equals(key, anchor.Key, StringComparison.OrdinalIgnoreCase));
        if (anchorIndex < 0)
            return;

        var anchorAngle = anchor.AngleDegrees;
        var baseAngle = anchorAngle - offsets[anchorIndex];
        SetRowsInBatch(() =>
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (Row(keys[i]) is { } row)
                    row.AngleDegrees = baseAngle + offsets[i];
            }
        }, $"Balance angles from {anchor.Name}");

        AppendEvent($"Balance angles applied using {anchor.Name} as anchor. Anchor angle stayed {anchorAngle:0.000} deg.");
    }

    private static bool TryResolveBalanceGroup(string key, bool reverse, out string[] keys, out double[] offsets)
    {
        var isLineLine = IsAnyKey(key, "Vab", "Vbc", "Vca");
        if (isLineLine)
        {
            keys = ["Vab", "Vbc", "Vca"];
            offsets = reverse ? [-30, 90, -150] : [30, -90, 150];
            return true;
        }

        if (IsAnyKey(key, "Va", "Vb", "Vc"))
        {
            keys = ["Va", "Vb", "Vc"];
            offsets = reverse ? [0, 120, -120] : [0, -120, 120];
            return true;
        }

        if (IsAnyKey(key, "Ia", "Ib", "Ic"))
        {
            keys = ["Ia", "Ib", "Ic"];
            offsets = reverse ? [0, 120, -120] : [0, -120, 120];
            return true;
        }

        keys = [];
        offsets = [];
        return false;
    }

    private static bool TryResolveMagnitudeGroup(string key, string kind, out string[] keys)
    {
        if (IsAnyKey(key, "Va", "Vb", "Vc"))
        {
            keys = ["Va", "Vb", "Vc"];
            return true;
        }

        if (IsAnyKey(key, "Vab", "Vbc", "Vca"))
        {
            keys = ["Vab", "Vbc", "Vca"];
            return true;
        }

        if (IsAnyKey(key, "Ia", "Ib", "Ic"))
        {
            keys = ["Ia", "Ib", "Ic"];
            return true;
        }

        if (IsAnyKey(key, "V1", "V2", "V0"))
        {
            keys = ["V1", "V2", "V0"];
            return true;
        }

        if (IsAnyKey(key, "I1", "I2", "I0"))
        {
            keys = ["I1", "I2", "I0"];
            return true;
        }

        keys = string.Equals(kind, "V", StringComparison.OrdinalIgnoreCase)
            ? ["Va", "Vb", "Vc"]
            : ["Ia", "Ib", "Ic"];
        return false;
    }

    private static bool IsAnyKey(string key, params string[] candidates)
        => candidates.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

    private static double ResolveNominalMagnitude(ManualOutputRowViewModel row)
        => row.Key switch
        {
            "Vab" or "Vbc" or "Vca" => NominalVoltageLl,
            "Va" or "Vb" or "Vc" or "V1" => NominalVoltageLn,
            "Ia" or "Ib" or "Ic" or "I1" => NominalCurrent,
            _ => 0
        };

    private static double ResolveLineAngle(string key)
        => key switch
        {
            "Vab" => 30,
            "Vbc" => -90,
            "Vca" => 150,
            "Vb" or "Ib" => -120,
            "Vc" or "Ic" => 120,
            _ => 0
        };

    private void ApplyBalancedDefaults()
    {
        ApplyNominalValues();
        AppendEvent("Balanced 3-phase defaults applied.");
    }

    private void ApplyNominalValues()
    {
        if (ManualSetMode == SymmetricalSetMode)
            ApplySymmetricalNominalValues(reverse: false);
        else if (ManualSetMode == LineLineSetMode)
            ApplyLineLineNominalValues(reverse: false, loadCurrent: NominalCurrent);
        else
            ApplyDirectNominalValues(reverse: false, loadCurrent: NominalCurrent);

        AppendEvent("Nominal value preset applied.");
    }

    private void ZeroOutputs()
    {
        SetRowsInBatch(() =>
        {
            foreach (var row in ManualRows)
            {
                row.Magnitude = 0;
                row.IsEnabled = true;
            }
        }, "Zero output applied");
        AppendEvent("Zero output: all manual setpoint magnitudes set to zero.");
    }

    private void EqualMagnitudes()
    {
        SetRowsInBatch(() =>
        {
            var voltage = ManualRows.Where(r => r.Kind == "V" && r.Magnitude > 0).Select(r => r.Magnitude).DefaultIfEmpty(NominalVoltageLn).First();
            var current = ManualRows.Where(r => r.Kind == "I" && r.Magnitude > 0).Select(r => r.Magnitude).DefaultIfEmpty(NominalCurrent).First();
            foreach (var row in ManualRows)
                row.Magnitude = row.Kind == "V" ? voltage : current;
        }, "Equal magnitudes applied");
        AppendEvent("Equal magnitudes applied per signal group.");
    }

    private void ApplyHundredPercentLoad()
    {
        if (ManualSetMode == SymmetricalSetMode)
            ApplySymmetricalNominalValues(reverse: false);
        else if (ManualSetMode == LineLineSetMode)
            ApplyLineLineNominalValues(reverse: false, loadCurrent: NominalCurrent);
        else
            ApplyDirectNominalValues(reverse: false, loadCurrent: NominalCurrent);

        AppendEvent("100% load preset applied.");
    }

    private void ApplyFiftyPercentLoad()
    {
        if (ManualSetMode == SymmetricalSetMode)
            ApplySymmetricalNominalValues(reverse: false, currentMagnitude: NominalCurrent * 0.5);
        else if (ManualSetMode == LineLineSetMode)
            ApplyLineLineNominalValues(reverse: false, loadCurrent: NominalCurrent * 0.5);
        else
            ApplyDirectNominalValues(reverse: false, loadCurrent: NominalCurrent * 0.5);

        AppendEvent("50% load preset applied.");
    }

    private void ApplyUnload()
    {
        if (ManualSetMode == SymmetricalSetMode)
            ApplySymmetricalNominalValues(reverse: false, currentMagnitude: 0);
        else if (ManualSetMode == LineLineSetMode)
            ApplyLineLineNominalValues(reverse: false, loadCurrent: 0);
        else
            ApplyDirectNominalValues(reverse: false, loadCurrent: 0);

        AppendEvent("Unload preset applied: voltage nominal, current zero.");
    }

    private void BalanceAngles()
    {
        SetRowsInBatch(() =>
        {
            if (ManualSetMode == SymmetricalSetMode)
            {
                SetRowAngle("V1", 0);
                SetRowAngle("V2", 0);
                SetRowAngle("V0", 0);
                SetRowAngle("I1", 0);
                SetRowAngle("I2", 0);
                SetRowAngle("I0", 0);
            }
            else if (ManualSetMode == LineLineSetMode)
            {
                SetRowAngle("Vab", 30);
                SetRowAngle("Vbc", -90);
                SetRowAngle("Vca", 150);
                SetRowAngle("Ia", 0);
                SetRowAngle("Ib", -120);
                SetRowAngle("Ic", 120);
            }
            else
            {
                SetRowAngle("Va", 0);
                SetRowAngle("Vb", -120);
                SetRowAngle("Vc", 120);
                SetRowAngle("Ia", 0);
                SetRowAngle("Ib", -120);
                SetRowAngle("Ic", 120);
            }
        }, "Balance angles applied");

        AppendEvent("Balance angles applied.");
    }

    private void ReverseRotationFromContext()
    {
        if (_contextManualRow is not { } anchor)
        {
            LiveApplyText = "Right-click an angle cell first.";
            return;
        }

        if (!TryResolveBalanceGroup(anchor.Key, reverse: true, out var keys, out var offsets))
        {
            LiveApplyText = "Reverse Rotation is available only for three-phase voltage/current angle rows.";
            return;
        }

        var anchorIndex = Array.FindIndex(keys, key => string.Equals(key, anchor.Key, StringComparison.OrdinalIgnoreCase));
        if (anchorIndex < 0)
            return;

        var anchorAngle = anchor.AngleDegrees;
        var baseAngle = anchorAngle - offsets[anchorIndex];
        SetRowsInBatch(() =>
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (Row(keys[i]) is { } row)
                    row.AngleDegrees = baseAngle + offsets[i];
            }
        }, $"Reverse rotation from {anchor.Name}");
        AppendEvent($"Reverse rotation applied using {anchor.Name} as anchor. Anchor angle stayed {anchorAngle:0.000} deg.");
    }

    private void NominalFrequencyFromContext()
    {
        if (_contextManualRow is not { } row)
            return;

        SetRowsInBatch(() => row.FrequencyHz = NominalFrequencyHz, $"Nominal frequency applied to {row.Name}", preserveFrequencies: true);
        AppendEvent($"Nominal frequency {NominalFrequencyHz:0.000} Hz applied to {row.Name}.");
    }

    private void DcFrequencyFromContext()
    {
        if (_contextManualRow is not { } row)
            return;

        SetRowsInBatch(() => row.FrequencyHz = 0, $"DC frequency applied to {row.Name}", preserveFrequencies: true);
        AppendEvent($"DC frequency applied to {row.Name}.");
    }

    private void EqualFrequenciesFromContext()
    {
        if (_contextManualRow is not { } anchor)
            return;

        if (!TryResolveMagnitudeGroup(anchor.Key, anchor.Kind, out var keys))
        {
            LiveApplyText = "Equal Frequencies is available only for compatible voltage/current rows.";
            return;
        }

        var value = anchor.FrequencyHz;
        SetRowsInBatch(() =>
        {
            foreach (var key in keys)
            {
                if (Row(key) is { } row)
                    row.FrequencyHz = value;
            }
        }, $"Equal frequencies from {anchor.Name}", preserveFrequencies: true);
        AppendEvent($"Equal frequencies applied using {anchor.Name}={value:0.000} Hz as reference.");
    }

    private void ReverseRotation()
    {
        if (ManualSetMode == SymmetricalSetMode)
            ApplySymmetricalNominalValues(reverse: true);
        else if (ManualSetMode == LineLineSetMode)
            ApplyLineLineNominalValues(reverse: true, loadCurrent: NominalCurrent);
        else
            ApplyDirectNominalValues(reverse: true, loadCurrent: NominalCurrent);

        AppendEvent("Reverse rotation applied.");
    }

    private void ApplyDirectNominalValues(bool reverse, double loadCurrent)
    {
        SetRowsInBatch(() =>
        {
            SetRow("Va", NominalVoltageLn, 0, true);
            SetRow("Vb", NominalVoltageLn, reverse ? 120 : -120, true);
            SetRow("Vc", NominalVoltageLn, reverse ? -120 : 120, true);
            SetRow("Vn", 0, 0, false);
            SetRow("Ia", loadCurrent, 0, loadCurrent > 0);
            SetRow("Ib", loadCurrent, reverse ? 120 : -120, loadCurrent > 0);
            SetRow("Ic", loadCurrent, reverse ? -120 : 120, loadCurrent > 0);
            SetRow("In", 0, 0, false);
        }, "Direct nominal preset applied");
    }

    private void ApplyLineLineNominalValues(bool reverse, double loadCurrent)
    {
        SetRowsInBatch(() =>
        {
            SetRow("Vab", NominalVoltageLl, reverse ? -30 : 30, true);
            SetRow("Vbc", NominalVoltageLl, reverse ? 90 : -90, true);
            SetRow("Vca", NominalVoltageLl, reverse ? -150 : 150, true);
            SetRow("Ia", loadCurrent, 0, loadCurrent > 0);
            SetRow("Ib", loadCurrent, reverse ? 120 : -120, loadCurrent > 0);
            SetRow("Ic", loadCurrent, reverse ? -120 : 120, loadCurrent > 0);
            SetRow("In", 0, 0, false);
        }, "Line-line nominal preset applied");
    }

    private void ApplySymmetricalNominalValues(bool reverse, double currentMagnitude = NominalCurrent)
    {
        SetRowsInBatch(() =>
        {
            SetRow("V1", reverse ? 0 : NominalVoltageLn, 0, !reverse);
            SetRow("V2", reverse ? NominalVoltageLn : 0, 0, reverse);
            SetRow("V0", 0, 0, false);
            SetRow("I1", reverse ? 0 : currentMagnitude, 0, !reverse && currentMagnitude > 0);
            SetRow("I2", reverse ? currentMagnitude : 0, 0, reverse && currentMagnitude > 0);
            SetRow("I0", 0, 0, false);
        }, "Symmetrical component preset applied");
    }

    private void CopyManualTable()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Signal\tMagnitude\tAngleDeg\tFrequencyHz\tOn");
        foreach (var row in ManualRows)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\t{2}\t{3}\t{4}",
                row.Name,
                row.Magnitude,
                row.AngleDegrees,
                row.FrequencyHz,
                row.IsEnabled));
        }

        Clipboard.SetText(builder.ToString());
        AppendEvent("Manual output table copied to clipboard.");
    }

    private void PasteManualTable()
    {
        if (!Clipboard.ContainsText())
            return;

        var lines = Clipboard.GetText().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return;

        var dataLines = lines.Skip(lines[0].StartsWith("Signal", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ToArray();
        SetRowsInBatch(() =>
        {
            for (var i = 0; i < dataLines.Length && i < ManualRows.Count; i++)
            {
                var cells = dataLines[i].Split('\t');
                if (cells.Length < 4)
                    continue;

                var row = ManualRows[i];
                if (double.TryParse(cells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var magnitude))
                    row.Magnitude = magnitude;
                if (double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
                    row.AngleDegrees = angle;
                if (double.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency))
                    row.FrequencyHz = frequency;
                if (cells.Length >= 5 && bool.TryParse(cells[4], out var enabled))
                    row.IsEnabled = enabled;
            }
        }, "Manual output table pasted");
        AppendEvent("Manual output table pasted from clipboard.");
    }

    private void SetRowsInBatch(Action action, string reason, bool preserveFrequencies = false)
    {
        _isUpdatingManualRows = true;
        try
        {
            action();
            if (LinkFrequencies && !preserveFrequencies)
            {
                foreach (var row in ManualRows)
                    row.FrequencyHz = NominalFrequencyHz;
            }
        }
        finally
        {
            _isUpdatingManualRows = false;
        }

        ProjectManualRowsToChannels(reason);
    }

    private void SetAllManualFrequencies(double frequencyHz)
    {
        _isUpdatingManualRows = true;
        try
        {
            foreach (var row in ManualRows)
                row.FrequencyHz = frequencyHz;
        }
        finally
        {
            _isUpdatingManualRows = false;
        }

        ProjectManualRowsToChannels("Linked frequencies updated");
    }

    private void SetRow(string key, double magnitude, double angle, bool enabled)
    {
        if (Row(key) is not { } row)
            return;

        row.Magnitude = magnitude;
        row.AngleDegrees = angle;
        row.FrequencyHz = NominalFrequencyHz;
        row.IsEnabled = enabled;
    }

    private void SetRowAngle(string key, double angle)
    {
        if (Row(key) is { } row)
            row.AngleDegrees = angle;
    }

    private void SetChannel(string key, double magnitude, double angle, bool enabled, double frequencyHz)
    {
        var channel = Channel(key);
        if (channel is null)
            return;

        channel.Magnitude = Math.Max(0, magnitude);
        channel.AngleDegrees = NormalizeDegrees(angle);
        channel.FrequencyHz = frequencyHz >= 0 ? frequencyHz : NominalFrequencyHz;
        channel.IsEnabled = enabled;
    }

    private void SetChannelFromPhasor(string key, Complex phasor, bool enabled, double frequencyHz)
        => SetChannel(key, phasor.Magnitude, NormalizeDegrees(phasor.Phase * 180.0 / Math.PI), enabled, frequencyHz);

    private SignalChannelViewModel? Channel(string key)
        => Channels.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    private ManualOutputRowViewModel? Row(string key)
        => ManualRows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

    private Complex RowPhasor(string key)
    {
        var row = Row(key);
        if (row is null || !row.IsEnabled)
            return Complex.Zero;

        return Complex.FromPolarCoordinates(row.Magnitude, row.AngleDegrees * Math.PI / 180.0);
    }

    private Complex ChannelPhasor(string key)
    {
        var channel = Channel(key);
        if (channel is null || !channel.IsEnabled)
            return Complex.Zero;

        return Complex.FromPolarCoordinates(channel.Magnitude, channel.AngleDegrees * Math.PI / 180.0);
    }

    private double RowFrequency(params string[] keys)
    {
        foreach (var key in keys)
        {
            var row = Row(key);
            if (row is not null && row.FrequencyHz >= 0)
                return row.FrequencyHz;
        }

        return NominalFrequencyHz;
    }

    private bool IsAnyRowEnabled(params string[] keys)
        => keys.Any(key => Row(key) is { IsEnabled: true });

    private static (Complex Zero, Complex Positive, Complex Negative) ToSymmetricalComponents(Complex phaseA, Complex phaseB, Complex phaseC)
    {
        var a = Complex.FromPolarCoordinates(1, 120.0 * Math.PI / 180.0);
        var a2 = a * a;
        var zero = (phaseA + phaseB + phaseC) / 3.0;
        var positive = (phaseA + (a * phaseB) + (a2 * phaseC)) / 3.0;
        var negative = (phaseA + (a2 * phaseB) + (a * phaseC)) / 3.0;
        return (zero, positive, negative);
    }

    private static double NormalizeDegrees(double degrees)
    {
        while (degrees > 180)
            degrees -= 360;
        while (degrees <= -180)
            degrees += 360;
        return Math.Round(degrees, 6);
    }

    private void AddRampState()
    {
        var choice = SelectedRampSignalChoice ?? RampSignalChoices.FirstOrDefault(choice => string.Equals(choice.KeyCsv, "Ia", StringComparison.OrdinalIgnoreCase)) ?? RampSignalChoices.First();
        var channel = ResolveFirstRampBaseChannel(choice) ?? Channels.FirstOrDefault(c => c.Key == "Ia") ?? Channels.First();
        var from = channel.Magnitude;
        var to = Math.Max(0, from + (channel.Kind == "I" ? 1.0 : 10.0));
        var state = new RampStateViewModel(
            $"Ramp {RampStates.Count + 1}",
            choice.KeyCsv,
            choice.Name,
            choice.Quantity,
            from,
            to,
            (to - from) / 20.0,
            0.100,
            21,
            Math.Max(0.001, RampDurationSeconds));

        AttachRampState(state);
        RampStates.Add(state);
        SelectedRampState = state;
        OnPropertyChanged(nameof(RampTotalTimeSeconds));
        OnPropertyChanged(nameof(RampTotalTimeText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveSelectedRampState()
    {
        if (RampStates.Count == 0)
            return;

        var index = SelectedRampState is null ? RampStates.Count - 1 : RampStates.IndexOf(SelectedRampState);
        if (index < 0)
            index = RampStates.Count - 1;

        var removed = RampStates[index];
        DetachRampState(removed);
        RampStates.RemoveAt(index);
        SelectedRampState = RampStates.Count == 0 ? null : RampStates[Math.Clamp(index, 0, RampStates.Count - 1)];
        OnPropertyChanged(nameof(RampTotalTimeSeconds));
        OnPropertyChanged(nameof(RampTotalTimeText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void AddSequenceState()
    {
        var state = new SequenceStateViewModel($"State {SequenceStates.Count + 1}", 0.500, 1.0, 1.0, 0, NominalFrequencyHz);
        AttachSequenceState(state);
        SequenceStates.Add(state);
        SelectedSequenceState = state;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveLastSequenceState()
    {
        if (SequenceStates.Count == 0)
            return;

        var index = SelectedSequenceState is null ? SequenceStates.Count - 1 : SequenceStates.IndexOf(SelectedSequenceState);
        if (index < 0)
            index = SequenceStates.Count - 1;
        var removed = SequenceStates[index];
        DetachSequenceState(removed);
        SequenceStates.RemoveAt(index);
        SelectedSequenceState = SequenceStates.Count == 0 ? null : SequenceStates[Math.Clamp(index, 0, SequenceStates.Count - 1)];
        CommandManager.InvalidateRequerySuggested();
    }

    private SvPublisherConfigSnapshot CreateSnapshot()
        => new()
        {
            SclPath = SclPath,
            StreamControlBlock = StreamControlBlock,
            StreamId = StreamId,
            DataSetReference = DataSetReference,
            AppId = AppIdText,
            DestinationMac = DestinationMac,
            UseVlan = UseVlan,
            VlanId = VlanId,
            VlanPriority = VlanPriority,
            SourceMac = SourceMac,
            SampleRateHz = SampleRateHz,
            NominalFrequencyHz = NominalFrequencyHz,
            CurrentDlsb = CurrentDlsb,
            VoltageDlsb = VoltageDlsb,
            DurationSeconds = DurationSeconds,
            Continuous = Continuous,
            Mode = Mode,
            ManualSetMode = ManualSetMode,
            AutoApplyWhileRunning = AutoApplyWhileRunning,
            LinkFrequencies = LinkFrequencies,
            SyncPolicyMode = SyncPolicyMode,
            ExpectedPtpDomain = ExpectedPtpDomain,
            PtpAllowLocalFallback = PtpAllowLocalFallback,
            PtpPublisherMode = PtpPublisherMode,
            PtpClockIdentity = PtpClockIdentityText,
            PtpAnnounceIntervalMs = PtpAnnounceIntervalMs,
            PtpSyncIntervalMs = PtpSyncIntervalMs,
            PtpRespondToPeerDelay = PtpRespondToPeerDelay,
            RampSignalKey = SelectedRampSignalChoice?.KeyCsv ?? SelectedRampState?.SignalKey ?? string.Empty,
            RampTargetMagnitude = RampTargetMagnitude,
            RampDurationSeconds = RampDurationSeconds,
            Channels = Channels.Select(c => c.ToSnapshot()).ToArray(),
            SequenceStates = SequenceStates.Select(s => s.ToSnapshot()).ToArray()
        };

    private void AppendEvent(string message)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Dispatch(() => AppendEvent(message));
            return;
        }

        _eventLines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_eventLines.Count > 80)
            _eventLines.RemoveAt(_eventLines.Count - 1);

        EvidenceText = string.Join(Environment.NewLine, _eventLines);
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private static IReadOnlyDictionary<string, EffectiveChannel> ApplyOscillatorPhases(
        IReadOnlyDictionary<string, EffectiveChannel> channels,
        Dictionary<string, OscillatorState> states,
        double sampleRateHz)
    {
        var result = new Dictionary<string, EffectiveChannel>(channels.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in channels)
        {
            if (!states.TryGetValue(pair.Key, out var state))
            {
                state = new OscillatorState
                {
                    PhaseRadians = pair.Value.AngleDegrees * Math.PI / 180.0,
                    LastAngleDegrees = pair.Value.AngleDegrees
                };
                states[pair.Key] = state;
            }

            var angleDelta = NormalizeDegrees(pair.Value.AngleDegrees - state.LastAngleDegrees) * Math.PI / 180.0;
            state.PhaseRadians = WrapRadians(state.PhaseRadians + angleDelta);
            state.LastAngleDegrees = pair.Value.AngleDegrees;
            result[pair.Key] = pair.Value with { PhaseRadians = state.PhaseRadians };
            state.PhaseRadians = WrapRadians(state.PhaseRadians + (2.0 * Math.PI * pair.Value.FrequencyHz / sampleRateHz));
        }

        return result;
    }

    private static double WrapRadians(double radians)
    {
        const double twoPi = 2.0 * Math.PI;
        radians %= twoPi;
        return radians < -Math.PI ? radians + twoPi : radians > Math.PI ? radians - twoPi : radians;
    }

    private sealed class OscillatorState
    {
        public double PhaseRadians { get; set; }
        public double LastAngleDegrees { get; set; }
    }

    private readonly record struct EffectiveChannel(string Kind, bool IsEnabled, double MagnitudeRms, double AngleDegrees, double FrequencyHz, double PhaseRadians);
}
