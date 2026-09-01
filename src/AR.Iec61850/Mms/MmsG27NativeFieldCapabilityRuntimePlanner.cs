using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Durable application-supplied witness for a native, identity-compatible DataChange
/// InformationReportProven profile. Unlike the legacy P1.5b compatibility adapter, this
/// evidence is produced by the same per-IED commissioning transaction that records the
/// persisted activation + DataChange InformationReport chain.
///
/// The witness deliberately retains cleanup evidence outside the qualification profile so
/// general Dynamic RCB runtime cannot be unlocked by a DataChange profile alone. The engine
/// requires exact identity/profile/activation/report/RCB/DataSet binding plus successful
/// monitor cleanup, proof-field restore and fresh-association closure.
/// </summary>
public sealed record MmsDynamicReportNativeFieldCapabilityEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string StableIdentityKey { get; init; } = string.Empty;
    public string ModelFingerprint { get; init; } = string.Empty;
    public string ProfileRevision { get; init; } = string.Empty;
    public string RcbReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string RcbActivationEvidenceId { get; init; } = string.Empty;
    public string InformationReportEvidenceId { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludedMemberReferences { get; init; } = Array.Empty<string>();
    public bool ActualInformationReportReceived { get; init; }
    public bool DataChangeReasonVerified { get; init; }
    public bool GeneralInterrogationDisabled { get; init; }
    public bool ExactMemberMappingVerified { get; init; }
    public bool AssociationHealthyAfterReport { get; init; }
    public bool MonitorCleanupSucceeded { get; init; }
    public bool ProofFieldRestoreSucceeded { get; init; }
    public bool FreshCleanupClosureSucceeded { get; init; }

    public bool IsSuccess =>
        !string.IsNullOrWhiteSpace(EvidenceId) &&
        ObservedAtUtc != default &&
        !string.IsNullOrWhiteSpace(StableIdentityKey) &&
        !string.IsNullOrWhiteSpace(ModelFingerprint) &&
        !string.IsNullOrWhiteSpace(RcbReference) &&
        !string.IsNullOrWhiteSpace(DataSetReference) &&
        !string.IsNullOrWhiteSpace(RcbActivationEvidenceId) &&
        !string.IsNullOrWhiteSpace(InformationReportEvidenceId) &&
        IncludedMemberReferences.Count > 0 &&
        ActualInformationReportReceived &&
        DataChangeReasonVerified &&
        GeneralInterrogationDisabled &&
        ExactMemberMappingVerified &&
        AssociationHealthyAfterReport &&
        MonitorCleanupSucceeded &&
        ProofFieldRestoreSucceeded &&
        FreshCleanupClosureSucceeded;
}

/// <summary>
/// P1.7 native per-IED field-capability authorization policy.
///
/// A persisted DataChange InformationReport proof is necessary but deliberately insufficient:
/// the separately persisted cleanup witness must bind back to the exact activation/report
/// evidence IDs and exact RCB/DataSet on the current identity-compatible profile. Once that
/// binding is accepted, the physical witness proves the dynamic reporting mechanism rather
/// than permanently restricting runtime members to the commissioning DataSet.
/// </summary>
public static class MmsGuardedDynamicReportNativeFieldCapabilityPolicy
{
    public static bool TryValidate(
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportNativeFieldCapabilityEvidence? evidence,
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
            reason = $"Unsupported dynamic qualification profile schema {profile.SchemaVersion}; native field capability is withheld.";
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
            reason = $"Dynamic qualification profile is {profile.State}; native field capability requires InformationReportProven or stronger evidence.";
            return false;
        }

        if (envelope is null || activation is null || report is null)
        {
            reason = "InformationReportProven profile is missing accepted-envelope, activation, or InformationReport evidence.";
            return false;
        }

        if (!activation.IsSuccess || !report.IsSuccess)
        {
            reason = "Stored activation/report evidence is unsuccessful; native field capability is withheld.";
            return false;
        }

        if (report.Kind != MmsDynamicInformationReportKind.DataChange)
        {
            reason = $"Native field capability requires a persisted DataChange InformationReport proof; stored kind is {report.Kind}.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.SameRcb(activation.RcbReference, report.RcbReference))
        {
            reason = "Stored activation/report RCB identities differ.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.SameRcb(activation.DataSetReference, report.DataSetReference))
        {
            reason = "Stored activation/report DataSet identities differ.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.ExactMemberSequenceEquals(
                activation.MemberReferences,
                report.MemberReferences))
        {
            reason = "Stored activation/report member sequences differ.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.IsOrderedMemberSubset(
                report.MemberReferences,
                envelope.ExactProvenMemberReferences))
        {
            reason = "Stored DataChange report members are outside the exact accepted envelope.";
            return false;
        }

        if (report.MemberReferences.Count == 0 || report.MemberReferences.Count > envelope.ProvenMemberCount)
        {
            reason = "Stored DataChange report member count is outside the accepted envelope.";
            return false;
        }

        if (evidence?.IsSuccess != true)
        {
            reason = "No complete native physical dchg + cleanup capability witness was supplied.";
            return false;
        }

        if (!SameText(evidence.StableIdentityKey, currentIdentity.StableIdentityKey))
        {
            reason = "Native field-capability stable identity does not match the current IED identity.";
            return false;
        }

        if (!SameText(evidence.ModelFingerprint, currentIdentity.ModelFingerprint))
        {
            reason = "Native field-capability model fingerprint does not match the current IED model.";
            return false;
        }

        if (!SameText(evidence.ProfileRevision, currentIdentity.ProfileRevision))
        {
            reason = "Native field-capability profile revision does not match the current IED profile revision.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.SameRcb(evidence.RcbReference, report.RcbReference))
        {
            reason = "Native field-capability RCB does not match the persisted DataChange report RCB.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.SameRcb(evidence.DataSetReference, report.DataSetReference))
        {
            reason = "Native field-capability DataSet does not match the persisted DataChange report DataSet.";
            return false;
        }

        if (!SameText(evidence.RcbActivationEvidenceId, activation.EvidenceId))
        {
            reason = "Native field-capability witness is not bound to the current persisted RCB activation evidence.";
            return false;
        }

        if (!SameText(evidence.InformationReportEvidenceId, report.EvidenceId))
        {
            reason = "Native field-capability witness is not bound to the current persisted InformationReport evidence.";
            return false;
        }

        var normalizedIncluded = evidence.IncludedMemberReferences
            .Select(MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.NormalizeMms)
            .ToArray();
        if (normalizedIncluded.Any(string.IsNullOrWhiteSpace) ||
            normalizedIncluded.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedIncluded.Length)
        {
            reason = "Native field-capability included-member evidence is empty or duplicated.";
            return false;
        }

        if (report.ReportAuthoritativePointCount != evidence.IncludedMemberReferences.Count)
        {
            reason = $"Native field-capability included-member count {evidence.IncludedMemberReferences.Count} does not match persisted authoritative report count {report.ReportAuthoritativePointCount}.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.IsOrderedMemberSubset(
                evidence.IncludedMemberReferences,
                report.MemberReferences))
        {
            reason = "Native field-capability included members are not an ordered subset of the persisted DataChange report DataSet.";
            return false;
        }

        if (!MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.IsOrderedMemberSubset(
                evidence.IncludedMemberReferences,
                envelope.ExactProvenMemberReferences))
        {
            reason = "Native field-capability included members are outside the accepted qualification envelope.";
            return false;
        }

        reason =
            "P1.7 native field-capability witness accepted: exact identity/profile/activation/report/RCB/DataSet binding, actual NO-GI dchg mapping, association health, monitor cleanup, proof-field restore and fresh cleanup closure all match. The witness proves the dynamic reporting mechanism; fresh exact RCB availability and live member resolution still govern every runtime DataSet. ProductionEligible remains separate.";
        return true;
    }

    private static bool SameText(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// P1.7 general Dynamic RCB runtime for a natively commissioned per-IED field-capability
/// witness. Static reporting keeps precedence. Every still-uncovered exact-resolved signal
/// may use bounded fresh verified-free dynamic RCB slots; only genuine residuals poll.
/// </summary>
public static class MmsGuardedDynamicReportNativeFieldCapabilityRuntimePlanner
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
        MmsDynamicReportNativeFieldCapabilityEvidence evidence)
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
                         MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
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
                $"P1.7 native field-capability general dynamic reporting remains withheld: {authorizationReason}");
        }

        if (authorized && !ValidateDynamicSegments(plan, availability, liveDirectory, effectiveOptions, out var invariantFailure))
        {
            policyWarnings.Add(
                $"P1.7 native general dynamic plan failed fresh runtime invariants and was withheld: {invariantFailure}");
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
                ? "P1.7 native per-IED field-capability normal runtime is active for fresh exact-resolved residual signals and fresh verified-free dynamic RCBs; ProductionEligible certification remains separate."
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

        reason = $"P1.7 fresh runtime invariants accepted for {dynamicSegments.Length} dynamic RCB group(s).";
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
                ? "P1.7 baseline-static fresh availability snapshot"
                : fieldCapabilityAuthorized
                    ? "G2.7 native field-capability general-dynamic fresh availability snapshot"
                    : "G2.7 dynamic fresh availability snapshot";
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
        if (bool.TryParse(value, out var parsed))
            return parsed;
        if (int.TryParse(value, out var numeric))
            return numeric != 0;
        return null;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>
/// Stable per-RCB DataSet identity wrapper for P1.7 native field capability.
/// </summary>
public static class MmsGuardedDynamicReportNativeFieldCapabilityStableRuntimePlanner
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
        MmsDynamicReportNativeFieldCapabilityEvidence evidence)
    {
        var plan = MmsGuardedDynamicReportNativeFieldCapabilityRuntimePlanner.Build(
            catalog,
            requestedSignals,
            inventory,
            availability,
            liveDirectory,
            negotiatedCapabilities,
            options,
            sourceContext,
            evidence);

        return MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner
            .WithStableDynamicDataSetIdentities(plan);
    }
}
