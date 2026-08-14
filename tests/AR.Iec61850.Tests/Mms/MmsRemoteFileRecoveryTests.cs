using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRemoteFileRecoveryTests
{
    [Fact]
    public void Decide_CaseVariant_UsesObservedPathForOneRetry()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.PresentCaseVariant,
            RemotePath = "COMTRADE/FRA00163.cfg",
            MatchedPath = "COMTRADE/FRA00163.CFG"
        });

        Assert.True(decision.ShouldRetry);
        Assert.Equal("COMTRADE/FRA00163.CFG", decision.CandidatePath);
        Assert.Contains("case-sensitive", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_ExactPathPresent_DoesNotRepeatKnownFailedPath()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.PresentExactPath,
            RemotePath = "COMTRADE/FRA00163.cfg",
            MatchedPath = "COMTRADE/FRA00163.cfg"
        });

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRemoteFileRecoveryStatus.ExactPathStillUnopenable, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_DisappearedEntry_DoesNotRetryStalePath()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.EntryDisappeared,
            RemotePath = "COMTRADE/FRA00163.cfg"
        });

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRemoteFileRecoveryStatus.EntryDisappeared, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_DirectoryReadFailure_DoesNotGuessReplacementPath()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.DirectoryReadFailed,
            RemotePath = "COMTRADE/FRA00163.cfg"
        });

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRemoteFileRecoveryStatus.RevalidationInconclusive, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_AssociationUnavailable_DoesNotRetry()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.AssociationUnavailable,
            RemotePath = "COMTRADE/FRA00163.cfg"
        });

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRemoteFileRecoveryStatus.AssociationUnavailable, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_CaseVariantWithoutConcreteMatchedPath_RemainsInconclusive()
    {
        var decision = MmsRemoteFileRecoveryPolicy.Decide(new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.PresentCaseVariant,
            RemotePath = "COMTRADE/FRA00163.cfg",
            MatchedPath = string.Empty
        });

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRemoteFileRecoveryStatus.RevalidationInconclusive, decision.NoRetryStatus);
    }

    [Fact]
    public void RecoveryStatus_OrdinalsAreStableAndAppendOnly()
    {
        Assert.Equal(0, (int)MmsRemoteFileRecoveryStatus.NotChecked);
        Assert.Equal(1, (int)MmsRemoteFileRecoveryStatus.RecoveredByObservedCasePath);
        Assert.Equal(2, (int)MmsRemoteFileRecoveryStatus.ObservedCasePathRetryFailed);
        Assert.Equal(3, (int)MmsRemoteFileRecoveryStatus.ExactPathStillUnopenable);
        Assert.Equal(4, (int)MmsRemoteFileRecoveryStatus.EntryDisappeared);
        Assert.Equal(5, (int)MmsRemoteFileRecoveryStatus.RevalidationInconclusive);
        Assert.Equal(6, (int)MmsRemoteFileRecoveryStatus.AssociationUnavailable);
        Assert.Equal(7, (int)MmsRemoteFileRecoveryStatus.RetryUnavailable);
    }
}
