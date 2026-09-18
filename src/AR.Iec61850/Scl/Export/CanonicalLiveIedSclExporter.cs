using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Interoperable export boundary for a canonical live IED snapshot. All connection
/// addressing comes from the accepted-association evidence stored in the model.
/// No device-specific AP-title or selector default is invented here.
/// </summary>
public static class CanonicalLiveIedSclExporter
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static LiveIedSclExportResult WriteFiles(
        LiveIedCanonicalModel canonical,
        string sclPath,
        SclSchemaProfile schemaProfile = SclSchemaProfile.Edition2V31,
        string profile = "safe-connection")
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ValidateCanonicalCommunication(canonical);

        var communication = canonical.Communication;
        var association = communication.Association;
        LiveIedSclExportResult? result = null;

        try
        {
            result = AuthoritativeLiveIedSclExporter.WriteFiles(
                canonical.Discovery,
                sclPath,
                new LiveIedSclExportOptions
                {
                    Profile = profile,
                    SchemaProfile = schemaProfile,
                    SubNetworkName = string.IsNullOrWhiteSpace(communication.SubNetworkName)
                        ? "StationBus"
                        : communication.SubNetworkName.Trim(),
                    IpAddress = communication.Host.Trim(),
                    IedNameOverride = canonical.IedName,
                    IpSubnet = communication.IpSubnet?.Trim() ?? string.Empty,
                    IpGateway = communication.IpGateway?.Trim() ?? string.Empty,
                    OsiApTitle = association.ApTitle.Trim(),
                    OsiAeQualifier = association.AeQualifier!.Value.ToString(CultureInfo.InvariantCulture),
                    OsiPsel = association.PresentationSelector.Trim(),
                    OsiSsel = association.SessionSelector.Trim(),
                    OsiTsel = association.TransportSelector.Trim(),
                    IncludeDefaultOsiParameters = true
                });

            var document = XDocument.Load(result.SclPath, LoadOptions.PreserveWhitespace);
            ApplyCanonicalCommunication(document, canonical);
            PreserveRuntimeServiceCapacity(document, canonical.Discovery);
            ApplyCanonicalInstanceValues(document, canonical);
            ValidateRoundTripAssociation(document, canonical);
            document.Save(result.SclPath);
            return result;
        }
        catch
        {
            DeleteIfExists(sclPath);
            if (result is not null)
            {
                DeleteIfExists(result.ReportPath);
                DeleteIfExists(result.SummaryPath);
                DeleteIfExists(result.ExcludedAttributesPath);
            }
            throw;
        }
    }

    public static void ValidateCanonicalCommunication(LiveIedCanonicalModel canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var communication = canonical.Communication;
        var association = communication.Association;
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(canonical.IedName))
            errors.Add("Canonical IED identity is empty.");
        if (string.IsNullOrWhiteSpace(canonical.AccessPointName))
            errors.Add("Canonical access-point identity is empty.");
        if (string.IsNullOrWhiteSpace(communication.Host))
            errors.Add("Canonical communication evidence has no IP/host endpoint.");
        if (communication.Port != 102)
        {
            errors.Add(
                $"Canonical communication evidence uses MMS/TCP port {communication.Port}, but the current SCL association plan represents IEC 61850 MMS on port 102 only.");
        }
        if (string.IsNullOrWhiteSpace(association.ApTitle))
            errors.Add("Canonical communication evidence has no accepted remote OSI-AP-Title.");
        if (association.AeQualifier is not (>= 0 and <= 65535))
            errors.Add("Canonical communication evidence has no valid accepted remote OSI-AE-Qualifier.");
        if (string.IsNullOrWhiteSpace(association.PresentationSelector))
            errors.Add("Canonical communication evidence has no accepted remote OSI-PSEL.");
        if (string.IsNullOrWhiteSpace(association.SessionSelector))
            errors.Add("Canonical communication evidence has no accepted remote OSI-SSEL.");
        if (string.IsNullOrWhiteSpace(association.TransportSelector))
            errors.Add("Canonical communication evidence has no accepted remote OSI-TSEL.");

        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(" | ", errors));
    }

    private static void ApplyCanonicalCommunication(XDocument document, LiveIedCanonicalModel canonical)
    {
        var connectedAp = document.Descendants(Scl + "ConnectedAP").SingleOrDefault()
            ?? throw new InvalidDataException("Generated SCL must contain exactly one ConnectedAP.");
        connectedAp.SetAttributeValue("iedName", canonical.IedName);
        connectedAp.SetAttributeValue("apName", canonical.AccessPointName);

        var subNetwork = connectedAp.Parent;
        if (subNetwork is not null && subNetwork.Name == Scl + "SubNetwork")
        {
            subNetwork.SetAttributeValue(
                "name",
                string.IsNullOrWhiteSpace(canonical.Communication.SubNetworkName)
                    ? "StationBus"
                    : canonical.Communication.SubNetworkName.Trim());
            subNetwork.SetAttributeValue("type", "8-MMS");
        }

        var address = connectedAp.Element(Scl + "Address");
        if (address is null)
        {
            address = new XElement(Scl + "Address");
            connectedAp.Add(address);
        }
        address.RemoveNodes();

        var association = canonical.Communication.Association;
        AddP(address, "OSI-AP-Title", association.ApTitle);
        AddP(address, "OSI-AE-Qualifier", association.AeQualifier!.Value.ToString(CultureInfo.InvariantCulture));
        AddP(address, "OSI-PSEL", association.PresentationSelector);
        AddP(address, "OSI-SSEL", association.SessionSelector);
        AddP(address, "IP", canonical.Communication.Host);
        AddP(address, "OSI-TSEL", association.TransportSelector);
        AddP(address, "IP-SUBNET", canonical.Communication.IpSubnet);
        AddP(address, "IP-GATEWAY", canonical.Communication.IpGateway);
    }

    public static void PreserveRuntimeServiceCapacity(
        XDocument document,
        LiveIedModelDiscoveryDocument discovery)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(discovery);
        // ReportControl elements may be projected from concrete runtime instances to
        // indexed logical controls, but Services/ConfReportControl@max describes the
        // observed runtime capacity and therefore stays bound to the physical inventory.
        var confReportControl = document.Descendants(Scl + "ConfReportControl").SingleOrDefault();
        if (confReportControl is not null && discovery.ReportControls.Count > 0)
        {
            confReportControl.SetAttributeValue(
                "max",
                discovery.ReportControls.Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static void ApplyCanonicalInstanceValues(
        XDocument document,
        LiveIedCanonicalModel canonical)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(canonical);
        if (canonical.InstanceValues.Count == 0)
            return;

        var grouped = canonical.InstanceValues
            .Where(value =>
                !string.IsNullOrWhiteSpace(value.Domain) &&
                !string.IsNullOrWhiteSpace(value.LogicalNode) &&
                !string.IsNullOrWhiteSpace(value.DataObject) &&
                !string.IsNullOrWhiteSpace(value.AttributePath) &&
                value.Value is not null)
            .GroupBy(
                value => string.Concat(
                    value.Domain.Trim(), "\u001F",
                    value.LogicalNode.Trim(), "\u001F",
                    value.DataObject.Trim(), "\u001F",
                    value.AttributePath.Trim()),
                // MMS/SCL component names are case-sensitive. Tracking CDCs can
                // legitimately contain both "t" and "T" and they must retain
                // independent instance evidence.
                StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in grouped)
        {
            var candidates = group.ToArray();
            var formatted = candidates
                .Select(candidate => TryFormatScalarValue(candidate.Value, out var text)
                    ? text
                    : null)
                .Where(text => text is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (formatted.Length == 0)
                continue;
            if (formatted.Length != 1)
            {
                throw new InvalidDataException(
                    $"Conflicting live instance values were observed for '{candidates[0].Reference}'.");
            }

            var evidence = candidates[0];
            var logicalNodeModel = FindLogicalNodeModel(canonical.Discovery, evidence);
            if (logicalNodeModel is null)
                continue;

            var lDevice = FindExportedLogicalDevice(document, canonical, evidence.Domain);
            if (lDevice is null)
                continue;

            var logicalNode = FindExportedLogicalNode(lDevice, logicalNodeModel);
            if (logicalNode is null ||
                !IsExportedAttributePath(document, logicalNode, evidence.DataObject, evidence.AttributePath))
            {
                continue;
            }

            var doi = GetOrAddChild(logicalNode, "DOI", evidence.DataObject);
            var segments = evidence.AttributePath
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                continue;

            XElement parent = doi;
            for (var index = 0; index < segments.Length - 1; index++)
                parent = GetOrAddChild(parent, "SDI", segments[index]);

            var dai = GetOrAddChild(parent, "DAI", segments[^1]);
            var existingValues = dai.Elements(Scl + "Val").ToArray();
            if (existingValues.Length > 1)
            {
                throw new InvalidDataException(
                    $"Generated SCL contains multiple Val elements for '{evidence.Reference}'.");
            }

            if (existingValues.Length == 1)
            {
                if (!string.Equals(existingValues[0].Value, formatted[0], StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Generated SCL already contains a different Val for '{evidence.Reference}'.");
                }
                continue;
            }

            dai.Add(new XElement(Scl + "Val", formatted[0]));
        }
    }

    private static LiveIedLogicalNodeModel? FindLogicalNodeModel(
        LiveIedModelDiscoveryDocument discovery,
        LiveIedInstanceValueEvidence evidence)
        => discovery.LogicalDevices
            .Where(device => string.Equals(
                device.MmsDomain?.Trim(),
                evidence.Domain.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(device => device.LogicalNodes)
            .FirstOrDefault(node => string.Equals(
                node.Name?.Trim(),
                evidence.LogicalNode.Trim(),
                StringComparison.OrdinalIgnoreCase));

    private static XElement? FindExportedLogicalDevice(
        XDocument document,
        LiveIedCanonicalModel canonical,
        string domain)
    {
        var normalizedDomain = domain.Trim();
        var iedName = canonical.IedName.Trim();
        var stripped = normalizedDomain.StartsWith(iedName, StringComparison.OrdinalIgnoreCase) &&
                       normalizedDomain.Length > iedName.Length
            ? normalizedDomain[iedName.Length..]
            : normalizedDomain;

        var candidates = document.Descendants(Scl + "LDevice")
            .Where(element =>
            {
                var inst = ((string?)element.Attribute("inst") ?? string.Empty).Trim();
                return string.Equals(inst, stripped, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(inst, normalizedDomain, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static XElement? FindExportedLogicalNode(
        XElement lDevice,
        LiveIedLogicalNodeModel model)
    {
        if (string.Equals(model.Name, "LLN0", StringComparison.OrdinalIgnoreCase))
            return lDevice.Elements(Scl + "LN0").SingleOrDefault();

        return lDevice.Elements(Scl + "LN")
            .SingleOrDefault(element =>
                string.Equals(
                    ((string?)element.Attribute("prefix") ?? string.Empty).Trim(),
                    model.Prefix?.Trim() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ((string?)element.Attribute("lnClass") ?? string.Empty).Trim(),
                    model.LnClass?.Trim() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ((string?)element.Attribute("inst") ?? string.Empty).Trim(),
                    model.LnInst?.Trim() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExportedAttributePath(
        XDocument document,
        XElement logicalNode,
        string dataObjectName,
        string attributePath)
    {
        var lnTypeId = ((string?)logicalNode.Attribute("lnType") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(lnTypeId))
            return false;

        var templates = document.Root?.Element(Scl + "DataTypeTemplates");
        if (templates is null)
            return false;

        var lnType = templates.Elements(Scl + "LNodeType")
            .SingleOrDefault(element => string.Equals(
                ((string?)element.Attribute("id") ?? string.Empty).Trim(),
                lnTypeId,
                StringComparison.Ordinal));
        var dataObject = lnType?.Elements(Scl + "DO")
            .SingleOrDefault(element => string.Equals(
                ((string?)element.Attribute("name") ?? string.Empty).Trim(),
                dataObjectName.Trim(),
                // SCL DataObject identity is case-sensitive. Do not let instance
                // value evidence for e.g. Flag/flag resolve to the same template DO.
                StringComparison.Ordinal));
        var typeId = ((string?)dataObject?.Attribute("type") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(typeId))
            return false;

        XElement? currentType = FindTemplateById(templates, "DOType", typeId);
        var segments = attributePath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (currentType is null)
                return false;

            var segment = segments[index];
            var definition = currentType.Elements()
                .SingleOrDefault(element =>
                    (element.Name == Scl + "DA" ||
                     element.Name == Scl + "BDA" ||
                     element.Name == Scl + "SDO") &&
                    string.Equals(
                        ((string?)element.Attribute("name") ?? string.Empty).Trim(),
                        segment,
                        // SCL DA/BDA/SDO component names are case-sensitive.
                        // Tracking CDCs can contain both "t" and "T".
                        StringComparison.Ordinal));
            if (definition is null)
                return false;

            var isLast = index == segments.Length - 1;
            if (isLast)
                return definition.Name == Scl + "DA" || definition.Name == Scl + "BDA";

            var nestedTypeId = ((string?)definition.Attribute("type") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nestedTypeId))
                return false;

            currentType = definition.Name == Scl + "SDO"
                ? FindTemplateById(templates, "DOType", nestedTypeId)
                : FindTemplateById(templates, "DAType", nestedTypeId);
        }

        return false;
    }

    private static XElement? FindTemplateById(
        XElement templates,
        string localName,
        string id)
        => templates.Elements(Scl + localName)
            .SingleOrDefault(element => string.Equals(
                ((string?)element.Attribute("id") ?? string.Empty).Trim(),
                id,
                StringComparison.Ordinal));

    private static XElement GetOrAddChild(
        XElement parent,
        string localName,
        string name)
    {
        var existing = parent.Elements(Scl + localName)
            .SingleOrDefault(element => string.Equals(
                ((string?)element.Attribute("name") ?? string.Empty).Trim(),
                name.Trim(),
                // Preserve exact SCL instance component identity. Using an
                // ignore-case comparison here collapses legal pairs such as
                // LTRK tracking members "t" and "T".
                StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var created = new XElement(
            Scl + localName,
            new XAttribute("name", name.Trim()));
        parent.Add(created);
        return created;
    }

    private static bool TryFormatScalarValue(
        MmsDataValue value,
        out string text)
    {
        text = string.Empty;
        switch (value.Kind)
        {
            case MmsDataKind.Boolean:
                text = Convert.ToBoolean(value.Value, CultureInfo.InvariantCulture)
                    ? "true"
                    : "false";
                return true;
            case MmsDataKind.Integer:
            case MmsDataKind.Unsigned:
                text = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            case MmsDataKind.FloatingPoint:
                text = value.Value switch
                {
                    float single => single.ToString("R", CultureInfo.InvariantCulture),
                    double number => number.ToString("R", CultureInfo.InvariantCulture),
                    _ => string.Empty
                };
                return text.Length > 0;
            case MmsDataKind.VisibleString:
            case MmsDataKind.MmsString:
                text = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            case MmsDataKind.UtcTime:
                if (value.Value is Iec61850UtcTime utc)
                {
                    text = Iec61850UtcTimeFormatter.FormatFullPrecisionUtc(utc);
                    return true;
                }
                return false;
            case MmsDataKind.OctetString:
                text = Convert.ToHexString(value.RawValue.ToArray());
                return true;
            default:
                // BitString/Quality, BinaryTime, arrays, structures, and unknown tags
                // are intentionally omitted until their SCL lexical form is proven.
                return false;
        }
    }

    private static void ValidateRoundTripAssociation(XDocument document, LiveIedCanonicalModel canonical)
    {
        var profiles = SclMmsAssociationProfileReader.Read(document);
        var remote = profiles.Find(canonical.IedName, canonical.AccessPointName)
            ?? throw new InvalidDataException(
                $"Generated SCL cannot round-trip its own ConnectedAP '{canonical.IedName}/{canonical.AccessPointName}'.");

        var canonicalAssociation = canonical.Communication.Association;
        var roundTripErrors = new List<string>();
        CompareRoundTrip("IP", canonical.Communication.Host, remote.Endpoint.IpAddress, roundTripErrors);
        CompareRoundTrip("OSI-AP-Title", canonicalAssociation.ApTitle, remote.Association.ApTitle, roundTripErrors);
        if (canonicalAssociation.AeQualifier != remote.Association.AeQualifier)
        {
            roundTripErrors.Add(
                $"OSI-AE-Qualifier expected '{canonicalAssociation.AeQualifier?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}' " +
                $"but parsed '{remote.Association.AeQualifier?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}'.");
        }
        CompareRoundTrip("OSI-PSEL", canonicalAssociation.PresentationSelector, remote.Association.PresentationSelector, roundTripErrors);
        CompareRoundTrip("OSI-SSEL", canonicalAssociation.SessionSelector, remote.Association.SessionSelector, roundTripErrors);
        CompareRoundTrip("OSI-TSEL", canonicalAssociation.TransportSelector, remote.Association.TransportSelector, roundTripErrors);
        if (roundTripErrors.Count > 0)
        {
            throw new InvalidDataException(
                "Generated SCL changed canonical association evidence during serialization: " +
                string.Join(" | ", roundTripErrors));
        }

        var plan = SclAssistedMmsAssociationPlanBuilder.BuildExact(
            remote,
            MmsLocalAssociationProfile.SclInteroperabilityDefault);
        if (!plan.IsSuccess)
        {
            throw new InvalidDataException(
                "Generated SCL failed its own MMS association-plan validation: " +
                string.Join(" | ", plan.Errors));
        }
    }

    private static void CompareRoundTrip(
        string name,
        string? expected,
        string? actual,
        ICollection<string> errors)
    {
        var normalizedExpected = expected?.Trim() ?? string.Empty;
        var normalizedActual = actual?.Trim() ?? string.Empty;
        if (!string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal))
        {
            errors.Add(
                $"{name} expected '{normalizedExpected}' but parsed '{normalizedActual}'.");
        }
    }

    private static void AddP(XElement address, string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        address.Add(new XElement(Scl + "P", new XAttribute("type", type), value.Trim()));
    }

    private static void DeleteIfExists(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }
}
