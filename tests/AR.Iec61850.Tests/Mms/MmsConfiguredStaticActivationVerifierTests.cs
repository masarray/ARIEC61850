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
    public void BeforeWrite_Blocks_Positive_DataSet_Contradiction_But_Not_Missing_Text()
    {
        var mismatch = Snapshot(success: true, enabled: "false", dataSet: "LD0/LLN0.Other", stage: "before");
        var missing = Snapshot(success: true, enabled: "false", dataSet: string.Empty, stage: "before");

        Assert.Equal(
            MmsConfiguredStaticActivationProofKind.DataSetBindingMismatch,
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(mismatch, "LD0/LLN0.DS01").Kind);
        Assert.True(
            MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(missing, "LD0/LLN0.DS01").IsProven);
    }

    [Fact]
    public void BeforeWrite_Fails_Closed_When_Whole_Rcb_Read_Fails()
    {
        var snapshot = Snapshot(success: false, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "before");

        var proof = MmsConfiguredStaticActivationVerifier.VerifyBeforeWrite(snapshot, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.SnapshotReadFailed, proof.Kind);
        Assert.False(proof.IsProven);
    }

    [Fact]
    public void AfterEnable_Requires_Two_Independent_Readbacks()
    {
        var one = new[]
        {
            Snapshot(success: true, enabled: "true", dataSet: "LD0/LLN0.DS01", stage: "after-1")
        };

        var proof = MmsConfiguredStaticActivationVerifier.VerifyAfterEnable(one, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.InsufficientReadbackEvidence, proof.Kind);
        Assert.False(proof.IsProven);
    }

    [Fact]
    public void AfterEnable_Rejects_Write_Accepted_But_Readback_False()
    {
        var snapshots = new[]
        {
            Snapshot(success: true, enabled: "true", dataSet: "LD0/LLN0.DS01", stage: "after-1"),
            Snapshot(success: true, enabled: "false", dataSet: "LD0/LLN0.DS01", stage: "after-2")
        };

        var proof = MmsConfiguredStaticActivationVerifier.VerifyAfterEnable(snapshots, "LD0/LLN0.DS01");

        Assert.Equal(MmsConfiguredStaticActivationProofKind.ReportEnableStateMismatch, proof.Kind);
        Assert.False(proof.IsProven);
    }

    [Fact]
    public void AfterEnable_Proves_Activation_Only_When_Both_Readbacks_Agree()
    {
        var snapshots = new[]
        {
            Snapshot(success: true, enabled: "true", dataSet: "LD0/LLN0.DS01", stage: "after-1"),
            Snapshot(success: true, enabled: "true", dataSet: "LD0/LLN0.DS01", stage: "after-2")
        };

        var proof = MmsConfiguredStaticActivationVerifier.VerifyAfterEnable(snapshots, "LD0/LLN0.DS01");

        Assert.True(proof.IsProven);
        Assert.Equal(MmsConfiguredStaticActivationProofKind.Proven, proof.Kind);
    }

    private static MmsReportRcbSnapshot Snapshot(
        bool success,
        string enabled,
        string dataSet,
        string stage)
        => new()
        {
            Stage = stage,
            IsSuccess = success,
            Reference = "LD0/LLN0.BR.B01",
            EnabledState = enabled,
            DataSetReference = dataSet,
            Message = success ? "snapshot ok" : "snapshot failed"
        };
}
