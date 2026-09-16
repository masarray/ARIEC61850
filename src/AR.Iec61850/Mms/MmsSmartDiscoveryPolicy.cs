namespace AR.Iec61850.Mms;

/// <summary>
/// Pure scheduling and type-tree rules shared by smart discovery. Keeping these
/// decisions side-effect free makes them deterministic, cheap to test, and safe to
/// reuse from SCL-assisted and live-only orchestration.
/// </summary>
internal static class MmsSmartDiscoveryPolicy
{
    public static int ResolveWindow(int requested, int unknownPeerCap, int? negotiatedMaxOutstandingCalling)
    {
        var requestedBound = Math.Clamp(requested, 1, 32);
        var unknownBound = Math.Clamp(unknownPeerCap, 1, 16);

        if (negotiatedMaxOutstandingCalling is > 0)
            return Math.Max(1, Math.Min(requestedBound, negotiatedMaxOutstandingCalling.Value));

        return Math.Min(requestedBound, unknownBound);
    }

    /// <summary>
    /// Selects the live domain set independently from any SCL hint. This is important:
    /// SCL may prioritize online work, but it must never manufacture or suppress live
    /// MMS evidence.
    /// </summary>
    public static string[] SelectPublishedDomains(IEnumerable<string> observedDomains, int maxDomains)
    {
        ArgumentNullException.ThrowIfNull(observedDomains);

        return observedDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .Take(Math.Clamp(maxDomains, 1, 4096))
            .ToArray();
    }

    /// <summary>
    /// Reorders an already-selected live domain set for scheduling only. Exact SCL
    /// domain hints are attempted first, while unknown hints are ignored and every
    /// selected live domain remains present exactly once.
    /// </summary>
    public static string[] OrderDomainsForScheduling(
        IReadOnlyList<string> selectedLiveDomains,
        IReadOnlyList<string>? priorityDomains)
    {
        ArgumentNullException.ThrowIfNull(selectedLiveDomains);
        if (selectedLiveDomains.Count <= 1 || priorityDomains is null || priorityDomains.Count == 0)
            return selectedLiveDomains.ToArray();

        var live = new HashSet<string>(selectedLiveDomains, StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>(selectedLiveDomains.Count);

        foreach (var hint in priorityDomains)
        {
            if (string.IsNullOrWhiteSpace(hint))
                continue;

            var normalized = hint.Trim();
            if (live.Contains(normalized) && emitted.Add(normalized))
                ordered.Add(normalized);
        }

        foreach (var domain in selectedLiveDomains)
        {
            if (emitted.Add(domain))
                ordered.Add(domain);
        }

        return ordered.ToArray();
    }
}

internal static class MmsSmartTypeProbePolicy
{
    public static MmsObjectReference BuildDataObjectRoot(MmsFcResolvedPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var parts = Split(point.MmsItemName);
        var item = parts.Length >= 3
            ? string.Join('$', parts.Take(3))
            : point.MmsItemName;
        return new MmsObjectReference(point.Domain, item, point.FunctionalConstraint);
    }

    /// <summary>
    /// Returns true only when the supplied GVA result can prove the requested MMS
    /// item through its TypeSpecification hierarchy. A successful but shallow result
    /// is deliberately not treated as coverage for descendants.
    /// </summary>
    public static bool Covers(
        MmsVariableAccessAttributesResult result,
        string targetMmsItemName)
    {
        if (!result.IsSuccess || result.TypeSpecification is null || string.IsNullOrWhiteSpace(targetMmsItemName))
            return false;

        var rootParts = Split(result.Reference.Item);
        var targetParts = Split(targetMmsItemName);
        if (rootParts.Length == 0 || targetParts.Length < rootParts.Length)
            return false;

        for (var index = 0; index < rootParts.Length; index++)
        {
            if (!string.Equals(rootParts[index], targetParts[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var current = result.TypeSpecification;
        for (var index = rootParts.Length; index < targetParts.Length; index++)
        {
            var part = targetParts[index];
            var next = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
                return false;

            current = next;
        }

        return true;
    }

    private static string[] Split(string value)
        => (value ?? string.Empty).Split(
            '$',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
