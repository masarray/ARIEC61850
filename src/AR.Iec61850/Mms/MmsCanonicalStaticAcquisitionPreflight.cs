using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

public enum MmsCanonicalStaticAcquisitionProbeStatus
{
    NoSelection,
    NoConfiguredStaticCoverage,
    LiveInventoryRequired,
    ExactTargetsUnresolved,
    ExactTargetsReady,
    PartialExactTargetsReady,
    TargetBudgetExceeded,
    AvailabilityChecked,
    PartialAvailabilityChecked
}

public sealed class MmsCanonicalStaticAcquisitionProbePlan
{
    public MmsReportInventoryAuthority InventoryAuthority { get; init; }
    public CanonicalStaticReportCoveragePlan Coverage { get; init; } = new();
    public MmsCanonicalStaticRcbTargetResolution TargetResolution { get; init; } = new();
    public MmsCanonicalStaticAcquisitionProbeStatus Status { get; init; }
    public IReadOnlySet<string> ExactTargetReportControlReferences { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public int RequestedSignalCount => Coverage.RequestedSignalCount;
    public int StaticCoveredSignalCount => Coverage.CoveredSignalCount;
    public int CoveredSegmentCount => Coverage.Segments.Length;
    public int ResolvedSegmentCount => TargetResolution.Segments.Count(segment =>
        segment.ExactLiveReportControlReferences.Count > 0);
    public int UnresolvedSegmentCount => Math.Max(0, CoveredSegmentCount - ResolvedSegmentCount);
    public bool HasExactOperationalTargets =>
        InventoryAuthority == MmsReportInventoryAuthority.LiveMmsObserved &&
        ExactTargetReportControlReferences.Count > 0;

    public string Summary =>
        $"canonical static preflight: status={Status}, ingress={Coverage.Ingress}, requested={RequestedSignalCount}, " +
        $"staticCovered={StaticCoveredSignalCount}, segments={CoveredSegmentCount}, resolvedSegments={ResolvedSegmentCount}, " +
        $"unresolvedSegments={UnresolvedSegmentCount}, exactTargets={ExactTargetReportControlReferences.Count}, inventory={InventoryAuthority}.";
}

public sealed class MmsCanonicalStaticAcquisitionProbeOptions
{
    /// <summary>Maximum exact RCBs inspected per page, not a global catalog cap.</summary>
    public int MaxExactTargets { get; init; } = 64;
    public int MaxExactTargetPagesPerInvocation { get; init; } = 8;
    public string? ExactTargetContinuationToken { get; init; }
    public bool ReadDataSetDirectories { get; init; } = true;
    public IReadOnlySet<string> CallerOwnedRcbReferences { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MmsCanonicalStaticAcquisitionProbeResult
{
    public MmsCanonicalStaticAcquisitionProbePlan Plan { get; init; } = new();
    public MmsCanonicalStaticAcquisitionProbeStatus Status { get; init; }
    public bool NetworkProbePerformed { get; init; }
    public int ExactTargetPageCount { get; init; }
    public int DeferredExactTargetCount { get; init; }
    public string? ExactTargetContinuationToken { get; init; }
    public MmsRcbAvailabilityResult? Availability { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string Summary =>
        $"{Plan.Summary} networkProbe={NetworkProbePerformed.ToString().ToLowerInvariant()}, " +
        $"availability={(Availability is null ? "not-run" : Availability.Summary)}";
}

/// <summary>
/// Pure source-aware coordinator between canonical static coverage and live RCB targeting.
///
/// It deliberately refuses to turn an SCL-projected inventory into operational live evidence.
/// A caller must provide a live MMS-observed inventory before exact target resolution can
/// authorize network probing. Unresolved segments stay unresolved; this planner never broadens
/// them to siblings or starts a broad RCB scan.
/// </summary>
public static class MmsCanonicalStaticAcquisitionPreflightPlanner
{
    public static MmsCanonicalStaticAcquisitionProbePlan Build(
        CanonicalIedModel model,
        IEnumerable<CanonicalStaticReportSelection> selections,
        MmsReportInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(inventory);

        var requested = selections.ToArray();
        var coverage = CanonicalStaticReportCoverageResolver.Resolve(model, requested);
        return Build(coverage, inventory);
    }

    public static MmsCanonicalStaticAcquisitionProbePlan Build(
        CanonicalStaticReportCoveragePlan coverage,
        MmsReportInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(inventory);

        var warnings = new List<string>();

        if (coverage.RequestedSignalCount == 0)
        {
            return Result(
                coverage,
                inventory.Authority,
                new MmsCanonicalStaticRcbTargetResolution(),
                MmsCanonicalStaticAcquisitionProbeStatus.NoSelection,
                warnings);
        }

        if (coverage.CoveredSignalCount == 0 || coverage.Segments.Length == 0)
        {
            warnings.Add("No selected signal has configured static DataSet/RCB coverage; no operational RCB probe is required.");
            return Result(
                coverage,
                inventory.Authority,
                new MmsCanonicalStaticRcbTargetResolution(),
                MmsCanonicalStaticAcquisitionProbeStatus.NoConfiguredStaticCoverage,
                warnings);
        }

        if (inventory.Authority != MmsReportInventoryAuthority.LiveMmsObserved)
        {
            warnings.Add(
                "Configured static coverage exists, but operational RCB targeting requires a live MMS-observed report inventory. " +
                "SCL/design inventory is structural evidence only and was not promoted to live target evidence.");
            return Result(
                coverage,
                inventory.Authority,
                new MmsCanonicalStaticRcbTargetResolution(),
                MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired,
                warnings);
        }

        var targetResolution = MmsCanonicalStaticRcbTargetResolver.Resolve(coverage, inventory);
        warnings.AddRange(targetResolution.Warnings);

        if (!targetResolution.HasTargets)
        {
            warnings.Add(
                "Configured static coverage exists but no exact live RCB target was proven. No broad diagnostic scan or sibling-name guess was started.");
            return Result(
                coverage,
                inventory.Authority,
                targetResolution,
                MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsUnresolved,
                warnings);
        }

        var unresolved = coverage.Segments.Length - targetResolution.Segments.Count(segment =>
            segment.ExactLiveReportControlReferences.Count > 0);
        var status = unresolved > 0
            ? MmsCanonicalStaticAcquisitionProbeStatus.PartialExactTargetsReady
            : MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsReady;

        return Result(coverage, inventory.Authority, targetResolution, status, warnings);
    }

    private static MmsCanonicalStaticAcquisitionProbePlan Result(
        CanonicalStaticReportCoveragePlan coverage,
        MmsReportInventoryAuthority inventoryAuthority,
        MmsCanonicalStaticRcbTargetResolution targetResolution,
        MmsCanonicalStaticAcquisitionProbeStatus status,
        IReadOnlyList<string> warnings)
        => new()
        {
            InventoryAuthority = inventoryAuthority,
            Coverage = coverage,
            TargetResolution = targetResolution,
            Status = status,
            ExactTargetReportControlReferences = targetResolution.ExactLiveReportControlReferences,
            Warnings = warnings
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Preferred one-shot runtime entry point. Planning and execution consume the same
    /// inventory instance so callers cannot accidentally plan from one evidence source
    /// and execute against another.
    /// </summary>
    public Task<MmsCanonicalStaticAcquisitionProbeResult> PrepareCanonicalStaticAcquisitionAsync(
        CanonicalIedModel model,
        IEnumerable<CanonicalStaticReportSelection> selections,
        MmsReportInventory inventory,
        MmsIedModelDirectory? directory = null,
        MmsCanonicalStaticAcquisitionProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(inventory);

        var plan = MmsCanonicalStaticAcquisitionPreflightPlanner.Build(
            model,
            selections,
            inventory);
        return ProbeCanonicalStaticAcquisitionAsync(
            plan,
            inventory,
            directory,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Executes only the exact-target availability work authorized by the pure preflight
    /// planner. There is intentionally no implicit broad fallback.
    /// </summary>
    public async Task<MmsCanonicalStaticAcquisitionProbeResult> ProbeCanonicalStaticAcquisitionAsync(
        MmsCanonicalStaticAcquisitionProbePlan plan,
        MmsReportInventory liveInventory,
        MmsIedModelDirectory? directory = null,
        MmsCanonicalStaticAcquisitionProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(liveInventory);
        options ??= new MmsCanonicalStaticAcquisitionProbeOptions();

        if (liveInventory.Authority != MmsReportInventoryAuthority.LiveMmsObserved)
        {
            return new MmsCanonicalStaticAcquisitionProbeResult
            {
                Plan = plan,
                Status = MmsCanonicalStaticAcquisitionProbeStatus.LiveInventoryRequired,
                NetworkProbePerformed = false,
                Warnings = plan.Warnings
                    .Concat(new[]
                    {
                        "The execution inventory is not marked LiveMmsObserved; the exact-target plan was not allowed to touch the network."
                    })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        if (!plan.HasExactOperationalTargets)
        {
            return new MmsCanonicalStaticAcquisitionProbeResult
            {
                Plan = plan,
                Status = plan.Status,
                NetworkProbePerformed = false,
                Warnings = plan.Warnings
            };
        }

        var maxPerPage = Math.Clamp(options.MaxExactTargets, 1, 512);
        var maxPages = Math.Clamp(options.MaxExactTargetPagesPerInvocation, 1, 32);
        EnsureMmsReady();

        var snapshots = new List<MmsRcbAvailabilitySnapshot>();
        var probeWarnings = new List<string>();
        var continuation = options.ExactTargetContinuationToken;
        var totalExact = 0;
        var pages = 0;
        var stateReads = 0;
        var directoryReads = 0;
        var directoryHits = 0;

        while (pages < maxPages && IsMmsInitiated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = MmsCanonicalStaticExactTargetPager.Select(
                plan, liveInventory, maxPerPage, continuation,
                _canonicalStaticRcbAssociationKey);
            totalExact = page.TotalExactTargets;
            continuation = page.ContinuationToken;
            if (page.References.Count == 0)
                break;

            pages++;
            // Exact target list, never a broad RCB availability probe.
            var batch = await CheckReportControlAvailabilityAsync(
                liveInventory,
                directory,
                new MmsRcbAvailabilityOptions
                {
                    MaxReportControls = maxPerPage,
                    ReadDataSetDirectories = options.ReadDataSetDirectories,
                    TargetReportControlReferences = page.References,
                    CallerOwnedRcbReferences = options.CallerOwnedRcbReferences
                },
                cancellationToken).ConfigureAwait(false);
            snapshots.AddRange(batch.ReportControls);
            probeWarnings.AddRange(batch.Warnings);
            stateReads += batch.TargetedRcbStateLogicalReadCount;
            directoryReads += batch.DataSetDirectoryNetworkReadCount;
            directoryHits += batch.DataSetDirectoryCacheHitCount;

            // Once each resolved DataSet segment has an independently eligible
            // exact RCB, additional pages provide no benefit to this one-shot
            // selected acquisition. Availability is READ-ONLY; the consumer
            // still verifies ordered members and user authorization before writes.
            var resolvedSegments = plan.TargetResolution.Segments
                .Where(segment => segment.ExactLiveReportControlReferences.Count > 0)
                .ToArray();
            var allHaveEligible = resolvedSegments.Length > 0 &&
                resolvedSegments.All(segment =>
                    snapshots.Any(snapshot =>
                        segment.ExactLiveReportControlReferences.Any(reference =>
                            string.Equals(
                                MmsRcbAvailabilityEvaluator.NormalizeReference(reference),
                                MmsRcbAvailabilityEvaluator.NormalizeReference(snapshot.Reference),
                                StringComparison.OrdinalIgnoreCase)) &&
                        MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
                            snapshot, allowCallerOwned: true,
                            allowReducedMissingReservationEvidence: true).IsEligible));
            if (allHaveEligible || continuation is null)
                break;
        }

        var deferred = Math.Max(0, totalExact - snapshots.Count);
        if (continuation is not null)
            probeWarnings.Add(
                $"Exact target availability deferred {deferred} RCB(s) after {pages} bounded page(s); resume using ExactTargetContinuationToken.");
        var availability = new MmsRcbAvailabilityResult
        {
            ReportControls = snapshots,
            Warnings = probeWarnings.Distinct(StringComparer.Ordinal).ToArray(),
            InventoryReportControlCount = liveInventory.ReportControls.Count,
            TargetFilterApplied = true,
            RequestedTargetReportControlCount = plan.ExactTargetReportControlReferences.Count,
            MatchedTargetReportControlCount = snapshots.Count,
            TargetedRcbStateLogicalReadCount = stateReads,
            DataSetDirectoryNetworkReadCount = directoryReads,
            DataSetDirectoryCacheHitCount = directoryHits
        };

        return new MmsCanonicalStaticAcquisitionProbeResult
        {
            Plan = plan,
            Status = continuation is not null || plan.UnresolvedSegmentCount > 0
                ? MmsCanonicalStaticAcquisitionProbeStatus.PartialAvailabilityChecked
                : MmsCanonicalStaticAcquisitionProbeStatus.AvailabilityChecked,
            NetworkProbePerformed = pages > 0,
            Availability = pages > 0 ? availability : null,
            ExactTargetPageCount = pages,
            DeferredExactTargetCount = deferred,
            ExactTargetContinuationToken = continuation,
            Warnings = plan.Warnings
                .Concat(availability.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }
}
