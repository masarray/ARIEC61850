namespace AR.Iec61850.Mms;

/// <summary>
/// Production-facing bridge from a successful G2.6 shadow result to the persisted
/// production-acceptance contract. Unlike the generic shadow bridge, this policy
/// refuses to mark QualityRegressionPassed unless actual paired quality AND device
/// timestamp evidence were observed on report and independent-poll sides.
/// </summary>
public static class MmsDynamicReportShadowProductionAcceptancePolicy
{
    public static MmsDynamicReportProductionAcceptance BuildStrict(
        MmsDynamicReportShadowVerificationEvidence evidence,
        MmsDynamicReportShadowVerificationResult shadow,
        bool controlRegressionPassed,
        bool staticReportingRegressionPassed)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(shadow);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.EvidenceId);
        if (!shadow.IsSuccess)
            throw new InvalidOperationException("A failed G2.6 shadow cannot be converted into strict production acceptance evidence.");

        var baseAcceptance = MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(
            evidence,
            shadow,
            controlRegressionPassed,
            staticReportingRegressionPassed);

        var qualityEvidenceObserved = HasPairedQualityEvidence(evidence);
        var timestampEvidenceObserved = HasPairedTimestampEvidence(evidence);

        return new MmsDynamicReportProductionAcceptance
        {
            FieldEvidenceId = baseAcceptance.FieldEvidenceId,
            ObservedAtUtc = baseAcceptance.ObservedAtUtc,
            ControlRegressionPassed = baseAcceptance.ControlRegressionPassed,
            StaticReportingRegressionPassed = baseAcceptance.StaticReportingRegressionPassed,
            DynamicInformationReportRegressionPassed = baseAcceptance.DynamicInformationReportRegressionPassed,
            PollingAuthorityGuardPassed = baseAcceptance.PollingAuthorityGuardPassed,
            ReconnectRegressionPassed = baseAcceptance.ReconnectRegressionPassed,
            QualityRegressionPassed = baseAcceptance.QualityRegressionPassed &&
                                      qualityEvidenceObserved &&
                                      timestampEvidenceObserved,
            NoRepeatedMutationLoopPassed = baseAcceptance.NoRepeatedMutationLoopPassed
        };
    }

    public static bool HasPairedQualityEvidence(MmsDynamicReportShadowVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.ReportObservations.Any(report =>
        {
            var reportQuality = Normalize(report.Quality);
            if (reportQuality.Length == 0)
                return false;

            return evidence.PollObservations.Any(poll =>
                poll.DataSetIndex == report.DataSetIndex &&
                SameReference(poll.MemberReference, report.MemberReference) &&
                Normalize(poll.Quality).Length > 0);
        });
    }

    public static bool HasPairedTimestampEvidence(MmsDynamicReportShadowVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.ReportObservations.Any(report =>
            report.DeviceTimestampUtc.HasValue &&
            evidence.PollObservations.Any(poll =>
                poll.DataSetIndex == report.DataSetIndex &&
                SameReference(poll.MemberReference, report.MemberReference) &&
                poll.DeviceTimestampUtc.HasValue));
    }

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => Normalize(reference).Replace('$', '.');

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
