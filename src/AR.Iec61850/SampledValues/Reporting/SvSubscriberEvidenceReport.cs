using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.SampledValues.Reporting;

public sealed record SvSubscriberEvidenceReport
{
    public const string CurrentSchemaVersion = "arsvin.sv-subscriber-evidence/v1";
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset GeneratedAt { get; init; }
    public SvSubscriberSoftwareEvidence Software { get; init; } = new();
    public SvSubscriberCaptureEvidence Capture { get; init; } = new();
    public SvSubscriberSummaryEvidence Summary { get; init; } = new();
    public IReadOnlyList<SvSubscriberStreamEvidence> Streams { get; init; } = Array.Empty<SvSubscriberStreamEvidence>();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new InvalidOperationException($"Unsupported SV report schema '{SchemaVersion}'.");
        if (GeneratedAt == default) throw new InvalidOperationException("SV report requires a generation timestamp.");
        if (string.IsNullOrWhiteSpace(Software.Product)) throw new InvalidOperationException("SV report requires a product name.");
        if (Summary.StreamCount != Streams.Count) throw new InvalidOperationException("SV report summary stream count does not match the stream evidence collection.");
        if (Streams.Any(stream => string.IsNullOrWhiteSpace(stream.Key))) throw new InvalidOperationException("Every SV report stream requires a stable key.");
        if (Streams.Select(stream => stream.Key).Distinct(StringComparer.Ordinal).Count() != Streams.Count) throw new InvalidOperationException("SV report stream keys must be unique.");
    }
}

public sealed record SvSubscriberSoftwareEvidence { public string Product { get; init; } = string.Empty; public string Version { get; init; } = string.Empty; public string InformationalVersion { get; init; } = string.Empty; public string Commit { get; init; } = string.Empty; public string Repository { get; init; } = string.Empty; }
public sealed record SvSubscriberCaptureEvidence { public string Source { get; init; } = "Unknown"; public string SclPath { get; init; } = string.Empty; public string Adapter { get; init; } = string.Empty; public string Filter { get; init; } = string.Empty; public DateTimeOffset? StartedAt { get; init; } public DateTimeOffset EndedAt { get; init; } public double DurationSeconds { get; init; } public long RawFrames { get; init; } public long SvFrames { get; init; } public long ParseErrors { get; init; } public long DroppedByFilter { get; init; } }
public sealed record SvSubscriberSummaryEvidence { public string Health { get; init; } = "IDLE"; public int StreamCount { get; init; } public int RuntimeIssueCount { get; init; } public int ConfigurationFindingCount { get; init; } }

public sealed record SvSubscriberStreamEvidence
{
    public string Key { get; init; } = string.Empty;
    public string Health { get; init; } = "IDLE";
    public string HealthDetail { get; init; } = string.Empty;
    public SvSubscriberStreamIdentityEvidence Identity { get; init; } = new();
    public SvSubscriberRuntimeEvidence Runtime { get; init; } = new();
    public SvSubscriberObservationEvidence Observation { get; init; } = new();
    public IReadOnlyList<SvSubscriberPhasorEvidence> Phasors { get; init; } = Array.Empty<SvSubscriberPhasorEvidence>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed record SvSubscriberStreamIdentityEvidence
{
    public ushort AppId { get; init; }
    public string SourceMac { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public int AsduPerFrame { get; init; }
    public ushort? LastSampleCount { get; init; }
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public byte? SampleSynchronization { get; init; }
}

public sealed record SvSubscriberRuntimeEvidence
{
    public long FrameCount { get; init; }
    public long AsduCount { get; init; }
    public double ActualFramesPerSecond { get; init; }
    public double AverageFrameGapMilliseconds { get; init; }
    public double MaximumFrameGapMilliseconds { get; init; }
    public int SequenceGapCount { get; init; }
    public int DuplicateCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int PayloadIssueCount { get; init; }
    public int SclMismatchCount { get; init; }
    public bool IsWaveformWindowReady { get; init; }
    public string LayoutBinding { get; init; } = string.Empty;
    public string QualitySummary { get; init; } = string.Empty;
    public string CursorSummary { get; init; } = string.Empty;
    public string LastSeen { get; init; } = string.Empty;
}

public sealed record SvSubscriberObservationEvidence
{
    public IReadOnlyList<SvObservationInputKind> InputKinds { get; init; } = Array.Empty<SvObservationInputKind>();
    public SvObservationInputKind LastInputKind { get; init; }
    public bool IsBoundToScl { get; init; }
    public string ControlBlockReference { get; init; } = string.Empty;
    public int WindowFrames { get; init; }
    public int WindowSamples { get; init; }
    public double WindowDurationSeconds { get; init; }
    public DateTimeOffset? FirstTimestamp { get; init; }
    public DateTimeOffset? LastTimestamp { get; init; }
    public SvObservedStreamFacts Facts { get; init; } = new();
    public IReadOnlyDictionary<string, SvFactSource> FactProvenance { get; init; } = new Dictionary<string, SvFactSource>(StringComparer.Ordinal);
    public SvProfileDetectionResult? ProfileDetection { get; init; }
    public SvExpectedStreamConfiguration? ExpectedConfiguration { get; init; }
    public SvConfigurationComparisonResult? ConfigurationComparison { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed record SvSubscriberPhasorEvidence { public string Channel { get; init; } = string.Empty; public string Kind { get; init; } = string.Empty; public double Rms { get; init; } public double Peak { get; init; } public double AngleDegrees { get; init; } }

public static class SvSubscriberEvidenceReportSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ToJson(SvSubscriberEvidenceReport report) { ArgumentNullException.ThrowIfNull(report); report.Validate(); return JsonSerializer.Serialize(report, Options); }
    public static SvSubscriberEvidenceReport FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("SV report JSON cannot be empty.", nameof(json));
        var report = JsonSerializer.Deserialize<SvSubscriberEvidenceReport>(json, Options) ?? throw new InvalidDataException("SV report JSON did not contain a report document.");
        report.Validate(); return report;
    }

    public static string ToMarkdown(SvSubscriberEvidenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report); report.Validate();
        var b = new StringBuilder();
        b.AppendLine("# ARSVIN Subscriber Evidence Report").AppendLine();
        b.AppendLine("> Receiver-side engineering evidence. This document is not a formal IEC 61850 conformance certificate.").AppendLine();
        b.AppendLine("## Report metadata").AppendLine();
        Table(b, ("Schema", report.SchemaVersion), ("Generated", report.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)), ("Product", report.Software.Product), ("Version", report.Software.Version), ("Informational version", report.Software.InformationalVersion), ("Commit", report.Software.Commit), ("Repository", report.Software.Repository), ("Capture source", report.Capture.Source), ("SCL", Empty(report.Capture.SclPath, "not loaded")), ("Adapter", Empty(report.Capture.Adapter)), ("Filter", Empty(report.Capture.Filter, "none")));
        b.AppendLine("## Summary").AppendLine();
        Table(b, ("Health", report.Summary.Health), ("Raw frames", report.Capture.RawFrames.ToString("N0", CultureInfo.InvariantCulture)), ("SV frames", report.Capture.SvFrames.ToString("N0", CultureInfo.InvariantCulture)), ("Streams", report.Summary.StreamCount.ToString(CultureInfo.InvariantCulture)), ("Runtime issues", report.Summary.RuntimeIssueCount.ToString(CultureInfo.InvariantCulture)), ("Configuration findings", report.Summary.ConfigurationFindingCount.ToString(CultureInfo.InvariantCulture)));
        b.AppendLine("## Streams").AppendLine();
        foreach (var stream in report.Streams) AppendStream(b, stream);
        return b.ToString();
    }

    private static void AppendStream(StringBuilder b, SvSubscriberStreamEvidence stream)
    {
        b.Append("## 0x").Append(stream.Identity.AppId.ToString("X4", CultureInfo.InvariantCulture)).Append(" — ").AppendLine(Empty(stream.Identity.SvId)).AppendLine();
        Table(b, ("Stream key", stream.Key), ("Health", stream.Health), ("Health detail", stream.HealthDetail), ("Source MAC", stream.Identity.SourceMac), ("Destination MAC", stream.Identity.DestinationMac), ("Dataset", stream.Identity.DataSetReference), ("confRev", Value(stream.Identity.ConfigurationRevision)), ("nofASDU", stream.Identity.AsduPerFrame.ToString(CultureInfo.InvariantCulture)), ("SCL binding", stream.Observation.IsBoundToScl ? Empty(stream.Observation.ControlBlockReference) : "not bound"));
        b.AppendLine("### Observed facts and provenance").AppendLine();
        b.AppendLine("| Fact | Value | Source |").AppendLine("|---|---|---|");
        foreach (var item in stream.Observation.FactProvenance.OrderBy(item => item.Key, StringComparer.Ordinal))
            b.Append("| ").Append(Cell(item.Key)).Append(" | ").Append(Cell(FactValue(stream.Observation.Facts, item.Key))).Append(" | ").Append(Cell(item.Value.ToString())).AppendLine(" |");
        b.AppendLine();
        b.AppendLine("### Expected SCL configuration").AppendLine();
        if (stream.Observation.ExpectedConfiguration is { } expected) Table(b, ("APPID", Value(expected.AppId)), ("Destination MAC", expected.DestinationMac), ("svID", expected.SvId), ("Dataset", expected.DataSetReference), ("confRev", Value(expected.ConfigurationRevision)), ("ASDU/frame", Value(expected.AsduPerFrame)));
        else b.AppendLine("Not configured.").AppendLine();
        b.AppendLine("### Configuration comparison").AppendLine();
        var comparison = stream.Observation.ConfigurationComparison;
        if (comparison is null) b.AppendLine("Not configured.").AppendLine(); else { b.AppendLine(comparison.Summary).AppendLine(); foreach (var finding in comparison.Findings) b.Append("- **").Append(finding.Code).Append("** · ").Append(finding.Field).Append(" · ").AppendLine(finding.Message); b.AppendLine(); }
        b.AppendLine("### Profile detection evidence").AppendLine();
        var detection = stream.Observation.ProfileDetection;
        if (detection is null) b.AppendLine("Unknown.").AppendLine(); else { b.Append("- Profile: ").AppendLine(detection.Profile.DisplayName); b.Append("- Confidence: ").AppendLine(detection.Confidence.ToString()); foreach (var evidence in detection.Evidence) b.Append("- ").Append(evidence.Field).Append(" · ").Append(evidence.Outcome).Append(" · ").AppendLine(evidence.Message); b.AppendLine(); }
        b.AppendLine("### Phasors").AppendLine();
        if (stream.Phasors.Count == 0) b.AppendLine("None.").AppendLine(); else { b.AppendLine("| Channel | Kind | RMS | Peak | Angle |").AppendLine("|---|---|---:|---:|---:|"); foreach (var p in stream.Phasors) b.Append("| ").Append(Cell(p.Channel)).Append(" | ").Append(Cell(p.Kind)).Append(" | ").Append(p.Rms.ToString("0.###", CultureInfo.InvariantCulture)).Append(" | ").Append(p.Peak.ToString("0.###", CultureInfo.InvariantCulture)).Append(" | ").Append(p.AngleDegrees.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(" |"); b.AppendLine(); }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); return options;
    }
    private static void Table(StringBuilder b, params (string Key, string Value)[] rows) { b.AppendLine("| Field | Value |").AppendLine("|---|---|"); foreach (var row in rows) b.Append("| ").Append(Cell(row.Key)).Append(" | ").Append(Cell(row.Value)).AppendLine(" |"); b.AppendLine(); }
    private static string Empty(string? value, string fallback = "-") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string Value<T>(T? value) where T : struct => value?.ToString() ?? "-";
    private static string Cell(string? value) => Empty(value).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    private static string FactValue(SvObservedStreamFacts facts, string name) => name switch
    {
        nameof(SvObservedStreamFacts.AppId) => Value(facts.AppId), nameof(SvObservedStreamFacts.EtherType) => Value(facts.EtherType), nameof(SvObservedStreamFacts.DestinationMac) => facts.DestinationMac,
        nameof(SvObservedStreamFacts.VlanId) => Value(facts.VlanId), nameof(SvObservedStreamFacts.VlanPriority) => Value(facts.VlanPriority), nameof(SvObservedStreamFacts.SvId) => facts.SvId,
        nameof(SvObservedStreamFacts.DataSetReference) => facts.DataSetReference, nameof(SvObservedStreamFacts.ConfigurationRevision) => Value(facts.ConfigurationRevision),
        nameof(SvObservedStreamFacts.AsduPerFrame) => Value(facts.AsduPerFrame), nameof(SvObservedStreamFacts.PayloadBytesPerAsdu) => Value(facts.PayloadBytesPerAsdu),
        nameof(SvObservedStreamFacts.ObservedFramesPerSecond) => facts.ObservedFramesPerSecond?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
        nameof(SvObservedStreamFacts.ObservedSamplesPerSecond) => facts.ObservedSamplesPerSecond?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
        nameof(SvObservedStreamFacts.ObservedCounterWrap) => Value(facts.ObservedCounterWrap), nameof(SvObservedStreamFacts.DeclaredSampleRate) => Value(facts.DeclaredSampleRate),
        nameof(SvObservedStreamFacts.DeclaredSampleMode) => Value(facts.DeclaredSampleMode), nameof(SvObservedStreamFacts.NominalFrequencyHz) => facts.NominalFrequencyHz?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
        nameof(SvObservedStreamFacts.DataSetSignature) => string.Join(", ", facts.DataSetSignature.Select(item => item.NormalizedBType)), _ => "-"
    };
}
