using AR.Iec61850.Scl;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Canonical live IED snapshot used for interoperable export. The discovery tree and
/// the communication/association evidence that reached the same accepted MMS session
/// are kept together so exporters do not have to invent connection parameters.
/// </summary>
public sealed class LiveIedCanonicalModel
{
    public string SchemaVersion { get; init; } = "live-ied-canonical-v2";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public LiveIedModelDiscoveryDocument Discovery { get; init; } = new();
    public LiveIedCommunicationEvidence Communication { get; init; } = new();

    public string IedName => Discovery.IedName;
    public string AccessPointName => string.IsNullOrWhiteSpace(Communication.AccessPointName)
        ? Discovery.AccessPointName
        : Communication.AccessPointName;
}

/// <summary>
/// Communication evidence bound to the exact accepted association used for discovery.
/// Empty fields mean unknown; they must not be replaced by device-specific guesses.
/// </summary>
public sealed class LiveIedCommunicationEvidence
{
    public string Source { get; init; } = string.Empty;
    public string AssociationProfileName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string AccessPointName { get; init; } = "AP1";
    public string SubNetworkName { get; init; } = "StationBus";
    public string IpSubnet { get; init; } = string.Empty;
    public string IpGateway { get; init; } = string.Empty;
    public SclIsoAssociationAddress Association { get; init; } = new();

    public bool HasInteroperableSclAssociation =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Association.ApTitle) &&
        Association.AeQualifier is >= 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(Association.PresentationSelector) &&
        !string.IsNullOrWhiteSpace(Association.SessionSelector) &&
        !string.IsNullOrWhiteSpace(Association.TransportSelector);
}

public static class LiveIedCanonicalModelBuilder
{
    public static LiveIedCanonicalModel Build(
        LiveIedModelDiscoveryDocument discovery,
        LiveIedCommunicationEvidence communication)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(communication);

        if (!string.IsNullOrWhiteSpace(discovery.Host) &&
            !string.IsNullOrWhiteSpace(communication.Host) &&
            !string.Equals(discovery.Host.Trim(), communication.Host.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Discovery host '{discovery.Host}' does not match accepted-association host '{communication.Host}'.");
        }

        return new LiveIedCanonicalModel
        {
            Discovery = discovery,
            Communication = communication,
            GeneratedAtUtc = discovery.GeneratedAtUtc
        };
    }
}
