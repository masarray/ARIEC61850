using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Explicit production-planning evidence supplied by an application after it has loaded the
/// persisted qualification profile for the currently connected IED. Supplying this object is
/// not itself permission for a dynamic write; the engine revalidates the complete profile and
/// current identity through MmsDynamicReportQualificationProfilePolicy.
/// </summary>
public sealed record MmsDynamicReportProductionPlanningContext
{
    public MmsDynamicReportQualificationProfile Profile { get; init; } = new();
    public MmsDynamicReportIedIdentity CurrentIdentity { get; init; } = new();
}

/// <summary>
/// Capability-aware orchestration around the stable static -> dynamic -> polling planner.
/// Static RCB coverage keeps the stable planner's original fresh-availability semantics;
/// association capability qualification is an additional guard only for dynamic mutation.
/// </summary>
public sealed class MmsCapabilityAwareHybridReportAcquisitionPlan
{
    public MmsHybridReportAcquisitionPlan AcquisitionPlan { get; init; } = new();
    public MmsReportAssociationCapability AssociationCapability { get; init; } = new();
    public bool AutomaticDynamicActivationQuarantined { get; init; }
    public bool ProductionDynamicActivationAuthorized { get; init; }
    public string ProductionDynamicAuthorizationReason { get; init; } = string.Empty;
    public int ProductionQualifiedDynamicMemberCount { get; init; }
    public string ProductionQualifiedRcbReference { get; init; } = string.Empty;
    public IReadOnlyList<string> PolicyWarnings { get; init; } = Array.Empty<string>();

    public string Summary =>
        $"{AcquisitionPlan.Summary} {AssociationCapability.Summary} " +
        $"ProductionDynamic={(ProductionDynamicActivationAuthorized ? "authorized" : "not-authorized")}.";

    public IReadOnlyList<string> Warnings => AcquisitionPlan.Warnings
        .Concat(AssociationCapability.Warnings)
        .Concat(PolicyWarnings)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> Blockers => AcquisitionPlan.Blockers;
}

public static class MmsCapabilityAwareHybridReportAcquisitionPlanner
{
    public static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities = null,
        MmsHybridReportAcquisitionOptions? options = null,
        MmsDynamicReportProductionPlanningContext? productionContext = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedSignals);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(liveDirectory);

        options ??= new MmsHybridReportAcquisitionOptions();
        var capability = MmsReportAssociationCapabilityEvaluator.Evaluate(
            availability,
            negotiatedCapabilities,
            options);

        var dynamicIntent = options.AllowDynamicBrcb || options.AllowDynamicUrcb;
        var production = EvaluateProductionAuthorization(
            productionContext,
            capability,
            dynamicIntent);

        // P6.1 stability rule:
        // Do not place the P3 capability wrapper in front of the stable static planner.
        // A populated DataSet discovered with an exact fresh directory is static protocol
        // evidence and must remain visible to MmsHybridReportAcquisitionPlanner, which owns
        // the established static-usability checks.
        //
        // P6.2-B / G2.6 production rule:
        // Advertised MMS capability or an earlier qualification stage is never enough for
        // automatic full dynamic activation. A valid, identity-compatible ProductionEligible
        // profile is required. Even then, this first production consumer deliberately scopes
        // automatic dynamic planning to:
        //   - the exact RCB that produced the proven InformationReport;
        //   - only the exact members carried by that proven InformationReport;
        //   - at most one dynamic group and never beyond the accepted envelope size.
        // Everything outside that evidence remains static-report-backed or MMS polling.
        var configuredStaticReferences = availability.ReportControls
            .Where(HasConfiguredStaticDataSetEvidence)
            .Select(snapshot => Normalize(snapshot.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedRcbReferences = configuredStaticReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (production.IsAuthorized)
            allowedRcbReferences.Add(Normalize(production.RcbReference));

        var restrictedAvailability = RestrictAvailability(
            availability,
            allowedRcbReferences,
            capability.Warnings);
        var restrictedInventory = RestrictInventory(inventory, allowedRcbReferences);
        var planningDirectory = production.IsAuthorized
            ? RestrictDirectoryToQualifiedMembers(liveDirectory, production.MemberReferences)
            : liveDirectory;

        var automaticOptions = AutomaticMonitoringOptions(options, production);
        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            requestedSignals,
            restrictedInventory,
            restrictedAvailability,
            planningDirectory,
            automaticOptions);

        var policyWarnings = new List<string>();
        if (dynamicIntent && capability.MayAttemptDynamicReports && !production.IsAuthorized)
        {
            policyWarnings.Add(
                $"Automatic dynamic reporting remains quarantined: {production.Reason}");
        }

        // Treat persisted profiles as untrusted input. CanUseForProductionPlanning validates
        // the state/evidence chain, and this post-plan check additionally proves that the
        // generic hybrid planner did not emit a dynamic segment outside the exact production
        // evidence scope. Any mismatch drops back to the frozen static -> polling behavior.
        if (production.IsAuthorized &&
            !ValidateProductionDynamicSegments(plan, production, out var invariantFailure))
        {
            policyWarnings.Add(
                $"Production dynamic plan failed the exact evidence-scope invariant and was quarantined: {invariantFailure}");
            production = ProductionDynamicAuthorization.Denied(invariantFailure);

            var staticAvailability = RestrictAvailability(
                availability,
                configuredStaticReferences,
                capability.Warnings);
            var staticInventory = RestrictInventory(inventory, configuredStaticReferences);
            plan = MmsHybridReportAcquisitionPlanner.Build(
                catalog,
                requestedSignals,
                staticInventory,
                staticAvailability,
                liveDirectory,
                AutomaticMonitoringOptions(options, production));
        }

        RestoreFreshAttributeEvidence(plan, availability);

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = plan,
            AssociationCapability = capability,
            AutomaticDynamicActivationQuarantined =
                capability.MayAttemptDynamicReports &&
                dynamicIntent &&
                !production.IsAuthorized,
            ProductionDynamicActivationAuthorized = production.IsAuthorized,
            ProductionDynamicAuthorizationReason = production.Reason,
            ProductionQualifiedDynamicMemberCount = production.MemberReferences.Count,
            ProductionQualifiedRcbReference = production.RcbReference,
            PolicyWarnings = policyWarnings.ToArray()
        };
    }

    private static ProductionDynamicAuthorization EvaluateProductionAuthorization(
        MmsDynamicReportProductionPlanningContext? context,
        MmsReportAssociationCapability capability,
        bool dynamicIntent)
    {
        if (!dynamicIntent)
            return ProductionDynamicAuthorization.Denied("Dynamic BRCB/URCB acquisition is disabled by planner policy.");
        if (context is null)
            return ProductionDynamicAuthorization.Denied("No persisted ProductionEligible qualification context was supplied.");

        if (!MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(
                context.Profile,
                context.CurrentIdentity,
                out var productionReason))
        {
            return ProductionDynamicAuthorization.Denied(productionReason);
        }

        if (!capability.MayAttemptDynamicReports)
        {
            return ProductionDynamicAuthorization.Denied(
                "The current MMS association does not satisfy the dynamic-report capability gate.");
        }

        var envelope = context.Profile.AcceptedEnvelope;
        var activation = context.Profile.RcbActivationProof;
        var report = context.Profile.InformationReportProof;
        if (envelope is null || activation is null || report is null)
            return ProductionDynamicAuthorization.Denied("Production profile is missing dynamic qualification evidence.");

        if (!SameReference(activation.RcbReference, report.RcbReference))
            return ProductionDynamicAuthorization.Denied("Stored RCB activation and InformationReport RCB identities differ.");
        if (!SameReference(activation.DataSetReference, report.DataSetReference))
            return ProductionDynamicAuthorization.Denied("Stored RCB activation and InformationReport DataSet identities differ.");
        if (!ExactMemberSequenceEquals(activation.MemberReferences, report.MemberReferences))
            return ProductionDynamicAuthorization.Denied("Stored activation/report member sequences differ.");
        if (!IsOrderedMemberSubset(report.MemberReferences, envelope.ExactProvenMemberReferences))
            return ProductionDynamicAuthorization.Denied("InformationReport members are outside the accepted qualified envelope.");
        if (report.MemberReferences.Count == 0 || report.MemberReferences.Count > envelope.ProvenMemberCount)
            return ProductionDynamicAuthorization.Denied("InformationReport member count is outside the accepted qualified envelope.");

        var normalizedMembers = report.MemberReferences
            .Select(NormalizeMms)
            .ToArray();
        if (normalizedMembers.Any(string.IsNullOrWhiteSpace) ||
            normalizedMembers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedMembers.Length)
        {
            return ProductionDynamicAuthorization.Denied("InformationReport production member evidence is empty or duplicated.");
        }

        return new ProductionDynamicAuthorization(
            true,
            $"{productionReason} Automatic dynamic planning is scoped to the exact proven InformationReport RCB/member set.",
            report.RcbReference.Trim(),
            normalizedMembers);
    }

    private static MmsRcbAvailabilityResult RestrictAvailability(
        MmsRcbAvailabilityResult availability,
        IReadOnlySet<string> allowedReferences,
        IReadOnlyList<string> capabilityWarnings)
        => new()
        {
            CheckedAtUtc = availability.CheckedAtUtc,
            ReportControls = availability.ReportControls
                .Where(snapshot => allowedReferences.Contains(Normalize(snapshot.Reference)))
                .ToArray(),
            Warnings = availability.Warnings
                .Concat(capabilityWarnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static MmsReportInventory RestrictInventory(
        MmsReportInventory inventory,
        IReadOnlySet<string> allowedReferences)
    {
        var restricted = new MmsReportInventory();
        restricted.DataSets.AddRange(inventory.DataSets);
        restricted.ReportControls.AddRange(inventory.ReportControls
            .Where(candidate => allowedReferences.Contains(Normalize(candidate.Reference))));
        return restricted;
    }

    private static MmsIedModelDirectory RestrictDirectoryToQualifiedMembers(
        MmsIedModelDirectory liveDirectory,
        IReadOnlyList<string> qualifiedMembers)
    {
        var allowed = qualifiedMembers
            .Select(NormalizeMms)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new MmsIedModelDirectory(liveDirectory.Points
            .Where(point => allowed.Contains(NormalizeMms(point.MmsReference))));
    }

    private static MmsHybridReportAcquisitionOptions AutomaticMonitoringOptions(
        MmsHybridReportAcquisitionOptions source,
        ProductionDynamicAuthorization production)
        => new()
        {
            MaxStaticReportPlans = source.MaxStaticReportPlans,
            MaxDynamicReportPlans = production.IsAuthorized
                ? Math.Min(source.MaxDynamicReportPlans, 1)
                : source.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = production.IsAuthorized
                ? Math.Min(source.MaxDynamicMembersPerReport, production.MemberReferences.Count)
                : source.MaxDynamicMembersPerReport,
            RequireExactAvailabilityEvidence = source.RequireExactAvailabilityEvidence,
            AllowCallerOwnedReports = source.AllowCallerOwnedReports,
            AllowStaticBrcb = source.AllowStaticBrcb,
            AllowStaticUrcb = source.AllowStaticUrcb,
            AllowDynamicBrcb = production.IsAuthorized && source.AllowDynamicBrcb,
            AllowDynamicUrcb = production.IsAuthorized && source.AllowDynamicUrcb,
            AllowPollingFallback = source.AllowPollingFallback
        };

    private static bool ValidateProductionDynamicSegments(
        MmsHybridReportAcquisitionPlan plan,
        ProductionDynamicAuthorization production,
        out string reason)
    {
        var dynamicSegments = plan.Segments
            .Where(segment => segment.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();

        if (dynamicSegments.Length == 0)
        {
            reason = "No production dynamic segment was needed or safely available for this request.";
            return true;
        }
        if (dynamicSegments.Length > 1)
        {
            reason = "The first production consumer is limited to one proven dynamic RCB group.";
            return false;
        }

        var segment = dynamicSegments[0];
        if (!SameReference(segment.ReportControlReference, production.RcbReference))
        {
            reason = $"Planner selected RCB {segment.ReportControlReference} instead of proven RCB {production.RcbReference}.";
            return false;
        }
        if (segment.ReportPlan is null || segment.ReportPlan.DynamicPoints.Count == 0)
        {
            reason = "Dynamic segment is missing an exact resolved production member set.";
            return false;
        }

        var plannedMembers = segment.ReportPlan.DynamicPoints
            .Select(point => NormalizeMms(point.MmsReference))
            .ToArray();
        if (!IsOrderedMemberSubset(plannedMembers, production.MemberReferences))
        {
            reason = "Dynamic segment member order/content is not an ordered subset of the proven InformationReport member set.";
            return false;
        }

        reason = "Dynamic segment remains inside the exact proven production evidence scope.";
        return true;
    }

    private static bool HasConfiguredStaticDataSetEvidence(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
           snapshot.DataSetDirectorySuccess &&
           snapshot.DataSetMembers.Count > 0 &&
           !string.IsNullOrWhiteSpace(snapshot.DataSetReference);

    private static void RestoreFreshAttributeEvidence(
        MmsHybridReportAcquisitionPlan plan,
        MmsRcbAvailabilityResult availability)
    {
        foreach (var segment in plan.Segments.Where(segment => segment.IsReportBacked && segment.ReportPlan?.ReportControl is not null))
        {
            var candidate = segment.ReportPlan!.ReportControl!;
            var snapshot = availability.ReportControls.FirstOrDefault(item =>
                Normalize(item.Reference).Equals(Normalize(candidate.Reference), StringComparison.OrdinalIgnoreCase));
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

            candidate.Attributes = attributes
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            candidate.ProbeDiagnostics.Clear();
            candidate.ProbeDiagnostics.AddRange(snapshot.ProbeDiagnostics);
            candidate.Status = segment.Kind is MmsHybridAcquisitionKind.StaticBrcb or MmsHybridAcquisitionKind.StaticUrcb
                ? "P6.1 baseline-static fresh availability snapshot"
                : "G2.6 ProductionEligible dynamic fresh availability snapshot";
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

    private static bool SameReference(string left, string right)
        => Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static bool ExactMemberSequenceEquals(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!NormalizeMms(left[index]).Equals(NormalizeMms(right[index]), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool IsOrderedMemberSubset(
        IReadOnlyList<string> subset,
        IReadOnlyList<string> full)
    {
        var searchIndex = 0;
        foreach (var candidate in subset.Select(NormalizeMms))
        {
            var found = false;
            while (searchIndex < full.Count)
            {
                if (candidate.Equals(NormalizeMms(full[searchIndex]), StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    searchIndex++;
                    break;
                }
                searchIndex++;
            }
            if (!found)
                return false;
        }
        return true;
    }

    private static string NormalizeMms(string? reference)
        => MmsFcReferenceNormalizer.NormalizeMmsReference(reference ?? string.Empty);

    private static string Normalize(string? reference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(reference).Replace('\\', '/');

    private sealed record ProductionDynamicAuthorization(
        bool IsAuthorized,
        string Reason,
        string RcbReference,
        IReadOnlyList<string> MemberReferences)
    {
        public static ProductionDynamicAuthorization Denied(string reason)
            => new(false, reason, string.Empty, Array.Empty<string>());
    }
}
