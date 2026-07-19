using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

public sealed record SclReportControlSelection(
    string SelectionKey,
    string ExportName = "");

public sealed class SclReportControlDescriptor
{
    public string SelectionKey { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string LogicalDeviceInstance { get; init; } = string.Empty;
    public string LogicalNodePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayReference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public bool Indexed { get; init; } = true;
    public int InstanceCount { get; init; } = 1;
    public string DataSetName { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public int DataSetMemberCount { get; init; }
    public bool DataSetResolved { get; init; }

    public string Type => Buffered ? "Buffered" : "Unbuffered";
    public bool HasPopulatedDataSet => DataSetResolved && DataSetMemberCount > 0;
}

public sealed class SclReportControlInventoryResult
{
    public string SelectedIedName { get; init; } = string.Empty;
    public string SelectedAccessPointName { get; init; } = string.Empty;
    public IReadOnlyList<SclReportControlDescriptor> ReportControls { get; init; } = Array.Empty<SclReportControlDescriptor>();
    public IReadOnlyList<InteroperableSclFinding> Findings { get; init; } = Array.Empty<InteroperableSclFinding>();
}

public sealed class SclReportControlFilterOptions
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public IReadOnlyList<SclReportControlSelection> SelectedReportControls { get; init; }
        = Array.Empty<SclReportControlSelection>();
    public bool RequireExactlyOneReportControl { get; init; } = true;
    public bool RemoveUnreferencedDataSets { get; init; }
    public bool CollapseIndexedSelectionToSingleInstance { get; init; } = true;
}

public sealed class SclReportControlFilterResult
{
    public XDocument Document { get; init; } = new();
    public IReadOnlyList<SclReportControlDescriptor> RetainedReportControls { get; init; } = Array.Empty<SclReportControlDescriptor>();
    public int RemovedReportControlCount { get; init; }
    public int RemovedDataSetCount { get; init; }
    public IReadOnlyList<InteroperableSclFinding> Findings { get; init; } = Array.Empty<InteroperableSclFinding>();
}

public static class SclReportControlFilter
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static SclReportControlInventoryResult InspectFile(
        string inputPath,
        string iedName = "",
        string accessPointName = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        using var stream = File.OpenRead(inputPath);
        return Inspect(
            XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo),
            Path.GetFileName(inputPath),
            iedName,
            accessPointName);
    }

    public static SclReportControlInventoryResult Inspect(
        XDocument source,
        string sourceName = "",
        string iedName = "",
        string accessPointName = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        var root = source.Root ?? throw new InvalidDataException("SCL document has no root element.");
        if (!Is(root, "SCL"))
            throw new InvalidDataException("The selected file is not an IEC 61850 SCL document.");

        var findings = new List<InteroperableSclFinding>();
        var selectedIed = SelectIed(root, sourceName, iedName);
        var accessPoints = selectedIed.Elements().Where(element => Is(element, "AccessPoint")).ToArray();
        if (!string.IsNullOrWhiteSpace(accessPointName))
        {
            accessPoints = accessPoints
                .Where(element => Same(Attr(element, "name"), accessPointName))
                .ToArray();
            if (accessPoints.Length == 0)
                throw new InvalidOperationException($"AccessPoint '{accessPointName}' was not found in IED '{Attr(selectedIed, "name")}'.");
        }

        var descriptors = new List<SclReportControlDescriptor>();
        foreach (var accessPoint in accessPoints)
        {
            foreach (var logicalDevice in accessPoint.Descendants().Where(element => Is(element, "LDevice")))
            {
                foreach (var logicalNode in logicalDevice.Elements().Where(element => Is(element, "LN0") || Is(element, "LN")))
                {
                    foreach (var reportControl in logicalNode.Elements().Where(element => Is(element, "ReportControl")))
                        descriptors.Add(BuildDescriptor(selectedIed, accessPoint, logicalDevice, logicalNode, reportControl));
                }
            }
        }

        foreach (var descriptor in descriptors.Where(item => !item.DataSetResolved))
        {
            findings.Add(new InteroperableSclFinding
            {
                Severity = "Warning",
                Code = "SCL.REPORT_DATASET_UNRESOLVED",
                Reference = descriptor.DisplayReference,
                Message = string.IsNullOrWhiteSpace(descriptor.DataSetName)
                    ? "The ReportControl has no datSet reference."
                    : $"The ReportControl references DataSet '{descriptor.DataSetName}', but it was not found in the same Logical Node."
            });
        }

        foreach (var descriptor in descriptors.Where(item => item.DataSetResolved && item.DataSetMemberCount == 0))
        {
            findings.Add(new InteroperableSclFinding
            {
                Severity = "Warning",
                Code = "SCL.REPORT_DATASET_EMPTY",
                Reference = descriptor.DisplayReference,
                Message = $"The referenced DataSet '{descriptor.DataSetName}' contains no FCDA members."
            });
        }

        return new SclReportControlInventoryResult
        {
            SelectedIedName = Attr(selectedIed, "name"),
            SelectedAccessPointName = accessPoints.Length == 1 ? Attr(accessPoints[0], "name") : accessPointName,
            ReportControls = descriptors
                .OrderByDescending(item => item.HasPopulatedDataSet)
                .ThenByDescending(item => item.Buffered)
                .ThenBy(item => item.DisplayReference, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Findings = findings
        };
    }

    public static SclReportControlFilterResult Filter(
        XDocument source,
        SclReportControlFilterOptions options,
        string sourceName = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.SelectedReportControls.Count == 0)
            throw new InvalidOperationException("At least one ReportControl selection is required.");
        if (options.RequireExactlyOneReportControl && options.SelectedReportControls.Count != 1)
            throw new InvalidOperationException("Legacy SAS export requires exactly one selected ReportControl.");

        var document = new XDocument(source);
        var inventory = Inspect(document, sourceName, options.IedName, options.AccessPointName);
        var selectedByKey = options.SelectedReportControls
            .GroupBy(selection => NormalizeKey(selection.SelectionKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var availableByKey = inventory.ReportControls.ToDictionary(
            descriptor => NormalizeKey(descriptor.SelectionKey),
            descriptor => descriptor,
            StringComparer.OrdinalIgnoreCase);

        foreach (var requested in selectedByKey.Keys)
        {
            if (!availableByKey.ContainsKey(requested))
                throw new InvalidOperationException($"Selected ReportControl '{selectedByKey[requested].SelectionKey}' was not found in the target IED/AccessPoint.");
        }

        var selectedDescriptors = selectedByKey.Keys.Select(key => availableByKey[key]).ToArray();
        foreach (var descriptor in selectedDescriptors)
        {
            if (!descriptor.DataSetResolved)
                throw new InvalidOperationException($"Selected ReportControl '{descriptor.DisplayReference}' does not resolve a DataSet in the same Logical Node.");
            if (descriptor.DataSetMemberCount <= 0)
                throw new InvalidOperationException($"Selected ReportControl '{descriptor.DisplayReference}' references an empty DataSet.");
        }

        var selectedIed = SelectIed(document.Root!, sourceName, inventory.SelectedIedName);
        var removedReportControls = 0;
        foreach (var element in selectedIed.Descendants().Where(item => Is(item, "ReportControl")).ToArray())
        {
            var descriptor = BuildDescriptorForElement(selectedIed, element);
            var key = NormalizeKey(descriptor.SelectionKey);
            if (!selectedByKey.TryGetValue(key, out var selection))
            {
                element.Remove();
                removedReportControls++;
                continue;
            }

            if (options.CollapseIndexedSelectionToSingleInstance)
                CollapseToSingleInstance(element, selection.ExportName);
        }

        var removedDataSets = options.RemoveUnreferencedDataSets
            ? RemoveUnreferencedDataSets(selectedIed)
            : 0;

        var after = Inspect(document, sourceName, inventory.SelectedIedName, options.AccessPointName);
        if (options.RequireExactlyOneReportControl && after.ReportControls.Count != 1)
            throw new InvalidDataException($"Filtered SCL must contain exactly one ReportControl, but contains {after.ReportControls.Count}.");

        var retainedKeys = after.ReportControls.Select(item => NormalizeKey(item.SelectionKey)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in selectedByKey.Keys)
        {
            if (!retainedKeys.Contains(selected) && string.IsNullOrWhiteSpace(selectedByKey[selected].ExportName))
                throw new InvalidDataException($"Filtered SCL lost selected ReportControl '{selectedByKey[selected].SelectionKey}'.");
        }

        var findings = new List<InteroperableSclFinding>(after.Findings)
        {
            new()
            {
                Severity = "Info",
                Code = "SCL.REPORT_FILTER_APPLIED",
                Reference = string.Join(", ", after.ReportControls.Select(item => item.DisplayReference)),
                Message = $"Retained {after.ReportControls.Count} ReportControl(s), removed {removedReportControls}, and removed {removedDataSets} unreferenced DataSet(s)."
            }
        };

        return new SclReportControlFilterResult
        {
            Document = document,
            RetainedReportControls = after.ReportControls,
            RemovedReportControlCount = removedReportControls,
            RemovedDataSetCount = removedDataSets,
            Findings = findings
        };
    }

    public static LiveIedModelDiscoveryDocument FilterLiveModel(
        LiveIedModelDiscoveryDocument source,
        string selectedReportControlReference)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedReportControlReference);
        var normalized = NormalizeReference(selectedReportControlReference);
        var matches = source.ReportControls
            .Where(candidate => NormalizeReference(candidate.Reference).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            var requestedName = LastName(selectedReportControlReference);
            matches = source.ReportControls
                .Where(candidate => candidate.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        if (matches.Length != 1)
            throw new InvalidOperationException(matches.Length == 0
                ? $"ReportControl '{selectedReportControlReference}' was not found in the model."
                : $"ReportControl '{selectedReportControlReference}' is ambiguous in the model.");

        var selected = matches[0];
        var dataSet = source.DataSets.FirstOrDefault(item => NormalizeReference(item.Reference)
            .Equals(NormalizeReference(selected.DataSetReference), StringComparison.OrdinalIgnoreCase));
        if (dataSet is null)
            throw new InvalidOperationException($"Selected ReportControl '{selected.Reference}' references an unresolved DataSet '{selected.DataSetReference}'.");
        if (dataSet.MemberCount <= 0 || dataSet.Members.Count == 0)
            throw new InvalidOperationException($"Selected ReportControl '{selected.Reference}' references an empty DataSet.");

        var coverage = source.Coverage;
        var filteredCoverage = new LiveIedModelDiscoveryCoverage
        {
            LogicalDeviceCount = coverage.LogicalDeviceCount,
            LogicalNodeCount = coverage.LogicalNodeCount,
            DataObjectCount = coverage.DataObjectCount,
            DataAttributeCount = coverage.DataAttributeCount,
            ExactFunctionalConstraintCount = coverage.ExactFunctionalConstraintCount,
            HighConfidenceCdcCount = coverage.HighConfidenceCdcCount,
            MediumConfidenceCdcCount = coverage.MediumConfidenceCdcCount,
            LowConfidenceCdcCount = coverage.LowConfidenceCdcCount,
            UnknownCdcCount = coverage.UnknownCdcCount,
            DataSetCount = coverage.DataSetCount,
            FileCount = coverage.FileCount,
            VariableTypeReadAttemptCount = coverage.VariableTypeReadAttemptCount,
            VariableTypeReadSuccessCount = coverage.VariableTypeReadSuccessCount,
            VariableTypeReadFailureCount = coverage.VariableTypeReadFailureCount,
            ExactMmsTypeCount = coverage.ExactMmsTypeCount,
            ReportControlCount = 1,
            BufferedReportControlCount = selected.Buffered ? 1 : 0,
            UnbufferedReportControlCount = selected.Buffered ? 0 : 1,
            GooseControlBlockCount = coverage.GooseControlBlockCount,
            SampledValueControlBlockCount = coverage.SampledValueControlBlockCount,
            SettingGroupControlCount = coverage.SettingGroupControlCount,
            LogControlCount = coverage.LogControlCount
        };

        return new LiveIedModelDiscoveryDocument
        {
            SchemaVersion = source.SchemaVersion,
            GeneratedAtUtc = source.GeneratedAtUtc,
            Source = source.Source,
            Host = source.Host,
            Port = source.Port,
            IedName = source.IedName,
            IedIdentity = source.IedIdentity,
            AccessPointName = source.AccessPointName,
            Summary = $"{source.Summary} Legacy-SAS filter retained {selected.Reference}.",
            Coverage = filteredCoverage,
            LogicalDevices = source.LogicalDevices,
            FileDirectory = source.FileDirectory,
            DataSets = source.DataSets,
            ReportControls = new[] { selected },
            GooseControlBlocks = source.GooseControlBlocks,
            SampledValueControlBlocks = source.SampledValueControlBlocks,
            SettingGroupControls = source.SettingGroupControls,
            LogControls = source.LogControls,
            TypeTemplates = source.TypeTemplates,
            VariableTypeDiscoveries = source.VariableTypeDiscoveries,
            Warnings = source.Warnings
        };
    }

    private static SclReportControlDescriptor BuildDescriptorForElement(XElement selectedIed, XElement reportControl)
    {
        var logicalNode = reportControl.Parent ?? throw new InvalidDataException("ReportControl has no Logical Node parent.");
        var logicalDevice = logicalNode.Ancestors().First(element => Is(element, "LDevice"));
        var accessPoint = logicalDevice.Ancestors().First(element => Is(element, "AccessPoint"));
        return BuildDescriptor(selectedIed, accessPoint, logicalDevice, logicalNode, reportControl);
    }

    private static SclReportControlDescriptor BuildDescriptor(
        XElement ied,
        XElement accessPoint,
        XElement logicalDevice,
        XElement logicalNode,
        XElement reportControl)
    {
        var iedName = Attr(ied, "name");
        var apName = Attr(accessPoint, "name");
        var ldInst = Attr(logicalDevice, "inst");
        var lnPath = LogicalNodePath(logicalNode);
        var name = Attr(reportControl, "name");
        var buffered = ParseBool(Attr(reportControl, "buffered"));
        var indexedText = Attr(reportControl, "indexed");
        var indexed = indexedText.Length == 0 || ParseBool(indexedText);
        var rptEnabled = reportControl.Elements().FirstOrDefault(element => Is(element, "RptEnabled"));
        var instanceCount = ParsePositiveInt(Attr(rptEnabled, "max"), 1);
        var dataSetName = Attr(reportControl, "datSet");
        var dataSet = logicalNode.Elements().FirstOrDefault(element => Is(element, "DataSet") && Same(Attr(element, "name"), dataSetName));
        var mode = buffered ? "BR" : "RP";
        return new SclReportControlDescriptor
        {
            SelectionKey = SelectionKey(iedName, apName, ldInst, lnPath, name),
            IedName = iedName,
            AccessPointName = apName,
            LogicalDeviceInstance = ldInst,
            LogicalNodePath = lnPath,
            Name = name,
            DisplayReference = $"{iedName}{ldInst}/{lnPath}.{mode}.{name}",
            Buffered = buffered,
            Indexed = indexed,
            InstanceCount = instanceCount,
            DataSetName = dataSetName,
            DataSetReference = string.IsNullOrWhiteSpace(dataSetName) ? string.Empty : $"{iedName}{ldInst}/{lnPath}.{dataSetName}",
            DataSetResolved = dataSet is not null,
            DataSetMemberCount = dataSet?.Elements().Count(element => Is(element, "FCDA")) ?? 0
        };
    }

    private static void CollapseToSingleInstance(XElement reportControl, string exportName)
    {
        if (!string.IsNullOrWhiteSpace(exportName))
            reportControl.SetAttributeValue("name", exportName.Trim());
        reportControl.SetAttributeValue("indexed", "false");
        var rptEnabled = reportControl.Elements().FirstOrDefault(element => Is(element, "RptEnabled"));
        if (rptEnabled is null)
        {
            rptEnabled = new XElement(Scl + "RptEnabled");
            reportControl.Add(rptEnabled);
        }
        rptEnabled.SetAttributeValue("max", "1");
    }

    private static int RemoveUnreferencedDataSets(XElement selectedIed)
    {
        var removed = 0;
        foreach (var logicalNode in selectedIed.Descendants().Where(element => Is(element, "LN0") || Is(element, "LN")))
        {
            var used = logicalNode.Elements()
                .Where(element => Is(element, "ReportControl") || Is(element, "GSEControl") || Is(element, "SampledValueControl") || Is(element, "LogControl"))
                .Select(element => Attr(element, "datSet"))
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dataSet in logicalNode.Elements().Where(element => Is(element, "DataSet")).ToArray())
            {
                if (used.Contains(Attr(dataSet, "name")))
                    continue;
                dataSet.Remove();
                removed++;
            }
        }
        return removed;
    }

    private static XElement SelectIed(XElement root, string sourceName, string requestedIedName)
    {
        var ieds = root.Elements().Where(element => Is(element, "IED")).ToArray();
        if (ieds.Length == 0)
            throw new InvalidDataException("The SCL document contains no IED element.");
        if (!string.IsNullOrWhiteSpace(requestedIedName))
            return ieds.FirstOrDefault(element => Same(Attr(element, "name"), requestedIedName))
                ?? throw new InvalidOperationException($"IED '{requestedIedName}' was not found.");
        if (ieds.Length == 1)
            return ieds[0];
        var headerId = Attr(root.Elements().FirstOrDefault(element => Is(element, "Header")), "id");
        var headerMatch = ieds.FirstOrDefault(element => Same(Attr(element, "name"), headerId));
        if (headerMatch is not null)
            return headerMatch;
        var stem = Path.GetFileNameWithoutExtension(sourceName ?? string.Empty);
        var fileMatches = ieds.Where(element => stem.Contains(Attr(element, "name"), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fileMatches.Length == 1)
            return fileMatches[0];
        throw new InvalidOperationException($"The SCL contains multiple IEDs. Specify one of: {string.Join(", ", ieds.Select(element => Attr(element, "name")))}.");
    }

    private static string LogicalNodePath(XElement logicalNode)
    {
        if (Is(logicalNode, "LN0"))
            return "LLN0";
        return $"{Attr(logicalNode, "prefix")}{Attr(logicalNode, "lnClass")}{Attr(logicalNode, "inst")}";
    }

    private static string SelectionKey(string iedName, string accessPoint, string ldInst, string logicalNode, string name)
        => $"{iedName}|{accessPoint}|{ldInst}|{logicalNode}|{name}";

    private static string NormalizeKey(string? value) => (value ?? string.Empty).Trim();
    private static string NormalizeReference(string? value) => (value ?? string.Empty).Trim().Replace('$', '.');
    private static string LastName(string value)
    {
        var normalized = NormalizeReference(value);
        var index = normalized.LastIndexOf('.');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }
    private static bool ParseBool(string value) => bool.TryParse(value, out var parsed) && parsed;
    private static int ParsePositiveInt(string value, int fallback) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Is(XElement element, string localName) => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
    private static string Attr(XElement? element, string name) => element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value?.Trim() ?? string.Empty;
}
