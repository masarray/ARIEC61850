using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Builds bounded typed-LN hints from live MMS DataSet directories. A successful
/// DataSet directory is independent evidence when GetNameList stopped early.
/// No SCL, guessed LN names, values, or extra MMS name scans are used.
/// </summary>
public static class LiveIedDataSetTypeProbePlanner
{
    public static IReadOnlyList<MmsFcResolvedPoint> BuildVerifiedMemberHints(
        MmsDiscoveryResult discovery,
        int maxExtraLogicalNodes = 48,
        int maxMemberHints = 2048)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var extraLimit = Math.Clamp(maxExtraLogicalNodes, 0, 256);
        var pointLimit = Math.Clamp(maxMemberHints, 0, 8192);
        if (pointLimit == 0)
            return Array.Empty<MmsFcResolvedPoint>();

        // Only domains returned by this association can authorize probes.
        var domains = discovery.Snapshot.DomainVariables.Keys
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (domains.Count == 0)
            return Array.Empty<MmsFcResolvedPoint>();

        static string RootKey(string domain, string ln) => domain + "\u001f" + ln;
        var knownRoots = discovery.IedDirectory.LogicalDevices.Values
            .SelectMany(ld => ld.LogicalNodes.Keys.Select(ln => RootKey(ld.Name, ln)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedReferences = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<MmsFcResolvedPoint>();

        // ST first (event / control feedback), then MX; stable DataSet and
        // membership order within each priority tier.
        var liveMembers = discovery.DataSetDirectories
            .Where(directory => directory.IsSuccess)
            .OrderBy(directory => directory.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .SelectMany(directory => directory.Members)
            .OrderBy(member => string.Equals(member.FunctionalConstraint, "ST",
                StringComparison.OrdinalIgnoreCase) ? 0 : 1);

        foreach (var member in liveMembers)
        {
            if (results.Count >= pointLimit)
                break;
            if (!domains.Contains(member.Domain) ||
                string.IsNullOrWhiteSpace(member.MmsItemName) ||
                !(string.Equals(member.FunctionalConstraint, "ST", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(member.FunctionalConstraint, "MX", StringComparison.OrdinalIgnoreCase)))
                continue;

            // Do not accept a fabricated LN, incorrect FC, or ambiguous DO.
            if (!MmsIedModelDirectoryBuilder.TryParseLiveMmsVariable(
                    member.Domain, member.MmsItemName, out var point) ||
                !string.Equals(point.LogicalNode, member.LogicalNode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(point.FunctionalConstraint, member.FunctionalConstraint,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(point.DataObjectPath, member.DataObjectPath,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var root = RootKey(point.Domain, point.LogicalNode);
            if (!knownRoots.Contains(root) && !newRoots.Contains(root))
            {
                if (newRoots.Count >= extraLimit)
                    continue;
                newRoots.Add(root);
            }

            if (addedReferences.Add(point.MmsReference))
                results.Add(new MmsFcResolvedPoint
                {
                    Domain = point.Domain,
                    LogicalNode = point.LogicalNode,
                    FunctionalConstraint = point.FunctionalConstraint,
                    DataObjectPath = point.DataObjectPath,
                    MmsItemName = point.MmsItemName,
                    Source = "LiveMmsDataSetDirectoryTypeHint",
                    Confidence = member.Confidence
                });
        }

        return results;
    }
}
