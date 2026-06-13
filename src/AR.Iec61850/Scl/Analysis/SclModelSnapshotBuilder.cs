using System.Xml.Linq;

namespace AR.Iec61850.Scl.Analysis;

public static class SclModelSnapshotBuilder
{
    public static SclModelSnapshot Load(string sclPath)
    {
        if (string.IsNullOrWhiteSpace(sclPath))
            throw new ArgumentException("SCL path is required.", nameof(sclPath));
        if (!File.Exists(sclPath))
            throw new FileNotFoundException("SCL file was not found.", sclPath);

        var document = XDocument.Load(sclPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new InvalidDataException("SCL file has no root element.");
        var dtt = ElementsLocal(root, "DataTypeTemplates").FirstOrDefault();
        var doTypeById = dtt is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : ElementsLocal(dtt, "DOType")
                .Select(x => new
                {
                    Id = Attr(x, "id"),
                    Cdc = Attr(x, "cdc")
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Cdc, StringComparer.OrdinalIgnoreCase);

        var ldevices = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var lnodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var datasets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var reports = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var goose = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var sv = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var logs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var doCdcBindings = new List<SclDoCdcBinding>();
        var services = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ieds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ied in ElementsLocal(root, "IED"))
        {
            var iedName = Attr(ied, "name");
            if (!string.IsNullOrWhiteSpace(iedName))
                ieds.Add(iedName);

            var servicesElement = ElementsLocal(ied, "Services").FirstOrDefault();
            if (servicesElement is not null)
                CollectServices(servicesElement, iedName, services);

            foreach (var ldevice in DescendantsLocal(ied, "LDevice"))
            {
                var ldInst = Attr(ldevice, "inst");
                if (string.IsNullOrWhiteSpace(ldInst))
                    continue;

                var ldKey = MakeLdKey(iedName, ldInst);
                ldevices.Add(ldKey);

                foreach (var ln in ldevice.Elements().Where(IsLogicalNodeElement))
                {
                    var lnName = GetLogicalNodeName(ln);
                    var lnType = Attr(ln, "lnType");
                    var lnClass = Attr(ln, "lnClass");
                    lnodes.Add($"{ldKey}/{lnName}");

                    foreach (var dataSet in ElementsLocal(ln, "DataSet"))
                    {
                        var name = Attr(dataSet, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                            datasets.Add($"{ldKey}/{lnName}.{name}");
                    }

                    foreach (var report in ElementsLocal(ln, "ReportControl"))
                    {
                        var name = Attr(report, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                            reports.Add($"{ldKey}/{lnName}.{name}");
                    }

                    foreach (var control in ElementsLocal(ln, "GSEControl"))
                    {
                        var name = Attr(control, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                            goose.Add($"{ldKey}/{lnName}.{name}");
                    }

                    foreach (var control in ElementsLocal(ln, "SampledValueControl"))
                    {
                        var name = Attr(control, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                            sv.Add($"{ldKey}/{lnName}.{name}");
                    }

                    foreach (var control in ElementsLocal(ln, "SettingControl"))
                    {
                        var name = Attr(control, "name");
                        settings.Add(string.IsNullOrWhiteSpace(name)
                            ? $"{ldKey}/{lnName}.SettingControl"
                            : $"{ldKey}/{lnName}.{name}");
                    }

                    foreach (var control in ElementsLocal(ln, "LogControl"))
                    {
                        var name = Attr(control, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                            logs.Add($"{ldKey}/{lnName}.{name}");
                    }

                    if (!string.IsNullOrWhiteSpace(lnType) && dtt is not null)
                    {
                        var lNodeType = ElementsLocal(dtt, "LNodeType")
                            .FirstOrDefault(x => Attr(x, "id").Equals(lnType, StringComparison.OrdinalIgnoreCase));
                        if (lNodeType is not null)
                        {
                            var typeLnClass = Attr(lNodeType, "lnClass");
                            if (string.IsNullOrWhiteSpace(typeLnClass))
                                typeLnClass = lnClass;

                            foreach (var dataObject in ElementsLocal(lNodeType, "DO"))
                            {
                                var doName = Attr(dataObject, "name");
                                var doTypeId = Attr(dataObject, "type");
                                if (string.IsNullOrWhiteSpace(doName))
                                    continue;

                                doTypeById.TryGetValue(doTypeId, out var cdc);
                                doCdcBindings.Add(new SclDoCdcBinding
                                {
                                    LogicalNodeClass = typeLnClass,
                                    DataObjectName = doName,
                                    Cdc = cdc ?? string.Empty,
                                    DoTypeId = doTypeId,
                                    SourceLNodeTypeId = lnType
                                });
                            }
                        }
                    }
                }
            }
        }

        var lNodeTypes = GetTypeIds(dtt, "LNodeType");
        var doTypes = GetTypeIds(dtt, "DOType");
        var daTypes = GetTypeIds(dtt, "DAType");
        var enumTypes = GetTypeIds(dtt, "EnumType");

        return new SclModelSnapshot
        {
            SourcePath = Path.GetFullPath(sclPath),
            SourceName = Path.GetFileName(sclPath),
            NamespaceUri = root.Name.NamespaceName,
            Version = Attr(root, "version"),
            Revision = Attr(root, "revision"),
            Release = Attr(root, "release"),
            IedNames = ieds.ToArray(),
            LogicalDevices = ldevices.ToArray(),
            LogicalNodes = lnodes.ToArray(),
            DataSets = datasets.ToArray(),
            ReportControls = reports.ToArray(),
            GooseControls = goose.ToArray(),
            SampledValueControls = sv.ToArray(),
            SettingControls = settings.ToArray(),
            LogControls = logs.ToArray(),
            LNodeTypes = lNodeTypes,
            DoTypes = doTypes,
            DaTypes = daTypes,
            EnumTypes = enumTypes,
            DoCdcBindings = doCdcBindings
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DoTypeId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DoTypeSignatures = GetTypeSignatures(dtt, "DOType"),
            DaTypeSignatures = GetTypeSignatures(dtt, "DAType"),
            ServiceCapabilities = services
        };
    }

    private static string[] GetTypeIds(XElement? dataTypeTemplates, string localName)
        => dataTypeTemplates is null
            ? Array.Empty<string>()
            : ElementsLocal(dataTypeTemplates, localName)
                .Select(x => Attr(x, "id"))
                .Where(NotBlank)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static SclTypeSignature[] GetTypeSignatures(XElement? dataTypeTemplates, string localName)
        => dataTypeTemplates is null
            ? Array.Empty<SclTypeSignature>()
            : ElementsLocal(dataTypeTemplates, localName)
                .Select(x => BuildTypeSignature(localName, x))
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static void CollectServices(XElement servicesElement, string iedName, IDictionary<string, string> services)
    {
        foreach (var element in servicesElement.Elements())
        {
            var key = element.Name.LocalName;
            var value = element.HasAttributes
                ? string.Join(",", element.Attributes().OrderBy(x => x.Name.LocalName, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Name.LocalName}={x.Value}"))
                : "present";
            if (!string.IsNullOrWhiteSpace(iedName))
                key = $"{iedName}.{key}";
            services[key] = value;
        }
    }

    private static SclTypeSignature BuildTypeSignature(string kind, XElement element)
    {
        var members = new List<string>();
        foreach (var child in element.Elements())
            CollectTypeMembers(child, child.Name.LocalName, members);

        members.Sort(StringComparer.OrdinalIgnoreCase);
        return new SclTypeSignature
        {
            Kind = kind,
            Id = Attr(element, "id"),
            Cdc = Attr(element, "cdc"),
            Signature = string.Join(";", members),
            MemberCount = members.Count
        };
    }

    private static void CollectTypeMembers(XElement element, string path, ICollection<string> members)
    {
        var name = Attr(element, "name");
        if (!string.IsNullOrWhiteSpace(name) && !path.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            path = $"{path}.{name}";

        var fields = new[]
        {
            element.Name.LocalName,
            name,
            Attr(element, "fc"),
            Attr(element, "bType"),
            Attr(element, "type"),
            Attr(element, "count")
        };
        members.Add(string.Join("|", fields));

        foreach (var child in element.Elements())
            CollectTypeMembers(child, string.IsNullOrWhiteSpace(name) ? path : $"{path}.{name}", members);
    }

    private static string MakeLdKey(string iedName, string ldInst)
        => string.IsNullOrWhiteSpace(iedName) ? ldInst : $"{iedName}/{ldInst}";

    private static bool IsLogicalNodeElement(XElement element)
        => element.Name.LocalName is "LN0" or "LN";

    private static string GetLogicalNodeName(XElement element)
    {
        if (element.Name.LocalName == "LN0")
            return "LLN0";

        return string.Concat(Attr(element, "prefix"), Attr(element, "lnClass"), Attr(element, "inst"));
    }

    private static string Attr(XElement element, string name)
        => element.Attribute(name)?.Value.Trim() ?? string.Empty;

    private static bool NotBlank(string value)
        => !string.IsNullOrWhiteSpace(value);

    private static IEnumerable<XElement> ElementsLocal(XElement element, string localName)
        => element.Elements().Where(x => x.Name.LocalName.Equals(localName, StringComparison.Ordinal));

    private static IEnumerable<XElement> DescendantsLocal(XElement element, string localName)
        => element.Descendants().Where(x => x.Name.LocalName.Equals(localName, StringComparison.Ordinal));
}
