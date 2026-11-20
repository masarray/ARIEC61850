using System.Security.Cryptography;
using System.Text;

namespace AR.Iec61850.Mms;

public sealed class MmsCanonicalStaticExactTargetPage
{
    public IReadOnlySet<string> References { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
    public int TotalExactTargets { get; init; }
    public string? ContinuationToken { get; init; }
    public bool HasMore => ContinuationToken is not null;
}

/// <summary>
/// Resumable exact-target availability admission. Unlike a global 64/128 RCB
/// abort, this pure selector constrains work per page. It cannot add targets
/// missing from the verified live inventory or invent indexed RCB siblings.
/// </summary>
public static class MmsCanonicalStaticExactTargetPager
{
    public static MmsCanonicalStaticExactTargetPage Select(
        MmsCanonicalStaticAcquisitionProbePlan plan,
        MmsReportInventory inventory,
        int maxPerPage,
        string? continuationToken = null,
        string? associationKey = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inventory);
        if (maxPerPage <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerPage));
        if (inventory.Authority != MmsReportInventoryAuthority.LiveMmsObserved)
            throw new ArgumentException("Exact target paging requires live MMS inventory.", nameof(inventory));

        var liveReferences = inventory.ReportControls
            .Select(candidate => MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = plan.ExactTargetReportControlReferences
            .Select(MmsRcbAvailabilityEvaluator.NormalizeReference)
            .Where(reference => reference.Length > 0 && liveReferences.Contains(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();
        var input = new StringBuilder().Append("association=")
            .Append(associationKey ?? "detached").Append('\n');
        foreach (var reference in targets)
            input.Append(reference).Append('\n');
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));

        var start = 0;
        if (continuationToken is not null)
        {
            var parts = continuationToken.Split(':');
            if (parts.Length != 2 ||
                !string.Equals(parts[0], fingerprint, StringComparison.Ordinal) ||
                !int.TryParse(parts[1], out start) || start < 0 || start >= targets.Length)
                throw new ArgumentException(
                    "Exact RCB target continuation does not match current association/catalog.",
                    nameof(continuationToken));
        }
        var next = Math.Min(targets.Length, start + maxPerPage);
        return new MmsCanonicalStaticExactTargetPage
        {
            TotalExactTargets = targets.Length,
            References = targets.Skip(start).Take(next - start)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            ContinuationToken = next < targets.Length ? $"{fingerprint}:{next}" : null
        };
    }
}
