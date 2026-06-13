using System.Text;
using System.Text.Json;

namespace AR.Iec61850.Discovery;

public sealed class LiveIedServiceDiscoveryReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Target { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedServiceCoverageItem> Services { get; init; } = Array.Empty<LiveIedServiceCoverageItem>();
    public IReadOnlyList<string> NextGaps { get; init; } = Array.Empty<string>();
}

public sealed class LiveIedServiceCoverageItem
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Gap { get; init; } = string.Empty;
}

public static class LiveIedServiceDiscoveryReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static LiveIedServiceDiscoveryReport Build(LiveIedModelDiscoveryDocument document)
        => Build(document, null);

    public static LiveIedServiceDiscoveryReport Build(LiveIedModelDiscoveryDocument document, LiveIedOnlineServiceEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fileEvidence = evidence?.FileService ?? new LiveIedFileServiceEvidence();
        var settingReadbacks = evidence?.SettingGroupReadbacks ?? Array.Empty<LiveIedSettingGroupReadbackEvidence>();
        var settingMap = evidence?.SettingGroupMap ?? new LiveIedSettingGroupMapDocument();
        var typeProbe = evidence?.VariableTypeProbe ?? new LiveIedVariableTypeProbeEvidence();
        var typeQuarantine = evidence?.VariableSpecQuarantine ?? new LiveIedVariableSpecQuarantineEvidence();
        var goldenLearning = evidence?.GoldenSclTypeLearning ?? new LiveIedGoldenSclTypeLearningEvidence();
        var fileStatus = !fileEvidence.Attempted ? "Not attempted" : fileEvidence.IsSuccess ? "Discovered" : "Attempted, failed or unsupported";
        var fileCount = fileEvidence.Entries.Count;
        var fileMessage = !fileEvidence.Attempted ? "FileDirectory was not attempted in this run." : fileEvidence.IsSuccess ? $"FileDirectory returned {fileCount} entries from {fileEvidence.PageCount} page(s)." : fileEvidence.Message;
        var sgSuccessful = settingReadbacks.Count(x => x.HasAnySuccess);
        var sgCoreComplete = settingReadbacks.Count(IsSettingGroupCoreReadbackComplete);
        var sgStatus = BuildSettingGroupStatus(document, settingReadbacks.Count, sgSuccessful, sgCoreComplete, settingMap.EntryCount);
        var sgEvidence = BuildSettingGroupEvidence(document, settingReadbacks.Count, sgSuccessful, sgCoreComplete, settingMap);
        var sgGap = BuildSettingGroupGap(sgCoreComplete, settingMap);

        var services = new List<LiveIedServiceCoverageItem>
        {
            Item("Data model", document.Coverage.DataAttributeCount > 0 ? "Discovered" : "Missing", document.Coverage.DataAttributeCount, $"LD={document.Coverage.LogicalDeviceCount}, LN={document.Coverage.LogicalNodeCount}, DO={document.Coverage.DataObjectCount}, DA={document.Coverage.DataAttributeCount}", ""),
            Item("Functional constraints", document.Coverage.ExactFunctionalConstraintCount > 0 ? "Discovered" : "Missing", document.Coverage.ExactFunctionalConstraintCount, "FC is extracted from MMS $FC$ names and dataset/readback evidence.", ""),
            Item("DataSets", document.Coverage.DataSetCount > 0 ? "Discovered" : "Not exposed or not discovered", document.Coverage.DataSetCount, $"{document.DataSets.Count(x => x.MemberCount > 0)} dataset(s) have resolved directory members.", "Add deeper DataSet value reads and cross-link to GoCB/SVCB when exposed."),
            Item("Reports / RCB", document.Coverage.ReportControlCount > 0 ? "Discovered" : "Not exposed or not discovered", document.Coverage.ReportControlCount, $"BRCB={document.Coverage.BufferedReportControlCount}, URCB={document.Coverage.UnbufferedReportControlCount}", "Read all RCB attributes and normalize static SCL state vs runtime state."),
            Item("GOOSE control blocks", document.Coverage.GooseControlBlockCount > 0 ? "Inventory" : "Not exposed or not discovered", document.Coverage.GooseControlBlockCount, "Current implementation detects GSEControl attribute inventory when present.", "Implement GoCB value reader: GoEna, GoID, DatSet, ConfRev, NdsCom, MinTime, MaxTime, DstAddress."),
            Item("Sampled Value control blocks", document.Coverage.SampledValueControlBlockCount > 0 ? "Inventory" : "Not exposed or not discovered", document.Coverage.SampledValueControlBlockCount, "Current implementation detects MS/US/SVCB attribute inventory when present.", "Implement SVCB value reader: SvID/smvID, DatSet, ConfRev, SmpRate, SmpMod, NofASDU, DstAddress."),
            Item("Setting groups", sgStatus, Math.Max(Math.Max(document.Coverage.SettingGroupControlCount, sgSuccessful), settingMap.EntryCount), sgEvidence, sgGap),
            Item("Logs", document.Coverage.LogControlCount > 0 ? "Inventory" : "Not exposed or not discovered", document.Coverage.LogControlCount, "LogControl inventory is available when LG attributes are exposed.", "Implement log directory/query service."),
            Item("File service", fileStatus, fileCount, fileMessage, fileEvidence.IsSuccess ? "Add FileOpen/FileRead download support and recursive safe directory walking." : "Implement/verify FileDirectory support on this IED and add file download evidence."),
            Item("Variable specifications", BuildVariableTypeStatus(document, typeProbe, typeQuarantine), Math.Max(document.Coverage.VariableTypeReadSuccessCount, typeProbe.SuccessCount), BuildVariableTypeEvidence(document, typeProbe, typeQuarantine), BuildVariableTypeGap(typeProbe, typeQuarantine)),
            Item("Golden SCL type learning", BuildGoldenLearningStatus(goldenLearning), goldenLearning.CandidateImprovementCount, BuildGoldenLearningEvidence(goldenLearning), BuildGoldenLearningGap(goldenLearning)),
            Item("CDC resolution", document.Coverage.UnknownCdcCount == 0 ? "Resolved" : "Partially resolved", document.Coverage.HighConfidenceCdcCount + document.Coverage.MediumConfidenceCdcCount + document.Coverage.LowConfidenceCdcCount, $"high={document.Coverage.HighConfidenceCdcCount}, medium={document.Coverage.MediumConfidenceCdcCount}, low={document.Coverage.LowConfidenceCdcCount}, unknown={document.Coverage.UnknownCdcCount}", "Expand IEC 61850-7-3/7-4 registry and feed golden SCL learning results into normalized type generation.")
        };

        return new LiveIedServiceDiscoveryReport
        {
            Target = $"{document.Host}:{document.Port}",
            IedName = document.IedName,
            Services = services,
            NextGaps = services.Where(x => !string.IsNullOrWhiteSpace(x.Gap) &&
                    !x.Status.Equals("Discovered", StringComparison.OrdinalIgnoreCase) &&
                    !x.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
                .Select(x => $"{x.Name}: {x.Gap}")
                .ToArray()
        };
    }

    public static IReadOnlyList<string> WriteFiles(LiveIedModelDiscoveryDocument document, string outputDirectory)
        => WriteFiles(document, outputDirectory, null);

    public static IReadOnlyList<string> WriteFiles(LiveIedModelDiscoveryDocument document, string outputDirectory, LiveIedOnlineServiceEvidence? evidence)
    {
        Directory.CreateDirectory(outputDirectory);
        var report = Build(document, evidence);
        var jsonPath = Path.Combine(outputDirectory, "service-coverage-report.json");
        var markdownPath = Path.Combine(outputDirectory, "service-coverage-report.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
        File.WriteAllText(markdownPath, BuildMarkdown(report), Encoding.UTF8);
        return new[] { jsonPath, markdownPath };
    }

    public static string BuildMarkdown(LiveIedServiceDiscoveryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# IEC 61850 Online Service Discovery Coverage");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Target: {Escape(report.Target)}");
        sb.AppendLine($"- IED: {Escape(report.IedName)}");
        sb.AppendLine();
        sb.AppendLine("| Service area | Status | Count | Evidence | Remaining gap |");
        sb.AppendLine("| --- | --- | ---: | --- | --- |");
        foreach (var item in report.Services)
            sb.AppendLine($"| {Escape(item.Name)} | {Escape(item.Status)} | {item.Count} | {Escape(item.Evidence)} | {Escape(item.Gap)} |");
        sb.AppendLine();
        if (report.NextGaps.Count > 0)
        {
            sb.AppendLine("## Next implementation gaps");
            sb.AppendLine();
            foreach (var gap in report.NextGaps)
                sb.AppendLine($"- {Escape(gap)}");
            sb.AppendLine();
        }
        sb.AppendLine("## Interpretation");
        sb.AppendLine();
        sb.AppendLine("This report separates what ARIEC61850 already discovers online from what still needs a dedicated MMS service implementation. It is intentionally stricter than the SCL exporter: a service is not marked complete merely because a placeholder or attribute name was seen in the live model.");
        return sb.ToString();
    }


    private static string BuildVariableTypeStatus(LiveIedModelDiscoveryDocument document, LiveIedVariableTypeProbeEvidence probe, LiveIedVariableSpecQuarantineEvidence quarantine)
    {
        if (quarantine.IsQuarantined)
            return "Quarantined after peer-close";

        if (!probe.Attempted && document.Coverage.VariableTypeReadAttemptCount == 0)
            return "Not attempted";

        if (probe.ProtocolFaultSuspected && probe.SuccessCount == 0)
            return "Safe probe stopped or unsupported";

        if (probe.SuccessCount > 0 && probe.StoppedBeforeCandidateExhausted)
            return "Safely partially read";

        if (probe.SuccessCount > 0)
            return "Safely read";

        if (probe.Attempted && probe.SelectedCandidateCount == 0)
            return "No safe candidates";

        return "Safely attempted";
    }

    private static string BuildVariableTypeEvidence(LiveIedModelDiscoveryDocument document, LiveIedVariableTypeProbeEvidence probe, LiveIedVariableSpecQuarantineEvidence quarantine)
    {
        if (quarantine.IsQuarantined)
            return $"{quarantine.Summary} trigger={quarantine.TriggerReference}";

        if (!probe.Attempted)
            return $"attempted={document.Coverage.VariableTypeReadAttemptCount}, ok={document.Coverage.VariableTypeReadSuccessCount}, failed={document.Coverage.VariableTypeReadFailureCount}";

        return $"{probe.Summary} scalar={probe.ExactScalarTypeCount}, structure={probe.ExactStructureTypeCount}, selected={probe.SelectedCandidateCount}/{probe.RawCandidateCount}.";
    }

    private static string BuildVariableTypeGap(LiveIedVariableTypeProbeEvidence probe, LiveIedVariableSpecQuarantineEvidence quarantine)
    {
        if (quarantine.IsQuarantined)
            return "Use golden SCL/type registry learning for this IED; keep GetVariableAccessAttributes disabled or isolated.";

        if (!probe.Attempted)
            return "Use safe, leaf-only, dataset-first type reads to avoid IED peer-close behavior.";

        if (probe.ProtocolFaultSuspected)
            return "Reduce max type reads, keep dataset-first leaf-only probing, and avoid the last failed reference class.";

        if (probe.SuccessCount > 0)
            return "Feed exact type results into CDC/template normalization and expand safe candidate batches gradually.";

        return "Check whether this IED supports GetVariableAccessAttributes for selected leaf attributes; keep probe limits low.";
    }



    private static string BuildGoldenLearningStatus(LiveIedGoldenSclTypeLearningEvidence learning)
    {
        if (!learning.Attempted)
            return "Not attempted";
        if (!learning.IsSuccess)
            return "Unavailable";
        if (learning.CandidateImprovementCount > 0)
            return "Learning candidates found";
        if (learning.ExactKeyMatchCount > 0)
            return "Golden reference aligned";
        return "No applicable learning candidates";
    }

    private static string BuildGoldenLearningEvidence(LiveIedGoldenSclTypeLearningEvidence learning)
    {
        if (!learning.Attempted)
            return "No golden SCL file was supplied or auto-detected.";
        if (!learning.IsSuccess)
            return learning.Message;
        return $"goldenBindings={learning.GoldenBindingCount}, liveDO={learning.LiveDataObjectCount}, unknownOrMedium={learning.LiveUnknownOrMediumCount}, exactKeyMatches={learning.ExactKeyMatchCount}, candidates={learning.CandidateImprovementCount}, conflicts={learning.CdcConflictCount}.";
    }

    private static string BuildGoldenLearningGap(LiveIedGoldenSclTypeLearningEvidence learning)
    {
        if (!learning.Attempted)
            return "Provide --golden-scl samples/scl/<IED>.iid or keep the golden file in samples/scl for automatic CDC/type learning.";
        if (!learning.IsSuccess)
            return "Fix golden SCL path/parsing before using it as a type-learning reference.";
        if (learning.CandidateImprovementCount > 0)
            return "Promote confirmed golden CDC/type candidates into the standard/vendor registry and SCL normalizer.";
        return string.Empty;
    }

    private static bool IsSettingGroupCoreReadbackComplete(LiveIedSettingGroupReadbackEvidence readback)
    {
        var required = new[] { "NumOfSG", "ActSG", "EditSG", "CnfEdit", "LActTm" };
        return required.All(name => readback.Attributes.Any(attribute =>
            attribute.IsSuccess && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildSettingGroupStatus(
        LiveIedModelDiscoveryDocument document,
        int readbackCount,
        int successfulReadbacks,
        int coreCompleteReadbacks,
        int mapEntries)
    {
        if (coreCompleteReadbacks > 0 && mapEntries > 0)
            return "Core readback complete + SG/SE map";

        if (coreCompleteReadbacks > 0)
            return "Core readback complete";

        if (successfulReadbacks > 0)
            return "Partially read";

        if (mapEntries > 0)
            return "SG/SE map";

        return document.Coverage.SettingGroupControlCount > 0 || readbackCount > 0 ? "Inventory" : "Not exposed or not discovered";
    }

    private static string BuildSettingGroupEvidence(
        LiveIedModelDiscoveryDocument document,
        int readbackCount,
        int successfulReadbacks,
        int coreCompleteReadbacks,
        LiveIedSettingGroupMapDocument settingMap)
    {
        if (coreCompleteReadbacks > 0 || settingMap.EntryCount > 0)
        {
            var readText = settingMap.ReadAttemptCount > 0
                ? $", setting value reads={settingMap.ReadSuccessCount}/{settingMap.ReadAttemptCount}"
                : ", setting value reads=not attempted";
            return $"SGCB core readback complete={coreCompleteReadbacks}/{Math.Max(readbackCount, document.Coverage.SettingGroupControlCount)}; SG/SE map entries={settingMap.EntryCount}{readText}.";
        }

        if (successfulReadbacks > 0)
            return $"{successfulReadbacks}/{readbackCount} SGCB inventory item(s) have at least one readable SGCB attribute.";

        return "SG/SE inventory is available when exposed in the MMS directory.";
    }

    private static string BuildSettingGroupGap(int coreCompleteReadbacks, LiveIedSettingGroupMapDocument settingMap)
    {
        if (coreCompleteReadbacks > 0 && settingMap.EntryCount > 0 && settingMap.ReadAttemptCount > 0)
            return "Add edition-aware setting semantics and safe setting write/confirm workflow evidence later; no write is performed by service discovery.";

        if (coreCompleteReadbacks > 0 && settingMap.EntryCount > 0)
            return "Optionally enable --read-setting-values true for safe SG/SE value readback evidence; add edition-aware setting semantics later.";

        if (coreCompleteReadbacks > 0)
            return "Map SG/SE setting attributes and add setting value readback evidence.";

        return "Implement SGCB services/readback: NumOfSG, ActSG, EditSG, CnfEdit plus SG/SE setting attributes.";
    }

    private static LiveIedServiceCoverageItem Item(string name, string status, int count, string evidence, string gap)
        => new()
        {
            Name = name,
            Status = status,
            Count = count,
            Evidence = evidence,
            Gap = gap
        };

    private static string Escape(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
