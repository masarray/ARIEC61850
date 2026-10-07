using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

public enum MmsCanonicalStaticLiveReconciliationStatus
{
    NoStaticCoverage,
    Ready,
    DomainBudgetExceeded,
    CandidateBudgetExceeded,
    Partial,
    AssociationLost
}

public sealed class MmsCanonicalStaticLiveDomainTarget
{
    public string Domain { get; init; } = string.Empty;
    public bool NeedsBuffered { get; init; }
    public bool NeedsUnbuffered { get; init; }
}

public sealed class MmsCanonicalStaticLiveReconciliationPlan
{
    public MmsCanonicalStaticLiveReconciliationStatus Status { get; init; }
    public IReadOnlyList<MmsCanonicalStaticLiveDomainTarget> Domains { get; init; }
        = Array.Empty<MmsCanonicalStaticLiveDomainTarget>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool RequiresNetwork => Status == MmsCanonicalStaticLiveReconciliationStatus.Ready &&
                                   Domains.Count > 0;
}

public sealed class MmsCanonicalStaticLiveReconciliationOptions
{
    /// <summary>
    /// Hard guard. Targeted reconciliation never silently truncates the configured domain set.
    /// </summary>
    public int MaxDomains { get; init; } = 16;

    /// <summary>
    /// Per-domain GetNameList bound. This is a safety envelope, not a target count.
    /// </summary>
    public int MaxVariableNamesPerDomain { get; init; } = 20000;

    public int MaxNameListPages { get; init; } = 64;

    /// <summary>
    /// Bounded independent domain chains, additionally capped by negotiated MMS outstandingCalling.
    /// </summary>
    public int MaxConcurrentDomains { get; init; } = 4;
    public int UnknownPeerMaxConcurrentDomains { get; init; } = 2;

    /// <summary>
    /// Maximum exact live RCB candidates that may be read for DatSet binding evidence.
    /// The operation fails closed rather than reading an arbitrary subset.
    /// </summary>
    public int MaxReportControlCandidates { get; init; } = 64;
}

public sealed class MmsCanonicalStaticLiveReconciliationResult
{
    public MmsCanonicalStaticLiveReconciliationPlan Plan { get; init; } = new();
    public MmsCanonicalStaticLiveReconciliationStatus Status { get; init; }
    public MmsReportInventory Inventory { get; init; } = new();
    public int EnumeratedVariableCount { get; init; }
    public int CandidateReportControlCount { get; init; }
    public int DataSetBoundReportControlCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasLiveInventory =>
        Inventory.Authority == MmsReportInventoryAuthority.LiveMmsObserved;

    public string Summary =>
        $"targeted live RCB reconciliation: status={Status}, domains={Plan.Domains.Count}, " +
        $"rawVariables={EnumeratedVariableCount}, candidates={CandidateReportControlCount}, " +
        $"datasetBound={DataSetBoundReportControlCount}, authority={Inventory.Authority}.";
}

public sealed class MmsCanonicalStaticSmartPreparationResult
{
    public CanonicalStaticReportCoveragePlan Coverage { get; init; } = new();
    public MmsCanonicalStaticLiveReconciliationResult Reconciliation { get; init; } = new();
    public MmsCanonicalStaticAcquisitionProbeResult Acquisition { get; init; } = new();

    public string Summary =>
        $"{Reconciliation.Summary} {Acquisition.Summary}";
}

/// <summary>
/// Pure planner for the minimum online structure needed to reconcile configured static
/// reporting. It extracts only MMS domains and BRCB/URCB families actually referenced by
/// selected static coverage. No runtime RCB instance is guessed here.
/// </summary>
public static class MmsCanonicalStaticLiveReconciliationPlanner
{
    public static MmsCanonicalStaticLiveReconciliationPlan Build(
        CanonicalStaticReportCoveragePlan coverage,
        MmsCanonicalStaticLiveReconciliationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        options ??= new MmsCanonicalStaticLiveReconciliationOptions();

        if (coverage.Segments.Length == 0 || coverage.CoveredSignalCount == 0)
        {
            return new MmsCanonicalStaticLiveReconciliationPlan
            {
                Status = MmsCanonicalStaticLiveReconciliationStatus.NoStaticCoverage
            };
        }

        var map = new Dictionary<string, (bool Buffered, bool Unbuffered)>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var report in coverage.Segments.SelectMany(segment => segment.ReportControls))
        {
            var domain = ResolveDomain(report.MmsDomain, report.Reference, report.DataSetReference);
            if (domain.Length == 0)
            {
                warnings.Add($"Configured RCB '{report.Reference}' has no resolvable MMS domain and cannot be reconciled online.");
                continue;
            }

            map.TryGetValue(domain, out var current);
            map[domain] = report.Buffered
                ? (true, current.Unbuffered)
                : (current.Buffered, true);
        }

        var domains = map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MmsCanonicalStaticLiveDomainTarget
            {
                Domain = pair.Key,
                NeedsBuffered = pair.Value.Buffered,
                NeedsUnbuffered = pair.Value.Unbuffered
            })
            .ToArray();

        var maxDomains = Math.Clamp(options.MaxDomains, 1, 256);
        if (domains.Length > maxDomains)
        {
            warnings.Add(
                $"Configured static coverage requires {domains.Length} MMS domains, exceeding targeted reconciliation budget {maxDomains}; no partial domain subset is authorized.");
            return new MmsCanonicalStaticLiveReconciliationPlan
            {
                Status = MmsCanonicalStaticLiveReconciliationStatus.DomainBudgetExceeded,
                Domains = domains,
                Warnings = warnings
            };
        }

        return new MmsCanonicalStaticLiveReconciliationPlan
        {
            Status = domains.Length == 0
                ? MmsCanonicalStaticLiveReconciliationStatus.Partial
                : MmsCanonicalStaticLiveReconciliationStatus.Ready,
            Domains = domains,
            Warnings = warnings
        };
    }

    private static string ResolveDomain(
        string mmsDomain,
        string reportReference,
        string dataSetReference)
    {
        if (!string.IsNullOrWhiteSpace(mmsDomain))
            return mmsDomain.Trim();

        foreach (var reference in new[] { reportReference, dataSetReference })
        {
            var normalized = reference?.Trim() ?? string.Empty;
            var slash = normalized.IndexOf('/');
            if (slash > 0)
                return normalized[..slash];
        }

        return string.Empty;
    }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Performs bounded online reconciliation for configured static reporting without
    /// running full IED discovery.
    ///
    /// Work is proportional to the exact MMS domains involved in selected static coverage:
    /// one NamedVariable GetNameList chain per domain, followed by whole-RCB/DatSet reads
    /// only for live RCB candidates of the required BRCB/URCB class. DataSet directories,
    /// FC points, unrelated domains and unrelated RCB classes are not discovered here.
    /// </summary>
    public async Task<MmsCanonicalStaticLiveReconciliationResult> ReconcileCanonicalStaticReportInventoryAsync(
        CanonicalStaticReportCoveragePlan coverage,
        MmsCanonicalStaticLiveReconciliationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        options ??= new MmsCanonicalStaticLiveReconciliationOptions();

        var plan = MmsCanonicalStaticLiveReconciliationPlanner.Build(coverage, options);
        var emptyInventory = new MmsReportInventory
        {
            Authority = MmsReportInventoryAuthority.LiveMmsObserved
        };

        if (!plan.RequiresNetwork)
        {
            return new MmsCanonicalStaticLiveReconciliationResult
            {
                Plan = plan,
                Status = plan.Status,
                Inventory = emptyInventory,
                Warnings = plan.Warnings
            };
        }

        EnsureMmsReady();

        var maxNames = Math.Clamp(options.MaxVariableNamesPerDomain, 1, 100000);
        var maxPages = Math.Clamp(options.MaxNameListPages, 1, 256);
        var smartOptions = new MmsSmartDiscoveryOptions
        {
            MaxConcurrentChains = Math.Clamp(options.MaxConcurrentDomains, 1, 64),
            UnknownPeerMaxConcurrentChains = Math.Clamp(options.UnknownPeerMaxConcurrentDomains, 1, 32)
        };
        var window = ResolveSmartDiscoveryWindow(smartOptions);

        var work = plan.Domains
            .Select(target => new SmartNameChainWorkItem(
                target.Domain,
                MmsGetNameListObjectClass.NamedVariable,
                maxNames))
            .ToArray();

        var chains = await RunSmartNameChainsAsync(
                work,
                window,
                maxPages,
                cancellationToken)
            .ConfigureAwait(false);

        if (!IsMmsInitiated)
        {
            return new MmsCanonicalStaticLiveReconciliationResult
            {
                Plan = plan,
                Status = MmsCanonicalStaticLiveReconciliationStatus.AssociationLost,
                Inventory = emptyInventory,
                EnumeratedVariableCount = chains.Sum(chain => chain.Names.Count),
                Warnings = plan.Warnings
                    .Concat(new[] { "MMS association became unavailable during targeted RCB reconciliation." })
                    .ToArray()
            };
        }

        var variables = chains.ToDictionary(
            chain => chain.Domain,
            chain => (IReadOnlyList<string>)chain.Names,
            StringComparer.Ordinal);

        var observed = MmsReportDiscoveryMapper.BuildInventory(new MmsDiscoverySnapshot
        {
            DomainVariables = variables,
            DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        });

        var targetsByDomain = plan.Domains.ToDictionary(
            target => target.Domain,
            StringComparer.Ordinal);

        var candidates = observed.ReportControls
            .Where(candidate =>
                targetsByDomain.TryGetValue(candidate.Domain, out var target) &&
                ((candidate.Buffered && target.NeedsBuffered) ||
                 (!candidate.Buffered && target.NeedsUnbuffered)))
            .OrderBy(candidate => candidate.Domain, StringComparer.Ordinal)
            .ThenByDescending(candidate => candidate.Buffered)
            .ThenBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();

        var maxCandidates = Math.Clamp(options.MaxReportControlCandidates, 1, 512);
        if (candidates.Length > maxCandidates)
        {
            return new MmsCanonicalStaticLiveReconciliationResult
            {
                Plan = plan,
                Status = MmsCanonicalStaticLiveReconciliationStatus.CandidateBudgetExceeded,
                Inventory = emptyInventory,
                EnumeratedVariableCount = variables.Values.Sum(list => list.Count),
                CandidateReportControlCount = candidates.Length,
                Warnings = plan.Warnings
                    .Concat(new[]
                    {
                        $"Target domains expose {candidates.Length} required-family RCB candidates, exceeding live reconciliation budget {maxCandidates}; no partial RCB read subset was started."
                    })
                    .ToArray()
            };
        }

        var inventory = new MmsReportInventory
        {
            Authority = MmsReportInventoryAuthority.LiveMmsObserved
        };
        inventory.ReportControls.AddRange(candidates);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await TryReadReportControlStructureSmartAsync(
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(candidate.DataSetReference))
            {
                await TryReadReportAttributeSmartAsync(
                        candidate,
                        "DatSet",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!IsMmsInitiated)
                break;
        }

        var complete = IsMmsInitiated && chains.All(chain => chain.IsComplete);
        var warnings = plan.Warnings
            .Concat(chains
                .Where(chain => !chain.IsComplete)
                .Select(chain => $"Target domain '{chain.Domain}' NamedVariable enumeration is partial: {chain.Message}"))
            .Concat(candidates
                .Where(candidate => string.IsNullOrWhiteSpace(candidate.DataSetReference))
                .Select(candidate => $"Live RCB '{candidate.Reference}' did not expose a usable DatSet binding."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new MmsCanonicalStaticLiveReconciliationResult
        {
            Plan = plan,
            Status = !IsMmsInitiated
                ? MmsCanonicalStaticLiveReconciliationStatus.AssociationLost
                : complete
                    ? MmsCanonicalStaticLiveReconciliationStatus.Ready
                    : MmsCanonicalStaticLiveReconciliationStatus.Partial,
            Inventory = inventory,
            EnumeratedVariableCount = variables.Values.Sum(list => list.Count),
            CandidateReportControlCount = candidates.Length,
            DataSetBoundReportControlCount = candidates.Count(candidate =>
                !string.IsNullOrWhiteSpace(candidate.DataSetReference)),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Preferred Open-SCL/Discovery-neutral static acquisition entry point.
    /// Canonical coverage is resolved locally first. Only when configured static coverage
    /// exists does the engine perform targeted live RCB reconciliation, then exact-target
    /// availability. Full discovery is never implied by this method.
    /// </summary>
    public async Task<MmsCanonicalStaticSmartPreparationResult> PrepareCanonicalStaticAcquisitionSmartAsync(
        CanonicalIedModel model,
        IEnumerable<CanonicalStaticReportSelection> selections,
        MmsIedModelDirectory? directory = null,
        MmsCanonicalStaticLiveReconciliationOptions? reconciliationOptions = null,
        MmsCanonicalStaticAcquisitionProbeOptions? acquisitionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selections);

        var requested = selections.ToArray();
        var coverage = CanonicalStaticReportCoverageResolver.Resolve(model, requested);

        var reconciliation = await ReconcileCanonicalStaticReportInventoryAsync(
                coverage,
                reconciliationOptions,
                cancellationToken)
            .ConfigureAwait(false);

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            coverage,
            reconciliation.Inventory);

        var acquisition = await ProbeCanonicalStaticAcquisitionAsync(
                plan,
                reconciliation.Inventory,
                directory,
                acquisitionOptions,
                cancellationToken)
            .ConfigureAwait(false);

        return new MmsCanonicalStaticSmartPreparationResult
        {
            Coverage = coverage,
            Reconciliation = reconciliation,
            Acquisition = acquisition
        };
    }
}
