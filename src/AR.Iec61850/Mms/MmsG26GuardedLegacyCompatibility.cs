namespace AR.Iec61850.Mms;

/// <summary>
/// Application-supplied compatibility evidence for a legacy InformationReportProven profile
/// whose persisted InformationReport proof predates an explicit spontaneous data-change proof.
///
/// This evidence is intentionally separate from the persisted qualification profile. It may
/// only unlock the existing guarded-runtime planner after the engine verifies exact current
/// identity, exact proven RCB, exact ordered member sequence, a real dchg InformationReport,
/// NO-GI operation, association health, and complete cleanup. It never authorizes
/// ProductionEligible and it is never persisted by this policy.
/// </summary>
public sealed record MmsDynamicReportLegacyDataChangeCompatibilityEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string StableIdentityKey { get; init; } = string.Empty;
    public string ModelFingerprint { get; init; } = string.Empty;
    public string ProfileRevision { get; init; } = string.Empty;
    public string RcbReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public bool ActualInformationReportReceived { get; init; }
    public bool DataChangeReasonVerified { get; init; }
    public bool GeneralInterrogationDisabled { get; init; }
    public bool ExactMemberMappingVerified { get; init; }
    public bool AssociationHealthyAfterReport { get; init; }
    public bool CleanupSucceeded { get; init; }

    public bool IsSuccess =>
        !string.IsNullOrWhiteSpace(EvidenceId) &&
        !string.IsNullOrWhiteSpace(StableIdentityKey) &&
        !string.IsNullOrWhiteSpace(ModelFingerprint) &&
        !string.IsNullOrWhiteSpace(RcbReference) &&
        MemberReferences.Count > 0 &&
        ActualInformationReportReceived &&
        DataChangeReasonVerified &&
        GeneralInterrogationDisabled &&
        ExactMemberMappingVerified &&
        AssociationHealthyAfterReport &&
        CleanupSucceeded;
}

/// <summary>
/// P1.5 compatibility adapter for legacy InformationReportProven profiles.
///
/// The persisted profile is treated as untrusted input and is never mutated. If the stored
/// proof is already DataChange, the original context is returned unchanged. Otherwise a
/// compatibility view is created in memory only after the supplied physical evidence matches
/// the exact current identity, RCB, and ordered member sequence already present in the valid
/// persisted chain. The normal guarded planner then re-runs all of its fresh capability,
/// availability, exact-envelope and one-dynamic-group gates.
/// </summary>
public static class MmsGuardedDynamicReportLegacyCompatibilityPolicy
{
    public static bool TryBuildCompatibleContext(
        MmsDynamicReportGuardedRuntimePlanningContext sourceContext,
        MmsDynamicReportLegacyDataChangeCompatibilityEvidence? evidence,
        out MmsDynamicReportGuardedRuntimePlanningContext compatibleContext,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);

        compatibleContext = sourceContext;
        var profile = sourceContext.Profile;
        var currentIdentity = sourceContext.CurrentIdentity;
        var report = profile.InformationReportProof;

        if (report?.Kind == MmsDynamicInformationReportKind.DataChange)
        {
            reason = "Stored InformationReport proof is already DataChange; no legacy compatibility adaptation is required.";
            return true;
        }

        if (profile.SchemaVersion != MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
        {
            reason = $"Unsupported dynamic qualification profile schema {profile.SchemaVersion}; legacy compatibility is withheld.";
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
            reason = $"Dynamic qualification profile is {profile.State}; legacy compatibility requires InformationReportProven or stronger evidence.";
            return false;
        }

        var envelope = profile.AcceptedEnvelope;
        var activation = profile.RcbActivationProof;
        if (envelope is null || activation is null || report is null)
        {
            reason = "InformationReportProven profile is missing accepted-envelope, activation, or InformationReport evidence.";
            return false;
        }

        if (!activation.IsSuccess || !report.IsSuccess)
        {
            reason = "Stored activation/report evidence is unsuccessful; legacy compatibility is withheld.";
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
            reason = $"Stored InformationReport kind is {report.Kind}; no complete physical legacy dchg compatibility evidence was supplied.";
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

        if (!ExactMemberSequenceEquals(evidence.MemberReferences, report.MemberReferences))
        {
            reason = "Legacy dchg compatibility member sequence does not exactly match the persisted proven member sequence.";
            return false;
        }

        // Compatibility view only. The original profile object is not modified or saved.
        // The legacy evidence independently proves the later NO-GI dchg event on the same
        // exact RCB/member envelope; the existing planner still validates the original
        // activation/report DataSet chain plus fresh live availability before any write.
        var compatibilityView = profile with
        {
            InformationReportProof = report with
            {
                EvidenceId = "legacy-compatibility-view:" + evidence.EvidenceId.Trim(),
                Kind = MmsDynamicInformationReportKind.DataChange
            }
        };

        compatibleContext = sourceContext with { Profile = compatibilityView };
        reason =
            "Legacy InformationReportProven compatibility accepted from complete physical NO-GI dchg evidence on the exact current identity, proven RCB, and ordered member sequence. Persisted profile remains unchanged and ProductionEligible remains separate.";
        return true;
    }

    private static bool SameText(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameRcb(string? left, string? right)
        => string.Equals(
            MmsRcbAvailabilityEvaluator.NormalizeReference(left).Replace('\\', '/'),
            MmsRcbAvailabilityEvaluator.NormalizeReference(right).Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool ExactMemberSequenceEquals(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(
                    MmsFcReferenceNormalizer.NormalizeMmsReference(left[index] ?? string.Empty),
                    MmsFcReferenceNormalizer.NormalizeMmsReference(right[index] ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOrderedMemberSubset(
        IReadOnlyList<string> subset,
        IReadOnlyList<string> full)
    {
        var searchIndex = 0;
        foreach (var candidate in subset.Select(item => MmsFcReferenceNormalizer.NormalizeMmsReference(item ?? string.Empty)))
        {
            var found = false;
            while (searchIndex < full.Count)
            {
                var fullCandidate = MmsFcReferenceNormalizer.NormalizeMmsReference(full[searchIndex] ?? string.Empty);
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
}
