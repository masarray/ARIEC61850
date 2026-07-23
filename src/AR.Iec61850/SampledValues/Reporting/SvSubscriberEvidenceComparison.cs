using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AR.Iec61850.SampledValues.Reporting;

public enum SvEvidenceChangeKind { Added, Removed, Changed, Unchanged }
public enum SvEvidenceChangeSeverity { Info, Warning, Error }

public sealed record SvSubscriberEvidenceComparison
{
    public const string CurrentSchemaVersion = "arsvin.sv-subscriber-evidence-comparison/v1";
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset GeneratedAt { get; init; }
    public SvEvidenceReportReference Baseline { get; init; } = new();
    public SvEvidenceReportReference Candidate { get; init; } = new();
    public SvEvidenceComparisonSummary Summary { get; init; } = new();
    public IReadOnlyList<SvEvidenceFieldChange> ReportChanges { get; init; } = Array.Empty<SvEvidenceFieldChange>();
    public IReadOnlyList<SvSubscriberStreamComparison> Streams { get; init; } = Array.Empty<SvSubscriberStreamComparison>();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new InvalidOperationException($"Unsupported SV comparison schema '{SchemaVersion}'.");
        if (GeneratedAt == default) throw new InvalidOperationException("SV comparison requires a generation timestamp.");
        if (string.IsNullOrWhiteSpace(Baseline.SchemaVersion) || string.IsNullOrWhiteSpace(Candidate.SchemaVersion)) throw new InvalidOperationException("SV comparison requires baseline and candidate schema metadata.");
        if (Streams.Any(stream => string.IsNullOrWhiteSpace(stream.ComparisonKey))) throw new InvalidOperationException("Every stream comparison requires a stable comparison key.");
        if (Streams.Select(stream => stream.ComparisonKey).Distinct(StringComparer.Ordinal).Count() != Streams.Count) throw new InvalidOperationException("SV comparison keys must be unique.");
        var classified = Summary.AddedStreamCount + Summary.RemovedStreamCount + Summary.ChangedStreamCount + Summary.UnchangedStreamCount;
        if (classified != Streams.Count) throw new InvalidOperationException("SV comparison summary does not match the stream collection.");
    }
}

public sealed record SvEvidenceReportReference { public string SchemaVersion { get; init; } = string.Empty; public DateTimeOffset GeneratedAt { get; init; } public string Product { get; init; } = string.Empty; public string Version { get; init; } = string.Empty; public string Commit { get; init; } = string.Empty; public string CaptureSource { get; init; } = string.Empty; public string Health { get; init; } = string.Empty; public int StreamCount { get; init; } }
public sealed record SvEvidenceComparisonSummary { public int BaselineStreamCount { get; init; } public int CandidateStreamCount { get; init; } public int AddedStreamCount { get; init; } public int RemovedStreamCount { get; init; } public int ChangedStreamCount { get; init; } public int UnchangedStreamCount { get; init; } public int InfoChangeCount { get; init; } public int WarningChangeCount { get; init; } public int ErrorChangeCount { get; init; } public bool HasRegressions => WarningChangeCount > 0 || ErrorChangeCount > 0; }
public sealed record SvSubscriberStreamComparison { public string ComparisonKey { get; init; } = string.Empty; public string LogicalStreamKey { get; init; } = string.Empty; public SvEvidenceChangeKind Kind { get; init; } public SvEvidenceChangeSeverity Severity { get; init; } public string BaselineStreamKey { get; init; } = string.Empty; public string CandidateStreamKey { get; init; } = string.Empty; public SvSubscriberStreamIdentityEvidence Identity { get; init; } = new(); public IReadOnlyList<SvEvidenceFieldChange> Changes { get; init; } = Array.Empty<SvEvidenceFieldChange>(); }
public sealed record SvEvidenceFieldChange { public string Category { get; init; } = string.Empty; public string Field { get; init; } = string.Empty; public SvEvidenceChangeSeverity Severity { get; init; } public string Baseline { get; init; } = string.Empty; public string Candidate { get; init; } = string.Empty; public string Message { get; init; } = string.Empty; }

public sealed class SvSubscriberEvidenceComparator
{
    private const double RateTolerancePercent = 1.0;

    public SvSubscriberEvidenceComparison Compare(SvSubscriberEvidenceReport baseline, SvSubscriberEvidenceReport candidate, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(baseline); ArgumentNullException.ThrowIfNull(candidate); baseline.Validate(); candidate.Validate();
        if (generatedAt == default) throw new ArgumentException("Comparison requires a generation timestamp.", nameof(generatedAt));
        var reportChanges = new List<SvEvidenceFieldChange>();
        Text(reportChanges, "Software", "Version", baseline.Software.Version, candidate.Software.Version, SvEvidenceChangeSeverity.Info, "Software version changed.");
        Text(reportChanges, "Software", "Commit", baseline.Software.Commit, candidate.Software.Commit, SvEvidenceChangeSeverity.Info, "Build commit changed.");
        Health(reportChanges, "Report", baseline.Summary.Health, candidate.Summary.Health);
        var streams = CompareStreams(baseline.Streams, candidate.Streams);
        var all = reportChanges.Concat(streams.SelectMany(stream => stream.Changes)).ToArray();
        var result = new SvSubscriberEvidenceComparison
        {
            GeneratedAt = generatedAt,
            Baseline = Reference(baseline), Candidate = Reference(candidate), ReportChanges = reportChanges, Streams = streams,
            Summary = new SvEvidenceComparisonSummary
            {
                BaselineStreamCount = baseline.Streams.Count, CandidateStreamCount = candidate.Streams.Count,
                AddedStreamCount = streams.Count(x => x.Kind == SvEvidenceChangeKind.Added), RemovedStreamCount = streams.Count(x => x.Kind == SvEvidenceChangeKind.Removed),
                ChangedStreamCount = streams.Count(x => x.Kind == SvEvidenceChangeKind.Changed), UnchangedStreamCount = streams.Count(x => x.Kind == SvEvidenceChangeKind.Unchanged),
                InfoChangeCount = all.Count(x => x.Severity == SvEvidenceChangeSeverity.Info), WarningChangeCount = all.Count(x => x.Severity == SvEvidenceChangeSeverity.Warning), ErrorChangeCount = all.Count(x => x.Severity == SvEvidenceChangeSeverity.Error)
            }
        };
        result.Validate(); return result;
    }

    private static IReadOnlyList<SvSubscriberStreamComparison> CompareStreams(IReadOnlyList<SvSubscriberStreamEvidence> baseline, IReadOnlyList<SvSubscriberStreamEvidence> candidate)
    {
        var output = new List<SvSubscriberStreamComparison>(); var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in baseline)
        {
            var target = candidate.FirstOrDefault(item => item.Key == source.Key) ?? candidate.FirstOrDefault(item => !used.Contains(item.Key) && LogicalKey(item) == LogicalKey(source));
            if (target is null) { output.Add(Removed(source)); continue; }
            used.Add(target.Key); output.Add(Pair(source, target));
        }
        foreach (var target in candidate.Where(item => !used.Contains(item.Key))) output.Add(Added(target));
        return output.OrderBy(item => item.Identity.AppId).ThenBy(item => item.Identity.SvId, StringComparer.Ordinal).ThenBy(item => item.Kind).ToArray();
    }

    private static SvSubscriberStreamComparison Pair(SvSubscriberStreamEvidence baseline, SvSubscriberStreamEvidence candidate)
    {
        var changes = new List<SvEvidenceFieldChange>();
        Health(changes, "Stream", baseline.Health, candidate.Health);
        Text(changes, "Identity", "Source MAC", baseline.Identity.SourceMac, candidate.Identity.SourceMac, SvEvidenceChangeSeverity.Info, "Publisher source MAC changed while logical identity remained stable.");
        Issue(changes, "Sequence gaps", baseline.Runtime.SequenceGapCount, candidate.Runtime.SequenceGapCount, SvEvidenceChangeSeverity.Warning);
        Issue(changes, "Duplicates", baseline.Runtime.DuplicateCount, candidate.Runtime.DuplicateCount, SvEvidenceChangeSeverity.Warning);
        Issue(changes, "Out-of-order", baseline.Runtime.OutOfOrderCount, candidate.Runtime.OutOfOrderCount, SvEvidenceChangeSeverity.Error);
        Issue(changes, "Payload issues", baseline.Runtime.PayloadIssueCount, candidate.Runtime.PayloadIssueCount, SvEvidenceChangeSeverity.Error);
        Rate(changes, "Observed samples/s", baseline.Observation.Facts.ObservedSamplesPerSecond, candidate.Observation.Facts.ObservedSamplesPerSecond);
        return new SvSubscriberStreamComparison
        {
            ComparisonKey = $"PAIR|{baseline.Key}|{candidate.Key}", LogicalStreamKey = LogicalKey(candidate),
            Kind = changes.Count == 0 ? SvEvidenceChangeKind.Unchanged : SvEvidenceChangeKind.Changed,
            Severity = changes.Select(change => change.Severity).DefaultIfEmpty(SvEvidenceChangeSeverity.Info).Max(),
            BaselineStreamKey = baseline.Key, CandidateStreamKey = candidate.Key, Identity = candidate.Identity, Changes = changes
        };
    }

    private static SvSubscriberStreamComparison Added(SvSubscriberStreamEvidence stream) => new() { ComparisonKey = $"ADDED|{stream.Key}", LogicalStreamKey = LogicalKey(stream), Kind = SvEvidenceChangeKind.Added, Severity = SvEvidenceChangeSeverity.Info, CandidateStreamKey = stream.Key, Identity = stream.Identity, Changes = [Change("Stream", "Presence", SvEvidenceChangeSeverity.Info, "absent", "present", "Logical stream was added.")] };
    private static SvSubscriberStreamComparison Removed(SvSubscriberStreamEvidence stream) => new() { ComparisonKey = $"REMOVED|{stream.Key}", LogicalStreamKey = LogicalKey(stream), Kind = SvEvidenceChangeKind.Removed, Severity = SvEvidenceChangeSeverity.Error, BaselineStreamKey = stream.Key, Identity = stream.Identity, Changes = [Change("Stream", "Presence", SvEvidenceChangeSeverity.Error, "present", "absent", "Logical stream is missing from the candidate evidence.")] };
    private static string LogicalKey(SvSubscriberStreamEvidence stream) => $"{stream.Identity.AppId:X4}|{stream.Identity.DestinationMac}|{stream.Identity.VlanId}|{stream.Identity.SvId}|{stream.Identity.DataSetReference}";
    private static SvEvidenceReportReference Reference(SvSubscriberEvidenceReport report) => new() { SchemaVersion = report.SchemaVersion, GeneratedAt = report.GeneratedAt, Product = report.Software.Product, Version = report.Software.Version, Commit = report.Software.Commit, CaptureSource = report.Capture.Source, Health = report.Summary.Health, StreamCount = report.Streams.Count };

    private static void Health(ICollection<SvEvidenceFieldChange> changes, string category, string baseline, string candidate)
    {
        if (string.Equals(baseline, candidate, StringComparison.OrdinalIgnoreCase)) return;
        var rank = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase) { ["IDLE"] = 0, ["GOOD"] = 1, ["WARN"] = 2, ["BAD"] = 3, ["ERROR"] = 4 };
        var severity = rank.GetValueOrDefault(candidate) > rank.GetValueOrDefault(baseline) ? SvEvidenceChangeSeverity.Error : SvEvidenceChangeSeverity.Info;
        changes.Add(Change(category, "Health", severity, baseline, candidate, severity == SvEvidenceChangeSeverity.Error ? "Health regressed." : "Health changed."));
    }
    private static void Text(ICollection<SvEvidenceFieldChange> changes, string category, string field, string baseline, string candidate, SvEvidenceChangeSeverity severity, string message) { if (!string.Equals(baseline ?? string.Empty, candidate ?? string.Empty, StringComparison.Ordinal)) changes.Add(Change(category, field, severity, baseline, candidate, message)); }
    private static void Issue(ICollection<SvEvidenceFieldChange> changes, string field, int baseline, int candidate, SvEvidenceChangeSeverity severity) { if (baseline != candidate) changes.Add(Change("Runtime", field, candidate > baseline ? severity : SvEvidenceChangeSeverity.Info, baseline.ToString(CultureInfo.InvariantCulture), candidate.ToString(CultureInfo.InvariantCulture), candidate > baseline ? $"{field} increased." : $"{field} decreased.")); }
    private static void Rate(ICollection<SvEvidenceFieldChange> changes, string field, double? baseline, double? candidate) { if (!baseline.HasValue || !candidate.HasValue) return; var tolerance = Math.Abs(baseline.Value) * RateTolerancePercent / 100.0; if (Math.Abs(candidate.Value - baseline.Value) > tolerance) changes.Add(Change("Rate", field, SvEvidenceChangeSeverity.Warning, baseline.Value.ToString("0.###", CultureInfo.InvariantCulture), candidate.Value.ToString("0.###", CultureInfo.InvariantCulture), "Observed rate moved outside the comparison tolerance.")); }
    private static SvEvidenceFieldChange Change(string category, string field, SvEvidenceChangeSeverity severity, string? baseline, string? candidate, string message) => new() { Category = category, Field = field, Severity = severity, Baseline = baseline ?? string.Empty, Candidate = candidate ?? string.Empty, Message = message };
}

public static class SvSubscriberEvidenceComparisonSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static string ToJson(SvSubscriberEvidenceComparison comparison) { ArgumentNullException.ThrowIfNull(comparison); comparison.Validate(); return JsonSerializer.Serialize(comparison, Options); }
    public static SvSubscriberEvidenceComparison FromJson(string json) { if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("SV comparison JSON cannot be empty.", nameof(json)); var value = JsonSerializer.Deserialize<SvSubscriberEvidenceComparison>(json, Options) ?? throw new InvalidDataException("SV comparison JSON did not contain a document."); value.Validate(); return value; }
    public static string ToMarkdown(SvSubscriberEvidenceComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison); comparison.Validate(); var b = new StringBuilder();
        b.AppendLine("# ARSVIN Subscriber Evidence Comparison").AppendLine();
        b.AppendLine(comparison.Summary.HasRegressions ? "## REVIEW REQUIRED" : "## NO REGRESSION DETECTED").AppendLine();
        b.Append("- Baseline commit: ").AppendLine(comparison.Baseline.Commit).Append("- Candidate commit: ").AppendLine(comparison.Candidate.Commit).AppendLine();
        foreach (var stream in comparison.Streams)
        {
            b.Append("## ").Append(stream.Kind).Append(" — ").AppendLine(stream.Identity.SvId).AppendLine();
            foreach (var change in stream.Changes) b.Append("- **").Append(change.Field).Append("** · ").Append(change.Severity).Append(" · ").Append(change.Baseline).Append(" → ").Append(change.Candidate).Append(" — ").AppendLine(change.Message);
            b.AppendLine();
        }
        return b.ToString();
    }
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true }; options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); return options; }
}
