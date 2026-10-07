using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

/// <summary>
/// Declares where a ReportControl inventory came from. Operational target selection must
/// never mistake an SCL/design projection for live MMS evidence.
/// </summary>
public enum MmsReportInventoryAuthority
{
    Unknown,
    LiveMmsObserved,
    SclDesignProjection
}

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
    public int MaxExactTargets { get; init; } = 64;
    public bool ReadDataSetDirectories { get; init; } = true;
    public IReadOnlySet<string> CallerOwnedRcbReferences { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MmsCanonicalStaticAcquisitionProbeResult
{
    public MmsCanonicalStaticAcquisitionProbePlan Plan { get; init; } = new();
    public MmsCanonicalStaticAcquisitionProbeStatus Status { get; init; }
    public bool NetworkProbePerformed { get; init; }
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
        MmsReportInventory inventory,
        MmsReportInventoryAuthority inventoryAuthority)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(inventory);

        var requested = selections.ToArray();
        var coverage = CanonicalStaticReportCoverageResolver.Resolve(model, requested);
        return Build(coverage, inventory, inventoryAuthority);
    }

    public static MmsCanonicalStaticAcquisitionProbePlan Build(
        CanonicalStaticReportCoveragePlan coverage,
        MmsReportInventory inventory,
        MmsReportInventoryAuthority inventoryAuthority)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(inventory);

        var warnings = new List<string>();

        if (coverage.RequestedSignalCount == 0)
        {
            return Result(
                coverage,
                inventoryAuthority,
                new MmsCanonicalStaticRcbTargetResolution(),
                MmsCanonicalStaticAcquisitionProbeStatus.NoSelection,
                warnings);
        }

        if (coverage.CoveredSignalCount == 0 || coverage.Segments.Length == 0)
        {
            warnings.Add("No selected signal has configured static DataSet/RCB coverage; no operational RCB probe is required.");
            return Result(
                coverage,
                inventoryAuthority,
                new MmsCanonicalStaticRcbTargetResolution(),
                MmsCanonicalStaticAcquisitionProbeStatus.NoConfiguredStaticCoverage,
                warnings);
        }

        if (inventoryAuthority != MmsReportInventoryAuthority.LiveMmsObserved)
        {
            warnings.Add(
                "Configured static coverage exists, but operational RCB targeting requires a live MMS-observed report inventory. " +
                "SCL/design inventory is structural evidence only and was not promoted to live target evidence.");
            return Result(
                coverage,
                inventoryAuthority,
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
                inventoryAuthority,
                targetResolution,
                MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsUnresolved,
                warnings);
        }

        var unresolved = coverage.Segments.Length - targetResolution.Segments.Count(segment =>
            segment.ExactLiveReportControlReferences.Count > 0);
        var status = unresolved > 0
            ? MmsCanonicalStaticAcquisitionProbeStatus.PartialExactTargetsReady
            : MmsCanonicalStaticAcquisitionProbeStatus.ExactTargetsReady;

        return Result(coverage, inventoryAuthority, targetResolution, status, warnings);
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

        var maxTargets = Math.Clamp(options.MaxExactTargets, 1, 512);
        if (plan.ExactTargetReportControlReferences.Count > maxTargets)
        {
            return new MmsCanonicalStaticAcquisitionProbeResult
            {
                Plan = plan,
                Status = MmsCanonicalStaticAcquisitionProbeStatus.TargetBudgetExceeded,
                NetworkProbePerformed = false,
                Warnings = plan.Warnings
                    .Concat(new[]
                    {
                        $"Exact static RCB target count {plan.ExactTargetReportControlReferences.Count} exceeds the configured hot-path budget {maxTargets}; no partial probe was started."
                    })
                    .ToArray()
            };
        }

        EnsureMmsReady();

        var availability = await CheckReportControlAvailabilityAsync(
            liveInventory,
            directory,
            new MmsRcbAvailabilityOptions
            {
                MaxReportControls = maxTargets,
                ReadDataSetDirectories = options.ReadDataSetDirectories,
                TargetReportControlReferences = plan.ExactTargetReportControlReferences,
                CallerOwnedRcbReferences = options.CallerOwnedRcbReferences
            },
            cancellationToken).ConfigureAwait(false);

        var status = plan.UnresolvedSegmentCount > 0
            ? MmsCanonicalStaticAcquisitionProbeStatus.PartialAvailabilityChecked
            : MmsCanonicalStaticAcquisitionProbeStatus.AvailabilityChecked;

        return new MmsCanonicalStaticAcquisitionProbeResult
        {
            Plan = plan,
            Status = status,
            NetworkProbePerformed = true,
            Availability = availability,
            Warnings = plan.Warnings
                .Concat(availability.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }
}
