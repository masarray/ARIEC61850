namespace AR.Iec61850.Mms;

public sealed record MmsDynamicReportIedIdentity
{
    public string StableIdentityKey { get; init; } = string.Empty;
    public string ModelFingerprint { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string FirmwareRevision { get; init; } = string.Empty;
    public string ProfileRevision { get; init; } = string.Empty;
}

public enum MmsDynamicReportProfileCompatibilityStatus
{
    Compatible,
    MissingCurrentIdentity,
    StableIdentityMismatch,
    ModelFingerprintMismatch,
    ManufacturerMismatch,
    ModelMismatch,
    FirmwareRevisionMismatch,
    ProfileRevisionMismatch
}

public sealed record MmsDynamicReportProfileCompatibility
{
    public MmsDynamicReportProfileCompatibilityStatus Status { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsCompatible => Status == MmsDynamicReportProfileCompatibilityStatus.Compatible;
}

public sealed record MmsDynamicReportCapacityEvidence
{
    public int ObservedFreeBrcbSlots { get; init; }
    public int ObservedFreeUrcbSlots { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string EvidenceId { get; init; } = string.Empty;
}

public sealed record MmsDynamicRcbActivationProof
{
    public string EvidenceId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public bool FreshRcbAvailabilityVerified { get; init; }
    public bool DataSetReadbackVerified { get; init; }
    public bool RcbDataSetBindingAccepted { get; init; }
    public bool RptEnaAccepted { get; init; }
    public bool AssociationHealthyAfterActivation { get; init; }
    public bool IsSuccess =>
        FreshRcbAvailabilityVerified &&
        DataSetReadbackVerified &&
        RcbDataSetBindingAccepted &&
        RptEnaAccepted &&
        AssociationHealthyAfterActivation;
}

public enum MmsDynamicInformationReportKind
{
    Unknown,
    GeneralInterrogation,
    DataChange,
    Integrity,
    OtherVerified
}

public sealed record MmsDynamicInformationReportProof
{
    public string EvidenceId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public MmsDynamicInformationReportKind Kind { get; init; }
    public bool ActualInformationReportReceived { get; init; }
    public bool ReportIdentityVerified { get; init; }
    public bool ExactMemberMappingVerified { get; init; }
    public bool AssociationHealthyAfterReport { get; init; }
    public int ReportAuthoritativePointCount { get; init; }
    public bool IsSuccess =>
        ActualInformationReportReceived &&
        ReportIdentityVerified &&
        ExactMemberMappingVerified &&
        AssociationHealthyAfterReport &&
        ReportAuthoritativePointCount > 0 &&
        Kind != MmsDynamicInformationReportKind.Unknown;
}

public sealed record MmsDynamicReportProductionAcceptance
{
    public string FieldEvidenceId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public bool ControlRegressionPassed { get; init; }
    public bool StaticReportingRegressionPassed { get; init; }
    public bool DynamicInformationReportRegressionPassed { get; init; }
    public bool PollingAuthorityGuardPassed { get; init; }
    public bool ReconnectRegressionPassed { get; init; }
    public bool QualityRegressionPassed { get; init; }
    public bool NoRepeatedMutationLoopPassed { get; init; }

    public bool AllPassed =>
        ControlRegressionPassed &&
        StaticReportingRegressionPassed &&
        DynamicInformationReportRegressionPassed &&
        PollingAuthorityGuardPassed &&
        ReconnectRegressionPassed &&
        QualityRegressionPassed &&
        NoRepeatedMutationLoopPassed;
}

public sealed record MmsDynamicReportQualificationProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public MmsDynamicReportIedIdentity Identity { get; init; } = new();
    public MmsDynamicReportQualificationState State { get; init; } = MmsDynamicReportQualificationState.Advertised;
    public MmsDynamicDataSetQualifiedEnvelope? AcceptedEnvelope { get; init; }
    public IReadOnlyList<string> IsolatedRejectedMembers { get; init; } = Array.Empty<string>();
    public MmsDynamicReportCapacityEvidence? CapacityEvidence { get; init; }
    public MmsDynamicRcbActivationProof? RcbActivationProof { get; init; }
    public MmsDynamicInformationReportProof? InformationReportProof { get; init; }
    public MmsDynamicReportProductionAcceptance? ProductionAcceptance { get; init; }
    public IReadOnlyList<string> SourceEvidenceIds { get; init; } = Array.Empty<string>();
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int ProvenSafeMemberCount => AcceptedEnvelope?.ProvenMemberCount ?? 0;
    public int ProvenSafeDefineRequestByteCount => AcceptedEnvelope?.ProvenDefineRequestByteCount ?? 0;
    public int? NegotiatedMaxMmsPduSize => AcceptedEnvelope?.NegotiatedMaxMmsPduSize;
}

public static class MmsDynamicReportQualificationProfilePolicy
{
    public static MmsDynamicReportQualificationProfile CreateEnvelopeQualifiedProfile(
        MmsDynamicReportIedIdentity identity,
        MmsDynamicDataSetQualifiedEnvelope acceptedEnvelope,
        MmsDynamicDataSetQualificationAssessment assessment,
        MmsDynamicReportCapacityEvidence? capacityEvidence,
        string sourceEvidenceId,
        DateTimeOffset? nowUtc = null)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(acceptedEnvelope);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEvidenceId);
        ValidateCapacity(capacityEvidence);

        if (acceptedEnvelope.State != MmsDynamicReportQualificationState.EnvelopeQualified)
            throw new InvalidOperationException("The accepted envelope must be in EnvelopeQualified state.");
        if (acceptedEnvelope.ProvenMemberCount <= 1 || acceptedEnvelope.ExactProvenMemberReferences.Count != acceptedEnvelope.ProvenMemberCount)
            throw new InvalidOperationException("An EnvelopeQualified profile requires a consistent successful multi-member envelope.");
        if (acceptedEnvelope.ProvenDefineRequestByteCount <= 0)
            throw new InvalidOperationException("The accepted envelope must retain a positive encoded Define request size.");
        if (!assessment.HasMultiMemberEnvelopeCandidate)
            throw new InvalidOperationException("The qualification assessment does not contain multi-member success evidence.");

        var sourceAttempt = assessment.Attempts.FirstOrDefault(attempt =>
            attempt.AttemptId.Equals(acceptedEnvelope.SourceAttemptId, StringComparison.OrdinalIgnoreCase));
        if (sourceAttempt is null || !sourceAttempt.IsQualificationSuccess || sourceAttempt.MemberCount <= 1)
            throw new InvalidOperationException("The accepted envelope source attempt is not a cleanup-safe, association-surviving multi-member success.");
        if (!ExactSequenceEquals(sourceAttempt.MemberReferences, acceptedEnvelope.ExactProvenMemberReferences))
            throw new InvalidOperationException("The accepted envelope members do not exactly match its source qualification attempt.");
        if (sourceAttempt.DefineRequestByteCount != acceptedEnvelope.ProvenDefineRequestByteCount)
            throw new InvalidOperationException("The accepted envelope request-size evidence does not match its source attempt.");

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return new MmsDynamicReportQualificationProfile
        {
            Identity = NormalizeIdentity(identity),
            State = MmsDynamicReportQualificationState.EnvelopeQualified,
            AcceptedEnvelope = acceptedEnvelope,
            IsolatedRejectedMembers = assessment.IsolatedRejectedMembers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            CapacityEvidence = capacityEvidence,
            SourceEvidenceIds = DistinctEvidenceIds(sourceEvidenceId, acceptedEnvelope.SourceAttemptId, capacityEvidence?.EvidenceId),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public static MmsDynamicReportProfileCompatibility CheckIdentityCompatibility(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (currentIdentity is null ||
            string.IsNullOrWhiteSpace(currentIdentity.StableIdentityKey) ||
            string.IsNullOrWhiteSpace(currentIdentity.ModelFingerprint))
        {
            return Incompatible(
                MmsDynamicReportProfileCompatibilityStatus.MissingCurrentIdentity,
                "Current IED identity is incomplete; a persisted dynamic qualification profile cannot be trusted.");
        }

        var stored = profile.Identity;
        if (!EqualsNormalized(stored.StableIdentityKey, currentIdentity.StableIdentityKey))
            return Incompatible(MmsDynamicReportProfileCompatibilityStatus.StableIdentityMismatch, "Stable IED identity key changed; requalification is required.");
        if (!EqualsNormalized(stored.ModelFingerprint, currentIdentity.ModelFingerprint))
            return Incompatible(MmsDynamicReportProfileCompatibilityStatus.ModelFingerprintMismatch, "IED model/profile fingerprint changed; requalification is required.");

        var optionalMismatch = CompareOptional(stored.Manufacturer, currentIdentity.Manufacturer,
            MmsDynamicReportProfileCompatibilityStatus.ManufacturerMismatch, "IED manufacturer evidence changed; requalification is required.");
        if (optionalMismatch is not null)
            return optionalMismatch;
        optionalMismatch = CompareOptional(stored.Model, currentIdentity.Model,
            MmsDynamicReportProfileCompatibilityStatus.ModelMismatch, "IED model evidence changed; requalification is required.");
        if (optionalMismatch is not null)
            return optionalMismatch;
        optionalMismatch = CompareOptional(stored.FirmwareRevision, currentIdentity.FirmwareRevision,
            MmsDynamicReportProfileCompatibilityStatus.FirmwareRevisionMismatch, "IED firmware revision changed; requalification is required.");
        if (optionalMismatch is not null)
            return optionalMismatch;
        optionalMismatch = CompareOptional(stored.ProfileRevision, currentIdentity.ProfileRevision,
            MmsDynamicReportProfileCompatibilityStatus.ProfileRevisionMismatch, "IED configuration/profile revision changed; requalification is required.");
        if (optionalMismatch is not null)
            return optionalMismatch;

        return new MmsDynamicReportProfileCompatibility
        {
            Status = MmsDynamicReportProfileCompatibilityStatus.Compatible,
            Reason = "Persisted qualification profile identity matches the current IED evidence."
        };
    }

    public static MmsDynamicReportQualificationProfile RecordRcbActivationProof(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity,
        MmsDynamicRcbActivationProof proof)
    {
        RequireCompatible(profile, currentIdentity);
        ArgumentNullException.ThrowIfNull(proof);
        if (profile.State < MmsDynamicReportQualificationState.EnvelopeQualified || profile.AcceptedEnvelope is null)
            throw new InvalidOperationException("RCB activation cannot be proven before an accepted qualified envelope exists.");
        ValidateRcbActivationProof(profile.AcceptedEnvelope, proof);

        return profile with
        {
            State = MmsDynamicReportQualificationState.RcbActivationProven,
            RcbActivationProof = proof with { MemberReferences = proof.MemberReferences.ToArray() },
            InformationReportProof = null,
            ProductionAcceptance = null,
            SourceEvidenceIds = DistinctEvidenceIds(profile.SourceEvidenceIds.Append(proof.EvidenceId).ToArray()),
            UpdatedAtUtc = proof.ObservedAtUtc
        };
    }

    public static MmsDynamicReportQualificationProfile RecordInformationReportProof(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity,
        MmsDynamicInformationReportProof proof)
    {
        RequireCompatible(profile, currentIdentity);
        ArgumentNullException.ThrowIfNull(proof);
        if (profile.State < MmsDynamicReportQualificationState.RcbActivationProven || profile.RcbActivationProof is null)
            throw new InvalidOperationException("InformationReport proof cannot be recorded before RCB activation is proven.");
        ValidateInformationReportProof(profile.RcbActivationProof, proof);

        return profile with
        {
            State = MmsDynamicReportQualificationState.InformationReportProven,
            InformationReportProof = proof with { MemberReferences = proof.MemberReferences.ToArray() },
            ProductionAcceptance = null,
            SourceEvidenceIds = DistinctEvidenceIds(profile.SourceEvidenceIds.Append(proof.EvidenceId).ToArray()),
            UpdatedAtUtc = proof.ObservedAtUtc
        };
    }

    public static MmsDynamicReportQualificationProfile MarkProductionEligible(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity,
        MmsDynamicReportProductionAcceptance acceptance)
    {
        RequireCompatible(profile, currentIdentity);
        ArgumentNullException.ThrowIfNull(acceptance);
        if (profile.State < MmsDynamicReportQualificationState.InformationReportProven || profile.InformationReportProof is null)
            throw new InvalidOperationException("Production eligibility requires actual verified InformationReport evidence first.");
        if (!profile.InformationReportProof.IsSuccess)
            throw new InvalidOperationException("Stored InformationReport proof is not successful.");
        if (!acceptance.AllPassed)
            throw new InvalidOperationException("All G2.6 physical regression gates must pass before ProductionEligible can be set.");
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptance.FieldEvidenceId);

        return profile with
        {
            State = MmsDynamicReportQualificationState.ProductionEligible,
            ProductionAcceptance = acceptance,
            SourceEvidenceIds = DistinctEvidenceIds(profile.SourceEvidenceIds.Append(acceptance.FieldEvidenceId).ToArray()),
            UpdatedAtUtc = acceptance.ObservedAtUtc
        };
    }

    public static bool CanUseForProductionPlanning(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var compatibility = CheckIdentityCompatibility(profile, currentIdentity);
        if (!compatibility.IsCompatible)
        {
            reason = compatibility.Reason;
            return false;
        }

        if (profile.SchemaVersion != MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
        {
            reason = $"Unsupported dynamic qualification profile schema {profile.SchemaVersion}; requalification is required.";
            return false;
        }
        if (profile.State != MmsDynamicReportQualificationState.ProductionEligible)
        {
            reason = $"Dynamic qualification profile is {profile.State}, not ProductionEligible.";
            return false;
        }
        if (profile.AcceptedEnvelope is null || profile.RcbActivationProof is null || profile.InformationReportProof is null || profile.ProductionAcceptance is null)
        {
            reason = "ProductionEligible profile is missing required qualification evidence; fail closed.";
            return false;
        }
        if (!profile.RcbActivationProof.IsSuccess || !profile.InformationReportProof.IsSuccess || !profile.ProductionAcceptance.AllPassed)
        {
            reason = "ProductionEligible profile contains unsuccessful evidence; fail closed.";
            return false;
        }

        reason = "Identity-compatible ProductionEligible dynamic reporting profile is available.";
        return true;
    }

    private static void ValidateRcbActivationProof(
        MmsDynamicDataSetQualifiedEnvelope envelope,
        MmsDynamicRcbActivationProof proof)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proof.EvidenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proof.RcbReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(proof.DataSetReference);
        if (!proof.IsSuccess)
            throw new InvalidOperationException("RCB activation evidence is incomplete or unsuccessful.");
        if (proof.MemberReferences.Count == 0 || proof.MemberReferences.Count > envelope.ProvenMemberCount)
            throw new InvalidOperationException("RCB activation member count is outside the accepted qualification envelope.");
        if (!IsOrderedSubset(proof.MemberReferences, envelope.ExactProvenMemberReferences))
            throw new InvalidOperationException("RCB activation members are not an ordered subset of the exact qualified envelope.");
    }

    private static void ValidateInformationReportProof(
        MmsDynamicRcbActivationProof activation,
        MmsDynamicInformationReportProof proof)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proof.EvidenceId);
        if (!proof.IsSuccess)
            throw new InvalidOperationException("InformationReport evidence is incomplete or unsuccessful.");
        if (!EqualsNormalized(activation.RcbReference, proof.RcbReference))
            throw new InvalidOperationException("InformationReport RCB identity does not match the proven RCB activation.");
        if (!EqualsNormalized(activation.DataSetReference, proof.DataSetReference))
            throw new InvalidOperationException("InformationReport DataSet identity does not match the proven RCB activation.");
        if (!ExactSequenceEquals(activation.MemberReferences, proof.MemberReferences))
            throw new InvalidOperationException("InformationReport exact member mapping does not match the proven RCB activation member set.");
    }

    private static void ValidateIdentity(MmsDynamicReportIedIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.StableIdentityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ModelFingerprint);
    }

    private static void ValidateCapacity(MmsDynamicReportCapacityEvidence? capacity)
    {
        if (capacity is null)
            return;
        if (capacity.ObservedFreeBrcbSlots < 0 || capacity.ObservedFreeUrcbSlots < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Observed free RCB counts cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(capacity.EvidenceId);
    }

    private static MmsDynamicReportIedIdentity NormalizeIdentity(MmsDynamicReportIedIdentity identity)
        => identity with
        {
            StableIdentityKey = identity.StableIdentityKey.Trim(),
            ModelFingerprint = identity.ModelFingerprint.Trim(),
            Manufacturer = identity.Manufacturer.Trim(),
            Model = identity.Model.Trim(),
            FirmwareRevision = identity.FirmwareRevision.Trim(),
            ProfileRevision = identity.ProfileRevision.Trim()
        };

    private static void RequireCompatible(
        MmsDynamicReportQualificationProfile profile,
        MmsDynamicReportIedIdentity currentIdentity)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var compatibility = CheckIdentityCompatibility(profile, currentIdentity);
        if (!compatibility.IsCompatible)
            throw new InvalidOperationException(compatibility.Reason);
    }

    private static MmsDynamicReportProfileCompatibility? CompareOptional(
        string stored,
        string current,
        MmsDynamicReportProfileCompatibilityStatus status,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;
        if (string.IsNullOrWhiteSpace(current) || !EqualsNormalized(stored, current))
            return Incompatible(status, reason);
        return null;
    }

    private static MmsDynamicReportProfileCompatibility Incompatible(
        MmsDynamicReportProfileCompatibilityStatus status,
        string reason)
        => new() { Status = status, Reason = reason };

    private static bool EqualsNormalized(string left, string right)
        => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ExactSequenceEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!EqualsNormalized(left[index], right[index]))
                return false;
        }
        return true;
    }

    private static bool IsOrderedSubset(IReadOnlyList<string> subset, IReadOnlyList<string> full)
    {
        var searchIndex = 0;
        foreach (var candidate in subset)
        {
            var found = false;
            while (searchIndex < full.Count)
            {
                if (EqualsNormalized(candidate, full[searchIndex]))
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

    private static string[] DistinctEvidenceIds(params string?[] ids)
        => ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
