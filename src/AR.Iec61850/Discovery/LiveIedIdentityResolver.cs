namespace AR.Iec61850.Discovery;

public sealed class LiveIedIdentity
{
    public string IedName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public LiveIedDiscoveryConfidenceLevel Confidence { get; init; } = LiveIedDiscoveryConfidenceLevel.Unknown;
    public bool IsAmbiguous { get; init; }
    public IReadOnlyList<string> CandidateNames { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> LogicalDeviceAliases { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public string Summary => $"IED identity: name={IedName}, source={Source}, confidence={Confidence}, candidates={CandidateNames.Count}, ambiguous={IsAmbiguous.ToString().ToLowerInvariant()}";
}

public static class LiveIedIdentityResolver
{
    private static readonly string[] KnownLogicalDeviceStems =
    [
        "PROT", "CTRL", "MEAS", "PQM", "MET", "ANN", "BCU", "SYS", "COM", "RLY", "BAY", "DR", "LD", "MU"
    ];

    public static LiveIedIdentity Resolve(
        IEnumerable<string> domains,
        string host,
        string? explicitIedName = null,
        string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var materialized = domains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(explicitIedName))
        {
            var name = explicitIedName.Trim();
            return Create(name, "ExplicitOverride", LiveIedDiscoveryConfidenceLevel.Exact, false, [name], materialized,
                [$"IED name was supplied explicitly as '{name}'."]);
        }

        var suffixMatches = materialized
            .Select(domain => new
            {
                Domain = domain,
                Candidate = TryExtractKnownLogicalDevicePrefix(domain, out var candidate) ? candidate : string.Empty
            })
            .Where(match => !string.IsNullOrWhiteSpace(match.Candidate))
            .ToArray();
        var distinctSuffixCandidates = suffixMatches
            .Select(match => match.Candidate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A known LD suffix is authoritative only when it explains the complete domain set.
        // A single nested domain such as OCR7SJ8Mod2_MU1 must never rename the physical IED
        // to OCR7SJ8Mod2 while sibling domains prove the shared OCR7SJ8 prefix.
        if (materialized.Length > 0 && suffixMatches.Length == materialized.Length)
        {
            if (distinctSuffixCandidates.Length == 1)
            {
                var name = distinctSuffixCandidates[0];
                var confidence = materialized.Length > 1
                    ? LiveIedDiscoveryConfidenceLevel.High
                    : LiveIedDiscoveryConfidenceLevel.Medium;
                var evidence = suffixMatches
                    .Select(match => $"MMS domain '{match.Domain}' matched the logical-device suffix pattern for IED '{name}'.")
                    .ToArray();

                return Create(name, "MmsDomainKnownLogicalDeviceSuffix", confidence, false, distinctSuffixCandidates, materialized, evidence);
            }

            if (distinctSuffixCandidates.Length > 1)
            {
                return CreateFallback(
                    host,
                    fallbackName,
                    distinctSuffixCandidates,
                    materialized,
                    true,
                    $"MMS domains produced conflicting IED-name candidates: {string.Join(", ", distinctSuffixCandidates)}.");
            }
        }

        var commonPrefix = InferCommonPrefix(materialized);
        if (!string.IsNullOrWhiteSpace(commonPrefix))
        {
            var confidence = materialized.Length >= 3
                ? LiveIedDiscoveryConfidenceLevel.High
                : LiveIedDiscoveryConfidenceLevel.Medium;
            return Create(
                commonPrefix,
                "MmsDomainCommonPrefix",
                confidence,
                false,
                [commonPrefix],
                materialized,
                [$"IED name '{commonPrefix}' was derived from the common prefix of {materialized.Length} MMS domain(s)."]);
        }

        // Retain the useful single-domain behavior (for example OLSF501LD0), but do not
        // let one partially matching domain dominate a larger mixed domain inventory.
        if (distinctSuffixCandidates.Length == 1 &&
            (materialized.Length == 1 ||
             (suffixMatches.Length >= 2 && materialized.All(domain => domain.StartsWith(distinctSuffixCandidates[0], StringComparison.OrdinalIgnoreCase)))))
        {
            var name = distinctSuffixCandidates[0];
            return Create(
                name,
                "MmsDomainKnownLogicalDeviceSuffix",
                materialized.Length == 1 ? LiveIedDiscoveryConfidenceLevel.Medium : LiveIedDiscoveryConfidenceLevel.High,
                false,
                distinctSuffixCandidates,
                materialized,
                suffixMatches.Select(match => $"MMS domain '{match.Domain}' matched the logical-device suffix pattern for IED '{name}'.").ToArray());
        }

        if (distinctSuffixCandidates.Length > 1)
        {
            return CreateFallback(
                host,
                fallbackName,
                distinctSuffixCandidates,
                materialized,
                true,
                $"MMS domains produced conflicting IED-name candidates: {string.Join(", ", distinctSuffixCandidates)}.");
        }

        return CreateFallback(
            host,
            fallbackName,
            Array.Empty<string>(),
            materialized,
            false,
            "No safe IED-name candidate could be derived from the live MMS domains.");
    }

    private static LiveIedIdentity CreateFallback(
        string host,
        string? fallbackName,
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> domains,
        bool isAmbiguous,
        string evidence)
    {
        var name = !string.IsNullOrWhiteSpace(fallbackName)
            ? fallbackName.Trim()
            : !string.IsNullOrWhiteSpace(host)
                ? host.Trim()
                : "DISCOVERED_IED";

        return Create(
            name,
            isAmbiguous ? "MmsDomainAmbiguous" : "HostFallback",
            LiveIedDiscoveryConfidenceLevel.Low,
            isAmbiguous,
            candidates,
            domains,
            [evidence]);
    }

    private static LiveIedIdentity Create(
        string name,
        string source,
        LiveIedDiscoveryConfidenceLevel confidence,
        bool isAmbiguous,
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> evidence)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domains)
        {
            var alias = domain.StartsWith(name, StringComparison.OrdinalIgnoreCase) && domain.Length > name.Length
                ? domain[name.Length..]
                : domain;
            aliases[domain] = string.IsNullOrWhiteSpace(alias) ? domain : alias;
        }

        return new LiveIedIdentity
        {
            IedName = name,
            Source = source,
            Confidence = confidence,
            IsAmbiguous = isAmbiguous,
            CandidateNames = candidates,
            LogicalDeviceAliases = aliases,
            Evidence = evidence
        };
    }

    private static bool TryExtractKnownLogicalDevicePrefix(string domain, out string candidate)
    {
        candidate = string.Empty;
        var trimmed = domain.Trim();
        var suffixStart = trimmed.Length;
        while (suffixStart > 0 && char.IsDigit(trimmed[suffixStart - 1]))
            suffixStart--;

        var withoutIndex = trimmed[..suffixStart];
        foreach (var stem in KnownLogicalDeviceStems)
        {
            if (!withoutIndex.EndsWith(stem, StringComparison.OrdinalIgnoreCase) || withoutIndex.Length <= stem.Length)
                continue;

            var prefix = TrimBoundary(withoutIndex[..^stem.Length]);
            if (!IsViableName(prefix))
                continue;

            candidate = prefix;
            return true;
        }

        return false;
    }

    private static string InferCommonPrefix(IReadOnlyList<string> domains)
    {
        if (domains.Count < 2)
            return string.Empty;

        var prefix = domains[0];
        foreach (var domain in domains.Skip(1))
        {
            var length = 0;
            while (length < prefix.Length && length < domain.Length && char.ToUpperInvariant(prefix[length]) == char.ToUpperInvariant(domain[length]))
                length++;

            prefix = prefix[..length];
            if (prefix.Length == 0)
                return string.Empty;
        }

        prefix = TrimBoundary(prefix);
        foreach (var stem in KnownLogicalDeviceStems)
        {
            if (prefix.EndsWith(stem, StringComparison.OrdinalIgnoreCase) && prefix.Length > stem.Length)
            {
                var withoutStem = TrimBoundary(prefix[..^stem.Length]);
                if (IsViableName(withoutStem))
                    prefix = withoutStem;
                break;
            }
        }

        return IsViableName(prefix) ? prefix : string.Empty;
    }

    private static string TrimBoundary(string value)
        => value.TrimEnd('_', '-', '.', ' ');

    private static bool IsViableName(string value)
        => value.Count(char.IsLetterOrDigit) >= 3;
}
