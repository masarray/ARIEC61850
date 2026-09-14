namespace AR.Iec61850.Scl;

/// <summary>
/// Typed, read-only engineering context for one SCL Communication/ConnectedAP entry.
/// This model describes what the SCL declares; it does not imply that the endpoint
/// has been contacted or validated against live MMS evidence.
/// </summary>
public sealed class SclMmsAccessPoint
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string SubNetworkName { get; init; } = string.Empty;
    public string SubNetworkType { get; init; } = string.Empty;
    public SclMmsEndpoint Endpoint { get; init; } = new();
    public SclIsoAssociationAddress Association { get; init; } = new();

    /// <summary>
    /// Original direct Address/P parameters keyed case-insensitively by P/@type.
    /// Known parameters are also projected into Endpoint/Association; unknown
    /// parameters remain visible here instead of being silently discarded.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool HasNetworkEndpoint => !string.IsNullOrWhiteSpace(Endpoint.IpAddress);
}

/// <summary>
/// Network endpoint declared by SCL. MMS/TCP uses port 102 by protocol convention;
/// the port is deliberately not treated as an SCL-derived value here.
/// </summary>
public sealed class SclMmsEndpoint
{
    public string IpAddress { get; init; } = string.Empty;
    public string IpSubnet { get; init; } = string.Empty;
    public string IpGateway { get; init; } = string.Empty;
}

/// <summary>
/// ISO/ACSE association addressing declared in SCL. Selector text is preserved as
/// engineering data and is not encoded here; byte encoding belongs to the transport/
/// association layer in a later step.
/// </summary>
public sealed class SclIsoAssociationAddress
{
    public string ApTitle { get; init; } = string.Empty;
    public string AeQualifierText { get; init; } = string.Empty;
    public int? AeQualifier { get; init; }
    public string PresentationSelector { get; init; } = string.Empty;
    public string SessionSelector { get; init; } = string.Empty;
    public string TransportSelector { get; init; } = string.Empty;
}

/// <summary>
/// Result of reading MMS association declarations from an SCL document.
/// Missing/ambiguous engineering information stays explicit through warnings.
/// </summary>
public sealed class SclMmsAssociationProfileSet
{
    public IReadOnlyList<SclMmsAccessPoint> AccessPoints { get; init; } = Array.Empty<SclMmsAccessPoint>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public SclMmsAccessPoint? Find(string iedName, string accessPointName)
        => AccessPoints.FirstOrDefault(ap =>
            string.Equals(ap.IedName, iedName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ap.AccessPointName, accessPointName, StringComparison.OrdinalIgnoreCase));
}
