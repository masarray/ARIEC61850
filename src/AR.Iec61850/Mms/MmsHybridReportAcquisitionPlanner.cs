using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Steady-state acquisition mode selected for one or more IEC 61850 signals.
/// Initial MMS snapshot acquisition is intentionally outside this contract.
/// </summary>
public enum MmsHybridAcquisitionKind
{
    StaticBrcb,
    StaticUrcb,
    DynamicBrcb,
    DynamicUrcb,
    MmsPollingFallback,
    Uncovered
}

public enum MmsHybridAcquisitionPlanStatus
{
    FullReportCoverage,
    HybridReportAndPolling,
    PollingOnly,
    Incomplete
}

public enum MmsHybridReportActivation
{
    AlreadyActiveByCaller,
    EnableExistingDataSet,
    ConfigureDynamicDataSet,
    PollingFallback,
    None
}

/// <summary>
/// Guardrails for the P2.2 hybrid report acquisition planner. Defaults deliberately
/// require exact, fresh RCB availability evidence before an automatic report write plan
/// can be emitted.
/// </summary>
public sealed class MmsHybridReportAcquisitionOptions
{
    public int MaxStaticReportPlans { get; init; } = 64;
    public int MaxDynamicReportPlans { get; init; } = 8;
    public int MaxDynamicMembersPerReport { get; init; } = 64;
    public bool RequireExactAvailabilityEvidence { get; init; } = true;
    public bool AllowCallerOwnedReports { get; init; } = true;
    public bool AllowStaticBrcb { get; init; } = true;
    public bool AllowStaticUrcb { get; init; } = true;
    public bool AllowDynamicBrcb { get; init; } = true;
    public bool AllowDynamicUrcb { get; init; } = true;
    public bool AllowPollingFallback { get; init; } = true;
}

/// <summary>
/// Typed summary of discovered and freshly checked Report Control Block capability.
/// A discovered RCB is not counted as usable merely because it exists.
/// </summary>
public sealed class MmsHybridRcbCapabilitySummary
{
    public int DiscoveredRcbCount { get; init; }
    public int CheckedRcbCount { get; init; }
    public int BrcbCount { get; init; }
    public int UrcbCount { get; init; }
    public int StaticConfiguredCount { get; init; }
    public int StaticUsableCount { get; init; }
    public int StaticUsableBrcbCount { get; init; }
    public int StaticUsableUrcbCount { get; init; }
    public int DynamicEmptyVerifiedCount { get; init; }
    public int DynamicUsableCount { get; init; }
    public int DynamicUsableBrcbCount { get; init; }
    public int DynamicUsableUrcbCount { get; init; }
    public int BusyCount { get; init; }
    public int UnknownCount { get; init; }
    public int UnusableCount { get; init; }

    public string Summary =>
        $"RCB capability: discovered={DiscoveredRcbCount}, checked={CheckedRcbCount}, BRCB={BrcbCount}, URCB={UrcbCount}, " +
        $"staticUsable={StaticUsableCount}, dynamicUsable={DynamicUsableCount}, busy={BusyCount}, unknown={UnknownCount}.";
}

public sealed class MmsHybridSignalAssignment
{
    public string SignalReference { get; init; } = string.Empty;
    public MmsHybridAcquisitionKind Kind { get; init; }
    public string ReportControlReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public bool IsReportBacked { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class MmsHybridAcquisitionSegment
{
    public MmsHybridAcquisitionKind Kind { get; init; }
    public MmsHybridReportActivation Activation { get; init; }
    public MmsReportSubscriptionPlan? ReportPlan { get; init; }
    public MmsRcbAvailabilitySnapshot? Availability { get; init; }
    public IReadOnlyList<Iec61850SignalDescriptor> Signals { get; init; } = Array.Empty<Iec61850SignalDescriptor>();
    public bool RequiresWrite { get; init; }
    public bool IsAlreadyActiveByCaller { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string ReportControlReference => ReportPlan?.ReportControl?.Reference ?? Availability?.Reference ?? string.Empty;
    public string DataSetReference => ReportPlan?.DataSetReference ?? Availability?.DataSetReference ?? string.Empty;
    public int SignalCount => Signals.Count;
    public bool IsReportBacked => Kind is
        MmsHybridAcquisitionKind.StaticBrcb or
        MmsHybridAcquisitionKind.StaticUrcb or
        MmsHybridAcquisitionKind.DynamicBrcb or
        MmsHybridAcquisitionKind.DynamicUrcb;

    public string Summary =>
        $"{Kind}: signals={SignalCount}, rcb={TextOrDash(ReportControlReference)}, dataset={TextOrDash(DataSetReference)}, activation={Activation}.";

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

/// <summary>
/// Read-only steady-state acquisition plan. It describes which signals can be report-backed
/// and which residual signals require polling; it does not perform any RCB/DataSet writes.
/// </summary>
public sealed class MmsHybridReportAcquisitionPlan
{
    public string SchemaVersion { get; init; } = "iec61850-hybrid-acquisition-v1";
    public MmsHybridAcquisitionPlanStatus Status { get; init; }
    public MmsHybridRcbCapabilitySummary Capability { get; init; } = new();
    public IReadOnlyList<MmsHybridAcquisitionSegment> Segments { get; init; } = Array.Empty<MmsHybridAcquisitionSegment>();
    public IReadOnlyList<MmsHybridSignalAssignment> Assignments { get; init; } = Array.Empty<MmsHybridSignalAssignment>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public int RequestedSignalCount => Assignments.Count;
    public int ReportCoveredSignalCount => Assignments.Count(x => x.IsReportBacked);
    public int StaticBrcbSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.StaticBrcb);
    public int StaticUrcbSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.StaticUrcb);
    public int DynamicBrcbSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.DynamicBrcb);
    public int DynamicUrcbSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.DynamicUrcb);
    public int PollingFallbackSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.MmsPollingFallback);
    public int UncoveredSignalCount => Assignments.Count(x => x.Kind == MmsHybridAcquisitionKind.Uncovered);
    public bool HasFullReportCoverage => RequestedSignalCount > 0 && ReportCoveredSignalCount == RequestedSignalCount;
    public bool HasPollingResidual => PollingFallbackSignalCount > 0;

    public string Summary =>
        $"Hybrid acquisition: status={Status}, requested={RequestedSignalCount}, report={ReportCoveredSignalCount}, " +
        $"staticBrcb={StaticBrcbSignalCount}, staticUrcb={StaticUrcbSignalCount}, " +
        $"dynamicBrcb={DynamicBrcbSignalCount}, dynamicUrcb={DynamicUrcbSignalCount}, " +
        $"polling={PollingFallbackSignalCount}, uncovered={UncoveredSignalCount}.";
}

/// <summary>
/// P2.2 planner that composes the existing typed signal catalog, fresh RCB availability
/// evidence, static DataSet directories, and the existing report subscription planner.
///
/// Planning order is deliberately partial-coverage aware:
/// 1. reuse/enable safe configured static reports that cover requested signals;
/// 2. create bounded dynamic reports only for still-uncovered, exactly resolved signals;
/// 3. leave only the residual set on MMS polling when policy allows it.
///
/// The planner never claims, reserves, enables, disables, or rewrites an RCB. It only emits
/// typed plans. Unknown/busy RCB evidence is never promoted into an automatic write plan.
/// </summary>
public static class MmsHybridReportAcquisitionPlanner
{
    public static MmsHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        MmsHybridReportAcquisitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedSignals);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(liveDirectory);

        options ??= new MmsHybridReportAcquisitionOptions();
        ValidateOptions(options);

        var requested = requestedSignals
            .Where(signal => signal != null)
            .DistinctBy(SignalKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var capability = BuildCapability(inventory, availability, options);
        if (requested.Length == 0)
        {
            return new MmsHybridReportAcquisitionPlan
            {
                Status = MmsHybridAcquisitionPlanStatus.Incomplete,
                Capability = capability,
                Blockers = ["No requested signal was supplied to the hybrid acquisition planner."]
            };
        }

        var remaining = new List<Iec61850SignalDescriptor>(requested);
        var segments = new List<MmsHybridAcquisitionSegment>();
        var assignments = new Dictionary<string, MmsHybridSignalAssignment>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var blockers = new List<string>();
        var usedRcb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (availability.ReportControls.Count < inventory.ReportControls.Count)
        {
            warnings.Add(
                $"Fresh RCB availability covers {availability.ReportControls.Count} of {inventory.ReportControls.Count} discovered RCB(s). " +
                "Unchecked RCBs remain unavailable to automatic hybrid planning.");
        }

        PlanStaticCoverage(
            remaining,
            segments,
            assignments,
            usedRcb,
            inventory,
            availability,
            options,
            warnings);

        PlanDynamicCoverage(
            remaining,
            segments,
            assignments,
            usedRcb,
            availability,
            liveDirectory,
            options,
            warnings);

        if (remaining.Count > 0)
        {
            var residual = remaining
                .OrderBy(SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (options.AllowPollingFallback)
            {
                segments.Add(new MmsHybridAcquisitionSegment
                {
                    Kind = MmsHybridAcquisitionKind.MmsPollingFallback,
                    Activation = MmsHybridReportActivation.PollingFallback,
                    Signals = residual,
                    RequiresWrite = false,
                    Reason = "No safe report segment covered these residual signals. Keep bounded MMS polling only for this residual set; polling fallback is not evidence of signal absence."
                });

                foreach (var signal in residual)
                {
                    assignments[SignalKey(signal)] = Assignment(
                        signal,
                        MmsHybridAcquisitionKind.MmsPollingFallback,
                        string.Empty,
                        string.Empty,
                        false,
                        "Residual signal is not safely report-covered; bounded MMS polling fallback remains active.");
                }
            }
            else
            {
                segments.Add(new MmsHybridAcquisitionSegment
                {
                    Kind = MmsHybridAcquisitionKind.Uncovered,
                    Activation = MmsHybridReportActivation.None,
                    Signals = residual,
                    RequiresWrite = false,
                    Reason = "No safe report segment covered these signals and polling fallback is disabled. No missing/absent conclusion was made."
                });

                foreach (var signal in residual)
                {
                    assignments[SignalKey(signal)] = Assignment(
                        signal,
                        MmsHybridAcquisitionKind.Uncovered,
                        string.Empty,
                        string.Empty,
                        false,
                        "Signal remains uncovered because no safe report plan was available and polling fallback is disabled. No absence conclusion was made.");
                }

                blockers.Add($"{residual.Length} requested signal(s) remain uncovered because polling fallback is disabled.");
            }
        }

        foreach (var signal in requested)
        {
            var key = SignalKey(signal);
            if (!assignments.ContainsKey(key))
            {
                assignments[key] = Assignment(
                    signal,
                    MmsHybridAcquisitionKind.Uncovered,
                    string.Empty,
                    string.Empty,
                    false,
                    "Planner did not assign this signal. No missing/absent conclusion was made.");
            }
        }

        var orderedAssignments = requested
            .Select(signal => assignments[SignalKey(signal)])
            .ToArray();
        var reportCount = orderedAssignments.Count(x => x.IsReportBacked);
        var pollingCount = orderedAssignments.Count(x => x.Kind == MmsHybridAcquisitionKind.MmsPollingFallback);
        var uncoveredCount = orderedAssignments.Count(x => x.Kind == MmsHybridAcquisitionKind.Uncovered);

        var status = uncoveredCount > 0
            ? MmsHybridAcquisitionPlanStatus.Incomplete
            : reportCount == requested.Length
                ? MmsHybridAcquisitionPlanStatus.FullReportCoverage
                : reportCount > 0 && pollingCount > 0
                    ? MmsHybridAcquisitionPlanStatus.HybridReportAndPolling
                    : MmsHybridAcquisitionPlanStatus.PollingOnly;

        return new MmsHybridReportAcquisitionPlan
        {
            Status = status,
            Capability = capability,
            Segments = segments.ToArray(),
            Assignments = orderedAssignments,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Blockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static void PlanStaticCoverage(
        List<Iec61850SignalDescriptor> remaining,
        ICollection<MmsHybridAcquisitionSegment> segments,
        IDictionary<string, MmsHybridSignalAssignment> assignments,
        ISet<string> usedRcb,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsHybridReportAcquisitionOptions options,
        ICollection<string> warnings)
    {
        var candidates = availability.ReportControls
            .Where(snapshot => IsStaticUsable(snapshot, options))
            .ToList();

        var planCount = 0;
        while (remaining.Count > 0 && candidates.Count > 0 && planCount < options.MaxStaticReportPlans)
        {
            var scored = candidates
                .Where(snapshot => !usedRcb.Contains(NormalizeReference(snapshot.Reference)))
                .Select(snapshot => new
                {
                    Snapshot = snapshot,
                    Covered = remaining.Where(signal => StaticDataSetCovers(snapshot, signal)).ToArray()
                })
                .Where(item => item.Covered.Length > 0)
                .OrderByDescending(item => item.Covered.Length)
                .ThenBy(item => item.Snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller ? 0 : 1)
                .ThenBy(item => item.Snapshot.Buffered ? 0 : 1)
                .ThenBy(item => item.Snapshot.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scored.Length == 0)
                break;

            var selected = scored[0];
            var snapshot = selected.Snapshot;
            var plan = snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller
                ? BuildCallerOwnedStaticPlan(snapshot, inventory)
                : BuildFreshStaticPlan(snapshot);

            if (!plan.IsReady)
            {
                warnings.Add($"Fresh static candidate {snapshot.Reference} could not produce a ready subscription plan and was skipped: {string.Join(" | ", plan.Blockers)}");
                candidates.Remove(snapshot);
                continue;
            }

            var kind = snapshot.Buffered ? MmsHybridAcquisitionKind.StaticBrcb : MmsHybridAcquisitionKind.StaticUrcb;
            var callerOwned = snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller;
            var segment = new MmsHybridAcquisitionSegment
            {
                Kind = kind,
                Activation = callerOwned
                    ? MmsHybridReportActivation.AlreadyActiveByCaller
                    : MmsHybridReportActivation.EnableExistingDataSet,
                ReportPlan = plan,
                Availability = snapshot,
                Signals = selected.Covered,
                RequiresWrite = !callerOwned,
                IsAlreadyActiveByCaller = callerOwned,
                Reason = callerOwned
                    ? "Fresh availability evidence identifies this report as already active in the caller's current session; reuse it without reconfiguration."
                    : "Fresh exact availability evidence confirms a populated static DataSet and a free RCB; enable the existing report without changing its DataSet membership."
            };
            segments.Add(segment);

            foreach (var signal in selected.Covered)
            {
                assignments[SignalKey(signal)] = Assignment(
                    signal,
                    kind,
                    snapshot.Reference,
                    snapshot.DataSetReference,
                    true,
                    segment.Reason);
                remaining.Remove(signal);
            }

            usedRcb.Add(NormalizeReference(snapshot.Reference));
            candidates.Remove(snapshot);
            planCount++;
        }

        if (planCount >= options.MaxStaticReportPlans && remaining.Count > 0)
            warnings.Add($"Static report planning stopped at the configured limit of {options.MaxStaticReportPlans} plan(s); residual signals continue to dynamic/polling planning.");
    }

    private static void PlanDynamicCoverage(
        List<Iec61850SignalDescriptor> remaining,
        ICollection<MmsHybridAcquisitionSegment> segments,
        IDictionary<string, MmsHybridSignalAssignment> assignments,
        ISet<string> usedRcb,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        MmsHybridReportAcquisitionOptions options,
        ICollection<string> warnings)
    {
        if (remaining.Count == 0 || options.MaxDynamicReportPlans == 0)
            return;

        var safeSlots = availability.ReportControls
            .Where(snapshot => IsDynamicUsable(snapshot, options))
            .Where(snapshot => !usedRcb.Contains(NormalizeReference(snapshot.Reference)))
            .OrderBy(snapshot => snapshot.Buffered ? 0 : 1)
            .ThenBy(snapshot => snapshot.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (safeSlots.Count == 0)
            return;

        var resolved = remaining
            .Select(signal => new { Signal = signal, Point = TryResolveExactDynamicPoint(signal, liveDirectory) })
            .Where(item => item.Point != null)
            .Select(item => new ResolvedDynamicSignal(item.Signal, item.Point!))
            .OrderBy(item => item.Point.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => SignalKey(item.Signal), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var planIndex = 0;
        while (resolved.Count > 0 && safeSlots.Count > 0 && planIndex < options.MaxDynamicReportPlans)
        {
            var slotChoice = safeSlots
                .Select(slot => new
                {
                    Slot = slot,
                    SameDomainCount = resolved.Count(item => item.Point.Domain.Equals(slot.Domain, StringComparison.OrdinalIgnoreCase))
                })
                .OrderByDescending(item => item.SameDomainCount)
                .ThenBy(item => item.Slot.Buffered ? 0 : 1)
                .ThenBy(item => item.Slot.Reference, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (slotChoice == null)
                break;

            var slot = slotChoice.Slot;
            var chunk = resolved
                .OrderBy(item => item.Point.Domain.Equals(slot.Domain, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.Point.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => SignalKey(item.Signal), StringComparer.OrdinalIgnoreCase)
                .Take(options.MaxDynamicMembersPerReport)
                .ToArray();
            if (chunk.Length == 0)
            {
                safeSlots.Remove(slot);
                continue;
            }

            var plan = BuildFreshDynamicPlan(slot, chunk.Select(item => item.Point).ToArray(), liveDirectory, planIndex + 1);
            if (!plan.IsReady)
            {
                warnings.Add($"Fresh dynamic candidate {slot.Reference} could not produce a ready subscription plan and was skipped: {string.Join(" | ", plan.Blockers)}");
                safeSlots.Remove(slot);
                continue;
            }

            var kind = slot.Buffered ? MmsHybridAcquisitionKind.DynamicBrcb : MmsHybridAcquisitionKind.DynamicUrcb;
            var signals = chunk.Select(item => item.Signal).ToArray();
            var segment = new MmsHybridAcquisitionSegment
            {
                Kind = kind,
                Activation = MmsHybridReportActivation.ConfigureDynamicDataSet,
                ReportPlan = plan,
                Availability = slot,
                Signals = signals,
                RequiresWrite = true,
                Reason = "Fresh exact availability evidence proves an empty, explicitly free dynamic RCB slot. Configure a bounded dynamic DataSet only for these still-uncovered, exactly resolved signals."
            };
            segments.Add(segment);

            foreach (var signal in signals)
            {
                assignments[SignalKey(signal)] = Assignment(
                    signal,
                    kind,
                    slot.Reference,
                    plan.DataSetReference,
                    true,
                    segment.Reason);
                remaining.Remove(signal);
            }

            foreach (var item in chunk)
                resolved.Remove(item);

            usedRcb.Add(NormalizeReference(slot.Reference));
            safeSlots.Remove(slot);
            planIndex++;
        }

        if (planIndex >= options.MaxDynamicReportPlans && resolved.Count > 0)
            warnings.Add($"Dynamic report planning stopped at the configured limit of {options.MaxDynamicReportPlans} plan(s); residual signals remain eligible for polling fallback.");
    }

    private static MmsReportSubscriptionPlan BuildCallerOwnedStaticPlan(
        MmsRcbAvailabilitySnapshot snapshot,
        MmsReportInventory inventory)
    {
        var candidate = FindCandidate(inventory, snapshot.Reference) ?? CandidateFromSnapshot(snapshot);
        return new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyReadOnly,
            ReportControl = candidate,
            DataSetReference = snapshot.DataSetReference,
            Members = snapshot.DataSetMembers.ToArray(),
            Steps =
            [
                "Reuse the report already active in this caller association; do not rewrite DatSet, reservation, or RptEna merely to adopt it into the hybrid plan.",
                "Preserve DataSet member order when projecting received InformationReport values."
            ],
            RcbSelection = new MmsRcbSelectionEvidence
            {
                Mode = MmsRcbSelectionMode.StaticDataSet,
                PreferredRcbReference = snapshot.Reference,
                StrictRcb = true,
                SelectedRcbReference = snapshot.Reference,
                RequestedDataSetReference = snapshot.DataSetReference
            }
        };
    }

    private static MmsReportSubscriptionPlan BuildFreshStaticPlan(MmsRcbAvailabilitySnapshot snapshot)
    {
        var localInventory = new MmsReportInventory();
        localInventory.ReportControls.Add(CandidateFromSnapshot(snapshot));
        var directory = DataSetResultFromSnapshot(snapshot);
        return MmsReportSubscriptionPlanner.BuildStaticPlan(
            localInventory,
            [directory],
            preferredRcbReference: snapshot.Reference,
            preferredDataSetReference: snapshot.DataSetReference,
            strictRcb: true,
            allowUrCbFallback: true,
            allowPollingFallback: false);
    }

    private static MmsReportSubscriptionPlan BuildFreshDynamicPlan(
        MmsRcbAvailabilitySnapshot snapshot,
        IReadOnlyList<MmsFcResolvedPoint> points,
        MmsIedModelDirectory liveDirectory,
        int planNumber)
    {
        var localInventory = new MmsReportInventory();
        localInventory.ReportControls.Add(CandidateFromSnapshot(snapshot));
        return MmsReportSubscriptionPlanner.BuildDynamicPlan(
            localInventory,
            liveDirectory,
            points.Select(point => point.MmsReference),
            preferredLogicalDevice: snapshot.Domain,
            preferredRcbReference: snapshot.Reference,
            dataSetName: $"AR_HYB_{planNumber:00}",
            strictRcb: true,
            allowUrCbFallback: true,
            allowPollingFallback: false);
    }

    private static bool StaticDataSetCovers(MmsRcbAvailabilitySnapshot snapshot, Iec61850SignalDescriptor signal)
    {
        if (!snapshot.DataSetDirectorySuccess || snapshot.DataSetMembers.Count == 0)
            return false;

        var dataSetReference = NormalizeReference(snapshot.DataSetReference);
        var memberships = signal.DataSetMemberships
            .Where(membership => NormalizeReference(membership.DataSetReference).Equals(dataSetReference, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (memberships.Length > 0)
        {
            foreach (var membership in memberships)
            {
                if (snapshot.DataSetMembers.Any(member => MembershipMatchesMember(membership, member)))
                    return true;
            }
        }

        var signalMms = SignalMmsKeys(signal);
        var signalUser = SignalUserKeys(signal);
        return snapshot.DataSetMembers.Any(member =>
            signalMms.Contains(NormalizeMms(member.MmsReference)) ||
            signalUser.Contains(NormalizeUser(member.UserReference)));
    }

    private static bool MembershipMatchesMember(
        Iec61850SignalDataSetMembership membership,
        MmsDataSetDirectoryMember member)
    {
        var membershipReferences = new[]
        {
            membership.CanonicalMemberReference,
            membership.OriginalMemberReference
        };

        var memberMms = NormalizeMms(member.MmsReference);
        var memberUser = NormalizeUser(member.UserReference);
        return membershipReferences.Any(reference =>
        {
            if (string.IsNullOrWhiteSpace(reference))
                return false;
            var normalizedMms = NormalizeMms(reference);
            var normalizedUser = NormalizeUser(reference);
            return normalizedMms.Equals(memberMms, StringComparison.OrdinalIgnoreCase) ||
                   normalizedUser.Equals(memberUser, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static MmsFcResolvedPoint? TryResolveExactDynamicPoint(
        Iec61850SignalDescriptor signal,
        MmsIedModelDirectory liveDirectory)
    {
        foreach (var reference in new[]
                 {
                     signal.EffectiveMmsReference,
                     signal.ObservedMmsReference,
                     signal.CanonicalMmsReference
                 })
        {
            if (string.IsNullOrWhiteSpace(reference))
                continue;
            if (liveDirectory.TryFindByMmsReference(reference, out var point) && FunctionalConstraintMatches(signal, point))
                return point;
        }

        foreach (var reference in new[] { signal.ObservedReference, signal.DesignReference })
        {
            if (string.IsNullOrWhiteSpace(reference))
                continue;
            var matches = liveDirectory.FindByUserReference(reference)
                .Where(point => FunctionalConstraintMatches(signal, point))
                .ToArray();
            if (matches.Length == 1)
                return matches[0];
        }

        return null;
    }

    private static bool FunctionalConstraintMatches(Iec61850SignalDescriptor signal, MmsFcResolvedPoint point)
        => string.IsNullOrWhiteSpace(signal.FunctionalConstraint) ||
           signal.FunctionalConstraint.Equals(point.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);

    private static bool IsStaticUsable(MmsRcbAvailabilitySnapshot snapshot, MmsHybridReportAcquisitionOptions options)
    {
        if (snapshot.Buffered && !options.AllowStaticBrcb)
            return false;
        if (!snapshot.Buffered && !options.AllowStaticUrcb)
            return false;
        if (snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller)
            return options.AllowCallerOwnedReports &&
                   snapshot.DataSetDirectorySuccess &&
                   snapshot.DataSetMembers.Count > 0 &&
                   !string.IsNullOrWhiteSpace(snapshot.DataSetReference);
        if (snapshot.Availability != MmsRcbOperationalAvailability.Available)
            return false;
        if (options.RequireExactAvailabilityEvidence && snapshot.Confidence != MmsRcbAvailabilityConfidence.Exact)
            return false;
        return snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
               snapshot.DataSetDirectorySuccess &&
               snapshot.DataSetMembers.Count > 0 &&
               !string.IsNullOrWhiteSpace(snapshot.DataSetReference) &&
               ParseBool(snapshot.EnabledState) == false &&
               HasExplicitFreeReservation(snapshot);
    }

    private static bool IsDynamicUsable(MmsRcbAvailabilitySnapshot snapshot, MmsHybridReportAcquisitionOptions options)
    {
        if (snapshot.Buffered && !options.AllowDynamicBrcb)
            return false;
        if (!snapshot.Buffered && !options.AllowDynamicUrcb)
            return false;
        if (snapshot.Availability != MmsRcbOperationalAvailability.NoDataSet)
            return false;
        if (options.RequireExactAvailabilityEvidence && snapshot.Confidence != MmsRcbAvailabilityConfidence.Exact)
            return false;
        return snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
               string.IsNullOrWhiteSpace(snapshot.DataSetReference) &&
               ParseBool(snapshot.EnabledState) == false &&
               HasExplicitFreeReservation(snapshot);
    }

    private static bool HasExplicitFreeReservation(MmsRcbAvailabilitySnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Owner) && snapshot.Owner.Trim() != "-")
            return false;
        return snapshot.Buffered
            ? ParseUnsigned(snapshot.ReservationTimeSeconds) == 0
            : ParseBool(snapshot.ReservationState) == false;
    }

    private static MmsHybridRcbCapabilitySummary BuildCapability(
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsHybridReportAcquisitionOptions options)
    {
        var staticUsable = availability.ReportControls.Where(snapshot => IsStaticUsable(snapshot, options)).ToArray();
        var dynamicUsable = availability.ReportControls.Where(snapshot => IsDynamicUsable(snapshot, options)).ToArray();

        return new MmsHybridRcbCapabilitySummary
        {
            DiscoveredRcbCount = inventory.ReportControls.Count,
            CheckedRcbCount = availability.ReportControls.Count,
            BrcbCount = inventory.ReportControls.Count(rcb => rcb.Buffered),
            UrcbCount = inventory.ReportControls.Count(rcb => !rcb.Buffered),
            StaticConfiguredCount = availability.ReportControls.Count(snapshot => !string.IsNullOrWhiteSpace(snapshot.DataSetReference)),
            StaticUsableCount = staticUsable.Length,
            StaticUsableBrcbCount = staticUsable.Count(snapshot => snapshot.Buffered),
            StaticUsableUrcbCount = staticUsable.Count(snapshot => !snapshot.Buffered),
            DynamicEmptyVerifiedCount = availability.ReportControls.Count(snapshot =>
                snapshot.Availability == MmsRcbOperationalAvailability.NoDataSet &&
                snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
                string.IsNullOrWhiteSpace(snapshot.DataSetReference)),
            DynamicUsableCount = dynamicUsable.Length,
            DynamicUsableBrcbCount = dynamicUsable.Count(snapshot => snapshot.Buffered),
            DynamicUsableUrcbCount = dynamicUsable.Count(snapshot => !snapshot.Buffered),
            BusyCount = availability.ReportControls.Count(snapshot => snapshot.Availability == MmsRcbOperationalAvailability.InUse),
            UnknownCount = availability.ReportControls.Count(snapshot => snapshot.Availability == MmsRcbOperationalAvailability.Unknown),
            UnusableCount = availability.ReportControls.Count(snapshot => snapshot.Availability is
                MmsRcbOperationalAvailability.DataSetEmpty or MmsRcbOperationalAvailability.DataSetUnreadable)
        };
    }

    private static MmsReportControlCandidate CandidateFromSnapshot(MmsRcbAvailabilitySnapshot snapshot)
        => new()
        {
            Domain = snapshot.Domain,
            LogicalNode = snapshot.LogicalNode,
            FunctionalConstraint = snapshot.Buffered ? "BR" : "RP",
            Name = snapshot.Name,
            Reference = snapshot.Reference,
            Buffered = snapshot.Buffered,
            DataSetReference = snapshot.DataSetReference,
            DataSetProbeState = snapshot.DataSetProbeState,
            DataSetProbeMessage = snapshot.DataSetProbeMessage,
            ReportId = snapshot.ReportId,
            ConfRev = snapshot.ConfRev,
            IntegrityPeriodMs = snapshot.IntegrityPeriodMs,
            EnabledState = snapshot.EnabledState,
            ReservationState = snapshot.ReservationState,
            ReservationTimeSeconds = snapshot.ReservationTimeSeconds,
            Owner = snapshot.Owner,
            BufferTimeMs = snapshot.BufferTimeMs,
            TriggerOptions = snapshot.TriggerOptions,
            OptionalFields = snapshot.OptionalFields,
            Status = "P2.2 fresh availability snapshot"
        };

    private static MmsReportControlCandidate? FindCandidate(MmsReportInventory inventory, string reference)
        => inventory.ReportControls.FirstOrDefault(candidate =>
            NormalizeReference(candidate.Reference).Equals(NormalizeReference(reference), StringComparison.OrdinalIgnoreCase));

    private static MmsDataSetDirectoryResult DataSetResultFromSnapshot(MmsRcbAvailabilitySnapshot snapshot)
        => new()
        {
            IsSuccess = snapshot.DataSetDirectorySuccess,
            DataSetReference = snapshot.DataSetReference,
            Domain = snapshot.Domain,
            IsDeletable = snapshot.DataSetIsDeletable,
            Members = snapshot.DataSetMembers.ToArray(),
            Message = snapshot.Reason
        };

    private static MmsHybridSignalAssignment Assignment(
        Iec61850SignalDescriptor signal,
        MmsHybridAcquisitionKind kind,
        string rcb,
        string dataSet,
        bool reportBacked,
        string reason)
        => new()
        {
            SignalReference = DisplayReference(signal),
            Kind = kind,
            ReportControlReference = rcb,
            DataSetReference = dataSet,
            IsReportBacked = reportBacked,
            Reason = reason
        };

    private static HashSet<string> SignalMmsKeys(Iec61850SignalDescriptor signal)
        => new(new[]
            {
                signal.EffectiveMmsReference,
                signal.ObservedMmsReference,
                signal.CanonicalMmsReference
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeMms), StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> SignalUserKeys(Iec61850SignalDescriptor signal)
        => new(new[]
            {
                signal.ObservedReference,
                signal.DesignReference
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeUser), StringComparer.OrdinalIgnoreCase);

    private static string SignalKey(Iec61850SignalDescriptor signal)
        => FirstNonEmpty(
            NormalizeMms(signal.CanonicalMmsReference),
            NormalizeMms(signal.EffectiveMmsReference),
            NormalizeMms(signal.ObservedMmsReference),
            NormalizeUser(signal.DesignReference),
            NormalizeUser(signal.ObservedReference));

    private static string DisplayReference(Iec61850SignalDescriptor signal)
        => FirstNonEmpty(
            signal.EffectiveMmsReference,
            signal.CanonicalMmsReference,
            signal.ObservedMmsReference,
            signal.DesignReference,
            signal.ObservedReference);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace('\\', '/');

    private static string NormalizeMms(string? reference)
        => MmsFcReferenceNormalizer.NormalizeMmsReference(reference ?? string.Empty);

    private static string NormalizeUser(string? reference)
        => MmsFcReferenceNormalizer.NormalizeUserReference(reference ?? string.Empty);

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool? ParseBool(string? value)
        => MmsRcbAvailabilityEvaluator.ParseBool(value);

    private static ulong? ParseUnsigned(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;
        return ulong.TryParse(text, out var parsed) ? parsed : null;
    }

    private static void ValidateOptions(MmsHybridReportAcquisitionOptions options)
    {
        if (options.MaxStaticReportPlans < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxStaticReportPlans cannot be negative.");
        if (options.MaxDynamicReportPlans < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDynamicReportPlans cannot be negative.");
        if (options.MaxDynamicMembersPerReport <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDynamicMembersPerReport must be greater than zero.");
    }

    private sealed record ResolvedDynamicSignal(Iec61850SignalDescriptor Signal, MmsFcResolvedPoint Point);
}
