using System.Globalization;
using System.Xml.Linq;

namespace AR.Iec61850.Scl;

/// <summary>
/// Reads MMS endpoint and ISO/ACSE association context from SCL Communication/ConnectedAP.
/// The reader is intentionally pure: no socket, discovery, write, report, or control side effects.
/// </summary>
public static class SclMmsAssociationProfileReader
{
    private static readonly HashSet<string> CriticalAssociationParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "OSI-AP-Title",
        "OSI-AE-Qualifier",
        "OSI-PSEL",
        "OSI-SSEL",
        "OSI-TSEL"
    };

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
                var parameters = ReadDirectParameters(directAddress, iedName, accessPointName, warnings);

                string Get(string type)
                    => parameters.TryGetValue(type, out var value) ? value : string.Empty;

                var aeQualifierText = Get("OSI-AE-Qualifier");
                int? aeQualifier = null;
                if (!string.IsNullOrWhiteSpace(aeQualifierText))
                {
                    if (int.TryParse(aeQualifierText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                        parsed is >= 0 and <= 65535)
                    {
                        aeQualifier = parsed;
                    }
                    else
                    {
                        warnings.Add($"ConnectedAP {Describe(iedName, accessPointName)} has invalid OSI-AE-Qualifier '{aeQualifierText}'; expected 0..65535.");
                    }
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
                        // Some Edition-1/vendor exports serialize the OID as a quoted
                        // lexical literal (for example "1,1,1,999,1"). Preserve the raw
                        // parameter in Parameters, but normalize the typed projection so
                        // the ASN.1 OID parser sees the actual arcs rather than quote bytes.
                        ApTitle = NormalizeApTitleLiteral(Get("OSI-AP-Title")),
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

    private static IReadOnlyDictionary<string, string> ReadDirectParameters(
        XElement? address,
        string iedName,
        string accessPointName,
        ICollection<string> warnings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (address is null)
            return result;

        // Direct children only. Nested GSE/SMV Address/P values are process-bus
        // addressing and must never leak into the MMS association context.
        var groups = address.Elements()
            .Where(e => Is(e, "P"))
            .Select(parameter => new
            {
                Type = Attr(parameter, "type"),
                Value = (parameter.Value ?? string.Empty).Trim()
            })
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Type))
            .GroupBy(parameter => parameter.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var values = group.Select(parameter => parameter.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (CriticalAssociationParameters.Contains(group.Key) && values.Length > 1)
            {
                // Conflicting called-side association identity is deliberately rendered
                // unresolved so the exact association plan fails closed. Never choose a
                // selector/AP-title/AE value by element order.
                result[group.Key] = string.Empty;
                warnings.Add(
                    $"ConnectedAP {Describe(iedName, accessPointName)} has conflicting duplicate {group.Key} values; the association parameter is ambiguous and was left unresolved.");
                continue;
            }

            result[group.Key] = values[0];
            if (group.Count() > 1)
            {
                warnings.Add(
                    $"ConnectedAP {Describe(iedName, accessPointName)} repeats {group.Key} with the same value; the duplicate declaration was collapsed deterministically.");
            }
        }

        return result;
    }

    private static string NormalizeApTitleLiteral(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
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
