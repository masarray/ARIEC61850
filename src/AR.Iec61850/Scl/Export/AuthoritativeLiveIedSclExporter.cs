using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Exports a live-discovery model while keeping the physical IED identity separate from
/// communication-level MMS Logical Device domain names.
/// </summary>
public static class AuthoritativeLiveIedSclExporter
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static LiveIedSclExportResult WriteFiles(
        LiveIedModelDiscoveryDocument model,
        string sclPath,
        LiveIedSclExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new LiveIedSclExportOptions();

        var result = LiveIedSclExporter.WriteFiles(model, sclPath, options);
        if (string.IsNullOrWhiteSpace(options.IedNameOverride))
            return result;

        var document = XDocument.Load(result.SclPath, LoadOptions.PreserveWhitespace);
        ApplyIdentity(document, model, options.IedNameOverride);
        document.Save(result.SclPath);
        return result;
    }

    public static XDocument ApplyIdentity(
        XDocument source,
        LiveIedModelDiscoveryDocument model,
        string authoritativeIedName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(authoritativeIedName))
            throw new ArgumentException("Authoritative IED name is empty.", nameof(authoritativeIedName));

        var document = new XDocument(source);
        var root = document.Root ?? throw new InvalidDataException("Generated SCL document has no root element.");
        var safeIedName = SafeXmlName(authoritativeIedName);

        var ied = root.Elements(Scl + "IED").SingleOrDefault()
            ?? throw new InvalidDataException("Generated live SCL must contain exactly one IED element.");
        ied.SetAttributeValue("name", safeIedName);

        foreach (var connectedAp in root.Descendants(Scl + "ConnectedAP"))
            connectedAp.SetAttributeValue("iedName", safeIedName);

        var header = root.Element(Scl + "Header");
        header?.SetAttributeValue("id", $"{safeIedName}_GENERATED");

        var logicalDevices = ied.Descendants(Scl + "LDevice").ToArray();
        var unmatchedDomains = new HashSet<string>(
            model.LogicalDevices
                .Select(LogicalDeviceDomain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var logicalDevice in logicalDevices)
        {
            var inst = ((string?)logicalDevice.Attribute("inst") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(inst))
                continue;

            var domain = MatchMmsDomain(inst, model.IedName, unmatchedDomains);
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            unmatchedDomains.Remove(domain);
            var implicitName = $"{safeIedName}{inst}";
            logicalDevice.SetAttributeValue(
                "ldName",
                domain.Equals(implicitName, StringComparison.OrdinalIgnoreCase) ? null : domain);
        }

        ValidateIdentity(document, safeIedName, model);
        return document;
    }

    private static string MatchMmsDomain(
        string generatedInst,
        string previousIedName,
        IReadOnlySet<string> domains)
    {
        var direct = domains.FirstOrDefault(domain =>
            domain.Equals(generatedInst, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var previousImplicit = $"{previousIedName?.Trim()}{generatedInst}";
        var implicitMatch = domains.FirstOrDefault(domain =>
            domain.Equals(previousImplicit, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(implicitMatch))
            return implicitMatch;

        return string.Empty;
    }

    private static void ValidateIdentity(
        XDocument document,
        string authoritativeIedName,
        LiveIedModelDiscoveryDocument model)
    {
        var root = document.Root ?? throw new InvalidDataException("Normalized SCL has no root element.");
        var ied = root.Elements(Scl + "IED").Single();
        if (!authoritativeIedName.Equals((string?)ied.Attribute("name"), StringComparison.Ordinal))
            throw new InvalidDataException("Normalized SCL IED identity does not match the authoritative IED name.");

        if (root.Descendants(Scl + "ConnectedAP").Any(ap =>
                !authoritativeIedName.Equals((string?)ap.Attribute("iedName"), StringComparison.Ordinal)))
        {
            throw new InvalidDataException("ConnectedAP identity is inconsistent with the normalized IED name.");
        }

        var exportedCommunicationNames = root.Descendants(Scl + "LDevice")
            .Select(ld =>
            {
                var explicitName = ((string?)ld.Attribute("ldName") ?? string.Empty).Trim();
                var inst = ((string?)ld.Attribute("inst") ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(explicitName)
                    ? $"{authoritativeIedName}{inst}"
                    : explicitName;
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingDomains = model.LogicalDevices
            .Select(LogicalDeviceDomain)
            .Where(domain => !exportedCommunicationNames.Contains(domain))
            .ToArray();
        if (missingDomains.Length > 0)
        {
            throw new InvalidDataException(
                $"Normalized SCL lost MMS Logical Device domain(s): {string.Join(", ", missingDomains)}.");
        }
    }

    private static string LogicalDeviceDomain(LiveIedLogicalDeviceModel logicalDevice)
        => string.IsNullOrWhiteSpace(logicalDevice.MmsDomain)
            ? logicalDevice.Inst.Trim()
            : logicalDevice.MmsDomain.Trim();

    private static string SafeXmlName(string value)
    {
        var chars = value.Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray();
        var result = new string(chars);
        if (string.IsNullOrWhiteSpace(result))
            return "LIVE_IED";
        return char.IsLetter(result[0]) || result[0] == '_' ? result : $"_{result}";
    }
}
