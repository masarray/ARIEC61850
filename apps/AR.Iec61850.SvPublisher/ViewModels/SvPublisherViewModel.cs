using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
using Microsoft.Win32;

namespace AR.Iec61850.SvPublisher.ViewModels;

public sealed class SvPublisherViewModel : ObservableObject
{
    private readonly List<string> _eventLines = new();
    private SvStreamChoice? _selectedStream;
    private AdapterChoice? _selectedAdapter;
    private SignalChannelViewModel? _selectedRampChannel;
    private CancellationTokenSource? _publisherStop;
    private string _sclPath = string.Empty;
    private string _sclSummary = "Open an SCL file to resolve SV streams.";
    private string _statusText = "Idle";
    private string _publishText = "No active publisher.";
    private string _evidenceText = string.Empty;
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
    private bool _continuous;
    private bool _loopSequence = true;
    private bool _isLiveArmed;
    private bool _isPublishing;
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
            new SignalChannelViewModel("Ia", "Ia", "I", "A", 1.000, 0),
            new SignalChannelViewModel("Ib", "Ib", "I", "A", 1.000, -120),
            new SignalChannelViewModel("Ic", "Ic", "I", "A", 1.000, 120),
            new SignalChannelViewModel("In", "In", "I", "A", 0.000, 0) { IsEnabled = false },
            new SignalChannelViewModel("Va", "Va", "V", "V", 57.735, 0),
            new SignalChannelViewModel("Vb", "Vb", "V", "V", 57.735, -120),
            new SignalChannelViewModel("Vc", "Vc", "V", "V", 57.735, 120),
            new SignalChannelViewModel("Vn", "Vn", "V", "V", 0.000, 0) { IsEnabled = false }
        ];

        SequenceStates =
        [
            new SequenceStateViewModel("Prefault", 1.000, 1.0, 1.0, 0, 50),
            new SequenceStateViewModel("Fault", 0.200, 4.0, 0.25, 0, 50),
            new SequenceStateViewModel("Recovery", 1.000, 1.0, 1.0, 0, 50)
        ];

        SelectedRampChannel = Channels.FirstOrDefault(c => c.Key == "Ia");

        OpenSclCommand = new AsyncRelayCommand(OpenSclAsync, () => !IsPublishing);
        RefreshAdaptersCommand = new RelayCommand(RefreshAdapters, () => !IsPublishing);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => !IsPublishing);
        RunDryCommand = new AsyncRelayCommand(() => RunPublishAsync(live: false), () => !IsPublishing);
        RunLiveCommand = new AsyncRelayCommand(() => RunPublishAsync(live: true), () => !IsPublishing);
        StopCommand = new RelayCommand(StopPublisher, () => IsPublishing);
        ApplyBalancedDefaultsCommand = new RelayCommand(ApplyBalancedDefaults, () => !IsPublishing);
        AddSequenceStateCommand = new RelayCommand(AddSequenceState, () => !IsPublishing);
        RemoveSequenceStateCommand = new RelayCommand(RemoveLastSequenceState, () => !IsPublishing && SequenceStates.Count > 0);

        RefreshAdapters();
    }

    public ObservableCollection<SignalChannelViewModel> Channels { get; }
    public ObservableCollection<SequenceStateViewModel> SequenceStates { get; }
    public ObservableCollection<SvStreamChoice> Streams { get; } = new();
    public ObservableCollection<AdapterChoice> Adapters { get; } = new();

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

    public string EvidenceText
    {
        get => _evidenceText;
        private set => SetProperty(ref _evidenceText, value);
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
            if (SetProperty(ref _selectedAdapter, value) &&
                value is not null &&
                !string.IsNullOrWhiteSpace(value.MacAddress))
            {
                SourceMac = value.MacAddress;
            }
        }
    }

    public SignalChannelViewModel? SelectedRampChannel
    {
        get => _selectedRampChannel;
        set => SetProperty(ref _selectedRampChannel, value);
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

    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            if (SetProperty(ref _isPublishing, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public InjectionMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public double RampTargetMagnitude
    {
        get => _rampTargetMagnitude;
        set => SetProperty(ref _rampTargetMagnitude, value);
    }

    public double RampDurationSeconds
    {
        get => _rampDurationSeconds;
        set => SetProperty(ref _rampDurationSeconds, value);
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
            StatusText = live ? "Publishing to NIC." : "Dry-run publishing.";
            AppendEvent(live ? "Live NIC publisher started." : "Dry-run publisher started.");

            await Task.Run(async () => await PublishLoopAsync(live, stop.Token).ConfigureAwait(false)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Publisher stopped.";
            AppendEvent("Publisher stopped by user.");
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
        var frameLimit = Continuous ? (long?)null : Math.Max(1, (long)Math.Round(sampleRateHz * DurationSeconds));
        var startedTicks = Stopwatch.GetTimestamp();
        var startedAt = DateTimeOffset.UtcNow;
        var nextUiTicks = startedTicks;
        var rampStartMagnitude = SelectedRampChannel?.Magnitude ?? 0;
        var rampSignalKey = SelectedRampChannel?.Key ?? string.Empty;
        var sampleCounterWrap = ResolveSampleCounterWrap(selectedStream, sampleRateHz, NominalFrequencyHz);

        IProcessBusTransport transport = live
            ? new NpcapProcessBusTransport(SelectedAdapter?.Selector ?? string.Empty)
            : new InMemoryProcessBusTransport();

        IDisposable? disposableTransport = transport as IDisposable;

        long sent = 0;
        ushort sampleCount = 0;
        var lastFrameBytes = 0;

        try
        {
            while (!frameLimit.HasValue || sent < frameLimit.Value)
            {
                await DelayUntilSampleAsync(startedTicks, sent, sampleRateHz, cancellationToken).ConfigureAwait(false);

                var elapsedSeconds = sent / sampleRateHz;
                var timestamp = startedAt.AddTicks((long)Math.Round(sent * TimeSpan.TicksPerSecond / sampleRateHz));
                var sampleTime = new Iec61850UtcTime(timestamp, Quality: 0);
                var payload = BuildSamplePayload(selectedStream, elapsedSeconds, rampSignalKey, rampStartMagnitude, sampleTime);
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
                                SampleSynchronization = 2,
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
                    var elapsed = Stopwatch.GetElapsedTime(startedTicks);
                    var rate = sent / Math.Max(elapsed.TotalSeconds, 0.001);
                    var progress = frameLimit.HasValue ? $"{sent}/{frameLimit.Value}" : sent.ToString(CultureInfo.InvariantCulture);
                    var message = $"{(live ? "LIVE" : "DRY")} frames={progress} rate={rate:0.0} fps smpCnt={sampleCount} payload={payload.Length}B frame={lastFrameBytes}B";
                    Dispatch(() =>
                    {
                        PayloadBytes = payload.Length;
                        PublishText = message;
                    });
                    nextUiTicks = nowTicks + (long)Math.Round(0.25 * Stopwatch.Frequency);
                }
            }
        }
        finally
        {
            disposableTransport?.Dispose();
        }

        var totalElapsed = Stopwatch.GetElapsedTime(startedTicks);
        var effectiveRate = sent / Math.Max(totalElapsed.TotalSeconds, 0.001);
        Dispatch(() =>
        {
            PublishText = $"Complete frames={sent} elapsed={totalElapsed.TotalSeconds:0.###}s rate={effectiveRate:0.0} fps lastFrame={lastFrameBytes}B";
            StatusText = "Publisher complete.";
            AppendEvent(PublishText);
        });
    }

    private byte[] BuildSamplePayload(
        SclSampledValuesStream stream,
        double elapsedSeconds,
        string rampSignalKey,
        double rampStartMagnitude,
        Iec61850UtcTime timestamp)
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

            values.Add(BuildChannelValue(entry, element, elapsedSeconds, rampSignalKey, rampStartMagnitude));
        }

        return SampledValuesPayloadBuilder.BuildPayload(layout, values);
    }

    private MmsDataValue BuildChannelValue(
        SclDataSetEntry entry,
        SampledValuePayloadElement element,
        double elapsedSeconds,
        string rampSignalKey,
        double rampStartMagnitude)
    {
        var channel = ResolveChannel(entry);
        if (channel is null || !channel.IsEnabled)
            return ZeroValue(element);

        var effective = ResolveEffectiveChannel(channel, elapsedSeconds, rampSignalKey, rampStartMagnitude);
        var dlsb = channel.Kind == "I" ? CurrentDlsb : VoltageDlsb;
        if (dlsb <= 0)
            throw new InvalidOperationException("dLSB must be greater than 0.");

        var angle = (2.0 * Math.PI * effective.FrequencyHz * elapsedSeconds) + (effective.AngleDegrees * Math.PI / 180.0);
        var counts = effective.Magnitude / dlsb;
        var sample = counts * Math.Sin(angle);
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

    private EffectiveChannel ResolveEffectiveChannel(
        SignalChannelViewModel channel,
        double elapsedSeconds,
        string rampSignalKey,
        double rampStartMagnitude)
    {
        var magnitude = channel.Magnitude;
        var angle = channel.AngleDegrees;
        var frequency = NominalFrequencyHz;

        if (Mode == InjectionMode.Ramp && string.Equals(channel.Key, rampSignalKey, StringComparison.OrdinalIgnoreCase))
        {
            var duration = Math.Max(0.001, RampDurationSeconds);
            var position = Math.Clamp(elapsedSeconds / duration, 0.0, 1.0);
            magnitude = rampStartMagnitude + ((RampTargetMagnitude - rampStartMagnitude) * position);
        }
        else if (Mode == InjectionMode.Sequencer && ResolveSequenceState(elapsedSeconds) is { } state)
        {
            magnitude *= channel.Kind == "I" ? state.CurrentScale : state.VoltageScale;
            angle += state.AngleShiftDegrees;
            frequency = state.FrequencyHz > 0 ? state.FrequencyHz : frequency;
        }

        return new EffectiveChannel(magnitude, angle, frequency);
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

        if (live)
        {
            if (!IsLiveArmed)
                throw new InvalidOperationException("Arm live NIC before transmitting raw Ethernet frames.");

            if (SelectedAdapter is null)
                throw new InvalidOperationException("Select a NIC adapter before live publishing.");
        }
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

    private void StopPublisher()
        => _publisherStop?.Cancel();

    private void ApplyBalancedDefaults()
    {
        SetChannel("Ia", 1.000, 0, true);
        SetChannel("Ib", 1.000, -120, true);
        SetChannel("Ic", 1.000, 120, true);
        SetChannel("In", 0.000, 0, false);
        SetChannel("Va", 57.735, 0, true);
        SetChannel("Vb", 57.735, -120, true);
        SetChannel("Vc", 57.735, 120, true);
        SetChannel("Vn", 0.000, 0, false);
        NominalFrequencyHz = 50;
        AppendEvent("Balanced 3-phase defaults applied.");
    }

    private void SetChannel(string key, double magnitude, double angle, bool enabled)
    {
        var channel = Channels.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
            return;

        channel.Magnitude = magnitude;
        channel.AngleDegrees = angle;
        channel.IsEnabled = enabled;
    }

    private void AddSequenceState()
    {
        SequenceStates.Add(new SequenceStateViewModel($"State {SequenceStates.Count + 1}", 0.500, 1.0, 1.0, 0, NominalFrequencyHz));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveLastSequenceState()
    {
        if (SequenceStates.Count == 0)
            return;

        SequenceStates.RemoveAt(SequenceStates.Count - 1);
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
            RampSignalKey = SelectedRampChannel?.Key ?? string.Empty,
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

    private readonly record struct EffectiveChannel(double Magnitude, double AngleDegrees, double FrequencyHz);
}
