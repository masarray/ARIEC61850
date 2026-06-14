using AR.Iec61850.Binding;

namespace AR.Iec61850.Mms;

public sealed class MmsReportSignalUpdate
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Source { get; init; } = "report";
    public string Value { get; init; } = "-";
    public string Quality { get; init; } = "-";
    public string Timestamp { get; init; } = "-";
    public string Reason { get; init; } = "-";
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsProjectedChild { get; init; }
    public string ProjectionStatus { get; init; } = string.Empty;

    public string Summary => $"{Reference} [{FunctionalConstraint}]={Value} q={Quality} t={Timestamp} reason={Reason}";
}

public sealed class MmsReportValueProjection
{
    public IReadOnlyList<MmsReportSignalUpdate> Updates { get; init; } = Array.Empty<MmsReportSignalUpdate>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string Summary => $"report projection: signals={Updates.Count}, warnings={Warnings.Count}";
}

internal sealed class MmsReportProjectedSignalCandidate
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Value { get; init; } = "-";
    public string Quality { get; init; } = "-";
    public string Timestamp { get; init; } = "-";
    public string Reason { get; init; } = "-";
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsQualityCarrier { get; init; }
    public bool IsTimestampCarrier { get; init; }
    public bool IsProjectedChild { get; init; }
    public string ProjectionStatus { get; init; } = string.Empty;

    public string QualityBaseReference => BaseReferenceFor(Reference, ".q");
    public string TimestampBaseReference => BaseReferenceFor(Reference, ".t");

    private static string BaseReferenceFor(string reference, string suffix)
        => reference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? reference[..^suffix.Length]
            : reference;
}

public static class MmsReportValueProjector
{
    public static MmsReportValueProjection Project(MmsReportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var warnings = new List<string>();
        var candidates = new List<MmsReportProjectedSignalCandidate>();
        foreach (var value in frame.Values)
            candidates.AddRange(ProjectValue(value, frame.ReceivedAt, warnings));

        var qualityByBase = candidates
            .Where(x => x.IsQualityCarrier && !string.IsNullOrWhiteSpace(x.QualityBaseReference))
            .GroupBy(x => Normalize(x.QualityBaseReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);

        var timestampByBase = candidates
            .Where(x => x.IsTimestampCarrier && !string.IsNullOrWhiteSpace(x.TimestampBaseReference))
            .GroupBy(x => Normalize(x.TimestampBaseReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);

        var updates = candidates
            .Where(x => !x.IsQualityCarrier && !x.IsTimestampCarrier)
            .Select(x => Enrich(x, qualityByBase, timestampByBase))
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference))
            .GroupBy(x => Normalize(x.Reference) + "|" + x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MmsReportValueProjection
        {
            Updates = updates,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static IReadOnlyList<MmsReportSignalUpdate> ProjectUpdates(MmsReportFrame frame)
        => Project(frame).Updates;

    private static MmsReportSignalUpdate Enrich(
        MmsReportProjectedSignalCandidate candidate,
        IReadOnlyDictionary<string, string> qualityByBase,
        IReadOnlyDictionary<string, string> timestampByBase)
    {
        var q = candidate.Quality;
        var t = candidate.Timestamp;
        var normalized = Normalize(candidate.Reference);
        var baseRef = BaseDataObjectReference(candidate.Reference);
        if ((string.IsNullOrWhiteSpace(q) || q == "-") && qualityByBase.TryGetValue(normalized, out var exactQ))
            q = exactQ;
        if ((string.IsNullOrWhiteSpace(t) || t == "-") && timestampByBase.TryGetValue(normalized, out var exactT))
            t = exactT;
        if ((string.IsNullOrWhiteSpace(q) || q == "-") && qualityByBase.TryGetValue(Normalize(baseRef), out var doQ))
            q = doQ;
        if ((string.IsNullOrWhiteSpace(t) || t == "-") && timestampByBase.TryGetValue(Normalize(baseRef), out var doT))
            t = doT;

        return new MmsReportSignalUpdate
        {
            Reference = candidate.Reference,
            FunctionalConstraint = candidate.FunctionalConstraint,
            DisplayName = ShortReference(candidate.Reference),
            Source = "report",
            Value = string.IsNullOrWhiteSpace(candidate.Value) ? "-" : candidate.Value,
            Quality = string.IsNullOrWhiteSpace(q) ? "-" : q,
            Timestamp = string.IsNullOrWhiteSpace(t) ? "-" : t,
            Reason = string.IsNullOrWhiteSpace(candidate.Reason) ? "-" : candidate.Reason,
            UpdatedAt = candidate.UpdatedAt,
            IsProjectedChild = candidate.IsProjectedChild,
            ProjectionStatus = candidate.ProjectionStatus
        };
    }

    private static IEnumerable<MmsReportProjectedSignalCandidate> ProjectValue(MmsReportValue reportValue, DateTimeOffset receivedAt, ICollection<string> warnings)
    {
        var reference = reportValue.MemberReference;
        var fc = reportValue.Member?.FunctionalConstraint ?? string.Empty;
        var reason = reportValue.ReasonSummary;
        if (reportValue.FailureCode.HasValue || reportValue.Value == null)
        {
            yield return new MmsReportProjectedSignalCandidate
            {
                Reference = reference,
                FunctionalConstraint = fc,
                Value = reportValue.DisplayValue,
                Quality = "failed",
                Reason = reason,
                UpdatedAt = receivedAt,
                ProjectionStatus = "failure"
            };
            yield break;
        }

        var value = reportValue.Value;
        if (TryProjectKnownDataObjectStruct(reference, fc, value, reason, receivedAt, out var projected))
        {
            foreach (var item in projected)
                yield return item;
            yield break;
        }

        if (IsQualityReference(reference))
        {
            var quality = Iec61850QualityDecoder.Decode(value);
            yield return new MmsReportProjectedSignalCandidate
            {
                Reference = reference,
                FunctionalConstraint = fc,
                Value = quality.IsDecoded ? quality.Validity : MmsDataValueRenderer.ToCompactString(value, reference),
                Quality = quality.IsDecoded ? quality.Validity : "-",
                Reason = reason,
                UpdatedAt = receivedAt,
                IsQualityCarrier = true,
                ProjectionStatus = quality.IsDecoded ? "quality-carrier" : "quality-raw"
            };
            yield break;
        }

        if (IsTimestampReference(reference))
        {
            var timestamp = Iec61850TimestampDecoder.Decode(value);
            yield return new MmsReportProjectedSignalCandidate
            {
                Reference = reference,
                FunctionalConstraint = fc,
                Value = timestamp.IsDecoded ? timestamp.DisplayTime : MmsDataValueRenderer.ToCompactString(value, reference),
                Timestamp = timestamp.IsDecoded ? timestamp.DisplayTime : "-",
                Reason = reason,
                UpdatedAt = receivedAt,
                IsTimestampCarrier = true,
                ProjectionStatus = timestamp.IsDecoded ? "timestamp-carrier" : "timestamp-raw"
            };
            yield break;
        }

        var display = DisplayScalar(reference, value);
        if (display.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase) || display.StartsWith("Array(", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"REPORT_RAW_STRUCT: {reference} was not recognized by the report value projector; showing compact raw summary.");

        yield return new MmsReportProjectedSignalCandidate
        {
            Reference = reference,
            FunctionalConstraint = fc,
            Value = display,
            Reason = reason,
            UpdatedAt = receivedAt,
            ProjectionStatus = "direct"
        };
    }

    private static bool TryProjectKnownDataObjectStruct(
        string reference,
        string fc,
        MmsDataValue value,
        string reason,
        DateTimeOffset receivedAt,
        out IReadOnlyList<MmsReportProjectedSignalCandidate> projected)
    {
        projected = Array.Empty<MmsReportProjectedSignalCandidate>();
        if (value.Kind != MmsDataKind.Structure)
            return false;

        var leafName = LastSegment(reference).ToUpperInvariant();
        if (leafName is "OP" && value.Children.Count >= 3)
        {
            var q = DecodeQuality(value.Children[1]);
            var t = DecodeTimestamp(value.Children[2]);
            projected = new[]
            {
                MainChild(reference, fc, "general", value.Children[0], reason, receivedAt, q, t)
            };
            return true;
        }

        if (leafName is "STR" && value.Children.Count >= 10)
        {
            var q = DecodeQuality(value.Children[8]);
            var t = DecodeTimestamp(value.Children[9]);
            var names = new[] { "general", "dirGeneral", "phsA", "dirPhsA", "phsB", "dirPhsB", "phsC", "dirPhsC" };
            projected = names
                .Select((name, index) => MainChild(reference, fc, name, value.Children[index], reason, receivedAt, q, t))
                .ToArray();
            return true;
        }

        if (leafName is "POS" && value.Children.Count >= 3)
        {
            // DPC/DPS Pos commonly reports stVal, q, t for DataSet-level object values.  Some relays include origin/ctlNum
            // before stVal; prefer the last Dbpos-looking scalar before q/t only if schema is not available.
            var qIndex = FindQualityIndex(value.Children);
            var tIndex = FindTimestampIndex(value.Children);
            if (qIndex >= 1)
            {
                var stValIndex = Math.Max(0, qIndex - 1);
                var q = qIndex >= 0 ? DecodeQuality(value.Children[qIndex]) : "-";
                var t = tIndex >= 0 ? DecodeTimestamp(value.Children[tIndex]) : "-";
                projected = new[]
                {
                    new MmsReportProjectedSignalCandidate
                    {
                        Reference = Combine(reference, "stVal"),
                        FunctionalConstraint = string.IsNullOrWhiteSpace(fc) ? "ST" : fc,
                        Value = Iec61850EnumValueDecoder.DecodeDbpos(value.Children[stValIndex]),
                        Quality = q,
                        Timestamp = t,
                        Reason = reason,
                        UpdatedAt = receivedAt,
                        IsProjectedChild = true,
                        ProjectionStatus = "projected-pos"
                    }
                };
                return true;
            }
        }

        if (leafName is "A" or "PHV" or "PPV" or "W" or "VAR" or "VA" or "PF")
        {
            var phaseUpdates = ProjectPhaseMeasurement(reference, fc, value, reason, receivedAt).ToArray();
            if (phaseUpdates.Length > 0)
            {
                projected = phaseUpdates;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MmsReportProjectedSignalCandidate> ProjectPhaseMeasurement(string reference, string fc, MmsDataValue value, string reason, DateTimeOffset receivedAt)
    {
        var names = new[] { "phsA", "phsB", "phsC", "neut", "res" };
        var limit = Math.Min(names.Length, value.Children.Count);
        for (var index = 0; index < limit; index++)
        {
            var child = value.Children[index];
            if (child.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
                continue;
            var q = FindNestedQuality(child);
            var t = FindNestedTimestamp(child);
            yield return new MmsReportProjectedSignalCandidate
            {
                Reference = Combine(reference, names[index]),
                FunctionalConstraint = string.IsNullOrWhiteSpace(fc) ? "MX" : fc,
                Value = VectorSummary(child, Combine(reference, names[index])),
                Quality = q,
                Timestamp = t,
                Reason = reason,
                UpdatedAt = receivedAt,
                IsProjectedChild = true,
                ProjectionStatus = "projected-phase"
            };
        }
    }

    private static MmsReportProjectedSignalCandidate MainChild(string reference, string fc, string name, MmsDataValue value, string reason, DateTimeOffset receivedAt, string quality, string timestamp)
        => new()
        {
            Reference = Combine(reference, name),
            FunctionalConstraint = string.IsNullOrWhiteSpace(fc) ? "ST" : fc,
            Value = DisplayScalar(Combine(reference, name), value),
            Quality = quality,
            Timestamp = timestamp,
            Reason = reason,
            UpdatedAt = receivedAt,
            IsProjectedChild = true,
            ProjectionStatus = "projected-struct"
        };

    private static string DisplayScalar(string reference, MmsDataValue value)
    {
        if (IsQualityReference(reference))
        {
            var q = Iec61850QualityDecoder.Decode(value);
            return q.IsDecoded ? q.Validity : MmsDataValueRenderer.ToCompactString(value, reference);
        }

        if (IsTimestampReference(reference))
        {
            var t = Iec61850TimestampDecoder.Decode(value);
            return t.IsDecoded ? t.DisplayTime : MmsDataValueRenderer.ToCompactString(value, reference);
        }

        if (reference.EndsWith(".Pos.stVal", StringComparison.OrdinalIgnoreCase) || reference.EndsWith(".ctlVal", StringComparison.OrdinalIgnoreCase))
            return Iec61850EnumValueDecoder.DecodeDbpos(value);

        return MmsDataValueRenderer.ToCompactString(value, reference);
    }

    private static string VectorSummary(MmsDataValue value, string reference)
    {
        var flat = FlattenScalars(value).ToArray();
        string? mag = flat
            .Where(x => IsLikelyMagnitude(x.ReferenceSuffix))
            .Select(x => x.Value)
            .FirstOrDefault();
        string? ang = flat
            .Where(x => IsLikelyAngle(x.ReferenceSuffix))
            .Select(x => x.Value)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(mag) && !string.IsNullOrWhiteSpace(ang))
            return $"{mag} ∠ {EnsureDegree(ang)}";
        if (!string.IsNullOrWhiteSpace(mag))
            return mag;
        return MmsDataValueRenderer.ToCompactString(value, reference);
    }

    private static IEnumerable<(string ReferenceSuffix, string Value)> FlattenScalars(MmsDataValue value, string prefix = "")
    {
        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
        {
            yield return (prefix, MmsDataValueRenderer.ToCompactString(value));
            yield break;
        }

        for (var i = 0; i < value.Children.Count; i++)
        {
            var childPrefix = string.IsNullOrWhiteSpace(prefix) ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : prefix + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var child in FlattenScalars(value.Children[i], childPrefix))
                yield return child;
        }
    }

    private static bool IsLikelyMagnitude(string path) => path.EndsWith(".0", StringComparison.Ordinal) || path == "0";
    private static bool IsLikelyAngle(string path) => path.EndsWith(".1", StringComparison.Ordinal) || path == "1";

    private static string FindNestedQuality(MmsDataValue value)
    {
        var q = Iec61850QualityDecoder.Decode(value);
        return q.IsDecoded ? q.Validity : "-";
    }

    private static string FindNestedTimestamp(MmsDataValue value)
    {
        var t = Iec61850TimestampDecoder.Decode(value);
        return t.IsDecoded ? t.DisplayTime : "-";
    }

    private static string DecodeQuality(MmsDataValue value)
    {
        var q = Iec61850QualityDecoder.Decode(value);
        return q.IsDecoded ? q.Validity : "-";
    }

    private static string DecodeTimestamp(MmsDataValue value)
    {
        var t = Iec61850TimestampDecoder.Decode(value);
        return t.IsDecoded ? t.DisplayTime : "-";
    }

    private static int FindQualityIndex(IReadOnlyList<MmsDataValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Iec61850QualityDecoder.Decode(values[i]).IsDecoded)
                return i;
        }
        return -1;
    }

    private static int FindTimestampIndex(IReadOnlyList<MmsDataValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Iec61850TimestampDecoder.Decode(values[i]).IsDecoded)
                return i;
        }
        return -1;
    }

    private static string BaseDataObjectReference(string reference)
    {
        var text = reference.Trim();
        foreach (var suffix in new[] { ".stVal", ".general", ".dirGeneral", ".phsA", ".dirPhsA", ".phsB", ".dirPhsB", ".phsC", ".dirPhsC", ".mag.f", ".cVal.mag.f", ".instCVal.mag.f" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text[..^suffix.Length];
        }
        return text;
    }

    private static bool IsQualityReference(string reference)
        => reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) || reference.Contains(".q.", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampReference(string reference)
        => reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase) || reference.Contains(".t.", StringComparison.OrdinalIgnoreCase);

    private static string Combine(string reference, string child)
        => string.IsNullOrWhiteSpace(reference) ? child : reference.TrimEnd('.') + "." + child;

    private static string LastSegment(string reference)
    {
        var slash = reference.LastIndexOf('/');
        var start = slash >= 0 ? slash + 1 : 0;
        var dot = reference.LastIndexOf('.');
        return dot >= start && dot < reference.Length - 1 ? reference[(dot + 1)..] : reference[start..];
    }

    private static string ShortReference(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
    }

    private static string Normalize(string reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string EnsureDegree(string value)
        => value.Contains('°', StringComparison.Ordinal) ? value : value + "°";
}
