using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// P1.5b validator for legacy InformationReportProven profiles whose persisted report chain
/// covers a broader ordered member set than the later physical NO-GI dchg proof.
///
/// The persisted qualification profile is never rewritten. The later dchg evidence may
/// authorize only its own exact ordered member subset when that subset is contained in both
/// the stored successful report sequence and the accepted qualification envelope.
/// </summary>
public static class MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy
{
    public static bool TryValidate(
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence? evidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);

        var profile = sourceContext.Profile;
        var currentIdentity = sourceContext.CurrentIdentity;
        var envelope = profile.AcceptedEnvelope;
        var activation = profile.RcbActivationProof;
        var report = profile.InformationReportProof;

        if (profile.SchemaVersion != MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
        {
            reason = $"Unsupported dynamic qualification profile schema {profile.SchemaVersion}; P1.5b compatibility is withheld.";
            return false;
        }

        var identityCompatibility = MmsDynamicReportQualificationProfilePolicy.CheckIdentityCompatibility(
            profile,
            currentIdentity);
        if (!identityCompatibility.IsCompatible)
        {
            reason = identityCompatibility.Reason;
            return false;
        }

        if (profile.State < MmsDynamicReportQualificationState.InformationReportProven)
        {
            reason = $"Dynamic qualification profile is {profile.State}; P1.5b requires InformationReportProven or stronger evidence.";
            return false;
        }

        if (envelope is null || activation is null || report is null)
        {
            reason = "InformationReportProven profile is missing accepted-envelope, activation, or InformationReport evidence.";
            return false;
        }

        if (!activation.IsSuccess || !report.IsSuccess)
        {
            reason = "Stored activation/report evidence is unsuccessful; P1.5b compatibility is withheld.";
            return false;
        }

        if (report.Kind != MmsDynamicInformationReportKind.GeneralInterrogation)
        {
            reason = $"P1.5b subset compatibility applies only to the reviewed GI-classified legacy chain; stored kind is {report.Kind}.";
            return false;
        }

        if (!SameRcb(activation.RcbReference, report.RcbReference))
        {
            reason = "Stored activation/report RCB identities differ.";
            return false;
        }

        if (!SameRcb(activation.DataSetReference, report.DataSetReference))
        {
            reason = "Stored activation/report DataSet identities differ.";
            return false;
        }

        if (!ExactMemberSequenceEquals(activation.MemberReferences, report.MemberReferences))
        {
            reason = "Stored activation/report member sequences differ.";
            return false;
        }

        if (!IsOrderedMemberSubset(report.MemberReferences, envelope.ExactProvenMemberReferences))
        {
            reason = "Stored InformationReport members are outside the exact accepted envelope.";
            return false;
        }

        if (report.MemberReferences.Count == 0 || report.MemberReferences.Count > envelope.ProvenMemberCount)
        {
            reason = "Stored InformationReport member count is outside the accepted envelope.";
            return false;
        }

        if (evidence?.IsSuccess != true)
        {
            reason = "No complete physical NO-GI dchg compatibility evidence was supplied.";
            return false;
        }

        if (!SameText(evidence.StableIdentityKey, currentIdentity.StableIdentityKey))
        {
            reason = "Legacy dchg compatibility stable identity does not match the current IED identity.";
            return false;
        }

        if (!SameText(evidence.ModelFingerprint, currentIdentity.ModelFingerprint))
        {
            reason = "Legacy dchg compatibility model fingerprint does not match the current IED model.";
            return false;
        }

        if (!SameText(evidence.ProfileRevision, currentIdentity.ProfileRevision))
        {
            reason = "Legacy dchg compatibility profile revision does not match the current IED profile revision.";
            return false;
        }

        if (!SameRcb(evidence.RcbReference, report.RcbReference))
        {
            reason = "Legacy dchg compatibility RCB does not match the persisted proven RCB.";
            return false;
        }

        var normalizedEvidenceMembers = evidence.MemberReferences
            .Select(NormalizeMms)
            .ToArray();
        if (normalizedEvidenceMembers.Any(string.IsNullOrWhiteSpace) ||
            normalizedEvidenceMembers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedEvidenceMembers.Length)
        {
            reason = "Legacy dchg compatibility member evidence is empty or duplicated.";
            return false;
        }

        if (!IsOrderedMemberSubset(evidence.MemberReferences, report.MemberReferences))
        {
            reason = "Legacy dchg compatibility members are not an ordered subset of the persisted successful report sequence.";
            return false;
        }

        if (!IsOrderedMemberSubset(evidence.MemberReferences, envelope.ExactProvenMemberReferences))
        {
            reason = "Legacy dchg compatibility members are outside the accepted qualification envelope.";
            return false;
        }

        reason =
            "P1.5b legacy subset compatibility accepted: the later physical NO-GI dchg members are an exact ordered subset of the unchanged persisted successful report chain and accepted envelope. ProductionEligible remains separate.";
        return true;
    }

    internal static bool SameRcb(string? left, string? right)
        => string.Equals(
            MmsRcbAvailabilityEvaluator.NormalizeReference(left).Replace('\\', '/'),
            MmsRcbAvailabilityEvaluator.NormalizeReference(right).Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    internal static bool ExactMemberSequenceEquals(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(NormalizeMms(left[index]), NormalizeMms(right[index]), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    internal static bool IsOrderedMemberSubset(
        IReadOnlyList<string> subset,
        IReadOnlyList<string> full)
    {
        var searchIndex = 0;
        foreach (var candidate in subset.Select(NormalizeMms))
        {
            var found = false;
            while (searchIndex < full.Count)
            {
                var fullCandidate = NormalizeMms(full[searchIndex]);
                searchIndex++;
                if (!string.Equals(candidate, fullCandidate, StringComparison.OrdinalIgnoreCase))
                    continue;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static bool SameText(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeMms(string? reference)
        => MmsFcReferenceNormalizer.NormalizeMmsReference(reference ?? string.Empty);
}

/// <summary>
/// Guarded runtime planner for P1.5b legacy subset compatibility.
///
/// Static reporting keeps precedence. The one reviewed dynamic RCB may be used only for the
/// later physically proven dchg subset. The broader persisted GI-classified member sequence
/// remains qualification evidence only and is never promoted to dchg authority.
/// </summary>
public static class MmsGuardedDynamicReportLegacySubsetRuntimePlanner
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
        var capability = MmsReportAssociationCapabilityEvaluator.Evaluate(
            availability,
            negotiatedCapabilities,
            options);
        var dynamicIntent = options.AllowDynamicBrcb || options.AllowDynamicUrcb;

        var authorized = dynamicIntent &&
                         capability.MayAttemptDynamicReports &&
                         MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
                             sourceContext,
                             evidence,
                             out var compatibilityReason);

        if (!dynamicIntent)
            compatibilityReason = "Dynamic BRCB/URCB acquisition is disabled by planner policy.";
        else if (!capability.MayAttemptDynamicReports)
            compatibilityReason = "The current MMS association does not satisfy the dynamic-report capability gate.";

        var configuredStaticReferences = availability.ReportControls
            .Where(HasConfiguredStaticDataSetEvidence)
            .Select(snapshot => Normalize(snapshot.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedRcbReferences = configuredStaticReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (authorized)
            allowedRcbReferences.Add(Normalize(evidence.RcbReference));

        var restrictedAvailability = RestrictAvailability(availability, allowedRcbReferences, capability.Warnings);
        var restrictedInventory = RestrictInventory(inventory, allowedRcbReferences);
        var planningDirectory = authorized
            ? RestrictDirectoryToMembers(liveDirectory, evidence.MemberReferences)
            : liveDirectory;
        var effectiveOptions = BuildOptions(options, authorized, evidence.MemberReferences.Count);

        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            requestedSignals,
            restrictedInventory,
            restrictedAvailability,
            planningDirectory,
            effectiveOptions);

        var policyWarnings = new List<string>();
        if (dynamicIntent && capability.MayAttemptDynamicReports && !authorized)
        {
            policyWarnings.Add($"P1.5b guarded legacy subset dynamic reporting remains withheld: {compatibilityReason}");
        }

        if (authorized && !ValidateDynamicSegments(plan, evidence, out var invariantFailure))
        {
            policyWarnings.Add(
                $"P1.5b guarded dynamic plan failed the exact physical dchg-subset invariant and was withheld: {invariantFailure}");
            authorized = false;
            compatibilityReason = invariantFailure;

            var staticAvailability = RestrictAvailability(availability, configuredStaticReferences, capability.Warnings);
            var staticInventory = RestrictInventory(inventory, configuredStaticReferences);
            plan = MmsHybridReportAcquisitionPlanner.Build(
                catalog,
                requestedSignals,
                staticInventory,
                staticAvailability,
                liveDirectory,
                BuildOptions(options, false, 0));
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
                ? "P1.5b guarded legacy subset runtime authorization is active only for the separately proven NO-GI dchg subset; ProductionEligible certification remains separate."
                : compatibilityReason,
            ProductionQualifiedDynamicMemberCount = 0,
            ProductionQualifiedRcbReference = string.Empty,
            PolicyWarnings = policyWarnings.ToArray()
        };
    }

    private static MmsHybridReportAcquisitionOptions BuildOptions(
        MmsHybridReportAcquisitionOptions source,
        bool authorized,
        int provenMemberCount)
        => new()
        {
            MaxStaticReportPlans = source.MaxStaticReportPlans,
            MaxDynamicReportPlans = authorized ? Math.Min(source.MaxDynamicReportPlans, 1) : source.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = authorized
                ? Math.Min(source.MaxDynamicMembersPerReport, provenMemberCount)
                : source.MaxDynamicMembersPerReport,
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
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence evidence,
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
            reason = "P1.5b guarded runtime is limited to one exact proven dynamic RCB group.";
            return false;
        }

        var segment = dynamicSegments[0];
        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.SameRcb(
                segment.ReportControlReference,
                evidence.RcbReference))
        {
            reason = $"Planner selected RCB {segment.ReportControlReference} instead of physical dchg-proven RCB {evidence.RcbReference}.";
            return false;
        }

        if (segment.ReportPlan is null || segment.ReportPlan.DynamicPoints.Count == 0)
        {
            reason = "Dynamic segment is missing an exact resolved P1.5b member subset.";
            return false;
        }

        var plannedMembers = segment.ReportPlan.DynamicPoints
            .Select(point => MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.NormalizeMms(point.MmsReference))
            .ToArray();
        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.IsOrderedMemberSubset(
                plannedMembers,
                evidence.MemberReferences))
        {
            reason = "Dynamic segment member order/content is outside the later physically proven dchg subset.";
            return false;
        }

        reason = "Dynamic segment remains inside the exact P1.5b physical dchg subset.";
        return true;
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

    private static MmsIedModelDirectory RestrictDirectoryToMembers(
        MmsIedModelDirectory liveDirectory,
        IReadOnlyList<string> members)
    {
        var allowed = members
            .Select(MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.NormalizeMms)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new MmsIedModelDirectory(liveDirectory.Points
            .Where(point => allowed.Contains(
                MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.NormalizeMms(point.MmsReference))));
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

            candidate.Attributes = attributes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            candidate.ProbeDiagnostics.Clear();
            candidate.ProbeDiagnostics.AddRange(snapshot.ProbeDiagnostics);
            candidate.Status = segment.Kind is MmsHybridAcquisitionKind.StaticBrcb or MmsHybridAcquisitionKind.StaticUrcb
                ? "P6.1 baseline-static fresh availability snapshot"
                : guardedDynamicAuthorized
                    ? "G2.6 P1.5b legacy-subset guarded-runtime dynamic fresh availability snapshot"
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

    private static string Normalize(string? reference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(reference).Replace('\\', '/');
}
