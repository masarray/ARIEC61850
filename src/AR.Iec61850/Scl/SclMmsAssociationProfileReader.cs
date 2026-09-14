using System.Globalization;
using System.Xml.Linq;

namespace AR.Iec61850.Scl;

/// <summary>
/// Reads MMS endpoint and ISO/ACSE association context from SCL Communication/ConnectedAP.
/// The reader is intentionally pure: no socket, discovery, write, report, or control side effects.
/// </summary>
public static class SclMmsAssociationProfileReader
{
    public static SclMmsAssociationProfileSet Read(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("SCL XML is empty.", nameof(xml));

        return Read(XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo));
    }

    public static SclMmsAssociationProfileSet Read(XDocument document)
    {
        var root = document.Root ?? throw new InvalidDataException("SCL document has no root element.");
        if (!Is(root, "SCL"))
            throw new InvalidDataException("The selected file is not an IEC 61850 SCL document.");

        var accessPoints = new List<SclMmsAccessPoint>();
        var warnings = new List<string>();
        var communication = root.Elements().FirstOrDefault(e => Is(e, "Communication"));
        if (communication is null)
            return new SclMmsAssociationProfileSet { AccessPoints = accessPoints, Warnings = warnings };

        foreach (var subNetwork in communication.Elements().Where(e => Is(e, "SubNetwork")))
        {
            var subNetworkName = Attr(subNetwork, "name");
            var subNetworkType = Attr(subNetwork, "type");

            foreach (var connectedAp in subNetwork.Elements().Where(e => Is(e, "ConnectedAP")))
            {
                var iedName = Attr(connectedAp, "iedName");
                var accessPointName = Attr(connectedAp, "apName");
                var directAddress = connectedAp.Elements().FirstOrDefault(e => Is(e, "Address"));
                var parameters = ReadDirectParameters(directAddress);

                string Get(string type)
                    => parameters.TryGetValue(type, out var value) ? value : string.Empty;

                var aeQualifierText = Get("OSI-AE-Qualifier");
                int? aeQualifier = null;
                if (!string.IsNullOrWhiteSpace(aeQualifierText))
                {
                    if (int.TryParse(aeQualifierText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        aeQualifier = parsed;
                    else
                        warnings.Add($"ConnectedAP {Describe(iedName, accessPointName)} has invalid OSI-AE-Qualifier '{aeQualifierText}'.");
                }

                if (string.IsNullOrWhiteSpace(iedName))
                    warnings.Add($"ConnectedAP in SubNetwork '{subNetworkName}' has no iedName.");
                if (string.IsNullOrWhiteSpace(accessPointName))
                    warnings.Add($"ConnectedAP {Describe(iedName, accessPointName)} has no apName.");

                accessPoints.Add(new SclMmsAccessPoint
                {
                    IedName = iedName,
                    AccessPointName = accessPointName,
                    SubNetworkName = subNetworkName,
                    SubNetworkType = subNetworkType,
                    Endpoint = new SclMmsEndpoint
                    {
                        IpAddress = Get("IP"),
                        IpSubnet = Get("IP-SUBNET"),
                        IpGateway = Get("IP-GATEWAY")
                    },
                    Association = new SclIsoAssociationAddress
                    {
                        ApTitle = Get("OSI-AP-Title"),
                        AeQualifierText = aeQualifierText,
                        AeQualifier = aeQualifier,
                        PresentationSelector = Get("OSI-PSEL"),
                        SessionSelector = Get("OSI-SSEL"),
                        TransportSelector = Get("OSI-TSEL")
                    },
                    Parameters = parameters
                });
            }
        }

        return new SclMmsAssociationProfileSet
        {
            AccessPoints = accessPoints,
            Warnings = warnings
        };
    }

    private static IReadOnlyDictionary<string, string> ReadDirectParameters(XElement? address)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (address is null)
            return result;

        // Direct children only. Nested GSE/SMV Address/P values are process-bus
        // addressing and must never leak into the MMS association context.
        foreach (var parameter in address.Elements().Where(e => Is(e, "P")))
        {
            var type = Attr(parameter, "type");
            if (string.IsNullOrWhiteSpace(type))
                continue;

            result[type] = (parameter.Value ?? string.Empty).Trim();
        }

        return result;
    }

    private static string Describe(string iedName, string accessPointName)
    {
        var ied = string.IsNullOrWhiteSpace(iedName) ? "<unknown-IED>" : iedName;
        var ap = string.IsNullOrWhiteSpace(accessPointName) ? "<unknown-AP>" : accessPointName;
        return $"'{ied}/{ap}'";
    }

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
    {
        if (element is null)
            return string.Empty;

        var attr = element.Attributes().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal));
        return attr?.Value?.Trim() ?? string.Empty;
    }
}
