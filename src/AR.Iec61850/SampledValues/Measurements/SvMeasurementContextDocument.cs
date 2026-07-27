using System.Text.Json;
using System.Text.Json.Serialization;

namespace AR.Iec61850.SampledValues.Measurements;

/// <summary>
/// User-supplied measurement-domain context for one logical SV stream.
/// The stream key is the evidence identity used by a consumer, not a vendor name.
/// </summary>
public sealed record SvStreamMeasurementContext
{
    public string StreamKey { get; init; } = string.Empty;
    public string SvId { get; init; } = string.Empty;
    public SvMeasurementValueDomain WireDomain { get; init; } = SvMeasurementValueDomain.PrimaryEngineering;
    public SvMeasurementValueDomain DisplayDomain { get; init; } = SvMeasurementValueDomain.PrimaryEngineering;
    public SvMeasurementRatio? CurrentRatio { get; init; }
    public SvMeasurementRatio? VoltageRatio { get; init; }
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public SvMeasurementRatio? ResolveRatio(string kindOrChannel)
    {
        var text = kindOrChannel?.Trim() ?? string.Empty;
        if (text.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("V", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("TVTR", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VolSv", StringComparison.OrdinalIgnoreCase))
            return VoltageRatio;

        if (text.Contains("current", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("I", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("TCTR", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AmpSv", StringComparison.OrdinalIgnoreCase))
            return CurrentRatio;

        return null;
    }

    public string Summary
    {
        get
        {
            var current = CurrentRatio?.IsValid == true
                ? $"I {CurrentRatio.PrimaryNominal:0.###}/{CurrentRatio.SecondaryNominal:0.###} {CurrentRatio.Unit}"
                : "I ratio unset";
            var voltage = VoltageRatio?.IsValid == true
                ? $"V {VoltageRatio.PrimaryNominal:0.###}/{VoltageRatio.SecondaryNominal:0.###} {VoltageRatio.Unit}"
                : "V ratio unset";
            return $"wire {WireDomain} → display {DisplayDomain} · {current} · {voltage}";
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(StreamKey))
            errors.Add("StreamKey is required.");
        if (WireDomain is not (SvMeasurementValueDomain.PrimaryEngineering or SvMeasurementValueDomain.SecondaryEquivalent))
            errors.Add("WireDomain must be PrimaryEngineering or SecondaryEquivalent.");
        if (DisplayDomain is not (SvMeasurementValueDomain.PrimaryEngineering or SvMeasurementValueDomain.SecondaryEquivalent))
            errors.Add("DisplayDomain must be PrimaryEngineering or SecondaryEquivalent.");

        ValidateRatio(CurrentRatio, "current", "A", errors);
        ValidateRatio(VoltageRatio, "voltage", "V", errors);
        return errors;
    }

    private static void ValidateRatio(
        SvMeasurementRatio? ratio,
        string label,
        string expectedUnit,
        ICollection<string> errors)
    {
        if (ratio is null)
            return;
        if (!ratio.IsValid)
            errors.Add($"The {label} ratio requires positive finite primary and secondary nominal values.");
        if (!string.Equals(ratio.Unit, expectedUnit, StringComparison.OrdinalIgnoreCase))
            errors.Add($"The {label} ratio unit must be {expectedUnit}.");
        if (ratio.Source == SvRatioSource.Unknown)
            errors.Add($"The {label} ratio source must be explicit.");
    }
}

public sealed record SvMeasurementContextDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SvStreamMeasurementContext> Streams { get; init; }
        = Array.Empty<SvStreamMeasurementContext>();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != CurrentSchemaVersion)
            errors.Add($"Unsupported measurement-context schema version {SchemaVersion}; expected {CurrentSchemaVersion}.");

        var duplicateKeys = Streams
            .Where(item => !string.IsNullOrWhiteSpace(item.StreamKey))
            .GroupBy(item => item.StreamKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        errors.AddRange(duplicateKeys.Select(key => $"Duplicate measurement context for stream key '{key}'."));

        foreach (var stream in Streams)
        {
            var label = string.IsNullOrWhiteSpace(stream.SvId) ? stream.StreamKey : stream.SvId;
            errors.AddRange(stream.Validate().Select(error => $"{label}: {error}"));
        }
        return errors;
    }
}

public static class SvMeasurementContextSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(SvMeasurementContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureValid(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static SvMeasurementContextDocument FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Measurement-context JSON is empty.");

        SvMeasurementContextDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SvMeasurementContextDocument>(json, Options)
                ?? throw new InvalidDataException("Measurement-context JSON did not contain a document.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Measurement-context JSON is invalid: {ex.Message}", ex);
        }

        EnsureValid(document);
        return document;
    }

    private static void EnsureValid(SvMeasurementContextDocument document)
    {
        var errors = document.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("Measurement-context validation failed: " + string.Join(" ", errors));
    }
}
