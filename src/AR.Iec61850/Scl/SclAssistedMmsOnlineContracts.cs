using System.Xml;
using System.Xml.Linq;

namespace AR.Iec61850.Scl;

public sealed class SclMmsDomainInventory
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public IReadOnlyList<string> ExpectedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsSuccess => Errors.Count == 0 && ExpectedDomains.Count > 0;
}

/// <summary>
/// Reads the MMS logical-device/domain inventory for one SCL IED/AccessPoint.
/// This is design evidence only: online presence is still established by MMS GetNameList.
/// </summary>
public static class SclMmsDomainInventoryReader
{
    public static SclMmsDomainInventory Read(string xml, string iedName, string accessPointName)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Build(
                iedName,
                accessPointName,
                Array.Empty<string>(),
                new[] { "SCL XML is empty." },
                Array.Empty<string>());
        }

        try
        {
            return Read(
                XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo),
                iedName,
                accessPointName);
        }
        catch (XmlException ex)
        {
            return Build(
                iedName,
                accessPointName,
                Array.Empty<string>(),
                new[] { $"SCL XML is malformed: {ex.Message}" },
                Array.Empty<string>());
        }
    }

    public static SclMmsDomainInventory Read(XDocument document, string iedName, string accessPointName)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<string>();
        var warnings = new List<string>();
        var domains = new List<string>();

        if (string.IsNullOrWhiteSpace(iedName))
            errors.Add("SCL MMS domain inventory requires an explicit IED name.");
        if (string.IsNullOrWhiteSpace(accessPointName))
            errors.Add("SCL MMS domain inventory requires an explicit AccessPoint name.");

        var root = document.Root;
        if (root is null || !Is(root, "SCL"))
        {
            errors.Add("The selected document is not an IEC 61850 SCL document.");
            return Build(iedName, accessPointName, domains, errors, warnings);
        }

        if (errors.Count > 0)
            return Build(iedName, accessPointName, domains, errors, warnings);

        var ied = root.Elements()
            .Where(element => Is(element, "IED"))
            .FirstOrDefault(element => string.Equals(Attr(element, "name"), iedName, StringComparison.Ordinal));
        if (ied is null)
        {
            errors.Add($"SCL does not contain IED '{iedName}'.");
            return Build(iedName, accessPointName, domains, errors, warnings);
        }

        var canonicalIedName = Attr(ied, "name");
        var accessPoint = ied.Elements()
            .Where(element => Is(element, "AccessPoint"))
            .FirstOrDefault(element => string.Equals(Attr(element, "name"), accessPointName, StringComparison.Ordinal));
        if (accessPoint is null)
        {
            errors.Add($"SCL IED '{canonicalIedName}' does not contain AccessPoint '{accessPointName}'.");
            return Build(canonicalIedName, accessPointName, domains, errors, warnings);
        }

        var server = accessPoint.Elements().FirstOrDefault(element => Is(element, "Server"));
        if (server is null)
        {
            errors.Add($"SCL IED '{canonicalIedName}' AccessPoint '{accessPointName}' has no direct Server model.");
            return Build(canonicalIedName, accessPointName, domains, errors, warnings);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var logicalDevice in server.Elements().Where(element => Is(element, "LDevice")))
        {
            var inst = Attr(logicalDevice, "inst");
            var explicitLdName = Attr(logicalDevice, "ldName");
            var domain = !string.IsNullOrWhiteSpace(explicitLdName)
                ? explicitLdName
                : !string.IsNullOrWhiteSpace(inst)
                    ? canonicalIedName + inst
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(domain))
            {
                warnings.Add($"SCL IED '{canonicalIedName}' AccessPoint '{accessPointName}' contains an LDevice without inst or ldName; it was not projected into an MMS domain.");
                continue;
            }

            if (!seen.Add(domain))
            {
                warnings.Add($"SCL MMS domain '{domain}' is declared more than once; duplicate design evidence was collapsed.");
                continue;
            }

            domains.Add(domain);
        }

        if (domains.Count == 0)
            errors.Add($"SCL IED '{canonicalIedName}' AccessPoint '{accessPointName}' has no resolvable logical-device MMS domains.");

        return Build(canonicalIedName, accessPointName, domains, errors, warnings);
    }

    private static SclMmsDomainInventory Build(
        string iedName,
        string accessPointName,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
        => new()
        {
            IedName = iedName,
            AccessPointName = accessPointName,
            ExpectedDomains = domains.ToArray(),
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?
            .Value?.Trim() ?? string.Empty;
}

public sealed class SclMmsDomainReconciliation
{
    public IReadOnlyList<string> ExpectedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ObservedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MatchedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingExpectedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraObservedDomains { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Compatible means every expected SCL domain exists online with the exact MMS identifier.
    /// Extra online domains are preserved as evidence and do not silently mutate or invalidate
    /// the SCL design model.
    /// </summary>
    public bool IsCompatible => ExpectedDomains.Count > 0 && MissingExpectedDomains.Count == 0;
    public bool IsExactMatch => IsCompatible && ExtraObservedDomains.Count == 0;
    public string Summary =>
        $"SCL/MMS domains: expected={ExpectedDomains.Count}, observed={ObservedDomains.Count}, matched={MatchedDomains.Count}, missing={MissingExpectedDomains.Count}, extra={ExtraObservedDomains.Count}, compatible={IsCompatible}.";
}

public static class SclMmsDomainReconciler
{
    public static SclMmsDomainReconciliation Reconcile(
        IEnumerable<string> expectedDomains,
        IEnumerable<string> observedDomains)
    {
        ArgumentNullException.ThrowIfNull(expectedDomains);
        ArgumentNullException.ThrowIfNull(observedDomains);

        var expected = Normalize(expectedDomains);
        var observed = Normalize(observedDomains);
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var observedSet = new HashSet<string>(observed, StringComparer.Ordinal);

        return new SclMmsDomainReconciliation
        {
            ExpectedDomains = expected,
            ObservedDomains = observed,
            MatchedDomains = expected.Where(observedSet.Contains).ToArray(),
            MissingExpectedDomains = expected.Where(domain => !observedSet.Contains(domain)).ToArray(),
            ExtraObservedDomains = observed.Where(domain => !expectedSet.Contains(domain)).ToArray()
        };
    }

    private static string[] Normalize(IEnumerable<string> domains)
        => domains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray();
}

public enum SclAssistedMmsOnlineStatus
{
    InvalidPlan,
    TimedOut,
    AssociationFailed,
    DomainInventoryFailed,
    DomainMismatch,
    Compatible
}

public sealed class SclAssistedMmsOnlineResult
{
    public SclAssistedMmsOnlineStatus Status { get; init; }
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public bool AssociationSucceeded { get; init; }
    public bool DomainInventorySucceeded { get; init; }
    public bool SessionRemainsOpen { get; init; }
    public SclMmsDomainReconciliation? Domains { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsCompatible => Status == SclAssistedMmsOnlineStatus.Compatible;
}
