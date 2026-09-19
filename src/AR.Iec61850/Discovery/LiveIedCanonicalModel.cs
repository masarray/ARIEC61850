using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Canonical live IED snapshot used for interoperable export. The discovery tree,
/// accepted communication evidence, and exact initial instance-value evidence are kept
/// together so exporters do not have to invent connection or engineering values.
/// </summary>
public sealed class LiveIedCanonicalModel
{
    public string SchemaVersion { get; init; } = "live-ied-canonical-v3";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public LiveIedModelDiscoveryDocument Discovery { get; init; } = new();
    public LiveIedCommunicationEvidence Communication { get; init; } = new();
    public IReadOnlyList<LiveIedInstanceValueEvidence> InstanceValues { get; init; } =
        Array.Empty<LiveIedInstanceValueEvidence>();

    public string IedName => Discovery.IedName;
    public string AccessPointName => string.IsNullOrWhiteSpace(Communication.AccessPointName)
        ? Discovery.AccessPointName
        : Communication.AccessPointName;
}

/// <summary>
/// One scalar leaf observed through an exact FC-root projection. Domain/LN/DO/path are
/// explicit so SCL instance data can be materialized without reparsing display strings.
/// </summary>
public sealed class LiveIedInstanceValueEvidence
{
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string DataObject { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public MmsDataValue Value { get; init; } = null!;
    public string Source { get; init; } = "InitialFcRootRead";

    public string Reference =>
        string.IsNullOrWhiteSpace(Domain) || string.IsNullOrWhiteSpace(LogicalNode)
            ? string.Empty
            : $"{Domain}/{LogicalNode}.{DataObject}.{AttributePath}".TrimEnd('.');
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
        LiveIedCommunicationEvidence communication,
        InitialFcReadExecutionResult? initialRead = null)
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
            InstanceValues = BuildInstanceValues(initialRead),
            GeneratedAtUtc = discovery.GeneratedAtUtc
        };
    }

    private static IReadOnlyList<LiveIedInstanceValueEvidence> BuildInstanceValues(
        InitialFcReadExecutionResult? initialRead)
    {
        if (initialRead is null || initialRead.Batches.Count == 0)
            return Array.Empty<LiveIedInstanceValueEvidence>();

        var values = new List<LiveIedInstanceValueEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in initialRead.Batches.OrderBy(batch => batch.BatchIndex))
        {
            foreach (var projection in batch.Projections)
            {
                var domain = projection.Target.Domain?.Trim() ?? string.Empty;
                var logicalNode = projection.Target.LogicalNode?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode))
                    continue;

                foreach (var leaf in projection.Leaves)
                {
                    if (leaf.Value is null ||
                        leaf.Value.Kind is MmsDataKind.Structure or MmsDataKind.Array or MmsDataKind.Unknown)
                    {
                        continue;
                    }

                    if (!TryResolveDataObject(
                            domain,
                            logicalNode,
                            leaf.Reference,
                            out var dataObject))
                    {
                        continue;
                    }

                    var attributePath = leaf.AttributePath?.Trim().Trim('.') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(dataObject) ||
                        string.IsNullOrWhiteSpace(attributePath))
                    {
                        continue;
                    }

                    var functionalConstraint = (leaf.FunctionalConstraint ?? string.Empty)
                        .Trim()
                        .ToUpperInvariant();
                    var key = string.Concat(
                        domain, "",
                        logicalNode, "",
                        dataObject, "",
                        attributePath, "",
                        functionalConstraint);
                    if (!seen.Add(key))
                        continue;

                    values.Add(new LiveIedInstanceValueEvidence
                    {
                        Domain = domain,
                        LogicalNode = logicalNode,
                        DataObject = dataObject,
                        AttributePath = attributePath,
                        FunctionalConstraint = functionalConstraint,
                        SclBType = leaf.SclBType?.Trim() ?? string.Empty,
                        Value = leaf.Value,
                        Source = "InitialFcRootRead"
                    });
                }
            }
        }

        return values
            .OrderBy(value => value.Domain, StringComparer.Ordinal)
            .ThenBy(value => value.LogicalNode, StringComparer.Ordinal)
            .ThenBy(value => value.DataObject, StringComparer.Ordinal)
            .ThenBy(value => value.AttributePath, StringComparer.Ordinal)
            .ThenBy(value => value.FunctionalConstraint, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryResolveDataObject(
        string domain,
        string logicalNode,
        string? reference,
        out string dataObject)
    {
        dataObject = string.Empty;
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var prefix = $"{domain}/{logicalNode}.";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = text[prefix.Length..].Trim('.');
        var separator = remainder.IndexOf('.');
        dataObject = (separator < 0 ? remainder : remainder[..separator]).Trim();
        return !string.IsNullOrWhiteSpace(dataObject);
    }
}
