namespace AR.Iec61850.Mms;

/// <summary>
/// Diagnostic request-budget evidence for one smart hierarchy GVA pass. Counts describe
/// the bounded probe ladder; they never influence canonical model semantics.
/// </summary>
public sealed class MmsSmartTypeProbeBudgetSnapshot
{
    public int DirectoryPoints { get; init; }
    public int SuppliedLogicalNodeCandidates { get; init; }
    public int SuppressedNonLiveLogicalNodeCandidates { get; init; }
    public int LogicalNodeRequests { get; init; }
    public int PointsCoveredByLogicalNode { get; init; }
    public int DataObjectRequests { get; init; }
    public int PointsCoveredByDataObject { get; init; }
    public int ExactLeafRequests { get; init; }
    public int SuppressedExactRepeatRequests { get; init; }
    public int PointsCoveredByExactLeaf { get; init; }
    public int RemainingUnresolvedPoints { get; init; }
    public int TotalPlannedRequests => LogicalNodeRequests + DataObjectRequests + ExactLeafRequests;

    public string Summary =>
        $"Smart type budget: points={DirectoryPoints}, LN={LogicalNodeRequests}, " +
        $"DO={DataObjectRequests}, leaf={ExactLeafRequests}, total={TotalPlannedRequests}, " +
        $"covered(LN/DO/leaf)={PointsCoveredByLogicalNode}/{PointsCoveredByDataObject}/{PointsCoveredByExactLeaf}, " +
        $"suppressed(nonLive/repeat)={SuppressedNonLiveLogicalNodeCandidates}/{SuppressedExactRepeatRequests}, " +
        $"unresolved={RemainingUnresolvedPoints}.";
}

public sealed partial class MmsClientSession
{
    private MmsSmartTypeProbeBudgetSnapshot? _lastSmartTypeProbeBudget;

    /// <summary>
    /// Most recent hierarchy GVA budget. This is local diagnostic evidence only and
    /// does not issue requests or alter the discovered IEC 61850 model.
    /// </summary>
    public MmsSmartTypeProbeBudgetSnapshot? LastSmartTypeProbeBudget
        => Volatile.Read(ref _lastSmartTypeProbeBudget);

    /// <summary>
    /// Convenience entry point for live-only callers. Canonical discovery code should
    /// prefer the overload that supplies logical-node root candidates from its semantic
    /// probe planner. The fallback ladder is LN root -> unresolved DO root -> unresolved
    /// exact leaf, with exact-reference repeat suppression across tiers.
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
    /// TypeSpecification suppresses every descendant probe it can prove. Only unresolved
    /// branches descend to distinct DO roots and finally exact leaves. An exact GVA
    /// reference is never reissued inside one pass merely because a shallower tier failed.
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
        {
            PublishSmartTypeProbeBudget(new MmsSmartTypeProbeBudgetSnapshot());
            return Array.Empty<MmsVariableAccessAttributesResult>();
        }

        var suppliedCandidates = logicalNodeRootCandidates
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Domain) &&
                                !string.IsNullOrWhiteSpace(reference.Item) &&
                                !reference.Item.Contains('$', StringComparison.Ordinal))
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .ToArray();
        var logicalNodeRoots = MmsSmartTypeProbePolicy.SelectLiveLogicalNodeRoots(directory, suppliedCandidates);
        var suppressedNonLiveRoots = Math.Max(0, suppliedCandidates.Length - logicalNodeRoots.Length);
        if (logicalNodeRoots.Length == 0)
        {
            PublishSmartTypeProbeBudget(new MmsSmartTypeProbeBudgetSnapshot
            {
                DirectoryPoints = points.Length,
                SuppliedLogicalNodeCandidates = suppliedCandidates.Length,
                SuppressedNonLiveLogicalNodeCandidates = suppressedNonLiveRoots,
                RemainingUnresolvedPoints = points.Length
            });
            return Array.Empty<MmsVariableAccessAttributesResult>();
        }

        var window = ResolveSmartDiscoveryWindow(options);
        var results = new List<MmsVariableAccessAttributesResult>(logicalNodeRoots.Length);
        var probedReferences = new List<MmsObjectReference>(logicalNodeRoots);

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

        var coveredByLogicalNode = points.Length - unresolvedAfterLogicalNode.Count;
        if (unresolvedAfterLogicalNode.Count == 0 || !IsMmsInitiated)
        {
            PublishSmartTypeProbeBudget(new MmsSmartTypeProbeBudgetSnapshot
            {
                DirectoryPoints = points.Length,
                SuppliedLogicalNodeCandidates = suppliedCandidates.Length,
                SuppressedNonLiveLogicalNodeCandidates = suppressedNonLiveRoots,
                LogicalNodeRequests = logicalNodeRoots.Length,
                PointsCoveredByLogicalNode = coveredByLogicalNode,
                RemainingUnresolvedPoints = unresolvedAfterLogicalNode.Count
            });
            return results;
        }

        var dataObjectRoots = unresolvedAfterLogicalNode
            .Select(MmsSmartTypeProbePolicy.BuildDataObjectRoot)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Item))
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .OrderBy(reference => reference.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        probedReferences.AddRange(dataObjectRoots);

        var dataObjectResults = await RunVariableAttributeBatchAsync(
                dataObjectRoots,
                window,
                cancellationToken)
            .ConfigureAwait(false);
        results.AddRange(dataObjectResults);

        var dataObjectIndex = dataObjectResults
            .GroupBy(result => result.Reference, MmsObjectReferenceKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Last(), MmsObjectReferenceKeyComparer.Instance);

        var unresolvedAfterDataObject = new List<MmsFcResolvedPoint>();
        foreach (var point in unresolvedAfterLogicalNode)
        {
            var dataObjectRoot = MmsSmartTypeProbePolicy.BuildDataObjectRoot(point);
            if (dataObjectIndex.TryGetValue(dataObjectRoot, out var rootResult) &&
                MmsSmartTypeProbePolicy.Covers(rootResult, point.MmsItemName))
            {
                continue;
            }

            unresolvedAfterDataObject.Add(point);
        }

        var coveredByDataObject = unresolvedAfterLogicalNode.Count - unresolvedAfterDataObject.Count;
        if (unresolvedAfterDataObject.Count == 0 || !IsMmsInitiated)
        {
            PublishSmartTypeProbeBudget(new MmsSmartTypeProbeBudgetSnapshot
            {
                DirectoryPoints = points.Length,
                SuppliedLogicalNodeCandidates = suppliedCandidates.Length,
                SuppressedNonLiveLogicalNodeCandidates = suppressedNonLiveRoots,
                LogicalNodeRequests = logicalNodeRoots.Length,
                PointsCoveredByLogicalNode = coveredByLogicalNode,
                DataObjectRequests = dataObjectRoots.Length,
                PointsCoveredByDataObject = coveredByDataObject,
                RemainingUnresolvedPoints = unresolvedAfterDataObject.Count
            });
            return results;
        }

        var exactFallbackCandidates = unresolvedAfterDataObject
            .Select(point => point.ToObjectReference())
            .Distinct(MmsObjectReferenceKeyComparer.Instance)
            .ToArray();
        var exactLeafFallbacks = MmsSmartTypeProbePolicy.BuildUnprobedExactFallbacks(
            unresolvedAfterDataObject,
            probedReferences);
        var suppressedExactRepeats = Math.Max(0, exactFallbackCandidates.Length - exactLeafFallbacks.Length);

        MmsVariableAccessAttributesResult[] exactLeafResults = Array.Empty<MmsVariableAccessAttributesResult>();
        if (exactLeafFallbacks.Length > 0 && IsMmsInitiated)
        {
            exactLeafResults = await RunVariableAttributeBatchAsync(
                    exactLeafFallbacks,
                    window,
                    cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(exactLeafResults);
        }

        var exactLeafIndex = exactLeafResults
            .GroupBy(result => result.Reference, MmsObjectReferenceKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Last(), MmsObjectReferenceKeyComparer.Instance);
        var remainingUnresolved = 0;
        foreach (var point in unresolvedAfterDataObject)
        {
            var exactReference = point.ToObjectReference();
            if (exactLeafIndex.TryGetValue(exactReference, out var exactResult) &&
                MmsSmartTypeProbePolicy.Covers(exactResult, point.MmsItemName))
            {
                continue;
            }

            remainingUnresolved++;
        }

        var coveredByExactLeaf = unresolvedAfterDataObject.Count - remainingUnresolved;
        PublishSmartTypeProbeBudget(new MmsSmartTypeProbeBudgetSnapshot
        {
            DirectoryPoints = points.Length,
            SuppliedLogicalNodeCandidates = suppliedCandidates.Length,
            SuppressedNonLiveLogicalNodeCandidates = suppressedNonLiveRoots,
            LogicalNodeRequests = logicalNodeRoots.Length,
            PointsCoveredByLogicalNode = coveredByLogicalNode,
            DataObjectRequests = dataObjectRoots.Length,
            PointsCoveredByDataObject = coveredByDataObject,
            ExactLeafRequests = exactLeafFallbacks.Length,
            SuppressedExactRepeatRequests = suppressedExactRepeats,
            PointsCoveredByExactLeaf = coveredByExactLeaf,
            RemainingUnresolvedPoints = remainingUnresolved
        });

        return results;
    }

    private void PublishSmartTypeProbeBudget(MmsSmartTypeProbeBudgetSnapshot snapshot)
        => Volatile.Write(ref _lastSmartTypeProbeBudget, snapshot);

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

        using var observation = ObserveSmartDiscoveryRequest(
            "type-enrichment",
            "GetVariableAccessAttributes",
            $"{reference.Domain}/{reference.Item}");

        try
        {
            var result = await GetVariableAccessAttributesAsync(reference, cancellationToken).ConfigureAwait(false);
            observation.Complete(result.IsSuccess);
            return result;
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
