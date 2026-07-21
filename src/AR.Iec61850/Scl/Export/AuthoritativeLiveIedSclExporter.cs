using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Exports a live-discovery model while keeping the physical IED identity separate from
/// communication-level MMS Logical Device domain names and preserving exact read-only
/// ReportControl configuration evidence.
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
        var document = XDocument.Load(result.SclPath, LoadOptions.PreserveWhitespace);
        if (!string.IsNullOrWhiteSpace(options.IedNameOverride))
            document = ApplyIdentity(document, model, options.IedNameOverride);

        document = ApplyReportControlConfiguration(document, model, options.ResolvedSchemaProfile);
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

    public static XDocument ApplyReportControlConfiguration(
        XDocument source,
        LiveIedModelDiscoveryDocument model,
        SclSchemaProfileDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(schema);

        var document = new XDocument(source);
        var reportControls = model.ReportControls.ToArray();
        foreach (var element in document.Descendants(Scl + "ReportControl"))
        {
            var name = ((string?)element.Attribute("name") ?? string.Empty).Trim();
            var buffered = bool.TryParse((string?)element.Attribute("buffered"), out var parsedBuffered) && parsedBuffered;
            var matches = reportControls
                .Where(control =>
                    control.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    control.Buffered == buffered)
                .ToArray();
            var modelControl = matches.Length == 1
                ? matches[0]
                : reportControls.Length == 1
                    ? reportControls[0]
                    : null;
            if (modelControl is null)
                continue;

            // MMS discovery returns a concrete RCB object name. IEC 61850-6 defines
            // ReportControl@indexed with a default value of true; if the attribute is
            // omitted, an engineering tool appends another two-digit instance suffix.
            // Therefore A_BRCB_1201 would become the invalid A_BRCB_120101. Preserve
            // the proven live object exactly as one non-indexed instance.
            element.SetAttributeValue("name", SafeXmlName(modelControl.Name));
            element.SetAttributeValue("indexed", "false");
            var rptEnabled = element.Element(Scl + "RptEnabled") ?? new XElement(Scl + "RptEnabled");
            rptEnabled.SetAttributeValue("max", "1");
            foreach (var clientLn in rptEnabled.Elements(Scl + "ClientLN").ToArray())
                clientLn.Remove();
            if (rptEnabled.Parent is null)
                element.Add(rptEnabled);

            var trigger = MmsReportControlFieldCodec.DecodeTriggerOptions(modelControl.TriggerOptions);
            var triggerElement = element.Element(Scl + "TrgOps") ?? new XElement(Scl + "TrgOps");
            triggerElement.SetAttributeValue("dchg", XmlBool(trigger.DataChange));
            triggerElement.SetAttributeValue("qchg", XmlBool(trigger.QualityChange));
            triggerElement.SetAttributeValue("dupd", XmlBool(trigger.DataUpdate));
            triggerElement.SetAttributeValue("period", XmlBool(trigger.Integrity));
            triggerElement.SetAttributeValue(
                "gi",
                schema.SupportsTriggerGi ? XmlBool(trigger.GeneralInterrogation) : null);
            if (triggerElement.Parent is null)
                element.Add(triggerElement);

            var optional = MmsReportControlFieldCodec.DecodeOptionalFields(modelControl.OptionalFields);
            var optionalElement = element.Element(Scl + "OptFields") ?? new XElement(Scl + "OptFields");
            optionalElement.SetAttributeValue("seqNum", XmlBool(optional.SequenceNumber));
            optionalElement.SetAttributeValue("timeStamp", XmlBool(optional.ReportTimestamp));
            optionalElement.SetAttributeValue("reasonCode", XmlBool(optional.ReasonForInclusion));
            optionalElement.SetAttributeValue("dataSet", XmlBool(optional.DataSetName));
            optionalElement.SetAttributeValue("dataRef", XmlBool(optional.DataReference));
            optionalElement.SetAttributeValue("bufOvfl", XmlBool(optional.BufferOverflow));
            optionalElement.SetAttributeValue("entryID", XmlBool(optional.EntryId));
            optionalElement.SetAttributeValue("configRef", XmlBool(optional.ConfigurationRevision));
            optionalElement.SetAttributeValue(
                "segmentation",
                schema.IsEdition2 ? XmlBool(optional.Segmentation) : null);
            if (optionalElement.Parent is null)
                element.Add(optionalElement);
        }

        var confReportControl = document.Descendants(Scl + "ConfReportControl").SingleOrDefault();
        if (confReportControl is not null)
            confReportControl.SetAttributeValue("max", reportControls.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

        ValidateReportControlIdentity(document, reportControls);
        return document;
    }

    private static void ValidateReportControlIdentity(
        XDocument document,
        IReadOnlyCollection<LiveIedReportControlModel> reportControls)
    {
        var exported = document.Descendants(Scl + "ReportControl").ToArray();
        if (exported.Length != reportControls.Count)
        {
            throw new InvalidDataException(
                $"Generated SCL contains {exported.Length} ReportControl element(s), but live discovery contains {reportControls.Count}.");
        }

        foreach (var modelControl in reportControls)
        {
            var matches = exported.Where(element =>
                string.Equals((string?)element.Attribute("name"), SafeXmlName(modelControl.Name), StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute("indexed"), "false", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Live RCB '{modelControl.Name}' was not exported exactly once as indexed=false.");
            }

            var rptEnabled = matches[0].Element(Scl + "RptEnabled");
            if (rptEnabled is null || !string.Equals((string?)rptEnabled.Attribute("max"), "1", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Live RCB '{modelControl.Name}' must be exported with RptEnabled max=1.");
            }
        }
    }

    private static string XmlBool(bool value) => value ? "true" : "false";

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