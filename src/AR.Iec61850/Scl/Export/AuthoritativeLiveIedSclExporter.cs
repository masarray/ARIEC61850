using System.Globalization;
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
        try
        {
            var document = XDocument.Load(result.SclPath, LoadOptions.PreserveWhitespace);
            if (!string.IsNullOrWhiteSpace(options.IedNameOverride))
                document = ApplyIdentity(document, model, options.IedNameOverride);

            document = ApplyReportControlConfiguration(document, model, options.ResolvedSchemaProfile);
            ValidateExportGraph(document);
            document.Save(result.SclPath);
            return WithReportControlCount(result, document.Descendants(Scl + "ReportControl").Count());
        }
        catch
        {
            // Authoritative export is all-or-nothing. The generic exporter writes its
            // artifacts first, so remove them if authoritative identity/RCB/DataSet/FCDA
            // validation rejects the generated graph. Never leave a half-valid CID behind.
            DeleteIfExists(result.SclPath);
            DeleteIfExists(result.ReportPath);
            DeleteIfExists(result.SummaryPath);
            DeleteIfExists(result.ExcludedAttributesPath);
            throw;
        }
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
        var runtimeControls = model.ReportControls.ToArray();
        var projections = LiveRcbLogicalGroupProjector.Project(runtimeControls);

        foreach (var projection in projections)
        {
            var runtimeElements = new List<XElement>(projection.RuntimeInstances.Count);
            foreach (var runtimeControl in projection.RuntimeInstances)
            {
                var matches = document.Descendants(Scl + "ReportControl")
                    .Where(element => MatchesRuntimeControl(element, runtimeControl))
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidDataException(
                        $"Live RCB '{runtimeControl.Reference}' matched {matches.Length} generated ReportControl element(s); exactly one is required before logical projection.");
                }
                runtimeElements.Add(matches[0]);
            }

            if (runtimeElements.Distinct(ReferenceEqualityComparer.Instance).Count() != runtimeElements.Count)
                throw new InvalidDataException("Multiple live RCB instances resolved to the same generated ReportControl element.");

            if (projection.Indexed && runtimeElements.Select(element => element.Parent).Distinct(ReferenceEqualityComparer.Instance).Count() != 1)
            {
                throw new InvalidDataException(
                    $"Indexed RCB group '{projection.LogicalName}' spans multiple logical nodes in the generated SCL.");
            }

            var element = runtimeElements[0];
            foreach (var redundant in runtimeElements.Skip(1))
                redundant.Remove();

            ApplyProjectedReportControlConfiguration(element, projection, schema);
        }

        var confReportControl = document.Descendants(Scl + "ConfReportControl").SingleOrDefault();
        if (confReportControl is not null)
            confReportControl.SetAttributeValue("max", projections.Count.ToString(CultureInfo.InvariantCulture));

        ValidateReportControlIdentity(document, projections);
        return document;
    }

    private static void ApplyProjectedReportControlConfiguration(
        XElement element,
        LiveRcbLogicalGroupProjector.Projection projection,
        SclSchemaProfileDescriptor schema)
    {
        var modelControl = projection.Representative;
        element.SetAttributeValue("name", SafeXmlName(projection.LogicalName));
        element.SetAttributeValue("indexed", projection.Indexed ? "true" : "false");
        element.SetAttributeValue("rptID", string.IsNullOrWhiteSpace(projection.ReportId) ? null : projection.ReportId);

        var rptEnabled = element.Element(Scl + "RptEnabled") ?? new XElement(Scl + "RptEnabled");
        rptEnabled.SetAttributeValue("max", projection.MaxInstances.ToString(CultureInfo.InvariantCulture));
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

    private static void ValidateReportControlIdentity(
        XDocument document,
        IReadOnlyList<LiveRcbLogicalGroupProjector.Projection> projections)
    {
        var exported = document.Descendants(Scl + "ReportControl").ToArray();
        if (exported.Length != projections.Count)
        {
            throw new InvalidDataException(
                $"Generated SCL contains {exported.Length} logical ReportControl element(s), but live evidence projects to {projections.Count}.");
        }

        foreach (var projection in projections)
        {
            var matches = exported.Where(element =>
                    MatchesProjectedControl(element, projection))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Logical RCB '{projection.LogicalName}' was not exported exactly once in its authoritative LD/LN context.");
            }

            var expectedIndexed = projection.Indexed ? "true" : "false";
            if (!string.Equals((string?)matches[0].Attribute("indexed"), expectedIndexed, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Logical RCB '{projection.LogicalName}' must be exported with indexed={expectedIndexed}.");
            }

            var rptEnabled = matches[0].Element(Scl + "RptEnabled");
            var expectedMax = projection.MaxInstances.ToString(CultureInfo.InvariantCulture);
            if (rptEnabled is null || !string.Equals((string?)rptEnabled.Attribute("max"), expectedMax, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Logical RCB '{projection.LogicalName}' must be exported with RptEnabled max={expectedMax}.");
            }

            if (projection.Indexed)
            {
                foreach (var runtime in projection.RuntimeInstances)
                {
                    if (string.Equals(runtime.Name, projection.LogicalName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (exported.Any(element => MatchesRuntimeControl(element, runtime)))
                    {
                        throw new InvalidDataException(
                            $"Concrete runtime RCB '{runtime.Name}' remained in SCL after projection to indexed logical control '{projection.LogicalName}'.");
                    }
                }
            }
        }
    }

    private static bool MatchesRuntimeControl(XElement element, LiveIedReportControlModel control)
    {
        var name = ((string?)element.Attribute("name") ?? string.Empty).Trim();
        if (!name.Equals(SafeXmlName(control.Name), StringComparison.OrdinalIgnoreCase))
            return false;

        var buffered = bool.TryParse((string?)element.Attribute("buffered"), out var parsedBuffered) && parsedBuffered;
        return buffered == control.Buffered && MatchesControlContext(element, control.Domain, control.LogicalNode);
    }

    private static bool MatchesProjectedControl(
        XElement element,
        LiveRcbLogicalGroupProjector.Projection projection)
    {
        var name = ((string?)element.Attribute("name") ?? string.Empty).Trim();
        if (!name.Equals(SafeXmlName(projection.LogicalName), StringComparison.OrdinalIgnoreCase))
            return false;

        var buffered = bool.TryParse((string?)element.Attribute("buffered"), out var parsedBuffered) && parsedBuffered;
        return buffered == projection.Representative.Buffered &&
               MatchesControlContext(
                   element,
                   projection.Representative.Domain,
                   projection.Representative.LogicalNode);
    }

    private static bool MatchesControlContext(XElement element, string domain, string logicalNode)
    {
        var lDevice = element.Ancestors(Scl + "LDevice").FirstOrDefault();
        var ln = element.Ancestors().FirstOrDefault(candidate => candidate.Name == Scl + "LN0" || candidate.Name == Scl + "LN");
        if (lDevice is null || ln is null)
            return false;

        if (!MatchesLogicalNode(ln, logicalNode))
            return false;

        var targetDomain = domain.Trim();
        var inst = ((string?)lDevice.Attribute("inst") ?? string.Empty).Trim();
        var explicitLdName = ((string?)lDevice.Attribute("ldName") ?? string.Empty).Trim();
        var iedName = ((string?)lDevice.Ancestors(Scl + "IED").FirstOrDefault()?.Attribute("name") ?? string.Empty).Trim();
        var implicitDomain = $"{iedName}{inst}";

        return targetDomain.Equals(inst, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(explicitLdName) && targetDomain.Equals(explicitLdName, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(iedName) && targetDomain.Equals(implicitDomain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesLogicalNode(XElement element, string logicalNode)
    {
        var target = logicalNode.Trim();
        if (element.Name == Scl + "LN0")
            return target.Equals("LLN0", StringComparison.OrdinalIgnoreCase);

        var prefix = ((string?)element.Attribute("prefix") ?? string.Empty).Trim();
        var lnClass = ((string?)element.Attribute("lnClass") ?? string.Empty).Trim();
        var inst = ((string?)element.Attribute("inst") ?? string.Empty).Trim();
        return target.Equals($"{prefix}{lnClass}{inst}", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateExportGraph(XDocument document)
    {
        foreach (var reportControl in document.Descendants(Scl + "ReportControl"))
        {
            var dataSetName = ((string?)reportControl.Attribute("datSet") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dataSetName))
                continue;

            var logicalNode = reportControl.Ancestors()
                .FirstOrDefault(element => element.Name == Scl + "LN0" || element.Name == Scl + "LN")
                ?? throw new InvalidDataException(
                    $"ReportControl '{(string?)reportControl.Attribute("name")}' is not contained by an LN/LN0 element.");

            var dataSets = logicalNode.Elements(Scl + "DataSet")
                .Where(element => string.Equals(
                    ((string?)element.Attribute("name") ?? string.Empty).Trim(),
                    dataSetName,
                    StringComparison.Ordinal))
                .ToArray();
            if (dataSets.Length != 1)
            {
                throw new InvalidDataException(
                    $"ReportControl '{(string?)reportControl.Attribute("name")}' references DataSet '{dataSetName}', but the generated SCL contains {dataSets.Length} matching DataSet element(s) in the same logical node.");
            }

            var members = dataSets[0].Elements(Scl + "FCDA").ToArray();
            if (members.Length == 0)
            {
                throw new InvalidDataException(
                    $"ReportControl '{(string?)reportControl.Attribute("name")}' references DataSet '{dataSetName}', but that DataSet contains no valid FCDA members.");
            }

            foreach (var fcda in members)
            {
                RequireFcdaAttribute(fcda, "ldInst", dataSetName);
                RequireFcdaAttribute(fcda, "lnClass", dataSetName);
                RequireFcdaAttribute(fcda, "doName", dataSetName);
                RequireFcdaAttribute(fcda, "fc", dataSetName);
            }
        }
    }

    private static void RequireFcdaAttribute(XElement fcda, string attributeName, string dataSetName)
    {
        if (!string.IsNullOrWhiteSpace(((string?)fcda.Attribute(attributeName) ?? string.Empty).Trim()))
            return;

        throw new InvalidDataException(
            $"DataSet '{dataSetName}' contains an FCDA without required '{attributeName}' identity.");
    }

    private static LiveIedSclExportResult WithReportControlCount(
        LiveIedSclExportResult source,
        int logicalReportControlCount)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            GeneratedAtUtc = source.GeneratedAtUtc,
            Profile = source.Profile,
            SclSchema = source.SclSchema,
            SclPath = source.SclPath,
            ReportPath = source.ReportPath,
            SummaryPath = source.SummaryPath,
            ExcludedAttributesPath = source.ExcludedAttributesPath,
            LogicalDeviceCount = source.LogicalDeviceCount,
            LogicalNodeCount = source.LogicalNodeCount,
            DataSetCount = source.DataSetCount,
            ReportControlCount = logicalReportControlCount,
            GooseControlBlockCount = source.GooseControlBlockCount,
            SampledValueControlBlockCount = source.SampledValueControlBlockCount,
            SettingGroupControlCount = source.SettingGroupControlCount,
            LogControlCount = source.LogControlCount,
            LNodeTypeCount = source.LNodeTypeCount,
            DoTypeCount = source.DoTypeCount,
            DaTypeCount = source.DaTypeCount,
            EnumTypeCount = source.EnumTypeCount,
            Warnings = source.Warnings,
            ExcludedAttributes = source.ExcludedAttributes,
            DataSetMappings = source.DataSetMappings,
            ReportMappings = source.ReportMappings,
            ControlBlockMappings = source.ControlBlockMappings
        };

    private static void DeleteIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Keep the original authoritative-validation exception as the primary failure.
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
