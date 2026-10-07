using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsConfiguredStaticActivationVerifierTests
{
    [Fact]
    public void BeforeWrite_Requires_Exact_Disabled_Readback()
    {
        var unknown = Snapshot(success: true, enabled: "-", dataSet: "LD0/LLN0.DS01", stage: "before");
        var enabled = Snapshot(success: true, enabled: "true", dataSet: "LD0/LLN0.DS01", stage: "before");
        var disabled = Snapshot(success: true, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "before");

        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(unknown, "LD0/LLN0.DS01").Kind);
        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(enabled, "LD0/LLN0.DS01").Kind);
        Assert.True(
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(disabled, "LD0/LLN0.DS01").IsProven);
    }

    [Fact]
    public void BeforeWrite_Blocks_Positive_DataSet_Contradiction_But_Not_Unread_DataSet()
    {
        var mismatch = Snapshot(
            success: true,
            enabled: "false",
            dataSet: "LD0/LLN0.Other",
            stage: "before",
            dataSetProbeState: MmsRcbDataSetProbeState.ReadSucceeded);
        var unread = Snapshot(
            success: true,
            enabled: "false",
            dataSet: string.Empty,
            stage: "before",
            dataSetProbeState: MmsRcbDataSetProbeState.ReadFailed);

        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.DataSetBindingMismatch,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(mismatch, "LD0/LLN0.DS01").Kind);
        Assert.True(
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(unread, "LD0/LLN0.DS01").IsProven);
    }

    [Fact]
    public void BeforeWrite_Blocks_Positive_Empty_Live_DataSet()
    {
        var empty = Snapshot(
            success: true,
            enabled: "false",
            dataSet: string.Empty,
            stage: "before",
            dataSetProbeState: MmsRcbDataSetProbeState.ReadSucceeded);

        var proof = MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(empty, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.DataSetBindingMismatch, proof.Kind);
    }

    [Fact]
    public void BeforeWrite_Blocks_Positive_Busy_Evidence()
    {
        var brcb = Snapshot(success: true, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "before", buffered: true);
        brcb = brcb with { ReservationTimeSeconds = "30" };

        var urcb = Snapshot(success: true, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "before", buffered: false);
        urcb = urcb with { ReservationState = "true" };

        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.BusyRuntimeEvidence,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(brcb, "LD0/LLN0.DS01").Kind);
        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.BusyRuntimeEvidence,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(urcb, "LD0/LLN0.DS01").Kind);
    }

    [Fact]
    public void BeforeWrite_Fails_Closed_When_Jit_Read_Fails()
    {
        var snapshot = Snapshot(success: false, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "before");

        var proof = MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(snapshot, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.SnapshotReadFailed, proof.Kind);
        Assert.False(proof.IsProven);
    }

    [Fact]
    public void AfterEnable_One_Exact_Readback_Is_Sufficient()
    {
        var snapshot = Snapshot(
            success: true,
            enabled: "true",
            dataSet: "LD0/LLN0.DS01",
            stage: "after-enable");

        var proof = MmsConfiguredStaticActivationVerifier.VerifyAfterEnable(snapshot, "LD0/LLN0.DS01");

        Assert.True(proof.IsProven);
        Assert.Equal(MmsConfiguredStaticActivationProofKind.Proven, proof.Kind);
    }

    [Fact]
    public void AfterEnable_Rejects_Write_Accepted_But_Readback_False()
    {
        var snapshot = Snapshot(
            success: true,
            enabled: "false",
            dataSet: "LD0/LLN0.DS01",
            stage: "after-enable");

        var proof = MmsConfiguredStaticActivationVerifier.VerifyAfterEnable(snapshot, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch, proof.Kind);
        Assert.False(proof.IsProven);
    }

    private static MmsReportRcbSnapshot Snapshot(
        bool success,
        string enabled,
        string dataSet,
        string stage,
        bool buffered = true,
        MmsRcbDataSetProbeState dataSetProbeState = MmsRcbDataSetProbeState.NotAttempted)
        => new()
        {
            Stage = stage,
            IsSuccess = success,
            Reference = buffered ? "LD0/LLN0.BR.B01" : "LD0/LLN0.RP.U01",
            Buffered = buffered,
            EnabledState = enabled,
            DataSetReference = dataSet,
            DataSetProbeState = dataSetProbeState,
            Message = success ? "snapshot ok" : "snapshot failed"
        };
}
