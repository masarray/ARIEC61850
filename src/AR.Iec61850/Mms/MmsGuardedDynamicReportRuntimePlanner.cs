using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Explicit application-supplied evidence for guarded automatic dynamic reporting.
/// This is intentionally separate from ProductionEligible certification: a successful,
/// identity-compatible InformationReportProven profile may authorize only the exact
/// RCB/member envelope that has already produced a real data-change InformationReport.
/// </summary>
public sealed record MmsDynamicReportGuardedRuntimePlanningContext
{
    public MmsDynamicReportQualificationProfile Profile { get; init; } = new();
    public MmsDynamicReportIedIdentity CurrentIdentity { get; init; } = new();
}

/// <summary>
/// Guarded G2.6 runtime planner used after physical InformationReport proof but before any
/// optional ProductionEligible certification. Static reporting keeps precedence. Dynamic
/// reporting is limited to one exact proven RCB and an ordered subset of the exact proven
/// InformationReport member sequence. Anything outside that envelope remains on polling.
/// </summary>
public static class MmsGuardedDynamicReportRuntimePlanner
{
    public static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities = null,
        MmsHybridReportAcquisitionOptions? options = null,
        MmsDynamicReportGuardedRuntimePlanningContext? guardedContext = null)
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
        var authorization = EvaluateGuardedAuthorization(guardedContext, capability, dynamicIntent);

        // Static RCBs remain eligible exactly as in the normal capability-aware planner.
        // The guarded dynamic path adds only the one already-proven empty/free RCB.
        var configuredStaticReferences = availability.ReportControls
            .Where(HasConfiguredStaticDataSetEvidence)
            .Select(snapshot => Normalize(snapshot.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedRcbReferences = configuredStaticReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (authorization.IsAuthorized)
            allowedRcbReferences.Add(Normalize(authorization.RcbReference));

        var restrictedAvailability = RestrictAvailability(
            availability,
            allowedRcbReferences,
            capability.Warnings);
        var restrictedInventory = RestrictInventory(inventory, allowedRcbReferences);
        var planningDirectory = authorization.IsAuthorized
            ? RestrictDirectoryToQualifiedMembers(liveDirectory, authorization.MemberReferences)
            : liveDirectory;

        var automaticOptions = GuardedMonitoringOptions(options, authorization);
        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            requestedSignals,
            restrictedInventory,
            restrictedAvailability,
            planningDirectory,
            automaticOptions);

        var policyWarnings = new List<string>();
        if (dynamicIntent && capability.MayAttemptDynamicReports && !authorization.IsAuthorized)
        {
            policyWarnings.Add(
                $"Guarded automatic dynamic reporting remains withheld: {authorization.Reason}");
        }

        // Treat persisted qualification JSON as untrusted input and re-check the emitted
        // dynamic plan. A mismatch cannot broaden the proven scope; it falls back to the
        // unchanged static -> polling plan.
        if (authorization.IsAuthorized &&
            !ValidateGuardedDynamicSegments(plan, authorization, out var invariantFailure))
        {
            policyWarnings.Add(
                $"Guarded dynamic plan failed the exact proven-envelope invariant and was withheld: {invariantFailure}");
            authorization = GuardedDynamicAuthorization.Denied(invariantFailure);

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
                GuardedMonitoringOptions(options, authorization));
        }

        RestoreFreshAttributeEvidence(plan, availability, authorization.IsAuthorized);

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = plan,
            AssociationCapability = capability,
            AutomaticDynamicActivationQuarantined =
                capability.MayAttemptDynamicReports &&
                dynamicIntent &&
                !authorization.IsAuthorized,
            // Guarded runtime authorization is deliberately not ProductionEligible.
            ProductionDynamicActivationAuthorized = false,
            ProductionDynamicAuthorizationReason = authorization.IsAuthorized
                ? "Guarded InformationReportProven runtime authorization is active; ProductionEligible certification remains separate."
                : authorization.Reason,
            ProductionQualifiedDynamicMemberCount = 0,
            ProductionQualifiedRcbReference = string.Empty,
            PolicyWarnings = policyWarnings.ToArray()
        };
    }

    private static GuardedDynamicAuthorization EvaluateGuardedAuthorization(
        MmsDynamicReportGuardedRuntimePlanningContext? context,
        MmsReportAssociationCapability capability,
        bool dynamicIntent)
    {
        if (!dynamicIntent)
            return GuardedDynamicAuthorization.Denied("Dynamic BRCB/URCB acquisition is disabled by planner policy.");
        if (context is null)
            return GuardedDynamicAuthorization.Denied("No InformationReportProven guarded-runtime context was supplied.");
        if (!capability.MayAttemptDynamicReports)
        {
            return GuardedDynamicAuthorization.Denied(
                "The current MMS association does not satisfy the dynamic-report capability gate.");
        }

        var profile = context.Profile;
        if (profile.SchemaVersion != MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
        {
            return GuardedDynamicAuthorization.Denied(
                $"Unsupported dynamic qualification profile schema {profile.SchemaVersion}; requalification is required.");
        }

        var compatibility = MmsDynamicReportQualificationProfilePolicy.CheckIdentityCompatibility(
            profile,
            context.CurrentIdentity);
        if (!compatibility.IsCompatible)
            return GuardedDynamicAuthorization.Denied(compatibility.Reason);

        if (profile.State < MmsDynamicReportQualificationState.InformationReportProven)
        {
            return GuardedDynamicAuthorization.Denied(
                $"Dynamic qualification profile is {profile.State}; guarded runtime requires InformationReportProven or stronger evidence.");
        }

        var envelope = profile.AcceptedEnvelope;
        var activation = profile.RcbActivationProof;
        var report = profile.InformationReportProof;
        if (envelope is null || activation is null || report is null)
            return GuardedDynamicAuthorization.Denied("InformationReportProven profile is missing required dynamic qualification evidence.");
        if (!activation.IsSuccess || !report.IsSuccess)
            return GuardedDynamicAuthorization.Denied("Stored activation/report evidence is unsuccessful; guarded runtime is withheld.");
        if (report.Kind != MmsDynamicInformationReportKind.DataChange)
        {
            return GuardedDynamicAuthorization.Denied(
                $"Guarded runtime requires a proven data-change InformationReport; stored kind is {report.Kind}.");
        }
        if (!SameReference(activation.RcbReference, report.RcbReference))
            return GuardedDynamicAuthorization.Denied("Stored activation/report RCB identities differ.");
        if (!SameReference(activation.DataSetReference, report.DataSetReference))
            return GuardedDynamicAuthorization.Denied("Stored activation/report DataSet identities differ.");
        if (!ExactMemberSequenceEquals(activation.MemberReferences, report.MemberReferences))
            return GuardedDynamicAuthorization.Denied("Stored activation/report member sequences differ.");
        if (!IsOrderedMemberSubset(report.MemberReferences, envelope.ExactProvenMemberReferences))
            return GuardedDynamicAuthorization.Denied("InformationReport members are outside the exact qualified envelope.");
        if (report.MemberReferences.Count == 0 || report.MemberReferences.Count > envelope.ProvenMemberCount)
            return GuardedDynamicAuthorization.Denied("InformationReport member count is outside the accepted qualified envelope.");

        var normalizedMembers = report.MemberReferences
            .Select(NormalizeMms)
            .ToArray();
        if (normalizedMembers.Any(string.IsNullOrWhiteSpace) ||
            normalizedMembers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedMembers.Length)
        {
            return GuardedDynamicAuthorization.Denied("InformationReport guarded-runtime member evidence is empty or duplicated.");
        }

        return new GuardedDynamicAuthorization(
            true,
            "Identity-compatible InformationReportProven data-change evidence authorizes guarded runtime on the exact proven RCB/member envelope.",
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

    private static MmsHybridReportAcquisitionOptions GuardedMonitoringOptions(
        MmsHybridReportAcquisitionOptions source,
        GuardedDynamicAuthorization authorization)
        => new()
        {
            MaxStaticReportPlans = source.MaxStaticReportPlans,
            MaxDynamicReportPlans = authorization.IsAuthorized
                ? Math.Min(source.MaxDynamicReportPlans, 1)
                : source.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = authorization.IsAuthorized
                ? Math.Min(source.MaxDynamicMembersPerReport, authorization.MemberReferences.Count)
                : source.MaxDynamicMembersPerReport,
            RequireExactAvailabilityEvidence = source.RequireExactAvailabilityEvidence,
            AllowCallerOwnedReports = source.AllowCallerOwnedReports,
            AllowStaticBrcb = source.AllowStaticBrcb,
            AllowStaticUrcb = source.AllowStaticUrcb,
            AllowDynamicBrcb = authorization.IsAuthorized && source.AllowDynamicBrcb,
            AllowDynamicUrcb = authorization.IsAuthorized && source.AllowDynamicUrcb,
            AllowPollingFallback = source.AllowPollingFallback
        };

    private static bool ValidateGuardedDynamicSegments(
        MmsHybridReportAcquisitionPlan plan,
        GuardedDynamicAuthorization authorization,
        out string reason)
    {
        var dynamicSegments = plan.Segments
            .Where(segment => segment.Kind is MmsHybridAcquisitionKind.DynamicBrcb or MmsHybridAcquisitionKind.DynamicUrcb)
            .ToArray();

        if (dynamicSegments.Length == 0)
        {
            reason = "No guarded dynamic segment was needed or safely available for this request.";
            return true;
        }
        if (dynamicSegments.Length > 1)
        {
            reason = "Guarded runtime is limited to one exact proven dynamic RCB group.";
            return false;
        }

        var segment = dynamicSegments[0];
        if (!SameReference(segment.ReportControlReference, authorization.RcbReference))
        {
            reason = $"Planner selected RCB {segment.ReportControlReference} instead of proven RCB {authorization.RcbReference}.";
            return false;
        }
        if (segment.ReportPlan is null || segment.ReportPlan.DynamicPoints.Count == 0)
        {
            reason = "Dynamic segment is missing an exact resolved guarded-runtime member set.";
            return false;
        }

        var plannedMembers = segment.ReportPlan.DynamicPoints
            .Select(point => NormalizeMms(point.MmsReference))
            .ToArray();
        if (!IsOrderedMemberSubset(plannedMembers, authorization.MemberReferences))
        {
            reason = "Dynamic segment member order/content is not an ordered subset of the proven InformationReport member set.";
            return false;
        }

        reason = "Dynamic segment remains inside the exact InformationReportProven guarded-runtime evidence scope.";
        return true;
    }

    private static bool HasConfiguredStaticDataSetEvidence(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
           snapshot.DataSetDirectorySuccess &&
           snapshot.DataSetMembers.Count > 0 &&
           !string.IsNullOrWhiteSpace(snapshot.DataSetReference);

    private static void RestoreFreshAttributeEvidence(
        MmsHybridReportAcquisitionPlan plan,
        MmsRcbAvailabilityResult availability,
        bool guardedDynamicAuthorized)
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
                : guardedDynamicAuthorized
                    ? "G2.6 InformationReportProven guarded-runtime dynamic fresh availability snapshot"
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

    private static bool SameReference(string? left, string? right)
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

    private sealed record GuardedDynamicAuthorization(
        bool IsAuthorized,
        string Reason,
        string RcbReference,
        IReadOnlyList<string> MemberReferences)
    {
        public static GuardedDynamicAuthorization Denied(string reason)
            => new(false, reason, string.Empty, Array.Empty<string>());
    }
}
