namespace AR.Iec61850.Mms;

public enum MmsConfiguredStaticActivationProofKind
{
    Proven,
    SnapshotReadFailed,
    ReportEnableStateMismatch,
    DataSetBindingMismatch,
    BusyRuntimeEvidence,
    InsufficientReadbackEvidence
}

public sealed class MmsConfiguredStaticActivationProof
{
    public MmsConfiguredStaticActivationProofKind Kind { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string ExpectedDataSetReference { get; init; } = string.Empty;
    public string ObservedDataSetReference { get; init; } = string.Empty;
    public string ObservedEnabledState { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsProven => Kind == MmsConfiguredStaticActivationProofKind.Proven;
}

/// <summary>
/// Pure proof policy for configured-static RCB activation.
///
/// Planning evidence can go stale between plan creation and mutation. This verifier treats
/// a minimal just-in-time RCB snapshot as the runtime authority. One exact post-write
/// readback is sufficient when it proves RptEna=true and no positive DataSet contradiction;
/// a second unconditional read would add latency without adding semantic evidence.
///
/// Missing reservation/Owner metadata is not converted into a blocker, but positive busy
/// evidence always blocks. Missing DataSet text is not a mismatch unless the live DatSet
/// read positively succeeded and proved an empty binding.
/// </summary>
public static class MmsConfiguredStaticActivationVerifier
{
    public static MmsConfiguredStaticActivationProof VerifyBeforeWrite(
        MmsReportRcbSnapshot snapshot,
        string expectedDataSetReference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsSuccess)
            return Fail(
                MmsConfiguredStaticActivationProofKind.SnapshotReadFailed,
                snapshot,
                expectedDataSetReference,
                "Pre-activation JIT RCB read failed; no activation write is permitted.");

        if (HasPositiveDataSetMismatch(snapshot, expectedDataSetReference))
            return Fail(
                MmsConfiguredStaticActivationProofKind.DataSetBindingMismatch,
                snapshot,
                expectedDataSetReference,
                "Live RCB DatSet contradicts the configured static plan; activation is blocked rather than rewriting DatSet.");

        if (MmsRcbAvailabilityEvaluator.ParseBool(snapshot.EnabledState) != false)
            return Fail(
                MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch,
                snapshot,
                expectedDataSetReference,
                "Configured static activation requires exact RptEna=false immediately before mutation.");

        if (HasPositiveBusyEvidence(snapshot))
            return Fail(
                MmsConfiguredStaticActivationProofKind.BusyRuntimeEvidence,
                snapshot,
                expectedDataSetReference,
                "Fresh reservation/Owner evidence indicates the RCB is occupied by another runtime context.");

        return Pass(
            snapshot,
            expectedDataSetReference,
            "Pre-activation JIT evidence proves RptEna=false, no conflicting DataSet binding, and no positive busy evidence.");
    }

    public static MmsConfiguredStaticActivationProof VerifyAfterEnable(
        MmsReportRcbSnapshot snapshot,
        string expectedDataSetReference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsSuccess)
            return Fail(
                MmsConfiguredStaticActivationProofKind.SnapshotReadFailed,
                snapshot,
                expectedDataSetReference,
                "Post-enable JIT readback failed; write acceptance alone is not activation proof.");

        if (HasPositiveDataSetMismatch(snapshot, expectedDataSetReference))
            return Fail(
                MmsConfiguredStaticActivationProofKind.DataSetBindingMismatch,
                snapshot,
                expectedDataSetReference,
                "Post-enable RCB DatSet contradicts the configured static plan.");

        if (MmsRcbAvailabilityEvaluator.ParseBool(snapshot.EnabledState) != true)
            return Fail(
                MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch,
                snapshot,
                expectedDataSetReference,
                "RptEna=true was not proven by post-enable JIT readback.");

        return Pass(
            snapshot,
            expectedDataSetReference,
            "Post-enable JIT readback proves RptEna=true with no conflicting live DataSet binding.");
    }

    private static bool HasPositiveBusyEvidence(MmsReportRcbSnapshot snapshot)
    {
        if (MmsRcbAvailabilityEvaluator.HasOwner(snapshot.Owner))
            return true;

        return snapshot.Buffered
            ? MmsRcbAvailabilityEvaluator.ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0
            : MmsRcbAvailabilityEvaluator.ParseBool(snapshot.ReservationState) == true;
    }

    private static bool HasPositiveDataSetMismatch(
        MmsReportRcbSnapshot snapshot,
        string expected)
    {
        var observedNormalized = Normalize(snapshot.DataSetReference);
        var expectedNormalized = Normalize(expected);

        if (snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
            observedNormalized.Length == 0 &&
            expectedNormalized.Length > 0)
        {
            return true;
        }

        return observedNormalized.Length > 0 &&
               expectedNormalized.Length > 0 &&
               !string.Equals(observedNormalized, expectedNormalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(value);

    private static MmsConfiguredStaticActivationProof Pass(
        MmsReportRcbSnapshot snapshot,
        string expectedDataSetReference,
        string message)
        => new()
        {
            Kind = MmsConfiguredStaticActivationProofKind.Proven,
            Stage = snapshot.Stage,
            ExpectedDataSetReference = Normalize(expectedDataSetReference),
            ObservedDataSetReference = Normalize(snapshot.DataSetReference),
            ObservedEnabledState = snapshot.EnabledState,
            Message = message
        };

    private static MmsConfiguredStaticActivationProof Fail(
        MmsConfiguredStaticActivationProofKind kind,
        MmsReportRcbSnapshot snapshot,
        string expectedDataSetReference,
        string message)
        => new()
        {
            Kind = kind,
            Stage = snapshot.Stage,
            ExpectedDataSetReference = Normalize(expectedDataSetReference),
            ObservedDataSetReference = Normalize(snapshot.DataSetReference),
            ObservedEnabledState = snapshot.EnabledState,
            Message = $"{message} {snapshot.Message}".Trim()
        };
}
