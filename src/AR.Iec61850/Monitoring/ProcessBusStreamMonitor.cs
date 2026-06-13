using AR.Iec61850.Capture;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Monitoring;

public sealed class ProcessBusStreamMonitor
{
    private readonly Dictionary<string, ProcessBusStreamSummary> _summaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<SampledValuesPublisherProfile> _sampledValuesProfiles;
    private readonly double _nominalFrequencyHz;

    public ProcessBusStreamMonitor()
        : this(Array.Empty<SampledValuesPublisherProfile>())
    {
    }

    public ProcessBusStreamMonitor(SclDocument document, double nominalFrequencyHz = 50)
        : this(SampledValuesPublisherProfile.CreateMany(document), nominalFrequencyHz)
    {
    }

    public ProcessBusStreamMonitor(
        IReadOnlyList<SampledValuesPublisherProfile> sampledValuesProfiles,
        double nominalFrequencyHz = 50)
    {
        _sampledValuesProfiles = sampledValuesProfiles ?? Array.Empty<SampledValuesPublisherProfile>();
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
        if (profile is not null && asdu is not null)
        {
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
            diagnostics);

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

        summary.RecordGoose(frame.Pdu.StateNumber, frame.Pdu.SequenceNumber);

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
            ValueCount = frame.Pdu.Values.Count,
            Detail = string.IsNullOrWhiteSpace(frame.Pdu.GoId) ? $"goCB={streamId}" : $"goID={frame.Pdu.GoId}"
        };
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
