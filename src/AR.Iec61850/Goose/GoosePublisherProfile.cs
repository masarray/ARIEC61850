using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Goose;

public sealed class GoosePublisherProfile
{
    private GoosePublisherProfile(
        SclGooseStream stream,
        ushort appId,
        MacAddress destination,
        VlanTag? vlan)
    {
        Stream = stream;
        AppId = appId;
        Destination = destination;
        Vlan = vlan;
    }

    public SclGooseStream Stream { get; }
    public ushort AppId { get; }
    public MacAddress Destination { get; }
    public VlanTag? Vlan { get; }
    public IReadOnlyList<SclDataSetEntry> Entries => Stream.Entries;

    public static IReadOnlyList<GoosePublisherProfile> CreateMany(SclDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GooseStreams.Select(Create).ToArray();
    }

    public static GoosePublisherProfile FromScl(SclDocument document, string? controlBlockReference = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = string.IsNullOrWhiteSpace(controlBlockReference)
            ? document.GooseStreams.FirstOrDefault()
            : document.GooseStreams.FirstOrDefault(s => string.Equals(s.ControlBlockReference, controlBlockReference, StringComparison.OrdinalIgnoreCase));

        if (stream is null)
            throw new SclProfileException("No matching GSEControl stream was found in the SCL document.");

        return Create(stream);
    }

    public GooseFrame CreateFrame(
        MacAddress source,
        IReadOnlyList<MmsDataValue> values,
        Iec61850UtcTime timestamp,
        uint stateNumber = 1,
        uint sequenceNumber = 0,
        bool test = false,
        bool needsCommissioning = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != Entries.Count)
            throw new SclProfileException($"GOOSE {Stream.ControlBlockReference} expects {Entries.Count} values but received {values.Count}.");

        return new GooseFrame
        {
            Destination = Destination,
            Source = source,
            Vlan = Vlan,
            AppId = AppId,
            Pdu = new GoosePdu
            {
                GoCbRef = Stream.ControlBlockReference,
                TimeAllowedToLiveMilliseconds = Stream.MaxTimeMilliseconds == 0 ? 1000 : Stream.MaxTimeMilliseconds,
                DataSetReference = Stream.DataSetReference,
                GoId = string.IsNullOrWhiteSpace(Stream.GoId) ? Stream.ControlName : Stream.GoId,
                Timestamp = timestamp,
                StateNumber = stateNumber,
                SequenceNumber = sequenceNumber,
                Test = test,
                ConfigurationRevision = Stream.ConfigurationRevision,
                NeedsCommissioning = needsCommissioning,
                Values = values
            }
        };
    }

    public byte[] BuildEthernetFrame(
        MacAddress source,
        IReadOnlyList<MmsDataValue> values,
        Iec61850UtcTime timestamp,
        uint stateNumber = 1,
        uint sequenceNumber = 0,
        bool test = false,
        bool needsCommissioning = false)
    {
        return GooseFrameBuilder.BuildEthernetFrame(CreateFrame(source, values, timestamp, stateNumber, sequenceNumber, test, needsCommissioning));
    }

    private static GoosePublisherProfile Create(SclGooseStream stream)
    {
        if (!stream.Address.AppId.HasValue)
            throw new SclProfileException($"GOOSE {stream.ControlBlockReference} has no valid APPID in SCL Communication/GSE.");

        if (!stream.Address.DestinationMac.HasValue)
            throw new SclProfileException($"GOOSE {stream.ControlBlockReference} has no valid destination MAC in SCL Communication/GSE.");

        if (string.IsNullOrWhiteSpace(stream.DataSetReference) || stream.Entries.Count == 0)
            throw new SclProfileException($"GOOSE {stream.ControlBlockReference} has no resolved DataSet entries.");

        return new GoosePublisherProfile(stream, stream.Address.AppId.Value, stream.Address.DestinationMac.Value, stream.Address.ToVlanTag());
    }
}
