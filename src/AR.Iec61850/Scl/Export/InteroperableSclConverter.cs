using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AR.Iec61850.Scl.Export;

public static class InteroperableSclConverter
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    // These standard-name elements occur in Edition 2.1 / vendor capability
    // declarations but are deliberately omitted by the selected 2007 B compatibility export
    // profile used as black-box interoperability evidence for this converter.
    private static readonly HashSet<string> CompatibilityExcludedElements = new(StringComparer.Ordinal)
    {
        "ClientServices",
        "CommProt",
        "ConfLdName",
        "GSESettings",
        "ProtNs",
        "RedProt",
        "SupSubscription",
        "TimeSyncProt",
        "ValueHandling"
    };

    private static readonly HashSet<string> CompatibilityExcludedAttributes = new(StringComparer.Ordinal)
    {
        "originalSclRelease",
        "bufMode"
    };

    public static InteroperableSclConversionResult Load(
        string inputPath,
        InteroperableSclConversionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("SCL input path is empty.", nameof(inputPath));

        using var stream = File.OpenRead(inputPath);
        var source = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return Convert(source, Path.GetFileName(inputPath), options) with { InputPath = Path.GetFullPath(inputPath) };
    }

    public static InteroperableSclConversionResult Convert(
        XDocument source,
        string sourceName = "",
        InteroperableSclConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new InteroperableSclConversionOptions();

        var sourceRoot = source.Root ?? throw new InvalidDataException("SCL document has no root element.");
        if (!Is(sourceRoot, "SCL"))
            throw new InvalidDataException("The selected file is not an IEC 61850 SCL document.");

        var state = new ConversionState();
        var selectedIedNames = SelectIedNames(sourceRoot, sourceName, options, state);
        var sourceRelease = Attr(sourceRoot, "release");
        if (sourceRelease.Length > 0)
        {
            state.Findings.Add(new InteroperableSclFinding
            {
                Severity = "Info",
                Code = "SCL.RELEASE_PROFILE_DOWNSCOPED",
                Reference = sourceRelease,
                Message = "The root release marker and known later-edition capability metadata were omitted for the generic compatibility profile."
            });
        }
        var root = new XElement(
            Scl + "SCL",
            new XAttribute("version", FirstNonEmpty(Attr(sourceRoot, "version"), "2007")),
            OptionalAttribute("revision", Attr(sourceRoot, "revision")),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", $"{Scl.NamespaceName} SCL.xsd"));

        root.Add(BuildHeader(sourceRoot, selectedIedNames, options));

        foreach (var child in sourceRoot.Elements())
        {
            if (Is(child, "Header") || Is(child, "Private"))
            {
                if (Is(child, "Private"))
                    state.RemovedPrivateElementCount += CountElements(child);
                continue;
            }

            if (Is(child, "IED") && !selectedIedNames.Contains(Attr(child, "name")))
                continue;

            if (Is(child, "Substation") && options.RemoveSubstationSection)
            {
                state.Findings.Add(new InteroperableSclFinding
                {
                    Severity = "Info",
                    Code = "SCL.SUBSTATION_OMITTED",
                    Message = "Substation engineering was omitted from the generic single-IED capability document."
                });
                continue;
            }

            var normalized = NormalizeElement(child, state);
            if (normalized is not null)
                root.Add(normalized);
        }

        PruneCommunication(root, selectedIedNames, state);
        PruneCrossIedReferences(root, selectedIedNames, options, state);
        if (options.RemoveUnusedTypeTemplates)
            PruneUnusedTypeTemplates(root, state);

        RemoveEmptyTopLevelContainers(root);
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        ValidateOutput(document, selectedIedNames, state);

        var outputIedNames = root.Elements(Scl + "IED").Select(x => Attr(x, "name")).ToArray();
        var result = new InteroperableSclConversionResult
        {
            Document = document,
            SelectedIedName = selectedIedNames.Count == 1 ? selectedIedNames.Single() : string.Empty,
            OutputIedNames = outputIedNames,
            RemovedIedNames = sourceRoot.Elements().Where(x => Is(x, "IED")).Select(x => Attr(x, "name")).Where(x => !selectedIedNames.Contains(x)).ToArray(),
            RemovedPrivateElementCount = state.RemovedPrivateElementCount,
            RemovedVendorElementCount = state.RemovedVendorElementCount,
            RemovedVendorAttributeCount = state.RemovedVendorAttributeCount,
            RemovedCompatibilityElementCount = state.RemovedCompatibilityElementCount,
            RemovedExternalInputCount = state.RemovedExternalInputCount,
            RemovedUnusedTypeTemplateCount = state.RemovedUnusedTypeTemplateCount,
            LogicalDeviceCount = root.Descendants(Scl + "LDevice").Count(),
            LogicalNodeCount = root.Descendants().Count(x => Is(x, "LN0") || Is(x, "LN")),
            DataSetCount = root.Descendants(Scl + "DataSet").Count(),
            ReportControlCount = root.Descendants(Scl + "ReportControl").Count(),
            GooseControlBlockCount = root.Descendants(Scl + "GSEControl").Count(),
            SampledValueControlBlockCount = root.Descendants(Scl + "SampledValueControl").Count(),
            LNodeTypeCount = root.Descendants(Scl + "LNodeType").Count(),
            DoTypeCount = root.Descendants(Scl + "DOType").Count(),
            DaTypeCount = root.Descendants(Scl + "DAType").Count(),
            EnumTypeCount = root.Descendants(Scl + "EnumType").Count(),
            Findings = state.Findings.ToArray()
        };

        return result;
    }

    public static InteroperableSclConversionResult WriteFiles(
        string inputPath,
        string outputPath,
        InteroperableSclConversionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("SCL output path is empty.", nameof(outputPath));

        var conversion = Load(inputPath, options);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using (var stream = File.Create(fullOutputPath))
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false
        }))
        {
            conversion.Document.Save(writer);
        }

        var reportPath = Path.ChangeExtension(fullOutputPath, ".scl-interoperability-report.json");
        var summaryPath = Path.ChangeExtension(fullOutputPath, ".scl-interoperability-summary.md");
        var written = conversion with
        {
            OutputPath = fullOutputPath,
            ReportPath = reportPath,
            SummaryPath = summaryPath
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(written, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(summaryPath, BuildMarkdown(written), new UTF8Encoding(false));
        return written;
    }

    private static XElement BuildHeader(
        XElement sourceRoot,
        IReadOnlySet<string> selectedIedNames,
        InteroperableSclConversionOptions options)
    {
        var sourceHeader = sourceRoot.Elements().FirstOrDefault(x => Is(x, "Header"));
        var id = selectedIedNames.Count == 1
            ? selectedIedNames.Single()
            : FirstNonEmpty(Attr(sourceHeader, "id"), "ARIEC61850_INTEROPERABLE");

        return new XElement(
            Scl + "Header",
            new XAttribute("id", id),
            new XAttribute("version", "1"),
            new XAttribute("revision", "0"),
            new XAttribute("toolID", FirstNonEmpty(options.ToolId, "ARIEC61850")),
            new XAttribute("nameStructure", FirstNonEmpty(Attr(sourceHeader, "nameStructure"), "IEDName")));
    }

    private static HashSet<string> SelectIedNames(
        XElement root,
        string sourceName,
        InteroperableSclConversionOptions options,
        ConversionState state)
    {
        var names = root.Elements().Where(x => Is(x, "IED")).Select(x => Attr(x, "name")).Where(x => x.Length > 0).ToArray();
        if (names.Length == 0)
            throw new InvalidDataException("The SCL document does not contain an IED element with a name.");

        if (options.PreserveAllIeds)
            return names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.IedName))
        {
            var explicitName = names.FirstOrDefault(x => string.Equals(x, options.IedName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (explicitName is null)
                throw new InvalidOperationException($"IED '{options.IedName}' was not found. Available IEDs: {string.Join(", ", names)}.");
            return new HashSet<string>(new[] { explicitName }, StringComparer.OrdinalIgnoreCase);
        }

        if (names.Length == 1)
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        var headerId = Attr(root.Elements().FirstOrDefault(x => Is(x, "Header")), "id");
        var headerMatch = names.FirstOrDefault(x => string.Equals(x, headerId, StringComparison.OrdinalIgnoreCase));
        if (headerMatch is not null)
        {
            state.Findings.Add(new InteroperableSclFinding
            {
                Severity = "Info",
                Code = "SCL.IED_SELECTED_FROM_HEADER",
                Reference = headerMatch,
                Message = "The local IED was selected from Header@id."
            });
            return new HashSet<string>(new[] { headerMatch }, StringComparer.OrdinalIgnoreCase);
        }

        var fileStem = Path.GetFileNameWithoutExtension(sourceName ?? string.Empty);
        var fileMatches = names.Where(x => fileStem.Contains(x, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fileMatches.Length == 1)
        {
            state.Findings.Add(new InteroperableSclFinding
            {
                Severity = "Info",
                Code = "SCL.IED_SELECTED_FROM_FILENAME",
                Reference = fileMatches[0],
                Message = "The local IED was selected from the source filename."
            });
            return new HashSet<string>(fileMatches, StringComparer.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException($"The SCL contains multiple IEDs and the local IED is ambiguous. Specify one of: {string.Join(", ", names)}.");
    }

    private static XElement? NormalizeElement(XElement source, ConversionState state)
    {
        if (Is(source, "Private"))
        {
            state.RemovedPrivateElementCount += CountElements(source);
            return null;
        }

        if (!IsStandardNamespace(source.Name.NamespaceName))
        {
            state.RemovedVendorElementCount += CountElements(source);
            return null;
        }

        if (CompatibilityExcludedElements.Contains(source.Name.LocalName))
        {
            state.RemovedCompatibilityElementCount += CountElements(source);
            return null;
        }

        var result = new XElement(Scl + source.Name.LocalName);
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;
            if (CompatibilityExcludedAttributes.Contains(attribute.Name.LocalName))
                continue;

            if (attribute.Name.Namespace == XNamespace.None)
            {
                result.Add(new XAttribute(attribute.Name.LocalName, attribute.Value));
                continue;
            }

            if (attribute.Name.Namespace == Xsi || attribute.Name.Namespace == XNamespace.Xml)
            {
                result.Add(new XAttribute(attribute.Name, attribute.Value));
                continue;
            }

            state.RemovedVendorAttributeCount++;
        }

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement child:
                {
                    var normalized = NormalizeElement(child, state);
                    if (normalized is not null)
                        result.Add(normalized);
                    break;
                }
                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    result.Add(new XText(text.Value));
                    break;
                case XCData cdata:
                    result.Add(new XCData(cdata.Value));
                    break;
            }
        }

        return result;
    }

    private static void PruneCommunication(XElement root, IReadOnlySet<string> selectedIedNames, ConversionState state)
    {
        var communication = root.Element(Scl + "Communication");
        if (communication is null)
            return;

        foreach (var connectedAp in communication.Descendants(Scl + "ConnectedAP").ToArray())
        {
            var iedName = Attr(connectedAp, "iedName");
            if (!selectedIedNames.Contains(iedName))
                connectedAp.Remove();
        }

        foreach (var subNetwork in communication.Elements(Scl + "SubNetwork").Where(x => !x.Elements(Scl + "ConnectedAP").Any()).ToArray())
            subNetwork.Remove();

        var outputIeds = root.Elements(Scl + "IED").Select(x => Attr(x, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connectedAp in communication.Descendants(Scl + "ConnectedAP"))
        {
            if (!outputIeds.Contains(Attr(connectedAp, "iedName")))
            {
                state.Findings.Add(new InteroperableSclFinding
                {
                    Severity = "Error",
                    Code = "SCL.CONNECTED_AP_DANGLING",
                    Reference = Attr(connectedAp, "iedName"),
                    Message = "ConnectedAP references an IED that is not present in the output."
                });
            }
        }
    }

    private static void PruneCrossIedReferences(
        XElement root,
        IReadOnlySet<string> selectedIedNames,
        InteroperableSclConversionOptions options,
        ConversionState state)
    {
        if (options.RemoveExternalInputs)
        {
            foreach (var inputs in root.Descendants(Scl + "Inputs").ToArray())
            {
                state.RemovedExternalInputCount += inputs.Descendants(Scl + "ExtRef").Count();
                inputs.Remove();
            }
        }

        foreach (var iedName in root.Descendants(Scl + "IEDName").ToArray())
        {
            if (!selectedIedNames.Contains(iedName.Value.Trim()))
                iedName.Remove();
        }

        foreach (var clientLn in root.Descendants(Scl + "ClientLN").ToArray())
        {
            var iedName = Attr(clientLn, "iedName");
            if (iedName.Length > 0 && !selectedIedNames.Contains(iedName))
                clientLn.Remove();
        }
    }

    private static void PruneUnusedTypeTemplates(XElement root, ConversionState state)
    {
        var templates = root.Element(Scl + "DataTypeTemplates");
        if (templates is null)
            return;

        var lNodeTypes = IndexTemplates(templates, "LNodeType", state);
        var doTypes = IndexTemplates(templates, "DOType", state);
        var daTypes = IndexTemplates(templates, "DAType", state);
        var enumTypes = IndexTemplates(templates, "EnumType", state);

        var usedLNodeTypes = root.Elements(Scl + "IED").SelectMany(x => x.Descendants()).Where(x => Is(x, "LN0") || Is(x, "LN")).Select(x => Attr(x, "lnType")).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var usedDoTypes = new HashSet<string>(StringComparer.Ordinal);
        var usedDaTypes = new HashSet<string>(StringComparer.Ordinal);
        var usedEnumTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in usedLNodeTypes)
        {
            if (!lNodeTypes.TryGetValue(id, out var lNodeType))
            {
                AddMissingTypeFinding(state, "LNodeType", id);
                continue;
            }

            foreach (var dataObject in lNodeType.Elements(Scl + "DO"))
                AddTypeReference(usedDoTypes, dataObject);
        }

        var visitedDoTypes = new HashSet<string>(StringComparer.Ordinal);
        while (visitedDoTypes.Count < usedDoTypes.Count)
        {
            var pending = usedDoTypes.Where(x => !visitedDoTypes.Contains(x)).ToArray();
            foreach (var id in pending)
            {
                visitedDoTypes.Add(id);
                if (!doTypes.TryGetValue(id, out var doType))
                {
                    AddMissingTypeFinding(state, "DOType", id);
                    continue;
                }

                foreach (var sdo in doType.Elements(Scl + "SDO"))
                    AddTypeReference(usedDoTypes, sdo);
                CollectAttributeTypeReferences(doType.Elements(Scl + "DA"), usedDaTypes, usedEnumTypes);
            }
        }

        var visitedDaTypes = new HashSet<string>(StringComparer.Ordinal);
        while (visitedDaTypes.Count < usedDaTypes.Count)
        {
            var pending = usedDaTypes.Where(x => !visitedDaTypes.Contains(x)).ToArray();
            foreach (var id in pending)
            {
                visitedDaTypes.Add(id);
                if (!daTypes.TryGetValue(id, out var daType))
                {
                    AddMissingTypeFinding(state, "DAType", id);
                    continue;
                }

                CollectAttributeTypeReferences(daType.Elements(Scl + "BDA"), usedDaTypes, usedEnumTypes);
            }
        }

        foreach (var id in usedEnumTypes.Where(id => !enumTypes.ContainsKey(id)))
            AddMissingTypeFinding(state, "EnumType", id);

        state.RemovedUnusedTypeTemplateCount += RemoveUnusedTemplates(templates, "LNodeType", usedLNodeTypes);
        state.RemovedUnusedTypeTemplateCount += RemoveUnusedTemplates(templates, "DOType", usedDoTypes);
        state.RemovedUnusedTypeTemplateCount += RemoveUnusedTemplates(templates, "DAType", usedDaTypes);
        state.RemovedUnusedTypeTemplateCount += RemoveUnusedTemplates(templates, "EnumType", usedEnumTypes);
    }

    private static Dictionary<string, XElement> IndexTemplates(XElement templates, string localName, ConversionState state)
    {
        var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in templates.Elements(Scl + localName))
        {
            var id = Attr(element, "id");
            if (id.Length == 0)
                continue;
            if (!index.TryAdd(id, element))
            {
                state.Findings.Add(new InteroperableSclFinding
                {
                    Severity = "Warning",
                    Code = "SCL.TYPE_ID_DUPLICATE",
                    Reference = id,
                    Message = $"Duplicate {localName} id was preserved for diagnostics."
                });
            }
        }
        return index;
    }

    private static void CollectAttributeTypeReferences(
        IEnumerable<XElement> attributes,
        ISet<string> usedDaTypes,
        ISet<string> usedEnumTypes)
    {
        foreach (var attribute in attributes)
        {
            var bType = Attr(attribute, "bType");
            if (string.Equals(bType, "Struct", StringComparison.OrdinalIgnoreCase))
                AddTypeReference(usedDaTypes, attribute);
            else if (string.Equals(bType, "Enum", StringComparison.OrdinalIgnoreCase))
                AddTypeReference(usedEnumTypes, attribute);
        }
    }

    private static void AddTypeReference(ISet<string> destination, XElement element)
    {
        var id = Attr(element, "type");
        if (id.Length > 0)
            destination.Add(id);
    }

    private static int RemoveUnusedTemplates(XElement templates, string localName, IReadOnlySet<string> usedIds)
    {
        var removed = 0;
        foreach (var element in templates.Elements(Scl + localName).ToArray())
        {
            if (usedIds.Contains(Attr(element, "id")))
                continue;
            element.Remove();
            removed++;
        }
        return removed;
    }

    private static void AddMissingTypeFinding(ConversionState state, string kind, string id)
        => state.Findings.Add(new InteroperableSclFinding
        {
            Severity = "Warning",
            Code = "SCL.TYPE_REFERENCE_MISSING",
            Reference = id,
            Message = $"Referenced {kind} '{id}' was not present in the source SCL."
        });

    private static void RemoveEmptyTopLevelContainers(XElement root)
    {
        foreach (var element in root.Elements().Where(x => Is(x, "Communication") || Is(x, "DataTypeTemplates")).ToArray())
        {
            if (!element.Elements().Any())
                element.Remove();
        }
    }

    private static void ValidateOutput(XDocument document, IReadOnlySet<string> selectedIedNames, ConversionState state)
    {
        var parsed = new SclParser().Parse(document, "interoperable.iid");
        var outputIeds = parsed.Ieds.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in selectedIedNames)
        {
            if (!outputIeds.Contains(selected))
                throw new InvalidDataException($"Interoperable SCL validation lost selected IED '{selected}'.");
        }

        foreach (var warning in parsed.Warnings.Where(x => x.Contains("not found", StringComparison.OrdinalIgnoreCase)))
        {
            state.Findings.Add(new InteroperableSclFinding
            {
                Severity = "Warning",
                Code = "SCL.REPARSE_WARNING",
                Message = warning
            });
        }
    }

    private static string BuildMarkdown(InteroperableSclConversionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Interoperable SCL Conversion");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        builder.AppendLine($"- Input: `{result.InputPath}`");
        builder.AppendLine($"- Output: `{result.OutputPath}`");
        builder.AppendLine($"- IED: `{(string.IsNullOrWhiteSpace(result.SelectedIedName) ? string.Join(", ", result.OutputIedNames) : result.SelectedIedName)}`");
        builder.AppendLine();
        builder.AppendLine("## Preserved model");
        builder.AppendLine();
        builder.AppendLine($"- Logical devices: {result.LogicalDeviceCount}");
        builder.AppendLine($"- Logical nodes: {result.LogicalNodeCount}");
        builder.AppendLine($"- DataSets: {result.DataSetCount}");
        builder.AppendLine($"- Report controls: {result.ReportControlCount}");
        builder.AppendLine($"- GOOSE controls: {result.GooseControlBlockCount}");
        builder.AppendLine($"- Sampled Value controls: {result.SampledValueControlBlockCount}");
        builder.AppendLine($"- Type templates: LN={result.LNodeTypeCount}, DO={result.DoTypeCount}, DA={result.DaTypeCount}, Enum={result.EnumTypeCount}");
        builder.AppendLine();
        builder.AppendLine("## Compatibility cleanup");
        builder.AppendLine();
        builder.AppendLine($"- Removed IEDs: {(result.RemovedIedNames.Count == 0 ? "none" : string.Join(", ", result.RemovedIedNames))}");
        builder.AppendLine($"- Private elements: {result.RemovedPrivateElementCount}");
        builder.AppendLine($"- Vendor elements/attributes: {result.RemovedVendorElementCount}/{result.RemovedVendorAttributeCount}");
        builder.AppendLine($"- Later-edition capability elements: {result.RemovedCompatibilityElementCount}");
        builder.AppendLine($"- External Inputs: {result.RemovedExternalInputCount}");
        builder.AppendLine($"- Unreachable type templates: {result.RemovedUnusedTypeTemplateCount}");

        if (result.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            foreach (var finding in result.Findings)
                builder.AppendLine($"- **{finding.Severity} / {finding.Code}**{(string.IsNullOrWhiteSpace(finding.Reference) ? string.Empty : $" `{finding.Reference}`")}: {finding.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("The converter preserves standard SCL instance data and reachable type templates. It does not claim formal schema or IEC conformance; validate the generated IID with the target engineering tool and relay workflow.");
        return builder.ToString();
    }

    private static bool IsStandardNamespace(string namespaceName)
        => string.IsNullOrWhiteSpace(namespaceName) || string.Equals(namespaceName, Scl.NamespaceName, StringComparison.Ordinal);

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
        => element?.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, localName, StringComparison.Ordinal))?.Value?.Trim() ?? string.Empty;

    private static XAttribute? OptionalAttribute(string name, string value)
        => string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static int CountElements(XElement element)
        => 1 + element.Descendants().Count();

    private sealed class ConversionState
    {
        public int RemovedPrivateElementCount { get; set; }
        public int RemovedVendorElementCount { get; set; }
        public int RemovedVendorAttributeCount { get; set; }
        public int RemovedCompatibilityElementCount { get; set; }
        public int RemovedExternalInputCount { get; set; }
        public int RemovedUnusedTypeTemplateCount { get; set; }
        public List<InteroperableSclFinding> Findings { get; } = [];
    }
}
