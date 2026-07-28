namespace AR.Iec61850.SampledValues.Field;

public enum SvSclBindingConfidence
{
    Rejected,
    Unknown,
    Possible,
    Likely,
    Confirmed
}

public enum SvBindingEvidenceOutcome
{
    Match,
    Conflict,
    Unknown
}

public sealed record SvSclBindingCandidate
{
    public string CandidateId { get; init; } = string.Empty;
    public ushort? ExpectedAppId { get; init; }
    public string ExpectedDestinationMac { get; init; } = string.Empty;
    public ushort? ExpectedVlanId { get; init; }
    public string ExpectedSvId { get; init; } = string.Empty;
    public string ExpectedDataSetReference { get; init; } = string.Empty;
    public uint? ExpectedConfigurationRevision { get; init; }
    public int? ExpectedAsduPerFrame { get; init; }
    public int? ExpectedPayloadBytesPerAsdu { get; init; }
}

public sealed record SvSclBindingObservation
{
    public ushort AppId { get; init; }
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public int AsduPerFrame { get; init; }
    public int PayloadBytesPerAsdu { get; init; }
}

public sealed record SvSclBindingEvidence(
    string Field,
    SvBindingEvidenceOutcome Outcome,
    int Weight,
    string Expected,
    string Observed,
    string Message,
    bool IsBlocking = false);

public sealed record SvSclBindingResult
{
    public string CandidateId { get; init; } = string.Empty;
    public SvSclBindingConfidence Confidence { get; init; }
    public int Score { get; init; }
    public int EvaluatedWeight { get; init; }
    public IReadOnlyList<SvSclBindingEvidence> Evidence { get; init; } = Array.Empty<SvSclBindingEvidence>();
    public bool HasBlockingConflict => Evidence.Any(item => item.IsBlocking && item.Outcome == SvBindingEvidenceOutcome.Conflict);
    public string Summary => $"{Confidence} · score {Score}%";
}

/// <summary>
/// Scores SCL candidates without requiring optional datSet on the wire. APPID and destination MAC
/// are blocking identity evidence; confRev is reported as configuration evidence, not a parser selector.
/// </summary>
public static class SvSclBindingScorer
{
    public static SvSclBindingResult Score(SvSclBindingCandidate candidate, SvSclBindingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(observation);
        var evidence = new List<SvSclBindingEvidence>();

        Compare(evidence, "APPID", candidate.ExpectedAppId, observation.AppId, 30, value => $"0x{value:X4}", blocking: true);
        CompareText(evidence, "Destination MAC", candidate.ExpectedDestinationMac, observation.DestinationMac, 25, NormalizeMac, blocking: true);
        CompareText(evidence, "svID", candidate.ExpectedSvId, observation.SvId, 25, NormalizeText, blocking: false);
        Compare(evidence, "Payload bytes/ASDU", candidate.ExpectedPayloadBytesPerAsdu, observation.PayloadBytesPerAsdu, 10, value => value.ToString(), blocking: false);
        Compare(evidence, "ASDU/frame", candidate.ExpectedAsduPerFrame, observation.AsduPerFrame, 5, value => value.ToString(), blocking: false);
        Compare(evidence, "VLAN ID", candidate.ExpectedVlanId, observation.VlanId, 5, value => value.ToString(), blocking: false);

        // datSet is optional on the wire. Missing observed data is unknown, never a conflict.
        CompareText(evidence, "datSet", candidate.ExpectedDataSetReference, observation.DataSetReference, 10, NormalizeText, blocking: false);

        if (candidate.ExpectedConfigurationRevision.HasValue)
        {
            var outcome = observation.ConfigurationRevision.HasValue
                ? candidate.ExpectedConfigurationRevision.Value == observation.ConfigurationRevision.Value
                    ? SvBindingEvidenceOutcome.Match
                    : SvBindingEvidenceOutcome.Conflict
                : SvBindingEvidenceOutcome.Unknown;
            evidence.Add(new SvSclBindingEvidence(
                "confRev",
                outcome,
                0,
                candidate.ExpectedConfigurationRevision.Value.ToString(),
                observation.ConfigurationRevision?.ToString() ?? "-",
                outcome == SvBindingEvidenceOutcome.Conflict
                    ? "Configuration revision differs; keep binding evidence but raise CONFIGURATION warning."
                    : "Configuration revision evidence.",
                false));
        }

        var evaluated = evidence.Where(item => item.Weight > 0 && item.Outcome != SvBindingEvidenceOutcome.Unknown).Sum(item => item.Weight);
        var matched = evidence.Where(item => item.Outcome == SvBindingEvidenceOutcome.Match).Sum(item => item.Weight);
        var score = evaluated == 0 ? 0 : (int)Math.Round(matched * 100.0 / evaluated);
        var blocking = evidence.Any(item => item.IsBlocking && item.Outcome == SvBindingEvidenceOutcome.Conflict);
        var confidence = blocking
            ? SvSclBindingConfidence.Rejected
            : score >= 85 && evaluated >= 70
                ? SvSclBindingConfidence.Confirmed
                : score >= 65 && evaluated >= 55
                    ? SvSclBindingConfidence.Likely
                    : score >= 40
                        ? SvSclBindingConfidence.Possible
                        : SvSclBindingConfidence.Unknown;

        return new SvSclBindingResult
        {
            CandidateId = candidate.CandidateId,
            Confidence = confidence,
            Score = score,
            EvaluatedWeight = evaluated,
            Evidence = evidence
        };
    }

    private static void Compare<T>(
        ICollection<SvSclBindingEvidence> evidence,
        string field,
        T? expected,
        T? observed,
        int weight,
        Func<T, string> format,
        bool blocking) where T : struct, IEquatable<T>
    {
        if (!expected.HasValue)
            return;
        if (!observed.HasValue)
        {
            evidence.Add(new(field, SvBindingEvidenceOutcome.Unknown, weight, format(expected.Value), "-", $"Observed {field} is unavailable.", blocking));
            return;
        }
        var match = expected.Value.Equals(observed.Value);
        evidence.Add(new(field, match ? SvBindingEvidenceOutcome.Match : SvBindingEvidenceOutcome.Conflict, weight,
            format(expected.Value), format(observed.Value), match ? $"{field} matches." : $"{field} differs.", blocking));
    }

    private static void Compare<T>(
        ICollection<SvSclBindingEvidence> evidence,
        string field,
        T? expected,
        T observed,
        int weight,
        Func<T, string> format,
        bool blocking) where T : struct, IEquatable<T>
        => Compare(evidence, field, expected, (T?)observed, weight, format, blocking);

    private static void CompareText(
        ICollection<SvSclBindingEvidence> evidence,
        string field,
        string expected,
        string observed,
        int weight,
        Func<string, string> normalize,
        bool blocking)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;
        if (string.IsNullOrWhiteSpace(observed))
        {
            evidence.Add(new(field, SvBindingEvidenceOutcome.Unknown, weight, expected, "-", $"Observed {field} is not present on wire.", blocking));
            return;
        }
        var match = string.Equals(normalize(expected), normalize(observed), StringComparison.Ordinal);
        evidence.Add(new(field, match ? SvBindingEvidenceOutcome.Match : SvBindingEvidenceOutcome.Conflict, weight,
            expected, observed, match ? $"{field} matches." : $"{field} differs.", blocking));
    }

    private static string NormalizeText(string value) => value.Trim();
    private static string NormalizeMac(string value)
        => new((value ?? string.Empty).Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
