namespace AR.Iec61850.Mms;

public enum MmsConfiguredStaticActivationProofKind
{
    Proven,
    SnapshotReadFailed,
    ReportEnableStateMismatch,
    DataSetBindingMismatch,
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
/// Planning evidence can go stale between plan creation and mutation. This verifier therefore
/// treats whole-RCB readback as the just-in-time authority immediately before and after
/// activation. Missing DataSet text is not converted into a mismatch, but an explicit live
/// binding contradiction blocks. RptEna must be exact because activation ownership cannot be
/// inferred safely from a missing/ambiguous enable state.
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
                "Pre-activation whole-RCB readback failed; no activation write is permitted.");

        if (HasPositiveDataSetMismatch(snapshot.DataSetReference, expectedDataSetReference))
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

        return Pass(snapshot, expectedDataSetReference,
            "Pre-activation whole-RCB evidence proves RptEna=false and no conflicting live DataSet binding.");
    }

    public static MmsConfiguredStaticActivationProof VerifyAfterEnable(
        IReadOnlyList<MmsReportRcbSnapshot> snapshots,
        string expectedDataSetReference)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count < 2)
        {
            return new MmsConfiguredStaticActivationProof
            {
                Kind = MmsConfiguredStaticActivationProofKind.InsufficientReadbackEvidence,
                Stage = "after-enable",
                ExpectedDataSetReference = Normalize(expectedDataSetReference),
                Message = "Configured static activation requires two whole-RCB verification reads after RptEna=true."
            };
        }

        foreach (var snapshot in snapshots)
        {
            if (!snapshot.IsSuccess)
                return Fail(
                    MmsConfiguredStaticActivationProofKind.SnapshotReadFailed,
                    snapshot,
                    expectedDataSetReference,
                    "Post-enable whole-RCB readback failed; write acceptance alone is not activation proof.");

            if (HasPositiveDataSetMismatch(snapshot.DataSetReference, expectedDataSetReference))
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
                    "RptEna=true was not proven by post-enable whole-RCB readback.");
        }

        return Pass(
            snapshots[^1],
            expectedDataSetReference,
            "Two post-enable whole-RCB reads prove RptEna=true with no conflicting live DataSet binding.");
    }

    private static bool HasPositiveDataSetMismatch(string observed, string expected)
    {
        var observedNormalized = Normalize(observed);
        var expectedNormalized = Normalize(expected);
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
