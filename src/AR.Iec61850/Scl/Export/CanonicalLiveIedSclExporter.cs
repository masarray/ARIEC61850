using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Discovery;

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
        var communication = canonical.Communication;
        var association = communication.Association;
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(canonical.IedName))
            errors.Add("Canonical IED identity is empty.");
        if (string.IsNullOrWhiteSpace(canonical.AccessPointName))
            errors.Add("Canonical access-point identity is empty.");
        if (string.IsNullOrWhiteSpace(communication.Host))
            errors.Add("Canonical communication evidence has no IP/host endpoint.");
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

    private static void PreserveRuntimeServiceCapacity(
        XDocument document,
        LiveIedModelDiscoveryDocument discovery)
    {
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

    private static void ValidateRoundTripAssociation(XDocument document, LiveIedCanonicalModel canonical)
    {
        var profiles = SclMmsAssociationProfileReader.Read(document);
        var remote = profiles.Find(canonical.IedName, canonical.AccessPointName)
            ?? throw new InvalidDataException(
                $"Generated SCL cannot round-trip its own ConnectedAP '{canonical.IedName}/{canonical.AccessPointName}'.");

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
