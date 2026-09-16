namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Convenience entry point for live-only callers. Canonical discovery code should
    /// prefer the overload that supplies logical-node root candidates from its semantic
    /// probe planner. The fallback ladder is LN root -> unresolved DO root -> unresolved
    /// leaf, so normal structured IEDs need only roughly one GVA per logical node.
    /// </summary>
    public Task<IReadOnlyList<MmsVariableAccessAttributesResult>> GetVariableAccessAttributesSmartAsync(
        MmsIedModelDirectory directory,
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var logicalNodeRoots = directory.LogicalDevices.Values
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(device => device.LogicalNodes.Values
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select(node => new MmsObjectReference(device.Name, node.Name, string.Empty)))
            .ToArray();

        return GetVariableAccessAttributesSmartAsync(
            directory,
            logicalNodeRoots,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Executes a bounded, coverage-aware type discovery ladder. A successful parent
    /// TypeSpecification suppresses all descendant probes it can actually prove; only
    /// unresolved branches descend to DO roots and finally exact leaves.
    /// </summary>
    public async Task<IReadOnlyList<MmsVariableAccessAttributesResult>> GetVariableAccessAttributesSmartAsync(
        MmsIedModelDirectory directory,
        IEnumerable<MmsObjectReference> logicalNodeRootCandidates,
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(logicalNodeRootCandidates);
        options ??= new MmsSmartDiscoveryOptions();

        var points = directory.Points
            .Where(point => !string.IsNullOrWhiteSpace(point.Domain) &&
                            !string.IsNullOrWhiteSpace(point.LogicalNode) &&
                            !string.IsNullOrWhiteSpace(point.MmsItemName))
            .OrderBy(point => point.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.MmsItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (points.Length == 0)
            return Array.Empty<MmsVariableAccessAttributesResult>();

        var logicalNodeRoots = logicalNodeRootCandidates
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Domain) &&
                                !string.IsNullOrWhiteSpace(reference.Item))
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(reference => reference.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (logicalNodeRoots.Length == 0)
            return Array.Empty<MmsVariableAccessAttributesResult>();

        var window = ResolveSmartDiscoveryWindow(options);
        var results = new List<MmsVariableAccessAttributesResult>(logicalNodeRoots.Length);

        var logicalNodeResults = await RunVariableAttributeBatchAsync(
                logicalNodeRoots,
                window,
                cancellationToken)
            .ConfigureAwait(false);
        results.AddRange(logicalNodeResults);

        var logicalNodeIndex = logicalNodeResults
            .GroupBy(result => result.Reference, MmsObjectReferenceKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Last(), MmsObjectReferenceKeyComparer.Instance);

        var unresolvedAfterLogicalNode = new List<MmsFcResolvedPoint>();
        foreach (var point in points)
        {
            var logicalNodeRoot = new MmsObjectReference(point.Domain, point.LogicalNode, string.Empty);
            if (logicalNodeIndex.TryGetValue(logicalNodeRoot, out var rootResult) &&
                MmsSmartTypeProbePolicy.Covers(rootResult, point.MmsItemName))
            {
                continue;
            }

            unresolvedAfterLogicalNode.Add(point);
        }

        if (unresolvedAfterLogicalNode.Count == 0 || !IsMmsInitiated)
            return results;

        var dataObjectRoots = unresolvedAfterLogicalNode
            .Select(MmsSmartTypeProbePolicy.BuildDataObjectRoot)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Item))
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(reference => reference.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dataObjectResults = await RunVariableAttributeBatchAsync(
                dataObjectRoots,
                window,
                cancellationToken)
            .ConfigureAwait(false);
        results.AddRange(dataObjectResults);

        var dataObjectIndex = dataObjectResults
            .GroupBy(result => result.Reference, MmsObjectReferenceKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Last(), MmsObjectReferenceKeyComparer.Instance);

        var leafFallbacks = new List<MmsObjectReference>();
        foreach (var point in unresolvedAfterLogicalNode)
        {
            var dataObjectRoot = MmsSmartTypeProbePolicy.BuildDataObjectRoot(point);
            if (dataObjectIndex.TryGetValue(dataObjectRoot, out var rootResult) &&
                MmsSmartTypeProbePolicy.Covers(rootResult, point.MmsItemName))
            {
                continue;
            }

            leafFallbacks.Add(point.ToObjectReference());
        }

        var distinctLeafFallbacks = leafFallbacks
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(reference => reference.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctLeafFallbacks.Length > 0 && IsMmsInitiated)
        {
            results.AddRange(await RunVariableAttributeBatchAsync(
                    distinctLeafFallbacks,
                    window,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return results;
    }

    private async Task<MmsVariableAccessAttributesResult[]> RunVariableAttributeBatchAsync(
        IReadOnlyList<MmsObjectReference> references,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
            return Array.Empty<MmsVariableAccessAttributesResult>();

        var results = new MmsVariableAccessAttributesResult?[references.Count];
        var nextIndex = -1;
        var workerCount = Math.Min(Math.Max(1, maxConcurrency), references.Count);
        var workers = new Task[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
            workers[worker] = WorkerAsync();

        await Task.WhenAll(workers).ConfigureAwait(false);

        var materialized = new MmsVariableAccessAttributesResult[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            materialized[index] = results[index] ?? BuildUnavailableVariableTypeResult(
                references[index],
                "Skipped because the MMS association became unavailable before this probe started.");
        }

        return materialized;

        async Task WorkerAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMmsInitiated)
                    return;

                var index = Interlocked.Increment(ref nextIndex);
                if (index >= references.Count)
                    return;

                var reference = references[index];
                results[index] = await ReadVariableAttributesSafeAsync(reference, cancellationToken)
                    .ConfigureAwait(false);

                if (!IsMmsInitiated)
                    return;
            }
        }
    }

    private async Task<MmsVariableAccessAttributesResult> ReadVariableAttributesSafeAsync(
        MmsObjectReference reference,
        CancellationToken cancellationToken)
    {
        if (!IsMmsInitiated)
            return BuildUnavailableVariableTypeResult(reference, "MMS association is unavailable.");

        try
        {
            return await GetVariableAccessAttributesAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BuildUnavailableVariableTypeResult(
                reference,
                "The receive pump stopped while this type probe was pending.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            return BuildUnavailableVariableTypeResult(
                reference,
                $"Type probe stopped safely after association fault: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static MmsVariableAccessAttributesResult BuildUnavailableVariableTypeResult(
        MmsObjectReference reference,
        string message)
        => new()
        {
            IsSuccess = false,
            Reference = reference,
            Message = message,
            Source = "SmartTypeProbe"
        };

    private sealed class MmsObjectReferenceKeyComparer : IEqualityComparer<MmsObjectReference>
    {
        public static MmsObjectReferenceKeyComparer Instance { get; } = new();

        public bool Equals(MmsObjectReference x, MmsObjectReference y)
            => string.Equals(x.Domain, y.Domain, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Item, y.Item, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(MmsObjectReference obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Domain ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item ?? string.Empty));
    }
}
