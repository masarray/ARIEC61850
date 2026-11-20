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
    /// Independent RCB read-only enrichment may fill the negotiated window,
    /// but cannot exceed the peer's confirmed outstanding-calling capacity,
    /// the finite report candidate count, or the existing hard worker cap.
    /// Per-RCB fallback reads themselves remain ordered and serial.
    /// </summary>
    public static int ResolveReportEnrichmentWorkers(int effectiveWindow, int candidateCount)
        => candidateCount <= 0 ? 0 :
            Math.Min(Math.Clamp(effectiveWindow, 1, 8), candidateCount);

    /// <summary>
    /// Fairly samples exact, MMS-observed RCB instances under a fixed Read
    /// budget. Large peers may expose many numbered client slots per configured
    /// RCB family. Reading the first N sorted instances then biases discovery
    /// toward only a few DataSets and can mislead an application into treating
    /// every other DataSet as unreportable. One observed member per family is
    /// read before spending more of the same budget on that family's siblings.
    ///
    /// The family key is a READ scheduling hint only: no RCB, DataSet binding,
    /// availability, or write authority is fabricated from the prefix.
    /// Every result must still be proven by an exact MMS Read of the observed
    /// instance. The peer-negotiated worker window and original cap remain.
    /// </summary>
    public static MmsReportControlCandidate[] SelectReportEnrichmentCandidates(
        IEnumerable<MmsReportControlCandidate> observed, int maxReads)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (maxReads <= 0)
            return Array.Empty<MmsReportControlCandidate>();

        var families = observed
            .Where(rcb => !string.IsNullOrWhiteSpace(rcb.Reference))
            .DistinctBy(rcb => rcb.Reference, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(rcb => rcb.Buffered)
            .ThenBy(rcb => rcb.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rcb => rcb.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rcb => rcb.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rcb => rcb.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rcb => rcb.Reference, StringComparer.Ordinal)
            .GroupBy(rcb => string.Concat(
                rcb.Domain, "\u001F", rcb.LogicalNode, "\u001F",
                rcb.FunctionalConstraint, "\u001F", RcbReadSchedulingFamily(rcb.Name)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray())
            .ToArray();

        var result = new List<MmsReportControlCandidate>(Math.Min(maxReads, families.Sum(x => x.Length)));
        for (var depth = 0; result.Count < maxReads; depth++)
        {
            var found = false;
            foreach (var family in families)
            {
                if (depth >= family.Length)
                    continue;
                found = true;
                result.Add(family[depth]);
                if (result.Count == maxReads)
                    break;
            }

            if (!found)
                break;
        }

        return result.ToArray();
    }

    private static string RcbReadSchedulingFamily(string? name)
    {
        var value = name ?? string.Empty;
        var stop = value.Length;
        while (stop > 0 && value[stop - 1] >= '0' && value[stop - 1] <= '9')
            stop--;
        return stop == 0 ? value : value[..stop];
    }

    /// <summary>
    /// Selects the live domain set independently from any SCL hint. This is important:
    /// SCL may prioritize online work, but it must never manufacture or suppress live
    /// MMS evidence. Case-only collisions are collapsed deterministically because the
    /// existing MMS directory model is case-insensitive and cannot represent both
    /// without a dictionary collision.
    /// </summary>
    public static string[] SelectPublishedDomains(IEnumerable<string> observedDomains, int maxDomains)
    {
        ArgumentNullException.ThrowIfNull(observedDomains);

        return observedDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .GroupBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(domain => domain, StringComparer.Ordinal).First())
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(domain => domain, StringComparer.Ordinal)
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
    private const char CompositeKeySeparator = '\u001F';

    /// <summary>
    /// Intersects supplied LN-root candidates with the logical nodes proven by the
    /// current live directory. Hints can affect order at higher layers, but a GVA must
    /// never be sent merely because a stale SCL/caller candidate names a non-live LN.
    /// Returned references use canonical live directory spelling and deterministic
    /// LD/LN order.
    /// </summary>
    public static MmsObjectReference[] SelectLiveLogicalNodeRoots(
        MmsIedModelDirectory directory,
        IEnumerable<MmsObjectReference> candidates)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(candidates);

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in candidates)
        {
            if (string.IsNullOrWhiteSpace(reference.Domain) ||
                string.IsNullOrWhiteSpace(reference.Item) ||
                reference.Item.Contains('$', StringComparison.Ordinal))
            {
                continue;
            }

            requested.Add(BuildReferenceKey(reference.Domain.Trim(), reference.Item.Trim()));
        }

        if (requested.Count == 0)
            return Array.Empty<MmsObjectReference>();

        return directory.LogicalDevices.Values
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Name, StringComparer.Ordinal)
            .SelectMany(device => device.LogicalNodes.Values
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Name, StringComparer.Ordinal)
                .Where(node => requested.Contains(BuildReferenceKey(device.Name, node.Name)))
                .Select(node => new MmsObjectReference(device.Name, node.Name, string.Empty)))
            .ToArray();
    }


    /// <summary>
    /// Wire-authoritative ST/MX LN candidates from successful DataSet directories.
    /// No SCL guesses, no duplicate GVA requests, no extra name-list crawl.
    /// </summary>
    public static MmsObjectReference[] SelectUnprobedDataSetLogicalNodeRoots(
        MmsIedModelDirectory directory,
        IReadOnlyList<MmsDataSetDirectoryResult> directories,
        IReadOnlyList<MmsVariableAccessAttributesResult> attemptedTypes)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(attemptedTypes);

        var domains = directory.LogicalDevices.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attempted = attemptedTypes
            .Where(result => !result.Reference.Item.Contains('$'))
            .Select(result => BuildReferenceKey(result.Reference.Domain, result.Reference.Item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, MmsObjectReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSet in directories.Where(dataSet => dataSet.IsSuccess))
        foreach (var member in dataSet.Members)
        {
            if (!domains.Contains(member.Domain) ||
                !(member.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
                  member.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase)))
                continue;

            var parts = member.MmsItemName.Split('$');
            if (parts.Length < 3 ||
                !parts[1].Equals(member.FunctionalConstraint, StringComparison.OrdinalIgnoreCase))
                continue;

            var ln = parts[0].Trim();
            if (ln.Length == 0 || ln.Length > 64 ||
                !ln.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                continue;

            var key = BuildReferenceKey(member.Domain, ln);
            if (!attempted.Contains(key))
                targets.TryAdd(key, new MmsObjectReference(member.Domain, ln, string.Empty));
        }

        return targets.Values
            .OrderBy(root => root.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(root => root.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
    /// Builds exact fallback probes without ever reissuing an exact GVA reference that
    /// was already attempted earlier in the same hierarchy ladder. This matters for
    /// flat inventories containing LN$FC$DO roots: after a DO-root GVA fails or is
    /// shallow, treating that same root as a leaf fallback would otherwise send the
    /// identical request twice with no new evidence boundary.
    /// </summary>
    public static MmsObjectReference[] BuildUnprobedExactFallbacks(
        IEnumerable<MmsFcResolvedPoint> unresolvedPoints,
        IEnumerable<MmsObjectReference> alreadyProbed)
    {
        ArgumentNullException.ThrowIfNull(unresolvedPoints);
        ArgumentNullException.ThrowIfNull(alreadyProbed);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in alreadyProbed)
        {
            if (string.IsNullOrWhiteSpace(reference.Domain) || string.IsNullOrWhiteSpace(reference.Item))
                continue;
            seen.Add(BuildReferenceKey(reference.Domain.Trim(), reference.Item.Trim()));
        }

        var fallback = new List<MmsObjectReference>();
        foreach (var point in unresolvedPoints
                     .Where(point => !string.IsNullOrWhiteSpace(point.Domain) &&
                                     !string.IsNullOrWhiteSpace(point.MmsItemName))
                     .OrderBy(point => point.Domain, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(point => point.MmsItemName, StringComparer.OrdinalIgnoreCase))
        {
            var reference = point.ToObjectReference();
            var key = BuildReferenceKey(reference.Domain, reference.Item);
            if (!seen.Add(key))
                continue;

            fallback.Add(reference);
        }

        return fallback.ToArray();
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

    private static string BuildReferenceKey(string domain, string item)
        => string.Concat(domain ?? string.Empty, CompositeKeySeparator, item ?? string.Empty);

    private static string[] Split(string value)
        => (value ?? string.Empty).Split(
            '$',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
