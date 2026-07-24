using System.Globalization;
using System.Text;

namespace AR.Iec61850.SampledValues;

/// <summary>
/// One diagnostic item recorded by a Sampled Values publisher preflight or runtime workflow.
/// </summary>
public sealed record SampledValuesEvidenceFinding(
    string Severity,
    string Area,
    string Message,
    string Detail);

/// <summary>
/// Evidence describing one configured Sampled Values publisher stream.
/// This records transmitter intent and local calculations; it does not prove remote subscription.
/// </summary>
public sealed record SampledValuesEvidenceStream(
    string SlotName,
    bool IsEnabled,
    string ControlBlockReference,
    string SvId,
    string DataSetReference,
    string AppId,
    string SourceMac,
    string DestinationMac,
    string Vlan,
    double SampleRateHz,
    double PublicationRateHz,
    ushort NoAsdu,
    int PayloadBytesPerAsdu,
    int EstimatedEthernetBytes,
    double EstimatedBandwidthBitsPerSecond,
    string SignalSource,
    string Quality,
    string SyncMode,
    string Status,
    IReadOnlyList<SampledValuesEvidenceFinding> Findings);

/// <summary>
/// Reusable transmitter-side evidence contract for a Sampled Values publisher application.
/// </summary>
public sealed record SampledValuesPublisherEvidenceReport(
    string ToolName,
    string ToolVersion,
    DateTimeOffset CreatedAt,
    string SclPath,
    string Adapter,
    string Mode,
    string TxTiming,
    string SafetyBoundary,
    IReadOnlyList<SampledValuesEvidenceStream> Streams,
    IReadOnlyList<SampledValuesEvidenceFinding> GlobalFindings);

public static class SampledValuesPublisherEvidenceReportWriter
{
    public static string ToMarkdown(SampledValuesPublisherEvidenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.Append("# ")
            .Append(EmptyAsDash(report.ToolName))
            .AppendLine(" Sampled Values Publisher Evidence Report");
        builder.AppendLine();
        builder.AppendLine("> TX-side publisher evidence only. This report records configured intent and local transmitter observations; it does not prove remote subscription, calibrated accuracy, or formal IEC 61850 conformance.");
        builder.AppendLine();
        AppendField(builder, "Tool", $"{report.ToolName} {report.ToolVersion}".Trim());
        AppendField(builder, "Created", report.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        AppendField(builder, "SCL", EmptyAsDash(report.SclPath));
        AppendField(builder, "Adapter", EmptyAsDash(report.Adapter));
        AppendField(builder, "Mode", EmptyAsDash(report.Mode));
        AppendField(builder, "TX timing", EmptyAsDash(report.TxTiming));
        AppendField(builder, "Boundary", EmptyAsDash(report.SafetyBoundary));
        builder.AppendLine();

        builder.AppendLine("## Streams");
        builder.AppendLine();
        if (report.Streams.Count == 0)
        {
            builder.AppendLine("No publisher streams were enabled.");
            builder.AppendLine();
        }
        else
        {
            foreach (var stream in report.Streams)
                AppendStream(builder, stream);
        }

        builder.AppendLine("## Global findings");
        builder.AppendLine();
        AppendFindings(builder, report.GlobalFindings);

        return builder.ToString();
    }

    private static void AppendStream(StringBuilder builder, SampledValuesEvidenceStream stream)
    {
        builder.Append("### ").AppendLine(EmptyAsDash(stream.SlotName));
        builder.AppendLine();
        AppendField(builder, "Enabled", stream.IsEnabled ? "yes" : "no");
        AppendField(builder, "Status", EmptyAsDash(stream.Status));
        AppendField(builder, "Control block", EmptyAsDash(stream.ControlBlockReference));
        AppendField(builder, "svID", EmptyAsDash(stream.SvId));
        AppendField(builder, "DataSet", EmptyAsDash(stream.DataSetReference));
        AppendField(builder, "APPID", EmptyAsDash(stream.AppId));
        AppendField(builder, "Source MAC", EmptyAsDash(stream.SourceMac));
        AppendField(builder, "Destination MAC", EmptyAsDash(stream.DestinationMac));
        AppendField(builder, "VLAN", EmptyAsDash(stream.Vlan));
        AppendField(builder, "Sample rate", $"{stream.SampleRateHz:0.###} sample/s");
        AppendField(builder, "Publication rate", $"{stream.PublicationRateHz:0.###} frame/s");
        AppendField(builder, "nofASDU", stream.NoAsdu.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "Payload", $"{stream.PayloadBytesPerAsdu} byte/ASDU");
        AppendField(builder, "Estimated Ethernet frame", stream.EstimatedEthernetBytes > 0 ? $"{stream.EstimatedEthernetBytes} byte" : "-");
        AppendField(builder, "Estimated bandwidth", stream.EstimatedBandwidthBitsPerSecond > 0
            ? $"{stream.EstimatedBandwidthBitsPerSecond / 1_000_000.0:0.###} Mbit/s"
            : "-");
        AppendField(builder, "Signal source", EmptyAsDash(stream.SignalSource));
        AppendField(builder, "Quality", EmptyAsDash(stream.Quality));
        AppendField(builder, "Synchronization", EmptyAsDash(stream.SyncMode));
        builder.AppendLine();
        builder.AppendLine("#### Findings");
        builder.AppendLine();
        AppendFindings(builder, stream.Findings);
    }

    private static void AppendFindings(
        StringBuilder builder,
        IReadOnlyList<SampledValuesEvidenceFinding> findings)
    {
        if (findings.Count == 0)
        {
            builder.AppendLine("- None.");
            builder.AppendLine();
            return;
        }

        foreach (var finding in findings)
        {
            builder.Append("- **")
                .Append(EscapeInline(EmptyAsDash(finding.Severity)))
                .Append("** · ")
                .Append(EscapeInline(EmptyAsDash(finding.Area)))
                .Append(" · ")
                .Append(EscapeInline(EmptyAsDash(finding.Message)));
            if (!string.IsNullOrWhiteSpace(finding.Detail))
                builder.Append(" — ").Append(EscapeInline(finding.Detail));
            builder.AppendLine();
        }
        builder.AppendLine();
    }

    private static void AppendField(StringBuilder builder, string label, string value)
        => builder.Append("- **").Append(label).Append(":** ").AppendLine(EscapeInline(value));

    private static string EmptyAsDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string EscapeInline(string value)
        => value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
