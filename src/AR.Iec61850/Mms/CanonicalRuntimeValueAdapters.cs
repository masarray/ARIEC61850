using AR.Iec61850.Binding;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Mms;

public sealed class CanonicalRuntimeAdapterResult
{
    public int InputUpdateCount { get; init; }
    public int AppliedSignalCount { get; init; }
    public int UnresolvedUpdateCount { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public bool IsComplete => UnresolvedUpdateCount == 0;
    public string Summary => $"Canonical runtime projection: inputs={InputUpdateCount}, appliedSignals={AppliedSignalCount}, unresolved={UnresolvedUpdateCount}.";
}

/// <summary>
/// Adapts established MMS read/report projections into the canonical runtime value plane.
/// Protocol decoding remains owned by the existing MMS layer; this adapter only binds
/// already-decoded runtime evidence to canonical signal identity.
/// </summary>
public static class CanonicalMmsRuntimeValueAdapter
{
    public static CanonicalRuntimeAdapterResult ApplyInitialFcRead(
        CanonicalRuntimeValuePlane plane,
        InitialFcReadExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(execution);

        var results = new List<CanonicalRuntimeApplyResult>();
        foreach (var leaf in execution.Batches
                     .SelectMany(batch => batch.Projections)
                     .SelectMany(projection => projection.Leaves))
        {
            results.AddRange(ApplyMmsValue(
                plane,
                leaf.Reference,
                leaf.FunctionalConstraint,
                leaf.Value,
                "initial-read",
                DateTimeOffset.UtcNow));
        }

        return Summarize(results);
    }

    public static CanonicalRuntimeAdapterResult ApplyReportProjection(
        CanonicalRuntimeValuePlane plane,
        MmsReportValueProjection projection)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(projection);

        var results = new List<CanonicalRuntimeApplyResult>(projection.Updates.Count);
        foreach (var update in projection.Updates)
        {
            results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
            {
                Reference = update.Reference,
                FunctionalConstraint = update.FunctionalConstraint,
                Value = update.Value,
                Quality = update.Quality,
                Timestamp = update.Timestamp,
                Reason = update.Reason,
                Source = string.IsNullOrWhiteSpace(update.Source) ? "report" : update.Source,
                UpdatedAtUtc = update.UpdatedAt == default ? DateTimeOffset.UtcNow : update.UpdatedAt,
                HasValue = update.HasValue,
                HasQuality = update.HasQuality,
                HasTimestamp = update.HasTimestamp,
                HasReason = !string.IsNullOrWhiteSpace(update.Reason) && update.Reason != "-"
            }));
        }

        return Summarize(results, projection.Warnings);
    }

    public static CanonicalRuntimeAdapterResult ApplyRead(
        CanonicalRuntimeValuePlane plane,
        string reference,
        string functionalConstraint,
        MmsReadResult read,
        string source = "poll")
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(read);

        if (!read.IsSuccess || read.Value is null)
        {
            return new CanonicalRuntimeAdapterResult
            {
                UnresolvedUpdateCount = 1,
                Diagnostics = [string.IsNullOrWhiteSpace(read.Message) ? "MMS Read did not return a usable value." : read.Message]
            };
        }

        return Summarize(ApplyMmsValue(
            plane,
            reference,
            functionalConstraint,
            read.Value,
            string.IsNullOrWhiteSpace(source) ? "poll" : source,
            DateTimeOffset.UtcNow));
    }

    private static IReadOnlyList<CanonicalRuntimeApplyResult> ApplyMmsValue(
        CanonicalRuntimeValuePlane plane,
        string reference,
        string functionalConstraint,
        MmsDataValue value,
        string source,
        DateTimeOffset updatedAt)
    {
        var results = new List<CanonicalRuntimeApplyResult>(2);
        var display = MmsDataValueRenderer.ToCompactString(value, reference);

        if (IsQualityReference(reference))
        {
            var quality = Iec61850QualityDecoder.Decode(value);
            if (quality.IsDecoded)
                display = quality.Validity;

            results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
            {
                Reference = reference,
                FunctionalConstraint = functionalConstraint,
                Value = display,
                Source = source,
                UpdatedAtUtc = updatedAt,
                HasValue = true
            }));

            if (quality.IsDecoded)
            {
                results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
                {
                    Reference = StripCompanionSuffix(reference, ".q"),
                    FunctionalConstraint = functionalConstraint,
                    Quality = quality.Validity,
                    Source = source,
                    UpdatedAtUtc = updatedAt,
                    HasQuality = true
                }));
            }

            return results;
        }

        if (IsTimestampReference(reference))
        {
            var timestamp = Iec61850TimestampDecoder.Decode(value);
            if (timestamp.IsDecoded)
                display = timestamp.DisplayTime;

            results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
            {
                Reference = reference,
                FunctionalConstraint = functionalConstraint,
                Value = display,
                Source = source,
                UpdatedAtUtc = updatedAt,
                HasValue = true
            }));

            if (timestamp.IsDecoded)
            {
                results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
                {
                    Reference = StripCompanionSuffix(reference, ".t"),
                    FunctionalConstraint = functionalConstraint,
                    Timestamp = timestamp.DisplayTime,
                    Source = source,
                    UpdatedAtUtc = updatedAt,
                    HasTimestamp = true
                }));
            }

            return results;
        }

        results.Add(plane.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            Value = display,
            Source = source,
            UpdatedAtUtc = updatedAt,
            HasValue = true
        }));
        return results;
    }

    private static CanonicalRuntimeAdapterResult Summarize(
        IEnumerable<CanonicalRuntimeApplyResult> results,
        IEnumerable<string>? upstreamDiagnostics = null)
    {
        var materialized = results.ToArray();
        var diagnostics = new List<string>();
        if (upstreamDiagnostics is not null)
            diagnostics.AddRange(upstreamDiagnostics.Where(message => !string.IsNullOrWhiteSpace(message)));
        diagnostics.AddRange(materialized.Where(result => !result.IsApplied).Select(result => result.Message));

        return new CanonicalRuntimeAdapterResult
        {
            InputUpdateCount = materialized.Length,
            AppliedSignalCount = materialized.Sum(result => result.AppliedSignalCount),
            UnresolvedUpdateCount = materialized.Count(result => !result.IsApplied),
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static bool IsQualityReference(string reference)
        => (reference ?? string.Empty).EndsWith(".q", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampReference(string reference)
        => (reference ?? string.Empty).EndsWith(".t", StringComparison.OrdinalIgnoreCase);

    private static string StripCompanionSuffix(string reference, string suffix)
        => reference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? reference[..^suffix.Length]
            : reference;
}
