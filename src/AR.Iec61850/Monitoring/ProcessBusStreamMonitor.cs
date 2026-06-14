using AR.Iec61850.Capture;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Monitoring;

public sealed class ProcessBusStreamMonitor
{
    private readonly Dictionary<string, ProcessBusStreamSummary> _summaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<SampledValuesPublisherProfile> _sampledValuesProfiles;
    private readonly IReadOnlyList<GoosePublisherProfile> _gooseProfiles;
    private readonly double _nominalFrequencyHz;

    public ProcessBusStreamMonitor()
        : this(Array.Empty<SampledValuesPublisherProfile>(), Array.Empty<GoosePublisherProfile>())
    {
    }

    public ProcessBusStreamMonitor(SclDocument document, double nominalFrequencyHz = 50)
        : this(SampledValuesPublisherProfile.CreateMany(document), GoosePublisherProfile.CreateMany(document), nominalFrequencyHz)
    {
    }

    public ProcessBusStreamMonitor(
        IReadOnlyList<SampledValuesPublisherProfile> sampledValuesProfiles,
        double nominalFrequencyHz = 50)
        : this(sampledValuesProfiles, Array.Empty<GoosePublisherProfile>(), nominalFrequencyHz)
    {
    }

    public ProcessBusStreamMonitor(
        IReadOnlyList<SampledValuesPublisherProfile> sampledValuesProfiles,
        IReadOnlyList<GoosePublisherProfile> gooseProfiles,
        double nominalFrequencyHz = 50)
    {
        _sampledValuesProfiles = sampledValuesProfiles ?? Array.Empty<SampledValuesPublisherProfile>();
        _gooseProfiles = gooseProfiles ?? Array.Empty<GoosePublisherProfile>();
        _nominalFrequencyHz = nominalFrequencyHz <= 0 ? 50 : nominalFrequencyHz;
    }

    public IReadOnlyCollection<ProcessBusStreamSummary> Summaries => _summaries.Values;

    public ProcessBusStreamEvent Observe(PcapPacket packet)
        => Observe(packet.Timestamp, packet.Frame);

    public ProcessBusStreamEvent Observe(DateTimeOffset timestamp, ReadOnlyMemory<byte> frame)
    {
        if (SampledValuesFrameParser.TryParseEthernetFrame(frame, out var svFrame))
            return ObserveSampledValues(timestamp, svFrame);

        if (GooseFrameParser.TryParseEthernetFrame(frame, out var gooseFrame))
            return ObserveGoose(timestamp, gooseFrame);

        return new ProcessBusStreamEvent
        {
            Kind = ProcessBusEventKind.Unknown,
            Timestamp = timestamp,
            PayloadBytes = frame.Length,
            Detail = "Unsupported or undecoded Ethernet frame"
        };
    }

    private ProcessBusStreamEvent ObserveSampledValues(DateTimeOffset timestamp, SampledValuesFrame frame)
    {
        var asdu = frame.Pdu.Asdus.FirstOrDefault();
        var streamId = string.IsNullOrWhiteSpace(asdu?.SvId) ? frame.AppId.ToString("X4") : asdu.SvId;
        var profile = asdu is null ? null : FindSampledValuesProfile(frame, asdu);
        var diagnostics = new List<string>();
        IReadOnlyList<SampledValuesDecodedValue> decodedValues = Array.Empty<SampledValuesDecodedValue>();

        if (asdu is null)
        {
            diagnostics.Add("SV frame does not contain an ASDU; stream-level payload checks cannot be evaluated.");
        }
        else
        {
            if (frame.Pdu.Asdus.Count != 1)
                diagnostics.Add($"SV frame contains {frame.Pdu.Asdus.Count} ASDU(s); current analyzer evaluates the first ASDU.");

            if (asdu.SampleSynchronization != 2)
                diagnostics.Add($"SV smpSynch is {asdu.SampleSynchronization}; expected synchronized value 2 for normal process-bus evidence.");
        }

        if (profile is not null && asdu is not null)
        {
            if (profile.Stream.ConfigurationRevision != asdu.ConfigurationRevision)
                diagnostics.Add($"SV confRev mismatch. SCL={profile.Stream.ConfigurationRevision}, frame={asdu.ConfigurationRevision}.");

            if (!string.Equals(profile.Destination.ToString(), frame.Destination.ToString(), StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"SV destination MAC differs from SCL. SCL={profile.Destination}, frame={frame.Destination}.");

            if (profile.Vlan?.VlanId != frame.Vlan?.VlanId)
                diagnostics.Add($"SV VLAN-ID differs from SCL. SCL={profile.Vlan?.VlanId.ToString() ?? "-"}, frame={frame.Vlan?.VlanId.ToString() ?? "-"}.");

            if (profile.Stream.NoAsdu != 0 && profile.Stream.NoAsdu != frame.Pdu.Asdus.Count)
                diagnostics.Add($"SV nofASDU mismatch. SCL={profile.Stream.NoAsdu}, frame={frame.Pdu.Asdus.Count}.");

            if (profile.Stream.SampleRate != 0 && asdu.SampleRate.HasValue && profile.Stream.SampleRate != asdu.SampleRate.Value)
                diagnostics.Add($"SV sample-rate mismatch. SCL={profile.Stream.SampleRate}, frame={asdu.SampleRate.Value}.");

            var expectedMode = TryMapSampleMode(profile.Stream.SampleMode);
            if (expectedMode.HasValue && asdu.SampleMode.HasValue && expectedMode.Value != asdu.SampleMode.Value)
                diagnostics.Add($"SV sample-mode mismatch. SCL={expectedMode.Value}, frame={asdu.SampleMode.Value}.");

            var decode = SampledValuesPayloadDecoder.Decode(profile.PayloadLayout, asdu.SamplePayload);
            decodedValues = decode.Values;
            diagnostics.AddRange(decode.Diagnostics);
        }

        var key = $"SV|{frame.AppId:X4}|{frame.Source}|{frame.Destination}|{frame.Vlan?.VlanId}|{streamId}|{asdu?.ConfigurationRevision}";
        var summary = GetOrAddSummary(
            key,
            ProcessBusEventKind.SampledValues,
            frame.AppId,
            frame.Source.ToString(),
            frame.Destination.ToString(),
            frame.Vlan?.VlanId,
            frame.Vlan?.PriorityCodePoint,
            streamId,
            asdu?.ConfigurationRevision);

        var sequenceStatus = summary.RecordSample(
            asdu?.SampleCount,
            profile?.ResolveSampleCounterWrap(_nominalFrequencyHz),
            decodedValues.Count,
            diagnostics,
            asdu?.SamplePayload.Length ?? 0,
            asdu?.SampleRate,
            asdu?.SampleMode,
            asdu?.SampleSynchronization,
            frame.Pdu.Asdus.Count);

        return new ProcessBusStreamEvent
        {
            Kind = ProcessBusEventKind.SampledValues,
            Timestamp = timestamp,
            AppId = frame.AppId,
            Source = frame.Source.ToString(),
            Destination = frame.Destination.ToString(),
            VlanId = frame.Vlan?.VlanId,
            VlanPriority = frame.Vlan?.PriorityCodePoint,
            StreamId = streamId,
            ConfigurationRevision = asdu?.ConfigurationRevision,
            SampleCount = asdu?.SampleCount,
            PayloadBytes = asdu?.SamplePayload.Length ?? 0,
            SequenceStatus = sequenceStatus,
            IsBoundToScl = profile is not null,
            ControlBlockReference = profile?.Stream.ControlBlockReference ?? string.Empty,
            DecodedValueCount = decodedValues.Count,
            DecodedValues = decodedValues,
            Diagnostics = diagnostics,
            Detail = asdu is null
                ? "SV frame without ASDU"
                : profile is null
                    ? $"svID={streamId}; no SCL profile binding"
                    : $"svID={streamId}; bound={profile.Stream.ControlBlockReference}"
        };
    }

    private ProcessBusStreamEvent ObserveGoose(DateTimeOffset timestamp, GooseFrame frame)
    {
        var streamId = string.IsNullOrWhiteSpace(frame.Pdu.GoCbRef) ? frame.AppId.ToString("X4") : frame.Pdu.GoCbRef;
        var profile = FindGooseProfile(frame);
        var diagnostics = new List<string>();

        if (frame.Pdu.TimeAllowedToLiveMilliseconds == 0)
            diagnostics.Add("GOOSE TimeAllowedToLive is zero; supervision cannot be evaluated.");

        if (frame.Pdu.Test)
            diagnostics.Add("GOOSE test flag is set.");

        if (frame.Pdu.NeedsCommissioning)
            diagnostics.Add("GOOSE ndsCom flag is set.");

        if (profile is not null)
        {
            if (profile.Stream.ConfigurationRevision != frame.Pdu.ConfigurationRevision)
                diagnostics.Add($"GOOSE confRev mismatch. SCL={profile.Stream.ConfigurationRevision}, frame={frame.Pdu.ConfigurationRevision}.");

            if (!string.Equals(profile.Destination.ToString(), frame.Destination.ToString(), StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"GOOSE destination MAC differs from SCL. SCL={profile.Destination}, frame={frame.Destination}.");

            if (profile.Entries.Count != frame.Pdu.Values.Count)
                diagnostics.Add($"GOOSE DataSet value count mismatch. SCL={profile.Entries.Count}, frame={frame.Pdu.Values.Count}.");
        }

        var key = $"GOOSE|{frame.AppId:X4}|{frame.Source}|{frame.Destination}|{frame.Vlan?.VlanId}|{streamId}|{frame.Pdu.ConfigurationRevision}";
        var summary = GetOrAddSummary(
            key,
            ProcessBusEventKind.Goose,
            frame.AppId,
            frame.Source.ToString(),
            frame.Destination.ToString(),
            frame.Vlan?.VlanId,
            frame.Vlan?.PriorityCodePoint,
            streamId,
            frame.Pdu.ConfigurationRevision);

        if (summary.LastGooseTimestamp.HasValue &&
            summary.LastTimeAllowedToLiveMilliseconds is > 0 &&
            (timestamp - summary.LastGooseTimestamp.Value).TotalMilliseconds > summary.LastTimeAllowedToLiveMilliseconds.Value)
        {
            diagnostics.Add(
                $"GOOSE supervision expired before this frame. Gap={(timestamp - summary.LastGooseTimestamp.Value).TotalMilliseconds:0.###} ms, TAL={summary.LastTimeAllowedToLiveMilliseconds.Value} ms.");
        }

        var previousDisplays = summary.LastGooseValueDisplays.ToArray();
        var valueDisplays = frame.Pdu.Values.Select(MmsDataValueRenderer.ToCompactString).ToArray();
        var gooseStatus = summary.RecordGoose(
            timestamp,
            frame.Pdu.StateNumber,
            frame.Pdu.SequenceNumber,
            frame.Pdu.TimeAllowedToLiveMilliseconds,
            valueDisplays,
            diagnostics,
            out var changedIndexes,
            out var changedSummary);

        var changedValueCount = changedIndexes.Count(x => x);
        if (changedValueCount > 0 &&
            gooseStatus is GooseSequenceStatus.Retransmission or GooseSequenceStatus.Duplicate or GooseSequenceStatus.SequenceJump)
        {
            diagnostics.Add("GOOSE values changed without a state-number increment.");
        }

        if (changedValueCount == 0 && gooseStatus == GooseSequenceStatus.StateChange)
            diagnostics.Add("GOOSE state number changed but decoded DataSet values did not change.");

        summary.SetLastDiagnostics(diagnostics);
        var decodedValues = BuildGooseValues(profile, frame.Pdu.Values, changedIndexes, previousDisplays);

        return new ProcessBusStreamEvent
        {
            Kind = ProcessBusEventKind.Goose,
            Timestamp = timestamp,
            AppId = frame.AppId,
            Source = frame.Source.ToString(),
            Destination = frame.Destination.ToString(),
            VlanId = frame.Vlan?.VlanId,
            VlanPriority = frame.Vlan?.PriorityCodePoint,
            StreamId = streamId,
            ConfigurationRevision = frame.Pdu.ConfigurationRevision,
            StateNumber = frame.Pdu.StateNumber,
            SequenceNumber = frame.Pdu.SequenceNumber,
            GooseSequenceStatus = gooseStatus,
            TimeAllowedToLiveMilliseconds = frame.Pdu.TimeAllowedToLiveMilliseconds,
            ValueCount = frame.Pdu.Values.Count,
            IsBoundToScl = profile is not null,
            ControlBlockReference = profile?.Stream.ControlBlockReference ?? frame.Pdu.GoCbRef,
            DecodedValueCount = decodedValues.Count,
            GooseValues = decodedValues,
            ChangedValueCount = changedValueCount,
            ChangedSummary = changedSummary,
            Diagnostics = diagnostics,
            Detail = string.IsNullOrWhiteSpace(frame.Pdu.GoId) ? $"goCB={streamId}" : $"goID={frame.Pdu.GoId}"
        };
    }

    private IReadOnlyList<GooseDecodedValue> BuildGooseValues(
        GoosePublisherProfile? profile,
        IReadOnlyList<MmsDataValue> values,
        IReadOnlyList<bool> changedIndexes,
        IReadOnlyList<string> previousDisplays)
    {
        var result = new List<GooseDecodedValue>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var entry = profile is not null && i < profile.Entries.Count ? profile.Entries[i] : null;
            var display = MmsDataValueRenderer.ToCompactString(values[i]);
            result.Add(new GooseDecodedValue
            {
                Index = i,
                SignalReference = entry?.SignalReference ?? string.Empty,
                Fc = entry?.Fc ?? string.Empty,
                Cdc = entry?.Cdc ?? string.Empty,
                BType = entry?.BType ?? string.Empty,
                Value = values[i],
                DisplayValue = display,
                IsChanged = i < changedIndexes.Count && changedIndexes[i],
                PreviousDisplayValue = i < previousDisplays.Count ? previousDisplays[i] : string.Empty
            });
        }

        return result;
    }


    private static ushort? TryMapSampleMode(string sampleMode)
    {
        if (string.IsNullOrWhiteSpace(sampleMode))
            return null;

        return sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };
    }

    private GoosePublisherProfile? FindGooseProfile(GooseFrame frame)
    {
        var exact = _gooseProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Destination.ToString(), frame.Destination.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.Stream.ControlBlockReference, frame.Pdu.GoCbRef, StringComparison.OrdinalIgnoreCase) &&
            profile.Stream.ConfigurationRevision == frame.Pdu.ConfigurationRevision);
        if (exact is not null)
            return exact;

        var byGoCb = _gooseProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Stream.ControlBlockReference, frame.Pdu.GoCbRef, StringComparison.OrdinalIgnoreCase));
        if (byGoCb is not null)
            return byGoCb;

        var byDataSet = _gooseProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Stream.DataSetReference, frame.Pdu.DataSetReference, StringComparison.OrdinalIgnoreCase));
        if (byDataSet is not null)
            return byDataSet;

        return _gooseProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Stream.GoId, frame.Pdu.GoId, StringComparison.OrdinalIgnoreCase));
    }

    private SampledValuesPublisherProfile? FindSampledValuesProfile(SampledValuesFrame frame, SampledValueAsdu asdu)
    {
        var exact = _sampledValuesProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Destination.ToString(), frame.Destination.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.Stream.SvId, asdu.SvId, StringComparison.OrdinalIgnoreCase) &&
            profile.Stream.ConfigurationRevision == asdu.ConfigurationRevision);
        if (exact is not null)
            return exact;

        var bySvId = _sampledValuesProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Stream.SvId, asdu.SvId, StringComparison.OrdinalIgnoreCase));
        if (bySvId is not null)
            return bySvId;

        return _sampledValuesProfiles.FirstOrDefault(profile =>
            profile.AppId == frame.AppId &&
            string.Equals(profile.Stream.DataSetReference, asdu.DataSetReference, StringComparison.OrdinalIgnoreCase));
    }

    private ProcessBusStreamSummary GetOrAddSummary(
        string key,
        ProcessBusEventKind kind,
        ushort appId,
        string source,
        string destination,
        ushort? vlanId,
        byte? vlanPriority,
        string streamId,
        uint? configurationRevision)
    {
        if (_summaries.TryGetValue(key, out var summary))
            return summary;

        summary = new ProcessBusStreamSummary
        {
            Kind = kind,
            AppId = appId,
            Source = source,
            Destination = destination,
            VlanId = vlanId,
            VlanPriority = vlanPriority,
            StreamId = streamId,
            ConfigurationRevision = configurationRevision
        };
        _summaries[key] = summary;
        return summary;
    }
}
