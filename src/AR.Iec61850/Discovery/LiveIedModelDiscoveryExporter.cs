using System.Text;
using System.Text.Json;

namespace AR.Iec61850.Discovery;

public static class LiveIedModelDiscoveryExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyList<string> WriteBundle(LiveIedModelDiscoveryDocument document, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        var files = new List<string>();
        var modelJson = Path.Combine(outputDirectory, "ied-model.json");
        var summaryMd = Path.Combine(outputDirectory, "discovery-summary.md");
        var typeReportJson = Path.Combine(outputDirectory, "type-confidence-report.json");
        var datasetsJson = Path.Combine(outputDirectory, "datasets.json");
        var rcbJson = Path.Combine(outputDirectory, "rcb-inventory.json");
        var filesJson = Path.Combine(outputDirectory, "file-directory.json");
        var controlBlocksJson = Path.Combine(outputDirectory, "control-block-inventory.json");
        var legacyControlBlocksJson = Path.Combine(outputDirectory, "control-block-placeholders.json");
        var variableTypesJson = Path.Combine(outputDirectory, "variable-access-attributes.json");

        File.WriteAllText(modelJson, JsonSerializer.Serialize(document, JsonOptions), Encoding.UTF8);
        File.WriteAllText(summaryMd, BuildMarkdown(document), Encoding.UTF8);
        File.WriteAllText(typeReportJson, JsonSerializer.Serialize(BuildTypeConfidenceReport(document), JsonOptions), Encoding.UTF8);
        File.WriteAllText(datasetsJson, JsonSerializer.Serialize(document.DataSets, JsonOptions), Encoding.UTF8);
        File.WriteAllText(rcbJson, JsonSerializer.Serialize(document.ReportControls, JsonOptions), Encoding.UTF8);
        File.WriteAllText(filesJson, JsonSerializer.Serialize(document.FileDirectory, JsonOptions), Encoding.UTF8);
        var controlBlockInventory = new
        {
            document.GooseControlBlocks,
            document.SampledValueControlBlocks,
            document.SettingGroupControls,
            document.LogControls
        };
        File.WriteAllText(controlBlocksJson, JsonSerializer.Serialize(controlBlockInventory, JsonOptions), Encoding.UTF8);
        File.WriteAllText(legacyControlBlocksJson, JsonSerializer.Serialize(controlBlockInventory, JsonOptions), Encoding.UTF8);
        File.WriteAllText(variableTypesJson, JsonSerializer.Serialize(document.VariableTypeDiscoveries, JsonOptions), Encoding.UTF8);

        files.Add(modelJson);
        files.Add(summaryMd);
        files.Add(typeReportJson);
        files.Add(datasetsJson);
        files.Add(rcbJson);
        files.Add(filesJson);
        files.Add(controlBlocksJson);
        files.Add(legacyControlBlocksJson);
        files.Add(variableTypesJson);
        return files;
    }

    public static string BuildMarkdown(LiveIedModelDiscoveryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sb = new StringBuilder();
        sb.AppendLine("# Live IEC 61850 IED Model Discovery");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {document.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Source: {Escape(document.Source)}");
        sb.AppendLine($"- Target: {Escape(document.Host)}:{document.Port}");
        sb.AppendLine($"- IED: {Escape(document.IedName)}");
        sb.AppendLine($"- IED identity: source={Escape(document.IedIdentity.Source)}, confidence={document.IedIdentity.Confidence}, ambiguous={document.IedIdentity.IsAmbiguous.ToString().ToLowerInvariant()}");
        if (document.IedIdentity.CandidateNames.Count > 0)
            sb.AppendLine($"- IED candidates: {Escape(string.Join(", ", document.IedIdentity.CandidateNames))}");
        sb.AppendLine($"- AccessPoint: {Escape(document.AccessPointName)}");
        sb.AppendLine($"- Summary: {Escape(document.Summary)}");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | ---: |");
        AppendMetric(sb, "Logical devices", document.Coverage.LogicalDeviceCount);
        AppendMetric(sb, "Logical nodes", document.Coverage.LogicalNodeCount);
        AppendMetric(sb, "Data objects", document.Coverage.DataObjectCount);
        AppendMetric(sb, "Data attributes", document.Coverage.DataAttributeCount);
        AppendMetric(sb, "Exact FC attributes", document.Coverage.ExactFunctionalConstraintCount);
        AppendMetric(sb, "Variable type reads attempted", document.Coverage.VariableTypeReadAttemptCount);
        AppendMetric(sb, "Variable type reads OK", document.Coverage.VariableTypeReadSuccessCount);
        AppendMetric(sb, "Variable type reads failed", document.Coverage.VariableTypeReadFailureCount);
        AppendMetric(sb, "Exact MMS type attributes", document.Coverage.ExactMmsTypeCount);
        AppendMetric(sb, "DataSets", document.Coverage.DataSetCount);
        AppendMetric(sb, "Files", document.Coverage.FileCount);
        AppendMetric(sb, "RCB", document.Coverage.ReportControlCount);
        AppendMetric(sb, "BRCB", document.Coverage.BufferedReportControlCount);
        AppendMetric(sb, "URCB", document.Coverage.UnbufferedReportControlCount);
        AppendMetric(sb, "CDC high confidence", document.Coverage.HighConfidenceCdcCount);
        AppendMetric(sb, "CDC medium confidence", document.Coverage.MediumConfidenceCdcCount);
        AppendMetric(sb, "CDC low confidence", document.Coverage.LowConfidenceCdcCount);
        AppendMetric(sb, "CDC unknown", document.Coverage.UnknownCdcCount);
        sb.AppendLine();

        sb.AppendLine("## Functional Constraint Counts");
        sb.AppendLine();
        sb.AppendLine("| FC | Count |");
        sb.AppendLine("| --- | ---: |");
        foreach (var fc in document.LogicalDevices
            .SelectMany(x => x.LogicalNodes)
            .SelectMany(x => x.FunctionalConstraintCounts)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {Escape(fc.Key)} | {fc.Sum(x => x.Value)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## DataSets");
        sb.AppendLine();
        sb.AppendLine("| DataSet | Members | Used by RCB |");
        sb.AppendLine("| --- | ---: | --- |");
        foreach (var dataSet in document.DataSets.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| {Escape(dataSet.Reference)} | {dataSet.MemberCount} | {Escape(string.Join(", ", dataSet.UsedByReportControls.Take(8)))} |");
        sb.AppendLine();

        sb.AppendLine("## Report Controls");
        sb.AppendLine();
        sb.AppendLine("| Kind | RCB | DataSet | RptID | ConfRev | Status |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var rcb in document.ReportControls.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).Take(64))
        {
            var kind = rcb.Buffered ? "BRCB" : "URCB";
            sb.AppendLine($"| {kind} | {Escape(rcb.Reference)} | {Escape(rcb.DataSetReference)} | {Escape(rcb.ReportId)} | {Escape(rcb.ConfRev)} | {Escape(rcb.Status)} |");
        }
        if (document.ReportControls.Count > 64)
            sb.AppendLine($"| ... | ... | ... | ... | ... | {document.ReportControls.Count - 64} more RCB(s) in rcb-inventory.json | ");
        sb.AppendLine();

        sb.AppendLine("## File Directory");
        sb.AppendLine();
        if (!document.FileDirectory.Attempted)
        {
            sb.AppendLine("FileDirectory was not queried in this discovery run.");
        }
        else if (!document.FileDirectory.IsSuccess)
        {
            sb.AppendLine($"FileDirectory did not complete: {Escape(document.FileDirectory.Message)}");
        }
        else
        {
            sb.AppendLine("| Path | Size | Modified | Kind |");
            sb.AppendLine("| --- | ---: | --- | --- |");
            foreach (var file in document.FileDirectory.Entries.Take(80))
                sb.AppendLine($"| {Escape(file.Path)} | {file.SizeBytes?.ToString() ?? "-"} | {Escape(file.LastModified)} | {(file.IsLikelyDirectory ? "directory" : "file")} |");
            if (document.FileDirectory.Entries.Count > 80)
                sb.AppendLine($"| ... | ... | ... | {document.FileDirectory.Entries.Count - 80} more entries in file-directory.json |");
        }
        sb.AppendLine();

        sb.AppendLine("## MMS Type Discovery Snapshot");
        sb.AppendLine();
        sb.AppendLine("| Reference | Status | MMS Type | SCL bType | Signature | Message |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var type in document.VariableTypeDiscoveries.OrderByDescending(x => x.IsSuccess).ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).Take(80))
        {
            sb.AppendLine($"| {Escape(type.Reference)} | {(type.IsSuccess ? "OK" : "FAIL")} | {Escape(type.MmsType)} | {Escape(type.SclBType)} | {Escape(type.TypeSignature)} | {Escape(type.Message)} |");
        }
        if (document.VariableTypeDiscoveries.Count > 80)
            sb.AppendLine($"| ... | ... | ... | ... | ... | {document.VariableTypeDiscoveries.Count - 80} more item(s) in variable-access-attributes.json |");
        sb.AppendLine();

        sb.AppendLine("## Control Block Inventory");
        sb.AppendLine();
        sb.AppendLine("| Kind | Reference | FC | Attributes | Status | Message |");
        sb.AppendLine("| --- | --- | --- | ---: | --- | --- |");
        foreach (var control in document.GooseControlBlocks
            .Concat(document.SampledValueControlBlocks)
            .Concat(document.SettingGroupControls)
            .Concat(document.LogControls)
            .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Take(80))
        {
            sb.AppendLine($"| {Escape(control.Kind)} | {Escape(control.Reference)} | {Escape(control.FunctionalConstraint)} | {control.AttributeCount} | {Escape(control.DiscoveryStatus)} | {Escape(control.Message)} |");
        }
        var controlCount = document.GooseControlBlocks.Count + document.SampledValueControlBlocks.Count + document.SettingGroupControls.Count + document.LogControls.Count;
        if (controlCount > 80)
            sb.AppendLine($"| ... | ... | ... | ... | ... | {controlCount - 80} more item(s) in control-block-inventory.json |");
        sb.AppendLine();

        sb.AppendLine("## CDC Inference Snapshot");
        sb.AppendLine();
        sb.AppendLine("| Reference | CDC | Confidence | Evidence |");
        sb.AppendLine("| --- | --- | ---: | --- |");
        foreach (var dataObject in document.LogicalDevices
            .SelectMany(x => x.LogicalNodes)
            .SelectMany(x => x.DataObjects)
            .OrderByDescending(x => x.CdcConfidence)
            .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Take(80))
        {
            sb.AppendLine($"| {Escape(dataObject.Reference)} | {Escape(dataObject.InferredCdc)} | {dataObject.CdcConfidence:0.00} | {Escape(string.Join("; ", dataObject.Evidence.Take(3)))} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Planned SCL Export Notes");
        sb.AppendLine();
        sb.AppendLine("- FC is treated as exact when it comes from MMS `$FC$` names, DataSet members, or GetDataDirectoryFC-style discovery.");
        sb.AppendLine("- CDC is reconstructed with confidence scoring; generated SCL must not claim original vendor DataTypeTemplates.");
        sb.AppendLine("- Runtime states such as RptEna, ResvTms, EntryID, SqNum, and ownership contention belong in companion evidence JSON, not static SCL configuration.");
        sb.AppendLine("- This bundle is the canonical model input for the next Live-to-SCL exporter and SCL-backed simulator seed.");
        sb.AppendLine();

        if (document.Warnings.Count > 0)
        {
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var warning in document.Warnings)
                sb.AppendLine($"- {Escape(warning.Code)} {Escape(warning.Reference)}: {Escape(warning.Message)}");
        }

        return sb.ToString();
    }

    private static IReadOnlyList<object> BuildTypeConfidenceReport(LiveIedModelDiscoveryDocument document)
        => document.LogicalDevices
            .SelectMany(x => x.LogicalNodes)
            .SelectMany(x => x.DataObjects)
            .Select(x => new
            {
                x.Reference,
                x.Name,
                x.InferredCdc,
                x.CdcConfidence,
                x.ConfidenceLevel,
                x.ProposedDoTypeId,
                x.Evidence,
                Attributes = x.Attributes.Select(a => new
                {
                    a.AttributePath,
                    a.FunctionalConstraint,
                    a.SclBType,
                    a.MmsType,
                    a.MmsTypeSignature,
                    a.TypeDiscoveryStatus,
                    a.TypeSource,
                    a.TypeConfidence,
                    a.ObjectReference,
                    a.MmsReference
                })
            })
            .Cast<object>()
            .ToArray();

    private static void AppendMetric(StringBuilder sb, string name, int value)
        => sb.AppendLine($"| {Escape(name)} | {value} |");

    private static string Escape(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
