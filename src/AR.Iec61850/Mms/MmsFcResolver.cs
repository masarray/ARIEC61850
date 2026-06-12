namespace AR.Iec61850.Mms;

public sealed class MmsFcResolveResult
{
    public string RequestedReference { get; init; } = string.Empty;
    public IReadOnlyList<MmsFcResolvedPoint> Candidates { get; init; } = Array.Empty<MmsFcResolvedPoint>();
    public IReadOnlyList<string> HeuristicFunctionalConstraints { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;

    public bool IsResolved => Candidates.Count > 0;
    public bool IsAmbiguous => Candidates.Count > 1;
    public MmsFcResolvedPoint? BestCandidate => Candidates
        .OrderByDescending(x => x.Confidence)
        .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.UserReference, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
}

public static class MmsFcResolver
{
    public static MmsFcResolveResult Resolve(MmsIedModelDirectory directory, string reference)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (string.IsNullOrWhiteSpace(reference))
            return new MmsFcResolveResult
            {
                RequestedReference = string.Empty,
                Message = "Reference is empty."
            };

        var normalized = MmsFcReferenceNormalizer.NormalizeUserReference(reference);
        var candidates = new List<MmsFcResolvedPoint>();

        if (directory.TryFindByMmsReference(reference, out var mmsPoint))
            candidates.Add(mmsPoint);

        candidates.AddRange(directory.FindByUserReference(normalized));
        candidates.AddRange(ResolveReferenceWithEmbeddedFc(directory, normalized));

        if (candidates.Count == 0)
            candidates.AddRange(directory.FindByPathSuffix(normalized));

        var distinct = candidates
            .GroupBy(x => x.MmsReference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(p => p.Confidence).First())
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => ScoreFunctionalConstraintForReference(x, normalized))
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.UserReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinct.Length > 0)
        {
            return new MmsFcResolveResult
            {
                RequestedReference = reference,
                Candidates = distinct,
                Message = distinct.Length == 1
                    ? $"Resolved from live IED directory: {distinct[0].UserReference} [{distinct[0].FunctionalConstraint}]."
                    : $"Resolved {distinct.Length} candidate(s) from live IED directory. Pick the exact leaf when the object is ambiguous."
            };
        }

        var heuristic = MmsFunctionalConstraint.SuggestByPath(normalized);
        return new MmsFcResolveResult
        {
            RequestedReference = reference,
            HeuristicFunctionalConstraints = heuristic,
            Message = $"No exact live-directory match. Heuristic FC suggestion(s): {string.Join(", ", heuristic)}. Do not write/control by heuristic only."
        };
    }

    private static IEnumerable<MmsFcResolvedPoint> ResolveReferenceWithEmbeddedFc(MmsIedModelDirectory directory, string normalized)
    {
        var slash = normalized.IndexOf('/');
        if (slash < 0 || slash >= normalized.Length - 1)
            yield break;

        var domain = normalized[..slash];
        var path = normalized[(slash + 1)..];
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !MmsFunctionalConstraint.IsKnown(parts[1]))
            yield break;

        var fc = MmsFunctionalConstraint.Normalize(parts[1]);
        var item = string.Join('$', new[] { parts[0], fc }.Concat(parts.Skip(2)));
        var mmsReference = $"{domain}/{item}";
        if (directory.TryFindByMmsReference(mmsReference, out var point))
            yield return point;
    }

    private static int ScoreFunctionalConstraintForReference(MmsFcResolvedPoint point, string normalized)
    {
        var suggested = MmsFunctionalConstraint.SuggestByPath(normalized);
        var index = suggested
            .Select((fc, i) => new { Fc = fc, Index = i })
            .FirstOrDefault(x => x.Fc.Equals(point.FunctionalConstraint, StringComparison.OrdinalIgnoreCase));
        return index == null ? 1000 : index.Index;
    }
}

public static class MmsFcReferenceNormalizer
{
    public static string NormalizeUserReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var trimmed = reference.Trim().Replace('$', '.');
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
            return string.Join('.', trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var domain = trimmed[..slash].Trim();
        var path = trimmed[(slash + 1)..].Trim();
        path = string.Join('.', path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(domain) ? path : $"{domain}/{path}";
    }

    public static string NormalizeMmsReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var trimmed = reference.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
            return string.Join('$', trimmed.Split(['$', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var domain = trimmed[..slash].Trim();
        var item = trimmed[(slash + 1)..].Trim();
        item = string.Join('$', item.Split(['$', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(domain) ? item : $"{domain}/{item}";
    }
}
