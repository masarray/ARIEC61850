using System.Text;

namespace AR.Iec61850.Diagnostics.SampledValues;

public sealed class SampledValuesDiagnosticsProfile
{
    public string SourceName { get; init; } = string.Empty;
    public int ExpectedStreamCount { get; init; }
    public int ObservedStreamCount { get; init; }
    public int BoundStreamCount { get; init; }
    public int HealthyStreamCount { get; init; }
    public int HighCount => Findings.Count(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase));
    public int WarningCount => Findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
    public int SequenceAnomalyCount => Findings.Count(f => f.Code.Contains("SAMPLE", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("COUNTER", StringComparison.OrdinalIgnoreCase));
    public int PayloadIssueCount => Findings.Count(f => f.Code.Contains("PAYLOAD", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("DATASET", StringComparison.OrdinalIgnoreCase));
    public int SynchronizationIssueCount => Findings.Count(f => f.Code.Contains("SYNCH", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("SYNC", StringComparison.OrdinalIgnoreCase));
    public bool IsHealthy => HighCount == 0;
    public IReadOnlyList<SampledValuesDiagnosticsStream> Streams { get; init; } = Array.Empty<SampledValuesDiagnosticsStream>();
    public IReadOnlyList<SampledValuesDiagnosticsFinding> Findings { get; init; } = Array.Empty<SampledValuesDiagnosticsFinding>();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Sampled Values Diagnostics Profile");
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
        sb.AppendLine($"- Payload issues: {PayloadIssueCount}");
        sb.AppendLine($"- Synchronization issues: {SynchronizationIssueCount}");
        sb.AppendLine();

        sb.AppendLine("## Stream Matrix");
        sb.AppendLine();
        sb.AppendLine("| Expected control block | Status | APPID | Destination | VLAN | ConfRev | svID | Packets | smpCnt | Gaps | Missed | Duplicates | Out-of-order | Wraps | Payload bytes | smpSynch | Score | Findings |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var stream in Streams)
        {
            sb.AppendLine($"| `{Dash(stream.ExpectedControlBlockReference)}` | {stream.Status} | {AppId(stream.ExpectedAppId, stream.ObservedAppId)} | `{Pair(stream.ExpectedDestinationMac, stream.ObservedDestinationMac)}` | {Pair(stream.ExpectedVlanId, stream.ObservedVlanId)} | {Pair(stream.ExpectedConfigurationRevision, stream.ObservedConfigurationRevision)} | `{Dash(stream.ObservedStreamId)}` | {stream.ObservedPacketCount} | {CounterRange(stream.FirstSampleCount, stream.LastSampleCount)} | {stream.SequenceGapCount} | {stream.MissedSampleCount} | {stream.DuplicateSampleCount} | {stream.OutOfOrderSampleCount} | {stream.WrapCount} | {stream.ObservedPayloadBytes} | {Dash(stream.LastSampleSynchronization?.ToString())} | {stream.HealthScore} | {stream.Findings.Count} |");
        }
        if (Streams.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - | - | - | - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Object | Message | Recommendation |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var finding in Findings)
            sb.AppendLine($"| {finding.Severity} | `{finding.Code}` | `{Dash(finding.ObjectReference)}` | {Escape(finding.Message)} | {Escape(finding.Recommendation)} |");
        if (Findings.Count == 0)
            sb.AppendLine("| Info | `SV_DIAGNOSTICS_OK` | - | All expected Sampled Values streams were observed without critical diagnostics. | Keep this PCAP/evidence as the healthy baseline. |");

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
    private static string CounterRange(ushort? first, ushort? last) => first.HasValue || last.HasValue ? $"{Dash(first?.ToString())}..{Dash(last?.ToString())}" : "-";
    private static string NormalizeMac(string? value) => (value ?? string.Empty).Trim().Replace('-', ':').ToUpperInvariant();
}

public sealed class SampledValuesDiagnosticsStream
{
    public string ExpectedControlBlockReference { get; init; } = string.Empty;
    public string ExpectedDataSetReference { get; init; } = string.Empty;
    public string ExpectedStreamId { get; init; } = string.Empty;
    public ushort? ExpectedAppId { get; init; }
    public string ExpectedDestinationMac { get; init; } = string.Empty;
    public ushort? ExpectedVlanId { get; init; }
    public byte? ExpectedVlanPriority { get; init; }
    public uint? ExpectedConfigurationRevision { get; init; }
    public int ExpectedDataSetMemberCount { get; init; }
    public ushort? ExpectedSampleRate { get; init; }
    public ushort? ExpectedSampleMode { get; init; }
    public ushort ExpectedNoAsdu { get; init; }
    public int ExpectedPayloadBytes { get; init; }
    public SampledValuesDiagnosticsStreamStatus Status { get; init; }
    public string ObservedStreamId { get; init; } = string.Empty;
    public ushort? ObservedAppId { get; init; }
    public string ObservedSourceMac { get; init; } = string.Empty;
    public string ObservedDestinationMac { get; init; } = string.Empty;
    public ushort? ObservedVlanId { get; init; }
    public byte? ObservedVlanPriority { get; init; }
    public uint? ObservedConfigurationRevision { get; init; }
    public int ObservedPacketCount { get; init; }
    public int ObservedDecodedValueCount { get; init; }
    public ushort? FirstSampleCount { get; init; }
    public ushort? LastSampleCount { get; init; }
    public ushort? LastSampleRate { get; init; }
    public ushort? LastSampleMode { get; init; }
    public byte? LastSampleSynchronization { get; init; }
    public int LastAsduCount { get; init; }
    public int ObservedPayloadBytes { get; init; }
    public int PayloadLengthChangeCount { get; init; }
    public int SampleSynchronizationIssueCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int MissedSampleCount { get; init; }
    public int DuplicateSampleCount { get; init; }
    public int OutOfOrderSampleCount { get; init; }
    public int WrapCount { get; init; }
    public int HealthScore { get; init; }
    public IReadOnlyList<string> LastDiagnostics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SampledValuesDiagnosticsFinding> Findings { get; init; } = Array.Empty<SampledValuesDiagnosticsFinding>();
}

public sealed class SampledValuesDiagnosticsFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public enum SampledValuesDiagnosticsStreamStatus
{
    Missing,
    Healthy,
    Warning,
    Critical,
    Unexpected
}
