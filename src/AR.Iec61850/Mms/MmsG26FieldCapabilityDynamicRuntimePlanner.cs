using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// P1.6 field-capability policy.
///
/// A later physical NO-GI dchg witness proves that the exact IED/model/profile can safely
/// perform the dynamic DataSet -> RCB -> RptEna -> spontaneous InformationReport mechanism.
/// The witness remains tied to the persisted successful qualification chain, but it is a
/// capability witness rather than a permanent member-scope restriction.
///
/// Runtime member/RCB selection is still re-derived from fresh exact live-directory and
/// RCB-availability evidence on every association. This policy does not authorize
/// ProductionEligible and never mutates or saves the qualification profile.
/// </summary>
public static class MmsGuardedDynamicReportFieldCapabilityPolicy
{
    public static bool TryValidate(
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence? evidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
                sourceContext,
                evidence,
                out var bindingReason))
        {
            reason = bindingReason;
            return false;
        }

        reason =
            "P1.6 field-capability witness accepted: the exact identity/profile has a bound physical NO-GI dchg InformationReport, healthy association and successful cleanup. The witness proves the dynamic reporting mechanism; fresh exact RCB availability and exact live member resolution still govern every runtime DataSet. ProductionEligible remains separate.";
        return true;
    }
}

/// <summary>
/// P1.6 normal-runtime planner that restores the original Smart Auto contract:
/// static report coverage first, then bounded dynamic RCB/DataSet coverage for every
/// still-uncovered exactly resolved selected signal, then MMS polling only for genuine
/// residuals.
///
/// Unlike P1.5b, the physical Q0 witness is not treated as the only permissible runtime
/// member set or RCB. It proves the dynamic mechanism for the exact IED/model/profile.
/// Each actual runtime RCB must still be freshly verified empty/free and each member must
/// still resolve exactly in the current live MMS directory.
/// </summary>
public static class MmsGuardedDynamicReportFieldCapabilityRuntimePlanner
{
    public static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities,
        MmsHybridReportAcquisitionOptions? options,
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedSignals);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(liveDirectory);
        ArgumentNullException.ThrowIfNull(sourceContext);
        ArgumentNullException.ThrowIfNull(evidence);

        options ??= new MmsHybridReportAcquisitionOptions();
        var materializedSignals = requestedSignals.ToArray();
        var capability = MmsReportAssociationCapabilityEvaluator.Evaluate(
            availability,
            negotiatedCapabilities,
            options);
        var dynamicIntent = options.AllowDynamicBrcb || options.AllowDynamicUrcb;
        var authorizationReason = string.Empty;

        var authorized = dynamicIntent &&
                         capability.MayAttemptDynamicReports &&
                         MmsGuardedDynamicReportFieldCapabilityPolicy.TryValidate(
                             sourceContext,
                             evidence,
                             out authorizationReason);

        if (!dynamicIntent)
            authorizationReason = "Dynamic BRCB/URCB acquisition is disabled by planner policy.";
        else if (!capability.MayAttemptDynamicReports)
            authorizationReason = "The current MMS association does not satisfy the dynamic-report capability gate.";

        var effectiveOptions = BuildOptions(options, authorized);
        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            materializedSignals,
            inventory,
            availability,
            liveDirectory,
            effectiveOptions);

        var policyWarnings = new List<string>();
        if (dynamicIntent && capability.MayAttemptDynamicReports && !authorized)
        {
            policyWarnings.Add(
                $"P1.6 field-capability general dynamic reporting remains withheld: {authorizationReason}");
        }

        if (authorized && !ValidateDynamicSegments(plan, availability, liveDirectory, effectiveOptions, out var invariantFailure))
        {
            policyWarnings.Add(
                $"P1.6 general dynamic plan failed fresh runtime invariants and was withheld: {invariantFailure}");
            authorized = false;
            authorizationReason = invariantFailure;

            plan = MmsHybridReportAcquisitionPlanner.Build(
                catalog,
                materializedSignals,
                inventory,
                availability,
                liveDirectory,
                BuildOptions(options, false));
        }

        RestoreFreshAttributeEvidence(plan, availability, authorized);

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = plan,
            AssociationCapability = capability,
            AutomaticDynamicActivationQuarantined =
                capability.MayAttemptDynamicReports && dynamicIntent && !authorized,
            ProductionDynamicActivationAuthorized = false,
            ProductionDynamicAuthorizationReason = authorized
                ? "P1.6 field-capability normal runtime is active for fresh exact-resolved residual signals and fresh verified-free dynamic RCBs; ProductionEligible certification remains separate."
                : authorizationReason,
            ProductionQualifiedDynamicMemberCount = 0,
            ProductionQualifiedRcbReference = string.Empty,
            PolicyWarnings = policyWarnings.ToArray()
        };
    }

    private static MmsHybridReportAcquisitionOptions BuildOptions(
        MmsHybridReportAcquisitionOptions source,
        bool authorized)
        => new()
        {
            MaxStaticReportPlans = source.MaxStaticReportPlans,
            MaxDynamicReportPlans = source.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = source.MaxDynamicMembersPerReport,
            RequireExactAvailabilityEvidence = source.RequireExactAvailabilityEvidence,
            AllowCallerOwnedReports = source.AllowCallerOwnedReports,
            AllowStaticBrcb = source.AllowStaticBrcb,
            AllowStaticUrcb = source.AllowStaticUrcb,
            AllowDynamicBrcb = authorized && source.AllowDynamicBrcb,
            AllowDynamicUrcb = authorized && source.AllowDynamicUrcb,
            AllowPollingFallback = source.AllowPollingFallback
        };

    private static bool ValidateDynamicSegments(
        MmsHybridReportAcquisitionPlan plan,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        MmsHybridReportAcquisitionOptions options,
        out string reason)
    {
        var dynamicSegments = plan.Segments
            .Where(segment => segment.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();

        if (dynamicSegments.Length == 0)
        {
            reason = "No dynamic segment was needed or safely available for this request.";
            return true;
        }

        if (dynamicSegments.Length > options.MaxDynamicReportPlans)
        {
            reason = $"Planner emitted {dynamicSegments.Length} dynamic groups above configured limit {options.MaxDynamicReportPlans}.";
            return false;
        }

        var distinctRcbCount = dynamicSegments
            .Select(segment => NormalizeRcb(segment.ReportControlReference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctRcbCount != dynamicSegments.Length)
        {
            reason = "Planner reused one dynamic RCB for more than one runtime group.";
            return false;
        }

        var exactLiveMembers = liveDirectory.Points
            .Where(point => point.Confidence > 0)
            .Select(point => NormalizeMms(point.MmsReference))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in dynamicSegments)
        {
            var snapshots = availability.ReportControls
                .Where(snapshot => SameRcb(snapshot.Reference, segment.ReportControlReference))
                .ToArray();
            if (snapshots.Length != 1)
            {
                reason = $"Dynamic RCB {segment.ReportControlReference} does not have exactly one fresh availability snapshot.";
                return false;
            }

            var snapshot = snapshots[0];
            if (!IsFreshDynamicSlot(snapshot, segment.Kind, options))
            {
                reason = $"Dynamic RCB {segment.ReportControlReference} is not a fresh exact verified-free empty slot.";
                return false;
            }

            if (segment.ReportPlan is null || segment.ReportPlan.DynamicPoints.Count == 0)
            {
                reason = $"Dynamic RCB {segment.ReportControlReference} has no exact resolved members.";
                return false;
            }

            if (segment.ReportPlan.DynamicPoints.Count > options.MaxDynamicMembersPerReport)
            {
                reason = $"Dynamic RCB {segment.ReportControlReference} has {segment.ReportPlan.DynamicPoints.Count} members above configured limit {options.MaxDynamicMembersPerReport}.";
                return false;
            }

            foreach (var point in segment.ReportPlan.DynamicPoints)
            {
                var member = NormalizeMms(point.MmsReference);
                if (string.IsNullOrWhiteSpace(member) || !exactLiveMembers.Contains(member))
                {
                    reason = $"Dynamic member {point.MmsReference} is not present in the current exact live MMS directory.";
                    return false;
                }
            }
        }

        reason =
            $"P1.6 fresh runtime invariants accepted for {dynamicSegments.Length} dynamic RCB group(s).";
        return true;
    }

    private static bool IsFreshDynamicSlot(
        MmsRcbAvailabilitySnapshot snapshot,
        MmsHybridAcquisitionKind kind,
        MmsHybridReportAcquisitionOptions options)
    {
        var kindAllowed = kind switch
        {
            MmsHybridAcquisitionKind.DynamicBrcb => options.AllowDynamicBrcb && snapshot.Buffered,
            MmsHybridAcquisitionKind.DynamicUrcb => options.AllowDynamicUrcb && !snapshot.Buffered,
            _ => false
        };
        if (!kindAllowed)
            return false;

        if (snapshot.Availability != MmsRcbOperationalAvailability.NoDataSet ||
            snapshot.Confidence != MmsRcbAvailabilityConfidence.Exact ||
            snapshot.DataSetProbeState != MmsRcbDataSetProbeState.ReadSucceeded ||
            !string.IsNullOrWhiteSpace(snapshot.DataSetReference) ||
            ParseBoolean(snapshot.EnabledState) == true)
        {
            return false;
        }

        if (snapshot.Buffered)
        {
            var reservation = ParseInt(snapshot.ReservationTimeSeconds);
            return reservation is null or 0;
        }

        return ParseBoolean(snapshot.ReservationState) != true;
    }

    private static void RestoreFreshAttributeEvidence(
        MmsHybridReportAcquisitionPlan plan,
        MmsRcbAvailabilityResult availability,
        bool fieldCapabilityAuthorized)
    {
        foreach (var segment in plan.Segments.Where(segment => segment.IsReportBacked && segment.ReportPlan?.ReportControl is not null))
        {
            var candidate = segment.ReportPlan!.ReportControl!;
            var snapshot = availability.ReportControls.FirstOrDefault(item => SameRcb(item.Reference, candidate.Reference));
            if (snapshot is null)
                continue;

            var attributes = snapshot.Attributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            AddIfExposed(attributes, snapshot, "DatSet", snapshot.DataSetReference,
                snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded);
            AddIfExposed(attributes, snapshot, "RptEna", snapshot.EnabledState);
            AddIfExposed(attributes, snapshot, "TrgOps", snapshot.TriggerOptions);
            AddIfExposed(attributes, snapshot, "OptFlds", snapshot.OptionalFields);
            AddIfExposed(attributes, snapshot, "IntgPd", snapshot.IntegrityPeriodMs);
            AddIfExposed(attributes, snapshot, "GI", string.Empty);
            AddIfExposed(
                attributes,
                snapshot,
                snapshot.Buffered ? "ResvTms" : "Resv",
                snapshot.Buffered ? snapshot.ReservationTimeSeconds : snapshot.ReservationState);

            candidate.Attributes = attributes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            candidate.ProbeDiagnostics.Clear();
            candidate.ProbeDiagnostics.AddRange(snapshot.ProbeDiagnostics);
            candidate.Status = segment.Kind is MmsHybridAcquisitionKind.StaticBrcb or MmsHybridAcquisitionKind.StaticUrcb
                ? "P1.6 baseline-static fresh availability snapshot"
                : fieldCapabilityAuthorized
                    ? "G2.6 P1.6 field-capability general-dynamic fresh availability snapshot"
                    : "G2.6 dynamic fresh availability snapshot";
        }
    }

    private static void AddIfExposed(
        ISet<string> attributes,
        MmsRcbAvailabilitySnapshot snapshot,
        string attribute,
        string value,
        bool force = false)
    {
        if (force ||
            (!string.IsNullOrWhiteSpace(value) && value.Trim() != "-") ||
            snapshot.ProbeDiagnostics.Any(line =>
                line.StartsWith(attribute, StringComparison.OrdinalIgnoreCase) &&
                line.Contains(": OK", StringComparison.OrdinalIgnoreCase)))
        {
            attributes.Add(attribute);
        }
    }

    private static bool SameRcb(string? left, string? right)
        => string.Equals(NormalizeRcb(left), NormalizeRcb(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRcb(string? reference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(reference).Replace('\\', '/');

    private static string NormalizeMms(string? reference)
        => MmsFcReferenceNormalizer.NormalizeMmsReference(reference ?? string.Empty);

    private static bool? ParseBoolean(string? value)
    {
        if (bool.TryParse(value?.Trim(), out var parsed))
            return parsed;
        if (string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value?.Trim(), "0", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value?.Trim(), out var parsed) ? parsed : null;
}
