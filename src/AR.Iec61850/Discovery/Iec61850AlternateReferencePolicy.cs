namespace AR.Iec61850.Discovery;

public enum Iec61850AlternateReferenceStrategyKind
{
    ComplexValueInstantaneousSibling,
    MagnitudeInstantaneousSibling
}

public sealed class Iec61850AlternateReferenceCandidate
{
    public string CanonicalMmsReference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public Iec61850AlternateReferenceStrategyKind Strategy { get; init; }
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Generates a bounded set of semantically equivalent MMS leaf candidates for known
/// IEC 61850 measurement representation siblings. The policy operates on MMS item tokens,
/// never on arbitrary substring replacement, and deliberately contains no vendor/domain aliasing.
/// </summary>
public static class Iec61850AlternateReferencePolicy
{
    public static IReadOnlyList<Iec61850AlternateReferenceCandidate> GetCandidates(string? canonicalMmsReference)
    {
        var canonical = (canonicalMmsReference ?? string.Empty).Trim();
        var slash = canonical.IndexOf('/');
        if (slash <= 0 || slash >= canonical.Length - 1)
            return Array.Empty<Iec61850AlternateReferenceCandidate>();

        var domain = canonical[..slash];
        var item = canonical[(slash + 1)..];
        var tokens = item.Split('$', StringSplitOptions.None);
        if (tokens.Length == 0 || tokens.Any(string.IsNullOrWhiteSpace))
            return Array.Empty<Iec61850AlternateReferenceCandidate>();

        var candidates = new List<Iec61850AlternateReferenceCandidate>();

        if (HasSuffix(tokens, "cVal", "mag", "f"))
        {
            AddCandidate(
                candidates,
                canonical,
                domain,
                tokens,
                tokens.Length - 3,
                "instCVal",
                Iec61850AlternateReferenceStrategyKind.ComplexValueInstantaneousSibling,
                "IEC 61850 measurement sibling cVal.mag.f -> instCVal.mag.f.");
        }
        else if (HasSuffix(tokens, "instCVal", "mag", "f"))
        {
            AddCandidate(
                candidates,
                canonical,
                domain,
                tokens,
                tokens.Length - 3,
                "cVal",
                Iec61850AlternateReferenceStrategyKind.ComplexValueInstantaneousSibling,
                "IEC 61850 measurement sibling instCVal.mag.f -> cVal.mag.f.");
        }
        else if (HasSuffix(tokens, "instMag", "f"))
        {
            AddCandidate(
                candidates,
                canonical,
                domain,
                tokens,
                tokens.Length - 2,
                "mag",
                Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling,
                "IEC 61850 measurement sibling instMag.f -> mag.f.");
        }
        else if (HasSuffix(tokens, "mag", "f"))
        {
            AddCandidate(
                candidates,
                canonical,
                domain,
                tokens,
                tokens.Length - 2,
                "instMag",
                Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling,
                "IEC 61850 measurement sibling mag.f -> instMag.f.");
        }

        return candidates;
    }

    private static bool HasSuffix(IReadOnlyList<string> tokens, params string[] suffix)
    {
        if (tokens.Count < suffix.Length)
            return false;

        var offset = tokens.Count - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (!string.Equals(tokens[offset + i], suffix[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static void AddCandidate(
        ICollection<Iec61850AlternateReferenceCandidate> candidates,
        string canonical,
        string domain,
        IReadOnlyList<string> sourceTokens,
        int tokenIndex,
        string replacement,
        Iec61850AlternateReferenceStrategyKind strategy,
        string explanation)
    {
        var tokens = sourceTokens.ToArray();
        tokens[tokenIndex] = replacement;
        var candidateReference = $"{domain}/{string.Join('$', tokens)}";
        if (string.Equals(candidateReference, canonical, StringComparison.OrdinalIgnoreCase) ||
            candidates.Any(x => string.Equals(x.MmsReference, candidateReference, StringComparison.OrdinalIgnoreCase)))
            return;

        candidates.Add(new Iec61850AlternateReferenceCandidate
        {
            CanonicalMmsReference = canonical,
            MmsReference = candidateReference,
            Strategy = strategy,
            Explanation = explanation
        });
    }
}
