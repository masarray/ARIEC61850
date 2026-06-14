using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Engineering;

public sealed class Iec61850EngineeringProfileOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool ProbeReportAttributes { get; init; } = true;
    public int MaxReportAttributeProbes { get; init; } = 32;
    public bool ReadDataSetDirectories { get; init; } = true;
    public int MaxDataSetDirectories { get; init; } = 32;
}

public sealed class Iec61850EngineeringProfile
{
    public string SchemaVersion { get; init; } = "ariec61850-engineering-profile-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string DiscoverySummary { get; init; } = string.Empty;
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int PointCount { get; init; }
    public int ControlAttributeCount { get; init; }
    public int ReportAttributeCount { get; init; }
    public int DataSetCount { get; init; }
    public int DataSetDirectorySuccessCount { get; init; }
    public int DataSetMemberCount { get; init; }
    public int ReportControlCount { get; init; }
    public int BufferedReportControlCount { get; init; }
    public int UnbufferedReportControlCount { get; init; }
    public int SafeReportCandidateCount { get; init; }
    public IReadOnlyDictionary<string, int> FunctionalConstraintCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public MmsReportReadinessPlan ReportReadiness { get; init; } = new();
    public IReadOnlyList<Iec61850CapabilityAssessment> Capabilities { get; init; } = Array.Empty<Iec61850CapabilityAssessment>();
    public IReadOnlyList<Iec61850DiagnosticMessage> Diagnostics { get; init; } = Array.Empty<Iec61850DiagnosticMessage>();

    public bool HasUsableModel => LogicalDeviceCount > 0 && LogicalNodeCount > 0 && PointCount > 0;
    public bool HasReportPathCandidate => SafeReportCandidateCount > 0;
    public bool HasDataSetEvidence => DataSetCount > 0 || DataSetDirectorySuccessCount > 0;
    public bool IsReportLabReady => HasUsableModel && HasDataSetEvidence && HasReportPathCandidate;

    public string Summary =>
        $"Engineering profile: LD={LogicalDeviceCount}, LN={LogicalNodeCount}, points={PointCount}, DataSets={DataSetCount}, " +
        $"DataSetDirsOK={DataSetDirectorySuccessCount}, RCB={ReportControlCount} (safe={SafeReportCandidateCount}), reportLabReady={(IsReportLabReady ? "true" : "false")}.";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ARIEC61850 Engineering Profile");
        sb.AppendLine();
        sb.AppendLine($"Generated UTC: `{GeneratedAtUtc:O}`");
        sb.AppendLine($"Endpoint: `{Host}:{Port}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- {Summary}");
        if (!string.IsNullOrWhiteSpace(DiscoverySummary))
            sb.AppendLine($"- Discovery: {DiscoverySummary}");
        sb.AppendLine();
        sb.AppendLine("## Capabilities");
        sb.AppendLine();
        sb.AppendLine("| Area | Status | Evidence | Next action |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var capability in Capabilities)
            sb.AppendLine($"| {Escape(capability.Area)} | {capability.Status} | {Escape(capability.Evidence)} | {Escape(capability.NextAction)} |");
        sb.AppendLine();
        sb.AppendLine("## Diagnostics");
        sb.AppendLine();
        if (Diagnostics.Count == 0)
        {
            sb.AppendLine("No diagnostic findings were generated.");
        }
        else
        {
            sb.AppendLine("| Severity | Code | Reference | Message | Recommendation |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var diagnostic in Diagnostics)
                sb.AppendLine($"| {diagnostic.Severity} | {Escape(diagnostic.Code)} | {Escape(diagnostic.Reference)} | {Escape(diagnostic.Message)} | {Escape(diagnostic.Recommendation)} |");
        }

        return sb.ToString();
    }

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
