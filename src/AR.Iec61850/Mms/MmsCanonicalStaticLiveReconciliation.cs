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
    /// Maximum RCB Read candidates PER PAGE. A partial catalog is resumable;
    /// the total number of unrelated RCBs must never abort selected discovery.
    /// </summary>
    public int MaxReportControlCandidates { get; init; } = 64;

    /// <summary>Bounded batches per invocation; never a global catalog cap.</summary>
    public int MaxCandidatePagesPerInvocation { get; init; } = 8;

    /// <summary>Checked catalog continuation from a previously partial reconciliation.</summary>
    public string? CandidateContinuationToken { get; init; }
}

public sealed class MmsCanonicalStaticLiveReconciliationResult
{
    public MmsCanonicalStaticLiveReconciliationPlan Plan { get; init; } = new();
    public MmsCanonicalStaticLiveReconciliationStatus Status { get; init; }
    public MmsReportInventory Inventory { get; init; } = new();
    public int EnumeratedVariableCount { get; init; }
    public int CandidateReportControlCount { get; init; }
    public int DataSetBoundReportControlCount { get; init; }
    public int ExaminedReportControlCount { get; init; }
    public int DeferredReportControlCount { get; init; }
    public string? CandidateContinuationToken { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasLiveInventory =>
        Inventory.Authority == MmsReportInventoryAuthority.LiveMmsObserved;

    public string Summary =>
        $"targeted live RCB reconciliation: status={Status}, domains={Plan.Domains.Count}, " +
        $"rawVariables={EnumeratedVariableCount}, candidates={CandidateReportControlCount}, " +
        $"datasetBound={DataSetBoundReportControlCount}, examined={ExaminedReportControlCount}, " +
        $"deferred={DeferredReportControlCount}, authority={Inventory.Authority}.";
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

        // The raw RCB directory has no authoritative DatSet association yet.
        // Stage A: index by the exact selected DataSet and configured references.
        // Stage B: read only bounded pages of still-unknown compatible candidates.
        // Never reject the complete operation because *global* RCB count is 66/300.
        // Directory/catalog objects remain immutable while pages are calculated;
        // successful MMS reads enrich separate candidates, not continuation state.
        var maxReadsPerPage = Math.Clamp(options.MaxReportControlCandidates, 1, 512);
        var maxReadPages = Math.Clamp(options.MaxCandidatePagesPerInvocation, 1, 32);
        var expectedDataSets = coverage.Segments
            .Where(segment => segment.SelectedSignalReferences.Length > 0)
            .Select(segment => MmsRcbAvailabilityEvaluator.NormalizeReference(segment.DataSetReference))
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var inventory = new MmsReportInventory
        {
            Authority = MmsReportInventoryAuthority.LiveMmsObserved
        };
        var examined = 0;
        var rejected = 0;
        var catalogCount = 0;
        var eligibleProbeCount = 0;
        var pages = 0;
        var continuation = options.CandidateContinuationToken;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The pager binds its cursor to exact catalog content and coverage.
            // A changed/ambiguous cursor must fail closed, not silently skip RCBs.
            var page = MmsCanonicalStaticRcbCatalogPager.Select(
                coverage, observed, maxReadsPerPage, continuation);
            catalogCount = page.CatalogCount;
            eligibleProbeCount = page.EligibleProbeCount;
            continuation = page.ContinuationToken;
            pages++;

            foreach (var raw in page.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMmsInitiated)
                    break;

                // Do not mutate the directory source used for stable paging.
                var candidate = new MmsReportControlCandidate
                {
                    Domain = raw.Domain,
                    LogicalNode = raw.LogicalNode,
                    FunctionalConstraint = raw.FunctionalConstraint,
                    Name = raw.Name,
                    Reference = raw.Reference,
                    Buffered = raw.Buffered,
                    DataSetReference = raw.DataSetReference,
                    Attributes = raw.Attributes.ToList()
                };
                examined++;
                if (string.IsNullOrWhiteSpace(candidate.DataSetReference))
                {
                    await TryReadReportControlStructureSmartAsync(
                            candidate, cancellationToken).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(candidate.DataSetReference))
                        await TryReadReportAttributeSmartAsync(
                                candidate, "DatSet", cancellationToken).ConfigureAwait(false);
                }

                // Exact live DatSet equality is mandatory. A matching SCL RCB
                // name alone cannot become an operational report target.
                if (expectedDataSets.Contains(
                        MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.DataSetReference)))
                    inventory.ReportControls.Add(candidate);
                else
                    rejected++;
            }
        } while (continuation is not null &&
                 pages < maxReadPages &&
                 IsMmsInitiated);

        var complete = IsMmsInitiated &&
                       chains.All(chain => chain.IsComplete) &&
                       continuation is null;
        var coveredDataSets = inventory.ReportControls
            .Select(candidate => MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.DataSetReference))
            .ToHashSet(StringComparer.Ordinal);
        var unresolved = expectedDataSets.Where(ds => !coveredDataSets.Contains(ds)).ToArray();
        var warnings = plan.Warnings
            .Concat(chains
                .Where(chain => !chain.IsComplete)
                .Select(chain => $"Target domain '{chain.Domain}' NamedVariable enumeration is partial: {chain.Message}"))
            .Concat(rejected > 0
                ? new[] { $"{rejected} probed RCB(s) had no exact selected live DatSet binding; not admitted for activation." }
                : Array.Empty<string>())
            .Concat(continuation is not null
                ? new[] { $"Target-scoped RCB discovery deferred {Math.Max(0, eligibleProbeCount - examined)} candidate(s) after {pages} bounded page(s); resume using CandidateContinuationToken." }
                : Array.Empty<string>())
            .Concat(unresolved.Select(ds =>
                $"Selected DataSet '{ds}' has no verified live RCB binding in this acquisition window."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new MmsCanonicalStaticLiveReconciliationResult
        {
            Plan = plan,
            Status = !IsMmsInitiated
                ? MmsCanonicalStaticLiveReconciliationStatus.AssociationLost
                : complete && unresolved.Length == 0
                    ? MmsCanonicalStaticLiveReconciliationStatus.Ready
                    : MmsCanonicalStaticLiveReconciliationStatus.Partial,
            Inventory = inventory,
            EnumeratedVariableCount = variables.Values.Sum(list => list.Count),
            CandidateReportControlCount = catalogCount,
            DataSetBoundReportControlCount = inventory.ReportControls.Count,
            ExaminedReportControlCount = examined,
            DeferredReportControlCount = Math.Max(0, eligibleProbeCount - examined),
            CandidateContinuationToken = continuation,
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
