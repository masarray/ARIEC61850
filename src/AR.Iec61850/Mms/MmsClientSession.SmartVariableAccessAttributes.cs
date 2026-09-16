namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Reads variable type metadata structure-first. One GetVariableAccessAttributes
    /// request is issued for each IEC 61850 LN/FC/data-object root. Leaf probes are
    /// used only when the root request fails or does not return a structured type.
    /// This preserves exact type discovery while avoiding an eager request for every
    /// discovered leaf on IEDs that expose the normal MMS structure hierarchy.
    /// </summary>
    public async Task<IReadOnlyList<MmsVariableAccessAttributesResult>> GetVariableAccessAttributesSmartAsync(
        MmsIedModelDirectory directory,
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(directory);
        options ??= new MmsSmartDiscoveryOptions();

        var groups = directory.Points
            .Where(point => !string.IsNullOrWhiteSpace(point.Domain) && !string.IsNullOrWhiteSpace(point.MmsItemName))
            .GroupBy(
                point => BuildSmartTypeRoot(point),
                MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(group => group.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (groups.Length == 0)
            return Array.Empty<MmsVariableAccessAttributesResult>();

        var window = ResolveSmartDiscoveryWindow(options);
        using var gate = new SemaphoreSlim(window, window);

        var rootTasks = groups
            .Select(group => ReadVariableAttributesBoundedAsync(group.Key, gate, cancellationToken))
            .ToArray();
        var rootResults = await Task.WhenAll(rootTasks).ConfigureAwait(false);

        var results = new List<MmsVariableAccessAttributesResult>(rootResults.Length);
        results.AddRange(rootResults);

        var fallbackReferences = new List<MmsObjectReference>();
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var rootResult = rootResults[index];
            var hasDescendants = group.Any(point =>
                !point.MmsItemName.Equals(group.Key.Item, StringComparison.OrdinalIgnoreCase));
            var rootDescribesHierarchy = rootResult.IsSuccess &&
                                         (!hasDescendants || rootResult.TypeSpecification?.Children.Count > 0);

            if (rootDescribesHierarchy)
                continue;

            foreach (var point in group)
            {
                var reference = point.ToObjectReference();
                if (reference.Item.Equals(group.Key.Item, StringComparison.OrdinalIgnoreCase))
                    continue;
                fallbackReferences.Add(reference);
            }
        }

        var distinctFallbacks = fallbackReferences
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(reference => reference.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctFallbacks.Length > 0 && IsMmsInitiated)
        {
            var fallbackTasks = distinctFallbacks
                .Select(reference => ReadVariableAttributesBoundedAsync(reference, gate, cancellationToken))
                .ToArray();
            results.AddRange(await Task.WhenAll(fallbackTasks).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<MmsVariableAccessAttributesResult> ReadVariableAttributesBoundedAsync(
        MmsObjectReference reference,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await GetVariableAccessAttributesAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static MmsObjectReference BuildSmartTypeRoot(MmsFcResolvedPoint point)
    {
        var parts = point.MmsItemName.Split(
            '$',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var item = parts.Length >= 3
            ? string.Join('$', parts.Take(3))
            : point.MmsItemName;
        return new MmsObjectReference(point.Domain, item, point.FunctionalConstraint);
    }

    private sealed class MmsObjectReferenceKeyComparer : IEqualityComparer<MmsObjectReference>
    {
        public static MmsObjectReferenceKeyComparer Instance { get; } = new();

        public bool Equals(MmsObjectReference x, MmsObjectReference y)
            => x.Domain.Equals(y.Domain, StringComparison.OrdinalIgnoreCase) &&
               x.Item.Equals(y.Item, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(MmsObjectReference obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Domain ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item ?? string.Empty));
    }
}
