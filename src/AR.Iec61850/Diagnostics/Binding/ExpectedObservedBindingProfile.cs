using System.Text;
using AR.Iec61850.Monitoring;

namespace AR.Iec61850.Diagnostics.Binding;

public sealed class ExpectedObservedBindingProfile
{
    public string SourceName { get; init; } = string.Empty;
    public int ExpectedGooseCount { get; init; }
    public int ObservedGooseCount { get; init; }
    public int BoundGooseCount { get; init; }
    public int ExpectedSampledValuesCount { get; init; }
    public int ObservedSampledValuesCount { get; init; }
    public int BoundSampledValuesCount { get; init; }
    public IReadOnlyList<ExpectedObservedGooseBinding> Goose { get; init; } = Array.Empty<ExpectedObservedGooseBinding>();
    public IReadOnlyList<ExpectedObservedSampledValuesBinding> SampledValues { get; init; } = Array.Empty<ExpectedObservedSampledValuesBinding>();
    public IReadOnlyList<UnexpectedObservedProcessBusStream> UnexpectedObservedStreams { get; init; } = Array.Empty<UnexpectedObservedProcessBusStream>();
    public IReadOnlyList<ExpectedObservedFinding> Findings { get; init; } = Array.Empty<ExpectedObservedFinding>();

    public bool IsReady => Findings.All(f => !string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase));
    public int MissingExpectedCount => Findings.Count(f => f.Code.EndsWith("_MISSING", StringComparison.OrdinalIgnoreCase));
    public int UnexpectedObservedCount => UnexpectedObservedStreams.Count;
    public int MismatchCount => Findings.Count(f => f.Code.Contains("MISMATCH", StringComparison.OrdinalIgnoreCase));
    public int SequenceAnomalyCount => Findings.Count(f => f.Code.Contains("SEQUENCE", StringComparison.OrdinalIgnoreCase) || f.Code.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase));

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Expected vs Observed Process-Bus Binding");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{Dash(SourceName)}`");
        sb.AppendLine($"- Ready: {(IsReady ? "yes" : "no")}");
        sb.AppendLine($"- Expected GOOSE: {ExpectedGooseCount}");
        sb.AppendLine($"- Observed GOOSE: {ObservedGooseCount}");
        sb.AppendLine($"- Bound GOOSE: {BoundGooseCount}");
        sb.AppendLine($"- Expected SV: {ExpectedSampledValuesCount}");
        sb.AppendLine($"- Observed SV: {ObservedSampledValuesCount}");
        sb.AppendLine($"- Bound SV: {BoundSampledValuesCount}");
        sb.AppendLine($"- Missing expected streams: {MissingExpectedCount}");
        sb.AppendLine($"- Unexpected observed streams: {UnexpectedObservedCount}");
        sb.AppendLine($"- Mismatches: {MismatchCount}");
        sb.AppendLine($"- Sequence anomalies: {SequenceAnomalyCount}");
        sb.AppendLine();

        sb.AppendLine("## GOOSE Binding");
        sb.AppendLine();
        sb.AppendLine("| Expected control block | Match | APPID | MAC | VLAN | ConfRev | Packets | Findings |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|");
        foreach (var binding in Goose)
        {
            sb.AppendLine($"| `{Dash(binding.ExpectedControlBlockReference)}` | {binding.MatchKind} | {AppId(binding.ExpectedAppId, binding.ObservedAppId)} | `{Pair(binding.ExpectedDestinationMac, binding.ObservedDestinationMac)}` | {Pair(binding.ExpectedVlanId, binding.ObservedVlanId)} | {Pair(binding.ExpectedConfigurationRevision, binding.ObservedConfigurationRevision)} | {binding.ObservedPacketCount} | {binding.Findings.Count} |");
        }
        if (Goose.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Sampled Values Binding");
        sb.AppendLine();
        sb.AppendLine("| Expected control block | Match | APPID | MAC | VLAN | ConfRev | Packets | Findings |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|");
        foreach (var binding in SampledValues)
        {
            sb.AppendLine($"| `{Dash(binding.ExpectedControlBlockReference)}` | {binding.MatchKind} | {AppId(binding.ExpectedAppId, binding.ObservedAppId)} | `{Pair(binding.ExpectedDestinationMac, binding.ObservedDestinationMac)}` | {Pair(binding.ExpectedVlanId, binding.ObservedVlanId)} | {Pair(binding.ExpectedConfigurationRevision, binding.ObservedConfigurationRevision)} | {binding.ObservedPacketCount} | {binding.Findings.Count} |");
        }
        if (SampledValues.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Unexpected Observed Streams");
        sb.AppendLine();
        sb.AppendLine("| Kind | Stream | APPID | Destination | VLAN | ConfRev | Packets |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|");
        foreach (var stream in UnexpectedObservedStreams)
        {
            sb.AppendLine($"| {stream.Kind} | `{Dash(stream.StreamId)}` | {AppId(stream.AppId)} | `{Dash(stream.DestinationMac)}` | {Dash(stream.VlanId?.ToString())} | {Dash(stream.ConfigurationRevision?.ToString())} | {stream.PacketCount} |");
        }
        if (UnexpectedObservedStreams.Count == 0)
            sb.AppendLine("| - | - | - | - | - | - | - |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Object | Message |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var finding in Findings)
            sb.AppendLine($"| {finding.Severity} | `{finding.Code}` | `{Dash(finding.ObjectReference)}` | {EscapeMarkdown(finding.Message)} |");
        if (Findings.Count == 0)
            sb.AppendLine("| Info | `PB_BINDING_OK` | - | All expected observed process-bus streams matched the SCL engineering profile. |");

        return sb.ToString();
    }

    private static string Dash(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    private static string EscapeMarkdown(string text) => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);
    private static string AppId(ushort? appId) => appId.HasValue ? $"0x{appId.Value:X4}" : "-";
    private static string AppId(ushort? expected, ushort? observed) => expected == observed ? AppId(expected) : $"{AppId(expected)} / {AppId(observed)}";
    private static string Pair(string? expected, string? observed)
        => string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase) ? Dash(expected) : $"{Dash(expected)} / {Dash(observed)}";

    private static string Pair(ushort? expected, ushort? observed)
        => expected == observed ? Dash(expected?.ToString()) : $"{Dash(expected?.ToString())} / {Dash(observed?.ToString())}";

    private static string Pair(uint? expected, uint? observed)
        => expected == observed ? Dash(expected?.ToString()) : $"{Dash(expected?.ToString())} / {Dash(observed?.ToString())}";
}

public abstract class ExpectedObservedProcessBusBinding
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

    public ProcessBusBindingMatchKind MatchKind { get; init; } = ProcessBusBindingMatchKind.Missing;
    public string ObservedStreamId { get; init; } = string.Empty;
    public ushort? ObservedAppId { get; init; }
    public string ObservedDestinationMac { get; init; } = string.Empty;
    public ushort? ObservedVlanId { get; init; }
    public byte? ObservedVlanPriority { get; init; }
    public uint? ObservedConfigurationRevision { get; init; }
    public int ObservedPacketCount { get; init; }
    public int ObservedDecodedValueCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int DuplicateCount { get; init; }
    public int RegressionCount { get; init; }
    public int TimeoutCount { get; init; }
    public IReadOnlyList<ExpectedObservedFinding> Findings { get; init; } = Array.Empty<ExpectedObservedFinding>();
}

public sealed class ExpectedObservedGooseBinding : ExpectedObservedProcessBusBinding
{
    public uint? LastStateNumber { get; init; }
    public uint? LastSequenceNumber { get; init; }
    public uint? LastTimeAllowedToLiveMilliseconds { get; init; }
    public int StateChangeCount { get; init; }
    public int RetransmissionCount { get; init; }
    public int ValueChangeCount { get; init; }
}

public sealed class ExpectedObservedSampledValuesBinding : ExpectedObservedProcessBusBinding
{
    public ushort? FirstSampleCount { get; init; }
    public ushort? LastSampleCount { get; init; }
    public int MissedSampleCount { get; init; }
    public int OutOfOrderSampleCount { get; init; }
    public int WrapCount { get; init; }
}

public sealed class UnexpectedObservedProcessBusStream
{
    public ProcessBusEventKind Kind { get; init; }
    public string StreamId { get; init; } = string.Empty;
    public ushort? AppId { get; init; }
    public string SourceMac { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public uint? ConfigurationRevision { get; init; }
    public int PacketCount { get; init; }
}

public sealed class ExpectedObservedFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public enum ProcessBusBindingMatchKind
{
    Missing,
    Exact,
    Partial,
    Unexpected
}
