namespace AR.Iec61850.Mms;

/// <summary>
/// Capture-informed discovery policy for building a usable IEC 61850 model with
/// fewer round trips. Independent GetNameList chains may run concurrently, while
/// continuation pages inside one chain always remain sequential.
/// </summary>
public sealed class MmsSmartDiscoveryOptions
{
    /// <summary>
    /// Requested upper bound for independent domain/object-class discovery chains.
    /// The effective value is additionally capped by the peer's negotiated
    /// maxOutstandingCalling value when that evidence is available.
    /// </summary>
    public int MaxConcurrentChains { get; init; } = 8;

    /// <summary>
    /// Conservative cap used when the peer's maxOutstandingCalling value could not
    /// be decoded from InitiateResponse.
    /// </summary>
    public int UnknownPeerMaxConcurrentChains { get; init; } = 4;

    public int MaxDomains { get; init; } = 256;
    public int MaxVariableNamesPerDomain { get; init; } = 20000;
    public int MaxVariableListNamesPerDomain { get; init; } = 4096;
    public bool ProbeReportAttributes { get; init; } = true;
    public int MaxReportAttributeProbes { get; init; } = 32;
    public bool ReadDataSetDirectories { get; init; }
    public int MaxDataSetDirectoryReads { get; init; } = 64;
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Builds the same structural discovery result as the legacy discovery path,
    /// but enumerates VMD domains once and pipelines independent per-domain
    /// GetNameList chains. No eager per-leaf Read or GetVariableAccessAttributes
    /// sweep is performed by this method.
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
        var effectiveWindow = ResolveSmartDiscoveryWindow(options);

        var domainsResult = await GetNameListPagedAsync(
                MmsGetNameListObjectClass.Domain,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var domains = domainsResult.IsSuccess
            ? domainsResult.Names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(maxDomains)
                .ToArray()
            : Array.Empty<string>();

        using var gate = new SemaphoreSlim(effectiveWindow, effectiveWindow);
        var chainTasks = new List<Task<SmartNameChainResult>>(domains.Length * 2);
        foreach (var domain in domains)
        {
            chainTasks.Add(ReadSmartNameChainAsync(
                domain,
                MmsGetNameListObjectClass.NamedVariable,
                maxVariables,
                gate,
                cancellationToken));
            chainTasks.Add(ReadSmartNameChainAsync(
                domain,
                MmsGetNameListObjectClass.NamedVariableList,
                maxVariableLists,
                gate,
                cancellationToken));
        }

        var chains = chainTasks.Count == 0
            ? Array.Empty<SmartNameChainResult>()
            : await Task.WhenAll(chainTasks).ConfigureAwait(false);

        // Final dictionaries are rebuilt in sorted domain order so completion timing
        // never changes the published model ordering.
        var variablesByKey = chains
            .Where(chain => chain.ObjectClass == MmsGetNameListObjectClass.NamedVariable)
            .ToDictionary(chain => chain.Domain, StringComparer.OrdinalIgnoreCase);
        var listsByKey = chains
            .Where(chain => chain.ObjectClass == MmsGetNameListObjectClass.NamedVariableList)
            .ToDictionary(chain => chain.Domain, StringComparer.OrdinalIgnoreCase);

        var domainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var domainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domains)
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

        if (options.ProbeReportAttributes)
        {
            await EnrichReportInventoryAsync(
                    inventory,
                    Math.Max(0, options.MaxReportAttributeProbes),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var dataSetReferences = options.ReadDataSetDirectories
            ? inventory.DataSets
                .Select(dataSet => dataSet.Reference)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(options.MaxDataSetDirectoryReads, 0, 4096))
                .ToArray()
            : Array.Empty<string>();

        var dataSetDirectories = dataSetReferences.Length == 0
            ? Array.Empty<MmsDataSetDirectoryResult>()
            : (await GetDataSetDirectoriesAsync(
                    dataSetReferences,
                    iedDirectory,
                    cancellationToken)
                .ConfigureAwait(false))
                .ToArray();

        var failedChains = chains.Count(chain => !chain.IsSuccess);
        var successfulDataSetDirectories = dataSetDirectories.Count(directory => directory.IsSuccess);
        var discoveredDataSetMembers = dataSetDirectories
            .Where(directory => directory.IsSuccess)
            .Sum(directory => directory.Members.Count);
        var negotiated = LastNegotiatedCapabilities.MaxOutstandingCalling;
        var negotiatedText = negotiated.HasValue ? negotiated.Value.ToString() : "unknown";
        var domainStatus = domainsResult.IsSuccess
            ? $"domains={domains.Length}"
            : $"domain-list-failed={domainsResult.Message}";
        var dataSetDirectorySummary = options.ReadDataSetDirectories
            ? $"dataset directories={successfulDataSetDirectories}/{dataSetDirectories.Length}, dataset members={discoveredDataSetMembers}"
            : "dataset directories=not requested";

        LastDiscoveryAttemptSummary =
            $"Smart discovery: {domainStatus}, chains={chains.Length}, failedChains={failedChains}, " +
            $"window={effectiveWindow}, negotiatedCalling={negotiatedText}.";

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

        var requested = Math.Clamp(options.MaxConcurrentChains, 1, 32);
        var unknownPeerCap = Math.Clamp(options.UnknownPeerMaxConcurrentChains, 1, 16);
        var negotiated = LastNegotiatedCapabilities.MaxOutstandingCalling;

        if (negotiated is > 0)
            return Math.Max(1, Math.Min(requested, negotiated.Value));

        return Math.Min(requested, unknownPeerCap);
    }

    private async Task<SmartNameChainResult> ReadSmartNameChainAsync(
        string domain,
        MmsGetNameListObjectClass objectClass,
        int maxNames,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await GetNameListPagedAsync(objectClass, domain, cancellationToken).ConfigureAwait(false);
            var names = result.Names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(maxNames)
                .ToArray();

            return new SmartNameChainResult(domain, objectClass, result.IsSuccess, names, result.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record SmartNameChainResult(
        string Domain,
        MmsGetNameListObjectClass ObjectClass,
        bool IsSuccess,
        IReadOnlyList<string> Names,
        string Message);
}
