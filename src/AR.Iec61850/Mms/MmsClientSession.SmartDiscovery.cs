using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

/// <summary>
/// Capture-informed discovery policy for building a usable IEC 61850 model with
/// bounded work. Live MMS remains authoritative; optional domain hints only affect
/// scheduling order and never filter or manufacture online evidence.
/// </summary>
public sealed class MmsSmartDiscoveryOptions
{
    /// <summary>
    /// Requested upper bound for independent discovery operations. The effective
    /// value is additionally capped by the peer's negotiated maxOutstandingCalling
    /// when that evidence is available.
    /// </summary>
    public int MaxConcurrentChains { get; init; } = 8;

    /// <summary>
    /// Conservative cap used when maxOutstandingCalling could not be decoded.
    /// </summary>
    public int UnknownPeerMaxConcurrentChains { get; init; } = 4;

    public int MaxDomains { get; init; } = 256;
    public int MaxVariableNamesPerDomain { get; init; } = 20000;
    public int MaxVariableListNamesPerDomain { get; init; } = 4096;

    /// <summary>
    /// Hard guard for one GetNameList continuation chain. It prevents a broken IED
    /// from keeping discovery alive indefinitely while still preserving partial data.
    /// </summary>
    public int MaxNameListPages { get; init; } = 64;

    /// <summary>
    /// Exact MMS domain identifiers that should be scheduled first after live domain
    /// enumeration. This is suitable for SCL expected-domain hints. It never changes
    /// the selected live domain set and extra live domains are still discovered.
    /// </summary>
    public IReadOnlyList<string> PriorityDomains { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Report attribute reads are enrichment, not structural discovery. Keep them off
    /// by default so a usable LD/LN/DO/DA tree is not blocked by many value reads.
    /// </summary>
    public bool ProbeReportAttributes { get; init; }
    public int MaxReportAttributeProbes { get; init; } = 32;

    public bool ReadDataSetDirectories { get; init; }
    public int MaxDataSetDirectoryReads { get; init; } = 64;
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Builds a structural MMS model with one VMD-domain enumeration and a fixed-size
    /// worker pool for independent per-domain chains. Continuation pages inside one
    /// chain are always sequential. Expected association loss is converted into a
    /// partial result instead of allowing queued workers to crash Task.WhenAll.
    /// </summary>
    public async Task<MmsDiscoveryResult> DiscoverSmartAsync(
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        options ??= new MmsSmartDiscoveryOptions();

        var maxDomains = Math.Clamp(options.MaxDomains, 1, 4096);
        var maxVariables = Math.Clamp(options.MaxVariableNamesPerDomain, 1, 100000);
        var maxVariableLists = Math.Clamp(options.MaxVariableListNamesPerDomain, 1, 20000);
        var maxPages = Math.Clamp(options.MaxNameListPages, 1, 256);
        var effectiveWindow = ResolveSmartDiscoveryWindow(options);

        var domainsResult = await GetNameListPagedSmartAsync(
                MmsGetNameListObjectClass.Domain,
                null,
                maxDomains,
                maxPages,
                cancellationToken)
            .ConfigureAwait(false);

        var publishedDomains = domainsResult.IsSuccess
            ? MmsSmartDiscoveryPolicy.SelectPublishedDomains(domainsResult.Names, maxDomains)
            : Array.Empty<string>();
        var scheduledDomains = MmsSmartDiscoveryPolicy.OrderDomainsForScheduling(
            publishedDomains,
            options.PriorityDomains);

        var workItems = new SmartNameChainWorkItem[scheduledDomains.Length * 2];
        var workIndex = 0;
        foreach (var domain in scheduledDomains)
        {
            workItems[workIndex++] = new SmartNameChainWorkItem(
                domain,
                MmsGetNameListObjectClass.NamedVariable,
                maxVariables);
            workItems[workIndex++] = new SmartNameChainWorkItem(
                domain,
                MmsGetNameListObjectClass.NamedVariableList,
                maxVariableLists);
        }

        var chains = await RunSmartNameChainsAsync(
                workItems,
                effectiveWindow,
                maxPages,
                cancellationToken)
            .ConfigureAwait(false);

        var variablesByKey = chains
            .Where(chain => chain.ObjectClass == MmsGetNameListObjectClass.NamedVariable)
            .ToDictionary(chain => chain.Domain, StringComparer.OrdinalIgnoreCase);
        var listsByKey = chains
            .Where(chain => chain.ObjectClass == MmsGetNameListObjectClass.NamedVariableList)
            .ToDictionary(chain => chain.Domain, StringComparer.OrdinalIgnoreCase);

        // Publication order is derived from live evidence, not SCL scheduling hints.
        var domainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var domainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in publishedDomains)
        {
            domainVariables[domain] = variablesByKey.TryGetValue(domain, out var variables)
                ? variables.Names
                : Array.Empty<string>();
            domainVariableLists[domain] = listsByKey.TryGetValue(domain, out var lists)
                ? lists.Names
                : Array.Empty<string>();
        }

        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = domainVariables,
            DomainVariableLists = domainVariableLists
        };

        var inventory = MmsReportDiscoveryMapper.BuildInventory(snapshot);
        var iedDirectory = MmsIedModelDirectoryBuilder.Build(snapshot);

        if (options.ProbeReportAttributes && IsMmsInitiated)
        {
            await EnrichReportInventoryAsync(
                    inventory,
                    Math.Max(0, options.MaxReportAttributeProbes),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var dataSetReferences = options.ReadDataSetDirectories && IsMmsInitiated
            ? inventory.DataSets
                .Select(dataSet => dataSet.Reference)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(options.MaxDataSetDirectoryReads, 0, 4096))
                .ToArray()
            : Array.Empty<string>();

        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories;
        if (dataSetReferences.Length == 0)
        {
            dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
        }
        else
        {
            try
            {
                dataSetDirectories = (await GetDataSetDirectoriesAsync(
                        dataSetReferences,
                        iedDirectory,
                        cancellationToken)
                    .ConfigureAwait(false))
                    .ToArray();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
            }
            catch (Exception ex) when (IsExpectedSmartDiscoveryFault(ex))
            {
                dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
            }
        }

        var incompleteChains = chains.Count(chain => !chain.IsComplete);
        var successfulDataSetDirectories = dataSetDirectories.Count(directory => directory.IsSuccess);
        var discoveredDataSetMembers = dataSetDirectories
            .Where(directory => directory.IsSuccess)
            .Sum(directory => directory.Members.Count);
        var negotiated = LastNegotiatedCapabilities.MaxOutstandingCalling;
        var negotiatedText = negotiated.HasValue ? negotiated.Value.ToString() : "unknown";
        var domainStatus = !domainsResult.IsSuccess
            ? $"domain-list-failed={domainsResult.Message}"
            : domainsResult.MoreFollows
                ? $"domains={publishedDomains.Length}, domain-list=partial"
                : $"domains={publishedDomains.Length}, domain-list=complete";
        var dataSetDirectorySummary = options.ReadDataSetDirectories
            ? $"dataset directories={successfulDataSetDirectories}/{dataSetReferences.Length}, dataset members={discoveredDataSetMembers}"
            : "dataset directories=deferred";
        var reportSummary = options.ProbeReportAttributes
            ? "report enrichment=requested"
            : "report enrichment=deferred";

        LastDiscoveryAttemptSummary =
            $"Smart discovery: {domainStatus}, chains={chains.Length}, incompleteChains={incompleteChains}, " +
            $"window={effectiveWindow}, negotiatedCalling={negotiatedText}, {reportSummary}.";

        return new MmsDiscoveryResult
        {
            Snapshot = snapshot,
            ReportInventory = inventory,
            IedDirectory = iedDirectory,
            DataSetDirectories = dataSetDirectories,
            Summary =
                $"Smart MMS discovery: LD={snapshot.DomainCount}, raw variables={snapshot.RawVariableCount}, " +
                $"FC-points={iedDirectory.PointCount}, datasets={inventory.DataSets.Count}, {dataSetDirectorySummary}, " +
                $"RCB={inventory.ReportControls.Count} (BRCB={inventory.BufferedCount}, URCB={inventory.UnbufferedCount}). " +
                LastDiscoveryAttemptSummary
        };
    }

    internal int ResolveSmartDiscoveryWindow(MmsSmartDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return MmsSmartDiscoveryPolicy.ResolveWindow(
            options.MaxConcurrentChains,
            options.UnknownPeerMaxConcurrentChains,
            LastNegotiatedCapabilities.MaxOutstandingCalling);
    }

    private async Task<SmartNameChainResult[]> RunSmartNameChainsAsync(
        IReadOnlyList<SmartNameChainWorkItem> workItems,
        int maxConcurrency,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (workItems.Count == 0)
            return Array.Empty<SmartNameChainResult>();

        var results = new SmartNameChainResult?[workItems.Count];
        var nextIndex = -1;
        var workerCount = Math.Min(Math.Max(1, maxConcurrency), workItems.Count);
        var workers = new Task[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
            workers[worker] = WorkerAsync();

        await Task.WhenAll(workers).ConfigureAwait(false);

        var materialized = new SmartNameChainResult[workItems.Count];
        for (var index = 0; index < workItems.Count; index++)
        {
            materialized[index] = results[index] ?? SmartNameChainResult.Skipped(
                workItems[index],
                "Skipped because the MMS association became unavailable before this chain started.");
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
                if (index >= workItems.Count)
                    return;

                var item = workItems[index];
                results[index] = await ReadSmartNameChainSafeAsync(
                        item,
                        maxPages,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!IsMmsInitiated)
                    return;
            }
        }
    }

    private async Task<SmartNameChainResult> ReadSmartNameChainSafeAsync(
        SmartNameChainWorkItem item,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (!IsMmsInitiated)
            return SmartNameChainResult.Skipped(item, "MMS association is unavailable.");

        try
        {
            var result = await GetNameListPagedSmartAsync(
                    item.ObjectClass,
                    item.Domain,
                    item.MaxNames,
                    maxPages,
                    cancellationToken)
                .ConfigureAwait(false);

            var names = result.Names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(item.MaxNames)
                .ToArray();

            return new SmartNameChainResult(
                item.Domain,
                item.ObjectClass,
                result.IsSuccess && !result.MoreFollows,
                names,
                result.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SmartNameChainResult.Skipped(
                item,
                "The receive pump stopped while this discovery chain was pending.");
        }
        catch (Exception ex) when (IsExpectedSmartDiscoveryFault(ex))
        {
            return SmartNameChainResult.Skipped(
                item,
                $"Discovery chain stopped safely after association fault: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Smart-path pager with O(n) boundary de-duplication plus explicit page, name,
    /// and continuation-token guards. It intentionally does not use the legacy
    /// List.Contains de-duplication loop on large directories.
    /// </summary>
    private async Task<MmsNameListResult> GetNameListPagedSmartAsync(
        MmsGetNameListObjectClass objectClass,
        string? domainId,
        int maxNames,
        int maxPages,
        CancellationToken cancellationToken)
    {
        EnsureMmsReady();

        var boundedNames = Math.Clamp(maxNames, 1, 100000);
        var boundedPages = Math.Clamp(maxPages, 1, 256);
        var names = new List<string>(Math.Min(boundedNames, 1024));
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenContinuationTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var continueAfter = string.Empty;
        var page = 0;
        var incomplete = false;
        var stopReason = string.Empty;
        MmsNameListResult? last = null;

        while (page < boundedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            page++;

            var invokeId = NextInvokeId();
            var request = MmsGetNameListRequest.Build(
                invokeId,
                objectClass,
                domainId,
                string.IsNullOrWhiteSpace(continueAfter) ? null : continueAfter);
            LastDiscoveryRequestHex = HexDump.ToCompactString(request);

            try
            {
                var response = await SendConfirmedPresentationPayloadAsync(
                        request,
                        invokeId,
                        cancellationToken)
                    .ConfigureAwait(false);
                last = MmsGetNameListResponseDecoder.Decode(response, invokeId);
                LastDiscoveryResponseHex = last.ResponseHexPreview;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsExpectedSmartDiscoveryFault(ex))
            {
                await MarkProtocolFaultAsync().ConfigureAwait(false);
                return new MmsNameListResult
                {
                    IsSuccess = false,
                    Names = names,
                    MoreFollows = false,
                    Message = $"GetNameList {objectClass}/{domainId ?? "VMD"} transport fault on page {page}: {ex.GetType().Name}: {ex.Message}",
                    ResponseHexPreview = LastDiscoveryResponseHex
                };
            }

            if (!last.IsSuccess)
            {
                return new MmsNameListResult
                {
                    IsSuccess = false,
                    Names = names,
                    MoreFollows = false,
                    Message = last.Message,
                    ResponseHexPreview = last.ResponseHexPreview
                };
            }

            var before = names.Count;
            foreach (var rawName in last.Names)
            {
                if (string.IsNullOrWhiteSpace(rawName))
                    continue;

                var name = rawName.Trim();
                if (seenNames.Add(name) && names.Count < boundedNames)
                    names.Add(name);
            }

            var newCount = names.Count - before;
            if (!last.MoreFollows)
                break;

            if (names.Count >= boundedNames)
            {
                incomplete = true;
                stopReason = $"name limit {boundedNames} reached";
                break;
            }

            if (newCount <= 0)
            {
                incomplete = true;
                stopReason = "IED returned no new names while moreFollows remained true";
                break;
            }

            var nextContinueAfter = last.Names.LastOrDefault()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nextContinueAfter))
            {
                incomplete = true;
                stopReason = "IED returned moreFollows without a continuation token";
                break;
            }

            if (!seenContinuationTokens.Add(nextContinueAfter))
            {
                incomplete = true;
                stopReason = $"continuation token repeated ({nextContinueAfter})";
                break;
            }

            continueAfter = nextContinueAfter;
        }

        if (page >= boundedPages && last?.MoreFollows == true)
        {
            incomplete = true;
            stopReason = $"page limit {boundedPages} reached";
        }

        var moreFollows = incomplete || (last?.MoreFollows ?? false);
        var suffix = string.IsNullOrWhiteSpace(stopReason) ? string.Empty : $" Stopped safely: {stopReason}.";
        return new MmsNameListResult
        {
            IsSuccess = true,
            Names = names,
            MoreFollows = moreFollows,
            MoreFollowsWasPresent = last?.MoreFollowsWasPresent ?? false,
            Message = $"Smart GetNameList {objectClass}/{domainId ?? "VMD"}: names={names.Count}, pages={page}, complete={!moreFollows}.{suffix}",
            ResponseHexPreview = last?.ResponseHexPreview ?? string.Empty
        };
    }

    private static bool IsExpectedSmartDiscoveryFault(Exception ex)
        => ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException;

    private sealed record SmartNameChainWorkItem(
        string Domain,
        MmsGetNameListObjectClass ObjectClass,
        int MaxNames);

    private sealed record SmartNameChainResult(
        string Domain,
        MmsGetNameListObjectClass ObjectClass,
        bool IsComplete,
        IReadOnlyList<string> Names,
        string Message)
    {
        public static SmartNameChainResult Skipped(SmartNameChainWorkItem item, string message)
            => new(item.Domain, item.ObjectClass, false, Array.Empty<string>(), message);
    }
}
