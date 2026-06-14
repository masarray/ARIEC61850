using System.Text;
using AR.Iec61850.Monitoring;

namespace AR.Iec61850.Diagnostics.Goose;

public sealed class GooseDiagnosticsProfile
{
    public string SourceName { get; init; } = string.Empty;
    public int ExpectedStreamCount { get; init; }
    public int ObservedStreamCount { get; init; }
    public int BoundStreamCount { get; init; }
    public int HealthyStreamCount { get; init; }
    public int WarningCount => Findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
    public int HighCount => Findings.Count(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase));
    public int SequenceAnomalyCount => Findings.Count(f => f.Code.Contains("SEQUENCE", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("STATE_REGRESSION", StringComparison.OrdinalIgnoreCase));
    public int SupervisionIssueCount => Findings.Count(f => f.Code.Contains("SUPERVISION", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("TAL", StringComparison.OrdinalIgnoreCase));
    public bool IsHealthy => HighCount == 0;
    public IReadOnlyList<GooseDiagnosticsStream> Streams { get; init; } = Array.Empty<GooseDiagnosticsStream>();
    public IReadOnlyList<GooseDiagnosticsFinding> Findings { get; init; } = Array.Empty<GooseDiagnosticsFinding>();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GOOSE Diagnostics Profile");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{Dash(SourceName)}`");
        sb.AppendLine($"- Healthy: {(IsHealthy ? "yes" : "no")}");
        sb.AppendLine($"- Expected streams: {ExpectedStreamCount}");
        sb.AppendLine($"- Observed streams: {ObservedStreamCount}");
        sb.AppendLine($"- Bound streams: {BoundStreamCount}");
        sb.AppendLine($"- Healthy streams: {HealthyStreamCount}");
        sb.AppendLine($"- High findings: {HighCount}");
        sb.AppendLine($"- Warning findings: {WarningCount}");
        sb.AppendLine($"- Sequence anomalies: {SequenceAnomalyCount}");
        sb.AppendLine($"- Supervision issues: {SupervisionIssueCount}");
        sb.AppendLine();

        sb.AppendLine("## Stream Matrix");
        sb.AppendLine();
        sb.AppendLine("| Expected control block | Status | APPID | Destination | VLAN | ConfRev | Packets | stNum | sqNum | TAL ms | Gaps | Duplicates | Regressions | Timeouts | Score | Findings |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var stream in Streams)
        {
            sb.AppendLine($"| `{Dash(stream.ExpectedControlBlockReference)}` | {stream.Status} | {AppId(stream.ExpectedAppId, stream.ObservedAppId)} | `{Pair(stream.ExpectedDestinationMac, stream.ObservedDestinationMac)}` | {Pair(stream.ExpectedVlanId, stream.ObservedVlanId)} | {Pair(stream.ExpectedConfigurationRevision, stream.ObservedConfigurationRevision)} | {stream.ObservedPacketCount} | {Dash(stream.LastStateNumber?.ToString())} | {Dash(stream.LastSequenceNumber?.ToString())} | {Dash(stream.LastTimeAllowedToLiveMilliseconds?.ToString())} | {stream.SequenceGapCount} | {stream.DuplicateCount} | {stream.RegressionCount} | {stream.TimeoutCount} | {stream.HealthScore} | {stream.Findings.Count} |");
        }
        if (Streams.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - | - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Object | Message | Recommendation |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var finding in Findings)
            sb.AppendLine($"| {finding.Severity} | `{finding.Code}` | `{Dash(finding.ObjectReference)}` | {Escape(finding.Message)} | {Escape(finding.Recommendation)} |");
        if (Findings.Count == 0)
            sb.AppendLine("| Info | `GOOSE_DIAGNOSTICS_OK` | - | All expected GOOSE streams were observed without critical diagnostics. | Keep this PCAP/evidence as the healthy baseline. |");

        return sb.ToString();
    }

    private static string Dash(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    private static string Escape(string? text) => Dash(text).Replace("|", "\\|", StringComparison.Ordinal);
    private static string AppId(ushort? appId) => appId.HasValue ? $"0x{appId.Value:X4}" : "-";
    private static string AppId(ushort? expected, ushort? observed) => expected == observed ? AppId(expected) : $"{AppId(expected)} / {AppId(observed)}";
    private static string Pair(string? expected, string? observed)
        => string.Equals(NormalizeMac(expected), NormalizeMac(observed), StringComparison.OrdinalIgnoreCase) ? Dash(expected) : $"{Dash(expected)} / {Dash(observed)}";
    private static string Pair(ushort? expected, ushort? observed) => expected == observed ? Dash(expected?.ToString()) : $"{Dash(expected?.ToString())} / {Dash(observed?.ToString())}";
    private static string Pair(uint? expected, uint? observed) => expected == observed ? Dash(expected?.ToString()) : $"{Dash(expected?.ToString())} / {Dash(observed?.ToString())}";
    private static string NormalizeMac(string? value) => (value ?? string.Empty).Trim().Replace('-', ':').ToUpperInvariant();
}

public sealed class GooseDiagnosticsStream
{
    public string ExpectedControlBlockReference { get; init; } = string.Empty;
    public string ExpectedDataSetReference { get; init; } = string.Empty;
    public ushort? ExpectedAppId { get; init; }
    public string ExpectedDestinationMac { get; init; } = string.Empty;
    public ushort? ExpectedVlanId { get; init; }
    public byte? ExpectedVlanPriority { get; init; }
    public uint? ExpectedConfigurationRevision { get; init; }
    public int ExpectedDataSetMemberCount { get; init; }
    public GooseDiagnosticsStreamStatus Status { get; init; }
    public string ObservedStreamId { get; init; } = string.Empty;
    public ushort? ObservedAppId { get; init; }
    public string ObservedSourceMac { get; init; } = string.Empty;
    public string ObservedDestinationMac { get; init; } = string.Empty;
    public ushort? ObservedVlanId { get; init; }
    public byte? ObservedVlanPriority { get; init; }
    public uint? ObservedConfigurationRevision { get; init; }
    public int ObservedPacketCount { get; init; }
    public int ObservedDecodedValueCount { get; init; }
    public uint? LastStateNumber { get; init; }
    public uint? LastSequenceNumber { get; init; }
    public uint? LastTimeAllowedToLiveMilliseconds { get; init; }
    public double? MaxArrivalGapMilliseconds { get; init; }
    public int StateChangeCount { get; init; }
    public int RetransmissionCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int DuplicateCount { get; init; }
    public int RegressionCount { get; init; }
    public int TimeoutCount { get; init; }
    public int ValueChangeCount { get; init; }
    public string LastChangedSummary { get; init; } = string.Empty;
    public int HealthScore { get; init; }
    public IReadOnlyList<string> LastDiagnostics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<GooseDiagnosticsFinding> Findings { get; init; } = Array.Empty<GooseDiagnosticsFinding>();
}

public sealed class GooseDiagnosticsFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public enum GooseDiagnosticsStreamStatus
{
    Missing,
    Healthy,
    Warning,
    Critical,
    Unexpected
}
