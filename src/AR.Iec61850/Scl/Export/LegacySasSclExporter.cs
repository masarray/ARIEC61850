using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AR.Iec61850.Scl.Export;

public sealed class LegacySasSclExportOptions
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public SclSchemaProfile SchemaProfile { get; init; } = SclSchemaProfile.Edition1V16;
    public SclReportControlSelection SelectedReportControl { get; init; } = new(string.Empty);
    public IReadOnlyList<SclReportControlSelection> SelectedReportControls { get; init; }
        = Array.Empty<SclReportControlSelection>();
    public bool RemoveUnreferencedDataSets { get; init; }
    public string ToolId { get; init; } = "ARIEC61850";

    internal IReadOnlyList<SclReportControlSelection> EffectiveSelections()
    {
        var explicitSelections = SelectedReportControls
            .Where(selection => !string.IsNullOrWhiteSpace(selection.SelectionKey))
            .GroupBy(selection => selection.SelectionKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (explicitSelections.Length > 0)
            return explicitSelections;
        return string.IsNullOrWhiteSpace(SelectedReportControl.SelectionKey)
            ? Array.Empty<SclReportControlSelection>()
            : new[] { SelectedReportControl };
    }
}

public sealed record LegacySasRetainedReportControl
{
    public string Reference { get; init; } = string.Empty;
    public string DataSetName { get; init; } = string.Empty;
    public int DataSetMemberCount { get; init; }
}

public sealed record LegacySasSclExportResult
{
    public XDocument Document { get; init; } = new();
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string SclSchema { get; init; } = string.Empty;
    public string RetainedReportControlReference { get; init; } = string.Empty;
    public string RetainedDataSetName { get; init; } = string.Empty;
    public int RetainedDataSetMemberCount { get; init; }
    public IReadOnlyList<LegacySasRetainedReportControl> RetainedReportControls { get; init; }
        = Array.Empty<LegacySasRetainedReportControl>();
    public int RetainedReportControlCount => RetainedReportControls.Count;
    public int RemovedReportControlCount { get; init; }
    public int RemovedDataSetCount { get; init; }
    public IReadOnlyList<InteroperableSclFinding> Findings { get; init; } = Array.Empty<InteroperableSclFinding>();
}

public static class LegacySasSclExporter
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static LegacySasSclExportResult Build(
        XDocument source,
        string sourceName,
        LegacySasSclExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var selections = options.EffectiveSelections();
        if (selections.Count == 0)
            throw new InvalidOperationException("Legacy SAS export requires at least one selected ReportControl.");

        var normalized = InteroperableSclConverter.Convert(
            source,
            sourceName,
            new InteroperableSclConversionOptions
            {
                IedName = options.IedName,
                PreserveAllIeds = false,
                RemoveExternalInputs = true,
                RemoveUnusedTypeTemplates = true,
                RemoveSubstationSection = true,
                ToolId = options.ToolId
            });
        var filtered = SclReportControlFilter.Filter(
            normalized.Document,
            new SclReportControlFilterOptions
            {
                IedName = normalized.SelectedIedName,
                AccessPointName = options.AccessPointName,
                SelectedReportControls = selections,
                RequireExactlyOneReportControl = selections.Count == 1,
                RemoveUnreferencedDataSets = options.RemoveUnreferencedDataSets,
                CollapseIndexedSelectionToSingleInstance = true
            },
            sourceName);

        var document = new XDocument(filtered.Document);
        ApplyExactRuntimeReportControlIdentities(document, selections);
        var root = document.Root ?? throw new InvalidDataException("Filtered SCL document has no root element.");
        var schema = SclSchemaProfiles.Get(options.SchemaProfile);
        ApplySchemaProfile(root, schema);
        Validate(document, normalized.SelectedIedName, selections.Count);
        ValidateExactRuntimeReportControlIdentities(document, selections);

        var retained = AssertRetained(filtered, selections.Count);
        var retainedResults = retained
            .Select((descriptor, index) => new LegacySasRetainedReportControl
            {
                Reference = ExactRetainedReference(descriptor, FindSelection(descriptor, selections, index)),
                DataSetName = descriptor.DataSetName,
                DataSetMemberCount = descriptor.DataSetMemberCount
            })
            .ToArray();
        var findings = normalized.Findings
            .Concat(filtered.Findings)
            .Append(new InteroperableSclFinding
            {
                Severity = "Info",
                Code = "SCL.LEGACY_SAS_EXPORT_READY",
                Reference = string.Join(", ", retainedResults.Select(item => item.Reference)),
                Message = $"Prepared a {retainedResults.Length}-RCB {schema.DisplayName} capability document for deterministic legacy SAS import."
            })
            .GroupBy(item => $"{item.Severity}|{item.Code}|{item.Reference}|{item.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new LegacySasSclExportResult
        {
            Document = document,
            IedName = normalized.SelectedIedName,
            AccessPointName = retained[0].AccessPointName,
            SclSchema = schema.DisplayName,
            RetainedReportControlReference = string.Join(", ", retainedResults.Select(item => item.Reference)),
            RetainedDataSetName = string.Join(", ", retainedResults.Select(item => item.DataSetName).Distinct(StringComparer.OrdinalIgnoreCase)),
            RetainedDataSetMemberCount = retainedResults.Sum(item => item.DataSetMemberCount),
            RetainedReportControls = retainedResults,
            RemovedReportControlCount = filtered.RemovedReportControlCount,
            RemovedDataSetCount = filtered.RemovedDataSetCount,
            Findings = findings
        };
    }

    public static LegacySasSclExportResult WriteFiles(
        string inputPath,
        string outputPath,
        LegacySasSclExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        using var input = File.OpenRead(inputPath);
        var source = XDocument.Load(input, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var built = Build(source, Path.GetFileName(inputPath), options);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using (var stream = File.Create(fullOutputPath))
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        }))
        {
            built.Document.Save(writer);
        }

        var reportPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-report.json");
        var summaryPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-summary.md");
        var written = built with
        {
            InputPath = Path.GetFullPath(inputPath),
            OutputPath = fullOutputPath,
            ReportPath = reportPath,
            SummaryPath = summaryPath
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(written, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(summaryPath, BuildMarkdown(written), new UTF8Encoding(false));
        return written;
    }

    private static void ApplyExactRuntimeReportControlIdentities(
        XDocument document,
        IReadOnlyList<SclReportControlSelection> selections)
    {
        var retained = document.Descendants(Scl + "ReportControl").ToArray();
        foreach (var selection in selections.Where(item => !string.IsNullOrWhiteSpace(item.ExportName)))
        {
            var exactRuntimeName = selection.ExportName.Trim();
            var sourceName = SourceNameFromSelectionKey(selection.SelectionKey);
            var matches = retained.Where(element =>
                    string.Equals((string?)element.Attribute("name"), exactRuntimeName, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(sourceName) &&
                     string.Equals((string?)element.Attribute("name"), sourceName, StringComparison.Ordinal)))
                .Distinct()
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Exact runtime RCB normalization could not uniquely map '{exactRuntimeName}'; found {matches.Length} retained candidate(s).");

            var reportControl = matches[0];
            reportControl.SetAttributeValue("name", exactRuntimeName);
            reportControl.SetAttributeValue("indexed", "false");
            foreach (var rptEnabled in reportControl.Elements(Scl + "RptEnabled").ToArray())
                rptEnabled.Remove();
        }
    }

    private static string SourceNameFromSelectionKey(string selectionKey)
    {
        var normalized = (selectionKey ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return string.Empty;
        var pipe = normalized.LastIndexOf('|');
        if (pipe >= 0 && pipe + 1 < normalized.Length)
            return normalized[(pipe + 1)..];
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < normalized.Length)
            return normalized[(slash + 1)..];
        return string.Empty;
    }

    private static SclReportControlSelection FindSelection(
        SclReportControlDescriptor retained,
        IReadOnlyList<SclReportControlSelection> selections,
        int fallbackIndex)
    {
        var exact = selections.FirstOrDefault(selection =>
            NormalizeSelectionKey(selection.SelectionKey).Equals(
                NormalizeSelectionKey(retained.SelectionKey), StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var byExportName = selections.FirstOrDefault(selection =>
            !string.IsNullOrWhiteSpace(selection.ExportName) &&
            retained.Name.Equals(selection.ExportName, StringComparison.OrdinalIgnoreCase));
        return byExportName ?? selections[Math.Min(fallbackIndex, selections.Count - 1)];
    }

    private static string NormalizeSelectionKey(string value)
        => (value ?? string.Empty).Trim().Replace('\\', '/');

    private static void ValidateExactRuntimeReportControlIdentities(
        XDocument document,
        IReadOnlyList<SclReportControlSelection> selections)
    {
        var retained = document.Descendants(Scl + "ReportControl").ToArray();
        foreach (var selection in selections.Where(item => !string.IsNullOrWhiteSpace(item.ExportName)))
        {
            var exactRuntimeName = selection.ExportName.Trim();
            var matches = retained.Where(element =>
                string.Equals((string?)element.Attribute("name"), exactRuntimeName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Filtered SCL must contain exact runtime RCB '{exactRuntimeName}' exactly once; found {matches.Length}.");
            var reportControl = matches[0];
            if (!string.Equals((string?)reportControl.Attribute("indexed"), "false", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Exact runtime RCB '{exactRuntimeName}' must be exported as non-indexed.");
            if (reportControl.Elements(Scl + "RptEnabled").Any())
                throw new InvalidDataException($"Exact runtime RCB '{exactRuntimeName}' must not contain RptEnabled because that can append a second instance suffix.");
        }
    }

    private static string ExactRetainedReference(
        SclReportControlDescriptor retained,
        SclReportControlSelection selection)
    {
        var exactRuntimeName = (selection.ExportName ?? string.Empty).Trim();
        if (exactRuntimeName.Length == 0)
            return retained.DisplayReference;

        var separator = retained.DisplayReference.LastIndexOf('.');
        return separator < 0
            ? exactRuntimeName
            : retained.DisplayReference[..(separator + 1)] + exactRuntimeName;
    }

    private static IReadOnlyList<SclReportControlDescriptor> AssertRetained(
        SclReportControlFilterResult filtered,
        int expectedCount)
    {
        if (filtered.RetainedReportControls.Count != expectedCount)
            throw new InvalidDataException($"Legacy SAS export must retain {expectedCount} selected ReportControl(s); found {filtered.RetainedReportControls.Count}.");
        foreach (var retained in filtered.RetainedReportControls)
        {
            if (!retained.HasPopulatedDataSet)
                throw new InvalidDataException($"Retained ReportControl '{retained.DisplayReference}' does not reference a populated DataSet.");
        }
        return filtered.RetainedReportControls;
    }

    private static void ApplySchemaProfile(XElement root, SclSchemaProfileDescriptor schema)
    {
        root.SetAttributeValue("version", schema.RootVersion);
        root.SetAttributeValue("revision", schema.RootRevision);
        if (!schema.SupportsTriggerGi)
        {
            foreach (var triggerOptions in root.Descendants(Scl + "TrgOps"))
                triggerOptions.SetAttributeValue("gi", null);
        }
        if (!schema.IsEdition2)
        {
            foreach (var confReportControl in root.Descendants(Scl + "ConfReportControl"))
                confReportControl.SetAttributeValue("bufConf", null);
        }
        if (!schema.SupportsReservationTime)
        {
            foreach (var reportSettings in root.Descendants(Scl + "ReportSettings"))
            {
                reportSettings.SetAttributeValue("owner", null);
                reportSettings.SetAttributeValue("resvTms", null);
            }
            foreach (var service in root.Descendants().Where(element => element.Name.LocalName is "SGEdit" or "ConfSG"))
                service.SetAttributeValue("resvTms", null);
        }
    }

    private static void Validate(XDocument document, string iedName, int expectedCount)
    {
        var parsed = new SclParser().Parse(document, "legacy-sas.cid");
        if (!parsed.Ieds.Any(item => item.Name.Equals(iedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Filtered SCL validation lost IED '{iedName}'.");
        if (parsed.ReportControls.Count != expectedCount)
            throw new InvalidDataException($"Filtered SCL validation expected {expectedCount} ReportControl(s), found {parsed.ReportControls.Count}.");
        foreach (var retained in parsed.ReportControls)
        {
            if (retained.DataSetBindingStatus != SclDataSetBindingStatus.Resolved || retained.Entries.Count == 0)
                throw new InvalidDataException($"Filtered SCL validation found an unresolved or empty DataSet for '{retained.ControlBlockReference}'.");
        }
    }

    private static string BuildMarkdown(LegacySasSclExportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Legacy SAS Selected-RCB Export");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        builder.AppendLine($"- Input: `{result.InputPath}`");
        builder.AppendLine($"- Output: `{result.OutputPath}`");
        builder.AppendLine($"- IED / AccessPoint: `{result.IedName}` / `{result.AccessPointName}`");
        builder.AppendLine($"- Schema: `{result.SclSchema}`");
        builder.AppendLine($"- Retained RCBs: {result.RetainedReportControlCount}");
        foreach (var retained in result.RetainedReportControls)
            builder.AppendLine($"  - `{retained.Reference}` → `{retained.DataSetName}` ({retained.DataSetMemberCount} FCDA)");
        builder.AppendLine($"- Total retained DataSet members: {result.RetainedDataSetMemberCount}");
        builder.AppendLine($"- Removed RCBs: {result.RemovedReportControlCount}");
        builder.AppendLine($"- Removed unreferenced DataSets: {result.RemovedDataSetCount}");
        builder.AppendLine();
        builder.AppendLine("The original source file was not modified. Validate the generated CID with the target SAS import workflow before operational use.");
        if (result.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            foreach (var finding in result.Findings)
                builder.AppendLine($"- **{finding.Severity} / {finding.Code}**{(string.IsNullOrWhiteSpace(finding.Reference) ? string.Empty : $" `{finding.Reference}`")}: {finding.Message}");
        }
        return builder.ToString();
    }
}
