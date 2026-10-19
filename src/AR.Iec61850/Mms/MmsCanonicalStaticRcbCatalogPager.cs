using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

public sealed class MmsCanonicalStaticRcbCatalogPage
{
    public IReadOnlyList<MmsReportControlCandidate> Candidates { get; init; }
        = Array.Empty<MmsReportControlCandidate>();
    public int CatalogCount { get; init; }
    public int TargetBoundCount { get; init; }
    public int EligibleProbeCount { get; init; }
    public string? ContinuationToken { get; init; }
    public bool HasMore => ContinuationToken is not null;
}

/// <summary>
/// Read-only candidate admission for one selected canonical DataSet scope.
/// An observed DatSet binding is authority; an exact configured RCB reference
/// is only a priority for a Read, never proof of its current DatSet.
/// No sibling name guess can authorize report activation.
/// Catalog size is never a reason to abandon discovery: a page limits only
/// the number of actual read candidates and carries a checked continuation.
/// </summary>
public static class MmsCanonicalStaticRcbCatalogPager
{
    public static MmsCanonicalStaticRcbCatalogPage Select(
        CanonicalStaticReportCoveragePlan coverage,
        MmsReportInventory inventory,
        int maxReads,
        string? continuationToken = null,
        string? associationKey = null)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(inventory);
        if (maxReads <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxReads));

        // Exact scoped keys are derived from selected SCL coverage, not a
        // vendor-specific RCB-name pattern or an arbitrary inventory cap.
        var requested = coverage.Segments
            .Where(s => s.SelectedSignalReferences.Length > 0)
            .SelectMany(segment => segment.ReportControls.Select(report => new
            {
                Domain = DomainOf(report.MmsDomain, report.Reference),
                report.Buffered,
                Configured = Normalize(report.Reference),
                TargetDataSet = Normalize(segment.DataSetReference)
            }))
            .Where(target => target.Domain.Length > 0 && target.TargetDataSet.Length > 0)
            .ToArray();

        var selectedDomains = requested.Select(x => (x.Domain, x.Buffered))
            .ToHashSet();
        var targetDataSets = requested.Select(x => x.TargetDataSet)
            .ToHashSet(StringComparer.Ordinal);
        var configured = requested.Select(x => x.Configured)
            .Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);

        var catalog = inventory.ReportControls
            .Where(c => selectedDomains.Contains((c.Domain, c.Buffered)))
            .Where(c =>
            {
                var bound = Normalize(c.DataSetReference);
                return bound.Length == 0 || targetDataSets.Contains(bound);
            })
            .DistinctBy(c => Normalize(c.Reference), StringComparer.Ordinal)
            .OrderBy(c => c.Reference, StringComparer.Ordinal)
            .ToArray();

        var bound = catalog
            .Where(c => targetDataSets.Contains(Normalize(c.DataSetReference)))
            .ToArray();
        var exactUnbound = catalog
            .Where(c => string.IsNullOrWhiteSpace(c.DataSetReference) &&
                        configured.Contains(Normalize(c.Reference)))
            .ToArray();

        // A proven DataSet scope does not trigger a blind read of other,
        // still-unbound RCBs. Without proof, an unknown candidate requires
        // a real read of DatSet; exact configured references are probed first.
        // A concrete indexed RCB instance (e.g. Events01 for declared
        // Events) is ONLY a prioritization hint for a read-only DatSet probe.
        // This never constitutes exact DataSet identity or activation proof.
        var indexedUnbound = catalog
            .Where(c => string.IsNullOrWhiteSpace(c.DataSetReference) &&
                        !configured.Contains(Normalize(c.Reference)) &&
                        configured.Any(reference =>
                            IsIndexedInstance(Normalize(c.Reference), reference)))
            .ToArray();
        var indexedSet = indexedUnbound.ToHashSet();

        var probes = bound.Length > 0
            ? bound.Concat(exactUnbound).ToArray()
            : exactUnbound.Concat(indexedUnbound).Concat(catalog
                .Where(c => string.IsNullOrWhiteSpace(c.DataSetReference) &&
                            !configured.Contains(Normalize(c.Reference)) &&
                            !indexedSet.Contains(c))).ToArray();

        // The token is bound to the exact catalog/selection. On a changed
        // inventory it fails closed instead of skipping an arbitrary RCB.
        var input = new StringBuilder();
        input.Append("association=").Append(associationKey ?? "detached").Append('\n');
        foreach (var target in requested
                     .OrderBy(t => t.Domain, StringComparer.Ordinal)
                     .ThenBy(t => t.Configured, StringComparer.Ordinal)
                     .ThenBy(t => t.TargetDataSet, StringComparer.Ordinal))
            input.Append(target.Domain).Append('|').Append(target.Buffered)
                .Append('|').Append(target.Configured).Append('|')
                .Append(target.TargetDataSet).Append('\n');
        foreach (var item in catalog)
            input.Append(item.Reference).Append('|').Append(item.DataSetReference).Append('\n');
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));

        var start = 0;
        if (continuationToken is not null)
        {
            var parts = continuationToken.Split(':');
            if (parts.Length != 2 ||
                !string.Equals(parts[0], fingerprint, StringComparison.Ordinal) ||
                !int.TryParse(parts[1], out start) || start < 0 || start >= probes.Length)
                throw new ArgumentException(
                    "RCB continuation does not belong to the current target catalog.",
                    nameof(continuationToken));
        }

        var size = Math.Min(maxReads, probes.Length - start);
        var next = start + size;
        return new MmsCanonicalStaticRcbCatalogPage
        {
            CatalogCount = catalog.Length,
            TargetBoundCount = bound.Length,
            EligibleProbeCount = probes.Length,
            Candidates = probes.Skip(start).Take(size).ToArray(),
            ContinuationToken = next < probes.Length ? $"{fingerprint}:{next}" : null
        };
    }

    private static bool IsIndexedInstance(string candidate, string configured)
    {
        if (configured.Length == 0 || candidate.Length <= configured.Length ||
            !candidate.StartsWith(configured, StringComparison.Ordinal))
            return false;
        // Exact numeric instance suffix only: no arbitrary text similarity,
        // and still no usable target until the live DatSet read succeeds.
        for (var i = configured.Length; i < candidate.Length; i++)
            if (candidate[i] is < '0' or > '9')
                return false;
        return true;
    }

    private static string Normalize(string? reference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(reference);

    private static string DomainOf(string? domain, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return domain.Trim();
        var slash = reference?.IndexOf('/') ?? -1;
        return slash > 0 ? reference![..slash] : string.Empty;
    }
}
