namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    public MmsRawDirectoryIdentityRecoveryEvidence? LastRawDirectoryIdentityRecovery { get; private set; }

    /// <summary>
    /// P4 recovery. Runs P0-P3 first, then re-lists the parent directory and replays
    /// one exact raw GraphicString only when FileDirectory supplied a distinct wire
    /// identity that has not already been attempted.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileRawDirectoryIdentityRecoveredAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastRawDirectoryIdentityRecovery = null;
        var requestedPath = MmsFileNameEncoding.Normalize(remotePath);

        var initial = await DownloadFileObservedDirectoryIdentityRecoveredAsync(
            requestedPath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (initial.IsSuccess)
            return initial;

        var p1 = LastRemoteFileRevalidation;
        var p2 = LastRemoteFileRecovery;
        var p3 = LastObservedDirectoryIdentityRecovery;
        if (p1?.Status != MmsRemoteFileRevalidationStatus.PresentExactPath ||
            p2?.Status != MmsRemoteFileRecoveryStatus.ExactPathStillUnopenable ||
            p3?.Status != MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName)
        {
            return initial;
        }

        if (!IsMmsInitiated)
        {
            return FinishWithoutRetry(
                initial,
                requestedPath,
                MmsRawDirectoryIdentityRecoveryStatus.AssociationUnavailable,
                p1.MatchedPath,
                Array.Empty<string>(),
                0,
                "The MMS association is no longer initiated before raw FileDirectory identity recovery.");
        }

        var slash = requestedPath.LastIndexOf('/');
        var parentDirectory = slash <= 0 ? string.Empty : requestedPath[..slash];
        IReadOnlyList<MmsFileDirectoryResult> pages;
        try
        {
            pages = await GetFileDirectoryPagedAsync(
                string.IsNullOrWhiteSpace(parentDirectory) ? null : parentDirectory,
                maxPages: 8,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            return FinishWithoutRetry(
                initial,
                requestedPath,
                IsMmsInitiated
                    ? MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed
                    : MmsRawDirectoryIdentityRecoveryStatus.AssociationUnavailable,
                p1.MatchedPath,
                Array.Empty<string>(),
                0,
                $"Raw FileDirectory identity recovery could not re-list '{(string.IsNullOrWhiteSpace(parentDirectory) ? "/" : parentDirectory)}': {ex.GetType().Name}: {ex.Message}");
        }

        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(requestedPath, pages);
        if (!decision.ShouldRetry)
        {
            return FinishWithoutRetry(
                initial,
                requestedPath,
                decision.NoRetryStatus,
                decision.ObservedCatalogPath,
                decision.RawNameComponents,
                pages.Count,
                decision.Reason);
        }

        if (!destination.CanSeek || !destination.CanWrite)
        {
            return FinishWithoutRetry(
                initial,
                requestedPath,
                MmsRawDirectoryIdentityRecoveryStatus.RetryUnavailable,
                decision.ObservedCatalogPath,
                decision.RawNameComponents,
                pages.Count,
                "A distinct raw FileDirectory GraphicString was observed, but the destination stream cannot be safely reset for the bounded retry.");
        }

        try
        {
            destination.Position = 0;
            destination.SetLength(0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return FinishWithoutRetry(
                initial,
                requestedPath,
                MmsRawDirectoryIdentityRecoveryStatus.RetryUnavailable,
                decision.ObservedCatalogPath,
                decision.RawNameComponents,
                pages.Count,
                $"The local stream could not be reset for raw FileDirectory identity recovery: {ex.GetType().Name}: {ex.Message}");
        }

        var preRetryDiagnostic = LastFileTransferDiagnosticText;
        var retry = await DownloadFileRawObservedIdentityAsync(
            decision.CandidateFileName,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        var retryDiagnostic = LastFileTransferDiagnosticText;

        var recovery = new MmsRawDirectoryIdentityRecoveryEvidence
        {
            Status = retry.IsSuccess
                ? MmsRawDirectoryIdentityRecoveryStatus.RecoveredByRawObservedFileName
                : MmsRawDirectoryIdentityRecoveryStatus.RawObservedFileNameRetryFailed,
            RequestedPath = requestedPath,
            RawObservedFileName = decision.CandidateFileName,
            ObservedCatalogPath = decision.ObservedCatalogPath,
            RawNameComponents = decision.RawNameComponents,
            RetryAttempted = true,
            PagesRead = pages.Count,
            Message = retry.IsSuccess
                ? $"Recovered the transfer by replaying exact raw FileDirectory GraphicString '{MmsRawDirectoryIdentityRecoveryPolicy.DisplayRaw(decision.CandidateFileName)}'."
                : $"The exact raw FileDirectory GraphicString '{MmsRawDirectoryIdentityRecoveryPolicy.DisplayRaw(decision.CandidateFileName)}' was replayed once, but FileOpen still failed."
        };
        LastRawDirectoryIdentityRecovery = recovery;

        AppendAdaptiveDiagnostic(
            preRetryDiagnostic.TrimEnd() + "\n\n" +
            BuildRawIdentityDiagnostic(recovery) + "\n\n" +
            "RAW FILEDIRECTORY IDENTITY RETRY DIAGNOSTIC\n" +
            new string('-', 72) + "\n" +
            retryDiagnostic);

        return CloneWithMessage(retry, retry.Message + " " + recovery.Message);
    }

    private MmsFileTransferResult FinishWithoutRetry(
        MmsFileTransferResult initial,
        string requestedPath,
        MmsRawDirectoryIdentityRecoveryStatus status,
        string observedCatalogPath,
        IReadOnlyList<string> rawComponents,
        int pagesRead,
        string message)
    {
        var evidence = new MmsRawDirectoryIdentityRecoveryEvidence
        {
            Status = status,
            RequestedPath = requestedPath,
            RawObservedFileName = rawComponents.Count == 1 ? rawComponents[0] : string.Empty,
            ObservedCatalogPath = observedCatalogPath,
            RawNameComponents = rawComponents.ToArray(),
            RetryAttempted = false,
            PagesRead = pagesRead,
            Message = message
        };
        LastRawDirectoryIdentityRecovery = evidence;
        AppendAdaptiveDiagnostic(
            LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
            BuildRawIdentityDiagnostic(evidence));
        return CloneWithMessage(initial, initial.Message + " " + message);
    }

    private static string BuildRawIdentityDiagnostic(MmsRawDirectoryIdentityRecoveryEvidence evidence)
    {
        var components = evidence.RawNameComponents.Count == 0
            ? "-"
            : string.Join(" | ", evidence.RawNameComponents.Select(MmsRawDirectoryIdentityRecoveryPolicy.DisplayRaw));

        return
            "RAW FILEDIRECTORY IDENTITY RECOVERY\n" +
            new string('=', 72) + "\n" +
            $"Requested path      : {evidence.RequestedPath}\n" +
            $"Raw observed name   : {(string.IsNullOrEmpty(evidence.RawObservedFileName) ? "-" : MmsRawDirectoryIdentityRecoveryPolicy.DisplayRaw(evidence.RawObservedFileName))}\n" +
            $"Raw components      : {components}\n" +
            $"Observed catalog    : {(string.IsNullOrWhiteSpace(evidence.ObservedCatalogPath) ? "-" : evidence.ObservedCatalogPath)}\n" +
            $"Status              : {evidence.Status}\n" +
            $"Retry attempted     : {evidence.RetryAttempted}\n" +
            $"Pages read          : {evidence.PagesRead}\n" +
            $"Interpretation      : {evidence.Message}";
    }
}
