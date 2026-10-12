using System.Net;
using System.Xml.Linq;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Evidence-only IEC 61850 identity reconciliation. A server exposes MMS domains,
/// not a guaranteed physical IED name or LDevice.inst boundary. Only a unique,
/// fully matched engineering SCL topology can promote a provisional identity.
/// No vendor-name heuristic, network call, cached-IP assumption, or model mutation.
/// </summary>
public static class TrustedSclIedIdentityMatcher
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static LiveIedIdentity? TryMatch(
        XDocument trustedScl,
        IEnumerable<string> observedMmsDomains,
        string? connectedHost,
        string? selectedIedName = null)
    {
        ArgumentNullException.ThrowIfNull(trustedScl);
        ArgumentNullException.ThrowIfNull(observedMmsDomains);
        if (trustedScl.Root?.Name != Scl + "SCL") return null;
        var observed = observedMmsDomains
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (observed.Count == 0) return null;

        var matches = new List<(string Ied, string Ap, Dictionary<string,string> Aliases, string? Ip)>();
        foreach (var ied in trustedScl.Root.Elements(Scl + "IED"))
        {
            var name = ((string?)ied.Attribute("name") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name) ||
                (!string.IsNullOrWhiteSpace(selectedIedName) &&
                 !name.Equals(selectedIedName.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var ap in ied.Elements(Scl + "AccessPoint"))
            {
                var apName = ((string?)ap.Attribute("name") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(apName)) continue;
                var devices = ap.Element(Scl + "Server")?.Elements(Scl + "LDevice").ToArray()
                    ?? Array.Empty<XElement>();
                if (devices.Length != observed.Count) continue;

                var aliases = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                var invalid = false;
                foreach (var ld in devices)
                {
                    var inst = ((string?)ld.Attribute("inst") ?? "").Trim();
                    var ldName = ((string?)ld.Attribute("ldName") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(inst) || !IsValidSclIdentifier(inst))
                    {
                        invalid = true;
                        break;
                    }
                    var domain = ldName.Length > 0 ? ldName : name + inst;
                    if (!observed.Contains(domain) || !aliases.TryAdd(domain,inst))
                    {
                        invalid = true;
                        break;
                    }
                }
                if (invalid || aliases.Count != observed.Count) continue;

                var connected = trustedScl.Root.Element(Scl + "Communication")?
                    .Descendants(Scl + "ConnectedAP")
                    .Where(x => string.Equals((string?)x.Attribute("iedName"),name,StringComparison.OrdinalIgnoreCase)
                        && string.Equals((string?)x.Attribute("apName"),apName,StringComparison.OrdinalIgnoreCase))
                    .ToArray() ?? Array.Empty<XElement>();
                if (connected.Length > 1) continue;
                var ipValues = connected.SingleOrDefault()?
                    .Element(Scl + "Address")?.Elements(Scl + "P")
                    .Where(x => string.Equals((string?)x.Attribute("type"),"IP",StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value.Trim()).Take(2).ToArray() ?? Array.Empty<string>();
                if (ipValues.Length > 1) continue;
                var ip = ipValues.SingleOrDefault();
                if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(connectedHost))
                {
                    if (!IPAddress.TryParse(ip,out var designIp) ||
                        !IPAddress.TryParse(connectedHost.Trim(),out var observedIp) ||
                        !designIp.Equals(observedIp))
                        continue; // never promote a model for a different endpoint
                }
                matches.Add((name, apName, aliases, ip));
            }
        }
        // A single IED can have multiple APs and the same LD topology. This
        // remains ambiguous until AP is independently selected/proven.
        if (matches.Count != 1) return null;
        var match=matches[0];
        return new LiveIedIdentity
        {
            IedName=match.Ied,
            Source="TrustedSclExactDomainMatch",
            Confidence=LiveIedDiscoveryConfidenceLevel.High,
            IsAmbiguous=false,
            CandidateNames=[match.Ied],
            LogicalDeviceAliases=match.Aliases,
            Evidence=[
                $"Trusted SCL IED '{match.Ied}' / AccessPoint '{match.Ap}' matches all {observed.Count} observed MMS domains and unique LDevice instances.",
                string.IsNullOrWhiteSpace(match.Ip) ? "No endpoint IP asserted by SCL." : $"SCL endpoint IP {match.Ip}."
            ]
        };
    }

    private static bool IsValidSclIdentifier(string s)
        => s.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
}
