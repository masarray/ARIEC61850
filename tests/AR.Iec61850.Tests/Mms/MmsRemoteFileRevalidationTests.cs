using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRemoteFileRevalidationTests
{
    [Fact]
    public void Classify_ExactPathStillPresent_IsConclusivePresent()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [SuccessPage(
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00163.cfg",
                    Path = "COMTRADE/FRA00163.cfg",
                    SizeBytes = 1234
                })]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.PresentExactPath, evidence.Status);
        Assert.True(evidence.IsConclusive);
        Assert.Equal("COMTRADE", evidence.ParentDirectory);
        Assert.Equal("FRA00163.cfg", evidence.ExpectedFileName);
        Assert.Equal("COMTRADE/FRA00163.cfg", evidence.MatchedPath);
        Assert.Contains("still present", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_CaseVariantPresent_DoesNotReportDisappeared()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [SuccessPage(
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00163.CFG",
                    Path = "COMTRADE/FRA00163.CFG",
                    SizeBytes = 1234
                })]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.PresentCaseVariant, evidence.Status);
        Assert.True(evidence.IsConclusive);
        Assert.Equal("COMTRADE/FRA00163.CFG", evidence.MatchedPath);
        Assert.Contains("case", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_CompleteRelistWithoutTarget_ReportsEntryDisappeared()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [SuccessPage(
                new MmsFileDirectoryEntry
                {
                    Name = "FRA00164.cfg",
                    Path = "COMTRADE/FRA00164.cfg",
                    SizeBytes = 2000
                })]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.EntryDisappeared, evidence.Status);
        Assert.True(evidence.IsConclusive);
        Assert.Empty(evidence.MatchedPath);
        Assert.Contains("stale", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_FailedDirectoryPage_IsInconclusive()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [new MmsFileDirectoryResult
            {
                IsSuccess = false,
                DirectoryName = "COMTRADE",
                Message = "MMS Confirmed-Error PDU during FileDirectory"
            }]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.DirectoryReadFailed, evidence.Status);
        Assert.False(evidence.IsConclusive);
        Assert.Contains("inconclusive", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_IncompletePagingWithoutTarget_IsInconclusive()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [new MmsFileDirectoryResult
            {
                IsSuccess = true,
                DirectoryName = "COMTRADE",
                MoreFollows = true,
                Entries =
                [
                    new MmsFileDirectoryEntry
                    {
                        Name = "FRA00164.cfg",
                        Path = "COMTRADE/FRA00164.cfg"
                    }
                ]
            }]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.DirectoryReadFailed, evidence.Status);
        Assert.False(evidence.IsConclusive);
        Assert.Contains("moreFollows", evidence.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_ExactPathWinsEvenWhenLaterPageFails()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.Classify(
            "COMTRADE/FRA00163.cfg",
            [
                SuccessPage(
                    new MmsFileDirectoryEntry
                    {
                        Name = "FRA00163.cfg",
                        Path = "COMTRADE/FRA00163.cfg"
                    },
                    moreFollows: true),
                new MmsFileDirectoryResult
                {
                    IsSuccess = false,
                    DirectoryName = "COMTRADE",
                    Message = "second page failed"
                }
            ]);

        Assert.Equal(MmsRemoteFileRevalidationStatus.PresentExactPath, evidence.Status);
        Assert.True(evidence.IsConclusive);
    }

    [Fact]
    public void Policy_RevalidatesOnlyFileOpenFileNonExistentBeforeTransfer()
    {
        var eligible = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 0,
            ReadOperations = 0,
            Message = "MMS Confirmed-Error PDU during FileOpen: A2 0A 80 01 03 A2 05 A0 03 8B 01 07"
        };
        var unreadable = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 0,
            ReadOperations = 0,
            Message = "MMS Confirmed-Error PDU during FileOpen: other failure"
        };
        var partialTransfer = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 128,
            ReadOperations = 1,
            Message = "MMS Confirmed-Error PDU during FileOpen: A2 0A 80 01 03 A2 05 A0 03 8B 01 07"
        };

        Assert.True(MmsRemoteFileRevalidationPolicy.ShouldRevalidate(eligible));
        Assert.False(MmsRemoteFileRevalidationPolicy.ShouldRevalidate(unreadable));
        Assert.False(MmsRemoteFileRevalidationPolicy.ShouldRevalidate(partialTransfer));
    }

    [Fact]
    public void AssociationUnavailable_PreservesExpectedIdentity()
    {
        var evidence = MmsRemoteFileRevalidationClassifier.AssociationUnavailable(
            @"COMTRADE\FRA00163.CFG");

        Assert.Equal(MmsRemoteFileRevalidationStatus.AssociationUnavailable, evidence.Status);
        Assert.Equal("COMTRADE/FRA00163.CFG", evidence.RemotePath);
        Assert.Equal("COMTRADE", evidence.ParentDirectory);
        Assert.Equal("FRA00163.CFG", evidence.ExpectedFileName);
        Assert.False(evidence.IsConclusive);
    }

    private static MmsFileDirectoryResult SuccessPage(
        MmsFileDirectoryEntry entry,
        bool moreFollows = false)
        => new()
        {
            IsSuccess = true,
            DirectoryName = "COMTRADE",
            Entries = [entry],
            MoreFollows = moreFollows,
            Message = "ok"
        };
}
