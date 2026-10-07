namespace AR.Iec61850.Mms;

public enum MmsConfiguredStaticRcbEligibilityKind
{
    Blocked,
    CallerOwned,
    ExactFree,
    ReducedMissingReservationEvidence
}

public sealed class MmsConfiguredStaticRcbEligibility
{
    public MmsConfiguredStaticRcbEligibilityKind Kind { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsEligible => Kind != MmsConfiguredStaticRcbEligibilityKind.Blocked;
    public bool RequiresWrite => Kind is
        MmsConfiguredStaticRcbEligibilityKind.ExactFree or
        MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence;
    public bool UsesReducedEvidence =>
        Kind == MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence;
}

/// <summary>
/// Evaluates only configured static RCB activation eligibility.
///
/// Exact-free evidence remains preferred. A separately gated reduced state may be emitted
/// only when the configured DataSet binding is freshly read and populated, RptEna is
/// explicitly false, no positive foreign ownership/reservation evidence exists, and the
/// reservation field is genuinely absent/unexposed. Malformed or contradictory values are
/// blockers, never "missing evidence".
///
/// This policy never relaxes dynamic DataSet mutation eligibility.
/// </summary>
public static class MmsConfiguredStaticRcbEligibilityPolicy
{
    public static MmsConfiguredStaticRcbEligibility Evaluate(
        MmsRcbAvailabilitySnapshot snapshot,
        bool allowCallerOwned,
        bool allowReducedMissingReservationEvidence)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller)
        {
            var callerReady = allowCallerOwned &&
                              HasVerifiedStaticDataSet(snapshot);
            return callerReady
                ? Eligible(
                    MmsConfiguredStaticRcbEligibilityKind.CallerOwned,
                    "The configured static RCB is already active in this caller association and its populated DataSet is verified.")
                : Blocked("Caller-owned RCB reuse is disabled or its configured DataSet is not fully verified.");
        }

        if (!HasVerifiedStaticDataSet(snapshot))
            return Blocked("Configured static RCB requires a successful live DatSet read and a populated DataSet directory.");

        if (MmsRcbAvailabilityEvaluator.ParseBool(snapshot.EnabledState) != false)
            return Blocked("Configured static RCB requires explicit RptEna=false before a new activation write.");

        if (HasPositiveBusyEvidence(snapshot))
            return Blocked("Positive RCB ownership/reservation evidence indicates the configured static RCB is not free.");

        if (snapshot.Availability == MmsRcbOperationalAvailability.Available &&
            snapshot.Confidence == MmsRcbAvailabilityConfidence.Exact &&
            HasExplicitFreeReservation(snapshot))
        {
            return Eligible(
                MmsConfiguredStaticRcbEligibilityKind.ExactFree,
                "Fresh exact evidence confirms a populated configured DataSet, RptEna=false, and explicit free reservation state.");
        }

        if (!allowReducedMissingReservationEvidence)
            return Blocked("Exact free reservation evidence is unavailable and reduced configured-static activation is not enabled.");

        if (snapshot.Availability != MmsRcbOperationalAvailability.Unknown)
            return Blocked($"Reduced configured-static activation accepts only missing reservation evidence; availability={snapshot.Availability} is not an unknown-reservation state.");

        if (!ReservationEvidenceIsGenuinelyMissing(snapshot))
            return Blocked("Reservation evidence is present but is neither explicitly free nor safely classifiable as missing.");

        return Eligible(
            MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence,
            "Configured static DataSet and RptEna=false are verified and no positive busy evidence exists, but the IED does not expose usable reservation metadata. Activation may proceed only under the explicit reduced-evidence policy and must still prove success by readback/report traffic.");
    }

    private static bool HasVerifiedStaticDataSet(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
           snapshot.DataSetDirectorySuccess &&
           snapshot.DataSetMembers.Count > 0 &&
           !string.IsNullOrWhiteSpace(snapshot.DataSetReference);

    private static bool HasPositiveBusyEvidence(MmsRcbAvailabilitySnapshot snapshot)
    {
        if (MmsRcbAvailabilityEvaluator.ParseBool(snapshot.EnabledState) == true)
            return true;
        if (MmsRcbAvailabilityEvaluator.HasOwner(snapshot.Owner))
            return true;

        return snapshot.Buffered
            ? MmsRcbAvailabilityEvaluator.ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0
            : MmsRcbAvailabilityEvaluator.ParseBool(snapshot.ReservationState) == true;
    }

    private static bool HasExplicitFreeReservation(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.Buffered
            ? MmsRcbAvailabilityEvaluator.ParseUnsigned(snapshot.ReservationTimeSeconds) == 0
            : MmsRcbAvailabilityEvaluator.ParseBool(snapshot.ReservationState) == false;

    private static bool ReservationEvidenceIsGenuinelyMissing(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.Buffered
            ? IsMissingToken(snapshot.ReservationTimeSeconds)
            : IsMissingToken(snapshot.ReservationState);

    private static bool IsMissingToken(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ||
               text == "-" ||
               text == "[]" ||
               text.Equals("null", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("unsupported", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("not-exposed", StringComparison.OrdinalIgnoreCase);
    }

    private static MmsConfiguredStaticRcbEligibility Eligible(
        MmsConfiguredStaticRcbEligibilityKind kind,
        string reason)
        => new() { Kind = kind, Reason = reason };

    private static MmsConfiguredStaticRcbEligibility Blocked(string reason)
        => new() { Kind = MmsConfiguredStaticRcbEligibilityKind.Blocked, Reason = reason };
}
