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
    public bool RemoveUnreferencedDataSets { get; init; }
    public string ToolId { get; init; } = "ARIEC61850";
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
        if (string.IsNullOrWhiteSpace(options.SelectedReportControl.SelectionKey))
            throw new InvalidOperationException("Legacy SAS export requires exactly one selected ReportControl.");

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
                SelectedReportControls = new[] { options.SelectedReportControl },
                RequireExactlyOneReportControl = true,
                RemoveUnreferencedDataSets = options.RemoveUnreferencedDataSets,
                CollapseIndexedSelectionToSingleInstance = true
            },
            sourceName);

        var document = new XDocument(filtered.Document);
        var root = document.Root ?? throw new InvalidDataException("Filtered SCL document has no root element.");
        var schema = SclSchemaProfiles.Get(options.SchemaProfile);
        ApplySchemaProfile(root, schema);
        Validate(document, normalized.SelectedIedName);

        var retained = AssertSingleRetained(filtered);
        var findings = normalized.Findings
            .Concat(filtered.Findings)
            .Append(new InteroperableSclFinding
            {
                Severity = "Info",
                Code = "SCL.LEGACY_SAS_EXPORT_READY",
                Reference = retained.DisplayReference,
                Message = $"Prepared a single-RCB {schema.DisplayName} capability document for deterministic legacy SAS import."
            })
            .GroupBy(item => $"{item.Severity}|{item.Code}|{item.Reference}|{item.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new LegacySasSclExportResult
        {
            Document = document,
            IedName = normalized.SelectedIedName,
            AccessPointName = retained.AccessPointName,
            SclSchema = schema.DisplayName,
            RetainedReportControlReference = retained.DisplayReference,
            RetainedDataSetName = retained.DataSetName,
            RetainedDataSetMemberCount = retained.DataSetMemberCount,
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

    private static SclReportControlDescriptor AssertSingleRetained(SclReportControlFilterResult filtered)
    {
        if (filtered.RetainedReportControls.Count != 1)
            throw new InvalidDataException($"Legacy SAS export must retain exactly one ReportControl; found {filtered.RetainedReportControls.Count}.");
        var retained = filtered.RetainedReportControls[0];
        if (!retained.HasPopulatedDataSet)
            throw new InvalidDataException("The retained ReportControl does not reference a populated DataSet.");
        return retained;
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

    private static void Validate(XDocument document, string iedName)
    {
        var parsed = new SclParser().Parse(document, "legacy-sas.cid");
        if (!parsed.Ieds.Any(item => item.Name.Equals(iedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Filtered SCL validation lost IED '{iedName}'.");
        if (parsed.ReportControls.Count != 1)
            throw new InvalidDataException($"Filtered SCL validation expected one ReportControl, found {parsed.ReportControls.Count}.");
        var retained = parsed.ReportControls[0];
        if (retained.DataSetBindingStatus != SclDataSetBindingStatus.Resolved || retained.Entries.Count == 0)
            throw new InvalidDataException($"Filtered SCL validation found an unresolved or empty DataSet for '{retained.ControlBlockReference}'.");
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
        builder.AppendLine($"- Retained RCB: `{result.RetainedReportControlReference}`");
        builder.AppendLine($"- DataSet: `{result.RetainedDataSetName}` ({result.RetainedDataSetMemberCount} FCDA)");
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
