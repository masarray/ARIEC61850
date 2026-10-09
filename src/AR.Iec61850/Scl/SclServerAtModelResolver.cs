using System.Xml.Linq;

namespace AR.Iec61850.Scl;

/// <summary>
/// Resolves an IEC 61850 ServerAt reference within the same IED. This is a
/// model reference, not an alternate MMS address. Fail closed for dangling,
/// ambiguous, or circular references; never bind another IED's server.
/// </summary>
internal static class SclServerAtModelResolver
{
    public static XElement? ResolveOwner(XElement ied, XElement selectedAccessPoint)
    {
        var accessPoints = ied.Elements()
            .Where(element => Is(element, "AccessPoint"))
            .ToArray();
        var visited = new HashSet<XElement>();

        var current = selectedAccessPoint;
        while (visited.Add(current))
        {
            if (current.Elements().Any(element => Is(element, "Server")))
                return current;

            var references = current.Elements()
                .Where(element => Is(element, "ServerAt"))
                .ToArray();
            if (references.Length != 1)
                return null;

            var referencedName = ((string?)references[0].Attribute("apName"))?.Trim();
            if (string.IsNullOrWhiteSpace(referencedName))
                return null;

            var matches = accessPoints
                .Where(element => string.Equals(
                    ((string?)element.Attribute("name"))?.Trim(),
                    referencedName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                return null;

            current = matches[0];
        }

        return null; // ServerAt cycle
    }

    private static bool Is(XElement element, string localName)
        => element.Name.LocalName.Equals(localName, StringComparison.Ordinal);
}
