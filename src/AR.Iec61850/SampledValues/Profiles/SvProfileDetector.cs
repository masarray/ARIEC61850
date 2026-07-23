namespace AR.Iec61850.SampledValues.Profiles;

/// <summary>
/// Evidence-weighted, vendor-neutral profile detector. It evaluates only observable wire,
/// SCL, capture-rate, and trusted-context facts; manufacturer identity is never an input.
/// </summary>
public sealed class SvProfileDetector
{
    private const int EtherTypeWeight = 5;
    private const int AsduWeight = 12;
    private const int PayloadWeight = 18;
    private const int DataSetCountWeight = 12;
    private const int DataSetSignatureWeight = 25;
    private const int SamplingRateWeight = 25;
    private const int NominalFrequencyWeight = 8;
    private const int CounterWrapWeight = 15;

    public IReadOnlyList<SvProfileDetectionResult> Detect(SvObservedStreamFacts facts, IEnumerable<SvProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(profiles);
        return profiles.Select(profile => Evaluate(facts, profile))
            .OrderByDescending(result => Rank(result.Confidence))
            .ThenByDescending(result => result.ScorePercent)
            .ThenByDescending(result => result.EvaluatedWeight)
            .ThenBy(result => result.Profile.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public SvProfileDetectionResult? DetectBest(SvObservedStreamFacts facts, IEnumerable<SvProfileDefinition> profiles)
        => Detect(facts, profiles).FirstOrDefault();

    public SvProfileDetectionResult Evaluate(SvObservedStreamFacts facts, SvProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var evidence = new List<SvProfileMatchEvidence>();
        Compare("EtherType", profile.ExpectedEtherType, facts.EtherType, EtherTypeWeight, value => $"0x{value:X4}", evidence);
        CompareAllowed("ASDU per frame", profile.AllowedAsduPerFrame, facts.AsduPerFrame, AsduWeight, evidence);
        Compare("Payload bytes per ASDU", profile.ExpectedPayloadBytesPerAsdu, facts.PayloadBytesPerAsdu, PayloadWeight, value => value.ToString(), evidence);
        Compare("Dataset element count", profile.ExpectedDataSetElementCount, facts.DataSetSignature.Count > 0 ? facts.DataSetSignature.Count : null, DataSetCountWeight, value => value.ToString(), evidence);
        CompareSignature(profile.ExpectedDataSetSignature, facts.DataSetSignature, evidence);
        CompareSampling(profile, facts, evidence);
        CompareAllowedDouble("Nominal frequency", profile.AllowedNominalFrequenciesHz, facts.NominalFrequencyHz, NominalFrequencyWeight, profile.RateTolerancePercent, evidence);
        Compare("Sample-counter wrap", profile.ExpectedCounterWrap, facts.ObservedCounterWrap, CounterWrapWeight, value => value.ToString(), evidence);

        var evaluated = evidence.Where(item => item.Outcome != SvProfileEvidenceOutcome.Unknown).Sum(item => item.Weight);
        var matched = evidence.Where(item => item.Outcome == SvProfileEvidenceOutcome.Match).Sum(item => item.Weight);
        var conflict = evidence.Where(item => item.Outcome == SvProfileEvidenceOutcome.Conflict).Sum(item => item.Weight);
        var score = evaluated == 0 ? 0 : Math.Round((double)matched / evaluated * 100, 2);
        var raw = ResolveRawConfidence(score, matched, conflict, evaluated,
            Match(evidence, "Dataset signature"),
            Match(evidence, "Observed samples per second") || Match(evidence, "Samples per cycle"));
        return new SvProfileDetectionResult
        {
            Profile = profile,
            RawConfidence = raw,
            ScorePercent = score,
            MatchedWeight = matched,
            ConflictWeight = conflict,
            EvaluatedWeight = evaluated,
            Evidence = evidence
        };
    }

    private static SvProfileConfidence ResolveRawConfidence(double score, int matched, int conflict, int evaluated, bool signature, bool sampling)
    {
        if (evaluated == 0) return SvProfileConfidence.Unknown;
        if (conflict >= matched && conflict > 0) return SvProfileConfidence.Conflict;
        if (conflict == 0 && score >= 90 && evaluated >= 70 && signature && sampling) return SvProfileConfidence.Confirmed;
        if (score >= 70 && evaluated >= 40) return SvProfileConfidence.Likely;
        if (score >= 45 && evaluated >= 15) return SvProfileConfidence.Possible;
        return conflict > 0 ? SvProfileConfidence.Conflict : SvProfileConfidence.Unknown;
    }

    private static void CompareSampling(SvProfileDefinition profile, SvObservedStreamFacts facts, List<SvProfileMatchEvidence> evidence)
    {
        if (profile.SamplingBasis == SvSamplingBasis.SamplesPerSecond && profile.ExpectedSamplesPerSecond.HasValue)
            CompareApproximate("Observed samples per second", profile.ExpectedSamplesPerSecond.Value, facts.ObservedSamplesPerSecond, SamplingRateWeight, profile.RateTolerancePercent, evidence);
        else if (profile.SamplingBasis == SvSamplingBasis.SamplesPerCycle && profile.ExpectedSamplesPerCycle.HasValue)
        {
            if (!facts.ObservedSamplesPerSecond.HasValue || !facts.NominalFrequencyHz.HasValue)
                evidence.Add(new("Samples per cycle", SvProfileEvidenceOutcome.Unknown, SamplingRateWeight, profile.ExpectedSamplesPerCycle.Value.ToString("0.###"), "-", "Observed rate and nominal frequency are both required."));
            else
                CompareApproximate("Samples per cycle", profile.ExpectedSamplesPerCycle.Value, facts.ObservedSamplesPerSecond.Value / facts.NominalFrequencyHz.Value, SamplingRateWeight, profile.RateTolerancePercent, evidence);
        }
    }

    private static void CompareSignature(IReadOnlyList<SvDatasetElementSignature> expected, IReadOnlyList<SvDatasetElementSignature> observed, List<SvProfileMatchEvidence> evidence)
    {
        if (expected.Count == 0) return;
        if (observed.Count == 0)
        {
            evidence.Add(new("Dataset signature", SvProfileEvidenceOutcome.Unknown, DataSetSignatureWeight, Signature(expected), "-", "No dataset signature is available for this observation window."));
            return;
        }
        var matches = expected.Select(Key).SequenceEqual(observed.Select(Key), StringComparer.Ordinal);
        evidence.Add(new("Dataset signature", matches ? SvProfileEvidenceOutcome.Match : SvProfileEvidenceOutcome.Conflict, DataSetSignatureWeight,
            Signature(expected), Signature(observed), matches ? "Dataset element order and types match the profile definition." : "Dataset element order or types conflict with the profile definition."));
    }

    private static void CompareAllowed(string field, IReadOnlyList<int> allowed, int? observed, int weight, List<SvProfileMatchEvidence> evidence)
    {
        if (allowed.Count == 0) return;
        if (!observed.HasValue) { evidence.Add(new(field, SvProfileEvidenceOutcome.Unknown, weight, string.Join("/", allowed), "-", $"Observed {field} is unavailable.")); return; }
        var matches = allowed.Contains(observed.Value);
        evidence.Add(new(field, matches ? SvProfileEvidenceOutcome.Match : SvProfileEvidenceOutcome.Conflict, weight, string.Join("/", allowed), observed.Value.ToString(), matches ? $"Observed {field} is allowed." : $"Observed {field} is not allowed by the profile definition."));
    }

    private static void CompareAllowedDouble(string field, IReadOnlyList<double> allowed, double? observed, int weight, double tolerance, List<SvProfileMatchEvidence> evidence)
    {
        if (allowed.Count == 0) return;
        if (!observed.HasValue) { evidence.Add(new(field, SvProfileEvidenceOutcome.Unknown, weight, string.Join("/", allowed), "-", $"Observed {field} is unavailable.")); return; }
        var matches = allowed.Any(value => Within(value, observed.Value, tolerance));
        evidence.Add(new(field, matches ? SvProfileEvidenceOutcome.Match : SvProfileEvidenceOutcome.Conflict, weight, string.Join("/", allowed), observed.Value.ToString("0.###"), matches ? $"Observed {field} is allowed." : $"Observed {field} conflicts with the allowed values."));
    }

    private static void CompareApproximate(string field, double expected, double? observed, int weight, double tolerance, List<SvProfileMatchEvidence> evidence)
    {
        if (!observed.HasValue) { evidence.Add(new(field, SvProfileEvidenceOutcome.Unknown, weight, expected.ToString("0.###"), "-", $"Observed {field} is unavailable.")); return; }
        var matches = Within(expected, observed.Value, tolerance);
        evidence.Add(new(field, matches ? SvProfileEvidenceOutcome.Match : SvProfileEvidenceOutcome.Conflict, weight, expected.ToString("0.###"), observed.Value.ToString("0.###"), matches ? $"Observed {field} is within {tolerance:0.###}% tolerance." : $"Observed {field} is outside {tolerance:0.###}% tolerance."));
    }

    private static void Compare<T>(string field, T? expected, T? observed, int weight, Func<T,string> format, List<SvProfileMatchEvidence> evidence) where T : struct, IEquatable<T>
    {
        if (!expected.HasValue) return;
        if (!observed.HasValue) { evidence.Add(new(field, SvProfileEvidenceOutcome.Unknown, weight, format(expected.Value), "-", $"Observed {field} is unavailable.")); return; }
        var matches = expected.Value.Equals(observed.Value);
        evidence.Add(new(field, matches ? SvProfileEvidenceOutcome.Match : SvProfileEvidenceOutcome.Conflict, weight, format(expected.Value), format(observed.Value), matches ? $"Observed {field} matches." : $"Observed {field} conflicts with the profile definition."));
    }

    private static bool Within(double expected, double observed, double tolerance) => expected == 0 ? observed == 0 : Math.Abs(observed - expected) / Math.Abs(expected) * 100 <= tolerance;
    private static bool Match(IEnumerable<SvProfileMatchEvidence> evidence, string field) => evidence.Any(item => item.Field == field && item.Outcome == SvProfileEvidenceOutcome.Match);
    private static string Signature(IReadOnlyList<SvDatasetElementSignature> value) => string.Join(", ", value.Select(element => element.NormalizedBType));
    private static string Key(SvDatasetElementSignature element) => $"{element.NormalizedBType}|{element.NormalizedCdc}|{element.IsQuality}|{element.IsTimestamp}";
    private static int Rank(SvProfileConfidence value) => value switch { SvProfileConfidence.Confirmed => 4, SvProfileConfidence.Likely => 3, SvProfileConfidence.Possible => 2, SvProfileConfidence.Unknown => 1, _ => 0 };
}

public static class SvProfileCatalog
{
    public static SvProfileDefinition GenericSclLayer2 { get; } = new()
    {
        Id = "generic-scl-layer2",
        DisplayName = "Generic SCL-driven Layer-2 SV",
        Family = "Generic Layer-2 SV",
        SamplingBasis = SvSamplingBasis.Custom,
        ExpectedEtherType = 0x88BA,
        EvidenceStatus = SvProfileEvidenceStatus.ImplementedGeneric,
        Sources = [new SvProfileSourceEvidence("ariec61850-engine", "Generic Layer-2 SV mechanisms implemented by the shared engine without a profile-specific conformance claim.", SvProfileEvidenceStatus.ImplementedGeneric)]
    };
    public static IReadOnlyList<SvProfileDefinition> BuiltIn { get; } = [GenericSclLayer2];
}
