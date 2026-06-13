using System.Text;
using System.Text.Json;

namespace AR.Iec61850.Scl.Analysis;

public static class SclGoldenDiffAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static SclGoldenDiffReport Analyze(string goldenPath, string candidatePath)
    {
        var golden = SclModelSnapshotBuilder.Load(goldenPath);
        var candidate = SclModelSnapshotBuilder.Load(candidatePath);
        var report = new SclGoldenDiffReport
        {
            GoldenPath = Path.GetFullPath(goldenPath),
            CandidatePath = Path.GetFullPath(candidatePath),
            Golden = golden,
            Candidate = candidate,
            LogicalDevices = Diff("Logical devices", golden.LogicalDevices, candidate.LogicalDevices),
            LogicalNodes = Diff("Logical nodes", golden.LogicalNodes, candidate.LogicalNodes),
            DataSets = Diff("DataSets", golden.DataSets, candidate.DataSets),
            Reports = Diff("Reports", golden.ReportControls, candidate.ReportControls),
            GooseControls = Diff("GOOSE control blocks", golden.GooseControls, candidate.GooseControls),
            SampledValueControls = Diff("Sampled Value control blocks", golden.SampledValueControls, candidate.SampledValueControls),
            SettingControls = Diff("Setting controls", golden.SettingControls, candidate.SettingControls),
            LogControls = Diff("Log controls", golden.LogControls, candidate.LogControls),
            LNodeTypes = Diff("LNodeTypes", golden.LNodeTypes, candidate.LNodeTypes),
            DoTypes = Diff("DOTypes", golden.DoTypes, candidate.DoTypes),
            DaTypes = Diff("DATypes", golden.DaTypes, candidate.DaTypes),
            EnumTypes = Diff("EnumTypes", golden.EnumTypes, candidate.EnumTypes),
            CdcDifferences = BuildCdcDifferences(golden, candidate),
            ServiceCapabilityDifferences = BuildServiceDifferences(golden, candidate),
            TypeReuse = BuildTypeReuse(golden, candidate)
        };

        return report;
    }

    public static IReadOnlyList<string> WriteReport(string goldenPath, string candidatePath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        var report = Analyze(goldenPath, candidatePath);
        var jsonPath = Path.Combine(outputDirectory, "scl-golden-diff-report.json");
        var markdownPath = Path.Combine(outputDirectory, "scl-golden-diff-report.md");
        var missingServicesPath = Path.Combine(outputDirectory, "missing-services.json");
        var cdcDiffPath = Path.Combine(outputDirectory, "do-cdc-diff.json");
        var typeReusePath = Path.Combine(outputDirectory, "type-template-reuse.json");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
        File.WriteAllText(markdownPath, BuildMarkdown(report), Encoding.UTF8);
        File.WriteAllText(missingServicesPath, JsonSerializer.Serialize(BuildMissingServices(report), JsonOptions), Encoding.UTF8);
        File.WriteAllText(cdcDiffPath, JsonSerializer.Serialize(report.CdcDifferences, JsonOptions), Encoding.UTF8);
        File.WriteAllText(typeReusePath, JsonSerializer.Serialize(report.TypeReuse, JsonOptions), Encoding.UTF8);

        return new[] { jsonPath, markdownPath, missingServicesPath, cdcDiffPath, typeReusePath };
    }

    public static string BuildMarkdown(SclGoldenDiffReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine("# SCL Golden Reference Diff");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Golden: `{Escape(report.Golden.SourceName)}`");
        sb.AppendLine($"- Candidate: `{Escape(report.Candidate.SourceName)}`");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Area | Golden | Candidate | Missing | Extra |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        AppendSetSummary(sb, report.LogicalDevices);
        AppendSetSummary(sb, report.LogicalNodes);
        AppendSetSummary(sb, report.DataSets);
        AppendSetSummary(sb, report.Reports);
        AppendSetSummary(sb, report.GooseControls);
        AppendSetSummary(sb, report.SampledValueControls);
        AppendSetSummary(sb, report.SettingControls);
        AppendSetSummary(sb, report.LogControls);
        AppendSetSummary(sb, report.LNodeTypes);
        AppendSetSummary(sb, report.DoTypes);
        AppendSetSummary(sb, report.DaTypes);
        AppendSetSummary(sb, report.EnumTypes);
        sb.AppendLine();

        sb.AppendLine("## Service capability differences");
        sb.AppendLine();
        if (report.ServiceCapabilityDifferences.Count == 0)
        {
            sb.AppendLine("No Service capability difference was detected in the parsed SCL Services blocks.");
        }
        else
        {
            sb.AppendLine("| Service | Golden | Candidate |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var item in report.ServiceCapabilityDifferences.OrderBy(x => x.Service, StringComparer.OrdinalIgnoreCase).Take(80))
                sb.AppendLine($"| {Escape(item.Service)} | {Escape(item.GoldenValue)} | {Escape(item.CandidateValue)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## CDC differences");
        sb.AppendLine();
        if (report.CdcDifferences.Count == 0)
        {
            sb.AppendLine("No CDC difference was detected for shared LNClass.DO keys.");
        }
        else
        {
            sb.AppendLine("| LNClass.DO | Golden CDC | Candidate CDC | Golden DOType | Candidate DOType |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var diff in report.CdcDifferences.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Take(120))
            {
                sb.AppendLine($"| {Escape(diff.Key)} | {Escape(string.Join(", ", diff.GoldenCdc))} | {Escape(string.Join(", ", diff.CandidateCdc))} | {Escape(string.Join(", ", diff.GoldenDoTypeIds.Take(8)))} | {Escape(string.Join(", ", diff.CandidateDoTypeIds.Take(8)))} |");
            }
            if (report.CdcDifferences.Count > 120)
                sb.AppendLine($"| ... | ... | ... | ... | {report.CdcDifferences.Count - 120} more item(s) in do-cdc-diff.json |");
        }
        sb.AppendLine();

        sb.AppendLine("## Type template reuse");
        sb.AppendLine();
        sb.AppendLine("| Kind | Golden types | Golden shapes | Candidate types | Candidate shapes | Candidate duplicate shapes |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var reuse in report.TypeReuse)
        {
            sb.AppendLine($"| {Escape(reuse.Kind)} | {reuse.GoldenTypeCount} | {reuse.GoldenUniqueShapeCount} | {reuse.CandidateTypeCount} | {reuse.CandidateUniqueShapeCount} | {reuse.CandidateDuplicateShapeCount} |");
        }
        sb.AppendLine();

        AppendDetailSection(sb, report.LogicalDevices);
        AppendDetailSection(sb, report.DataSets);
        AppendDetailSection(sb, report.Reports);
        AppendDetailSection(sb, report.GooseControls);
        AppendDetailSection(sb, report.SampledValueControls);
        AppendDetailSection(sb, report.SettingControls);
        AppendDetailSection(sb, report.LogControls);

        sb.AppendLine("## Reading the report");
        sb.AppendLine();
        sb.AppendLine("- Missing/extra control blocks or datasets are structural gaps that should be prioritized before cosmetic SCL cleanup.");
        sb.AppendLine("- CDC differences show where ARIEC61850 reconstruction diverges from the golden engineering model.");
        sb.AppendLine("- High duplicate-shape counts indicate missing type-template deduplication; this is one reason generated SCL can be much larger than IEDScout export.");
        sb.AppendLine("- This report intentionally compares engineering SCL structure; it does not prove that every object is readable via MMS.");
        return sb.ToString();
    }

    private static SclDiffSet Diff(string kind, IReadOnlyList<string> golden, IReadOnlyList<string> candidate)
    {
        var goldenSet = new SortedSet<string>(golden.Where(NotBlank), StringComparer.OrdinalIgnoreCase);
        var candidateSet = new SortedSet<string>(candidate.Where(NotBlank), StringComparer.OrdinalIgnoreCase);
        return new SclDiffSet
        {
            Kind = kind,
            GoldenCount = goldenSet.Count,
            CandidateCount = candidateSet.Count,
            MissingInCandidate = goldenSet.Except(candidateSet, StringComparer.OrdinalIgnoreCase).Take(500).ToArray(),
            ExtraInCandidate = candidateSet.Except(goldenSet, StringComparer.OrdinalIgnoreCase).Take(500).ToArray()
        };
    }

    private static IReadOnlyList<SclCdcDifference> BuildCdcDifferences(SclModelSnapshot golden, SclModelSnapshot candidate)
    {
        var goldenMap = GroupCdc(golden.DoCdcBindings);
        var candidateMap = GroupCdc(candidate.DoCdcBindings);
        var sharedKeys = goldenMap.Keys.Intersect(candidateMap.Keys, StringComparer.OrdinalIgnoreCase);
        var differences = new List<SclCdcDifference>();

        foreach (var key in sharedKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var g = goldenMap[key];
            var c = candidateMap[key];
            if (SameSet(g.Cdcs, c.Cdcs))
                continue;

            differences.Add(new SclCdcDifference
            {
                Key = key,
                GoldenCdc = g.Cdcs,
                CandidateCdc = c.Cdcs,
                GoldenDoTypeIds = g.DoTypeIds,
                CandidateDoTypeIds = c.DoTypeIds
            });
        }

        return differences;
    }

    private static IReadOnlyList<SclServiceCapabilityDifference> BuildServiceDifferences(SclModelSnapshot golden, SclModelSnapshot candidate)
    {
        var keys = golden.ServiceCapabilities.Keys.Union(candidate.ServiceCapabilities.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var items = new List<SclServiceCapabilityDifference>();
        foreach (var key in keys)
        {
            golden.ServiceCapabilities.TryGetValue(key, out var goldenValue);
            candidate.ServiceCapabilities.TryGetValue(key, out var candidateValue);
            goldenValue ??= string.Empty;
            candidateValue ??= string.Empty;
            if (goldenValue.Equals(candidateValue, StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new SclServiceCapabilityDifference
            {
                Service = key,
                GoldenValue = string.IsNullOrWhiteSpace(goldenValue) ? "missing" : goldenValue,
                CandidateValue = string.IsNullOrWhiteSpace(candidateValue) ? "missing" : candidateValue
            });
        }

        return items;
    }

    private static IReadOnlyList<SclTypeReuseSummary> BuildTypeReuse(SclModelSnapshot golden, SclModelSnapshot candidate)
        => new[]
        {
            BuildTypeReuse("DOType", golden.DoTypeSignatures, candidate.DoTypeSignatures),
            BuildTypeReuse("DAType", golden.DaTypeSignatures, candidate.DaTypeSignatures)
        };

    private static SclTypeReuseSummary BuildTypeReuse(string kind, IReadOnlyList<SclTypeSignature> golden, IReadOnlyList<SclTypeSignature> candidate)
        => new()
        {
            Kind = kind,
            GoldenTypeCount = golden.Count,
            GoldenUniqueShapeCount = golden.Select(x => x.Signature).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            CandidateTypeCount = candidate.Count,
            CandidateUniqueShapeCount = candidate.Select(x => x.Signature).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        };

    private static object BuildMissingServices(SclGoldenDiffReport report)
        => new
        {
            GoldenSourceName = report.Golden.SourceName,
            CandidateSourceName = report.Candidate.SourceName,
            MissingDataSets = report.DataSets.MissingInCandidate,
            MissingReports = report.Reports.MissingInCandidate,
            MissingGooseControls = report.GooseControls.MissingInCandidate,
            MissingSampledValueControls = report.SampledValueControls.MissingInCandidate,
            MissingSettingControls = report.SettingControls.MissingInCandidate,
            MissingLogControls = report.LogControls.MissingInCandidate,
            ServiceCapabilityDifferences = report.ServiceCapabilityDifferences
        };

    private static (IReadOnlyList<string> Cdcs, IReadOnlyList<string> DoTypeIds) MapEntry(IEnumerable<SclDoCdcBinding> items)
    {
        var array = items.ToArray();
        return (
            array.Select(x => string.IsNullOrWhiteSpace(x.Cdc) ? "-" : x.Cdc).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            array.Select(x => x.DoTypeId).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static Dictionary<string, (IReadOnlyList<string> Cdcs, IReadOnlyList<string> DoTypeIds)> GroupCdc(IReadOnlyList<SclDoCdcBinding> bindings)
        => bindings
            .Where(x => NotBlank(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => MapEntry(x), StringComparer.OrdinalIgnoreCase);

    private static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.Count == b.Count && !a.Except(b, StringComparer.OrdinalIgnoreCase).Any();

    private static void AppendSetSummary(StringBuilder sb, SclDiffSet set)
        => sb.AppendLine($"| {Escape(set.Kind)} | {set.GoldenCount} | {set.CandidateCount} | {set.MissingInCandidate.Count} | {set.ExtraInCandidate.Count} |");

    private static void AppendDetailSection(StringBuilder sb, SclDiffSet set)
    {
        if (!set.HasDifferences)
            return;

        sb.AppendLine($"## {Escape(set.Kind)} details");
        sb.AppendLine();
        if (set.MissingInCandidate.Count > 0)
        {
            sb.AppendLine("Missing in candidate:");
            foreach (var item in set.MissingInCandidate.Take(80))
                sb.AppendLine($"- `{Escape(item)}`");
            if (set.MissingInCandidate.Count > 80)
                sb.AppendLine($"- ... {set.MissingInCandidate.Count - 80} more item(s)");
            sb.AppendLine();
        }

        if (set.ExtraInCandidate.Count > 0)
        {
            sb.AppendLine("Extra in candidate:");
            foreach (var item in set.ExtraInCandidate.Take(80))
                sb.AppendLine($"- `{Escape(item)}`");
            if (set.ExtraInCandidate.Count > 80)
                sb.AppendLine($"- ... {set.ExtraInCandidate.Count - 80} more item(s)");
            sb.AppendLine();
        }
    }

    private static bool NotBlank(string value)
        => !string.IsNullOrWhiteSpace(value);

    private static string Escape(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
