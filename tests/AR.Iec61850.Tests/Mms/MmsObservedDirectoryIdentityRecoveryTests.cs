using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsObservedDirectoryIdentityRecoveryTests
{
    [Fact]
    public void Decide_RelativeObservedFileName_ReplaysServerReturnedIdentity()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [SuccessPage(
                moreFollows: false,
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00055.cfg",
                    Path = "COMTRADE/FRA00055.cfg",
                    SizeBytes = 2048
                })]);

        Assert.True(decision.ShouldRetry);
        Assert.Equal("FRA00055.cfg", decision.CandidateFileName);
        Assert.Equal("COMTRADE/FRA00055.cfg", decision.ObservedCatalogPath);
        Assert.Contains("server-returned", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_FullObservedFileNameAlreadyTried_DoesNotRepeatIt()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [SuccessPage(
                moreFollows: false,
                new MmsFileDirectoryEntry
                {
                    Name = "COMTRADE/FRA00055.cfg",
                    Path = "COMTRADE/FRA00055.cfg"
                })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(
            MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName,
            decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_ExactPositiveEntry_RemainsUsableEvenIfLaterPageFails()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [
                SuccessPage(
                    moreFollows: true,
                    new MmsFileDirectoryEntry
                    {
                        Name = "FRA00055.cfg",
                        Path = "COMTRADE/FRA00055.cfg"
                    }),
                new MmsFileDirectoryResult
                {
                    IsSuccess = false,
                    DirectoryName = "COMTRADE",
                    Message = "later page failed"
                }
            ]);

        Assert.True(decision.ShouldRetry);
        Assert.Equal("FRA00055.cfg", decision.CandidateFileName);
    }

    [Fact]
    public void Decide_CompletedListingWithoutEntry_DoesNotInventIdentity()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [SuccessPage(
                moreFollows: false,
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00054.cfg",
                    Path = "COMTRADE/FRA00054.cfg"
                })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(
            MmsObservedDirectoryIdentityRecoveryStatus.EntryNoLongerPresent,
            decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_IncompleteListing_RemainsInconclusive()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [SuccessPage(
                moreFollows: true,
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00054.cfg",
                    Path = "COMTRADE/FRA00054.cfg"
                })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(
            MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
            decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_PreservesObservedFileNameCase()
    {
        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00055.cfg",
            [SuccessPage(
                moreFollows: false,
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00055.CfG",
                    Path = "COMTRADE/FRA00055.cfg"
                })]);

        Assert.True(decision.ShouldRetry);
        Assert.Equal("FRA00055.CfG", decision.CandidateFileName);
    }

    [Fact]
    public void RecoveryStatus_OrdinalsAreStableAndAppendOnly()
    {
        Assert.Equal(0, (int)MmsObservedDirectoryIdentityRecoveryStatus.NotChecked);
        Assert.Equal(1, (int)MmsObservedDirectoryIdentityRecoveryStatus.RecoveredByObservedFileName);
        Assert.Equal(2, (int)MmsObservedDirectoryIdentityRecoveryStatus.ObservedFileNameRetryFailed);
        Assert.Equal(3, (int)MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName);
        Assert.Equal(4, (int)MmsObservedDirectoryIdentityRecoveryStatus.EntryNoLongerPresent);
        Assert.Equal(5, (int)MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed);
        Assert.Equal(6, (int)MmsObservedDirectoryIdentityRecoveryStatus.AssociationUnavailable);
        Assert.Equal(7, (int)MmsObservedDirectoryIdentityRecoveryStatus.RetryUnavailable);
    }

    private static MmsFileDirectoryResult SuccessPage(
        bool moreFollows,
        params MmsFileDirectoryEntry[] entries)
        => new()
        {
            IsSuccess = true,
            DirectoryName = "COMTRADE",
            Entries = entries,
            MoreFollows = moreFollows,
            Message = "ok"
        };
}
