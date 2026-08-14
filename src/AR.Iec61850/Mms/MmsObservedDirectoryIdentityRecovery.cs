namespace AR.Iec61850.Mms;

public enum MmsObservedDirectoryIdentityRecoveryStatus
{
    NotChecked = 0,
    RecoveredByObservedFileName = 1,
    ObservedFileNameRetryFailed = 2,
    NoDistinctObservedFileName = 3,
    EntryNoLongerPresent = 4,
    DirectoryReadFailed = 5,
    AssociationUnavailable = 6,
    RetryUnavailable = 7
}

public sealed class MmsObservedDirectoryIdentityRecoveryEvidence
{
    public MmsObservedDirectoryIdentityRecoveryStatus Status { get; init; }
    public string RequestedPath { get; init; } = string.Empty;
    public string ObservedFileName { get; init; } = string.Empty;
    public string ObservedCatalogPath { get; init; } = string.Empty;
    public bool RetryAttempted { get; init; }
    public int PagesRead { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class MmsObservedDirectoryIdentityDecision
{
    public bool ShouldRetry { get; init; }
    public string CandidateFileName { get; init; } = string.Empty;
    public string ObservedCatalogPath { get; init; } = string.Empty;
    public MmsObservedDirectoryIdentityRecoveryStatus NoRetryStatus { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal static class MmsObservedDirectoryIdentityRecoveryPolicy
{
    public static MmsObservedDirectoryIdentityDecision Decide(
        string requestedPath,
        IReadOnlyList<MmsFileDirectoryResult> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentNullException.ThrowIfNull(pages);

        var normalizedRequestedPath = MmsFileNameEncoding.Normalize(requestedPath);
        var successfulEntries = pages
            .Where(page => page.IsSuccess)
            .SelectMany(page => page.Entries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToArray();

        var matchedEntry = successfulEntries.FirstOrDefault(entry =>
            NormalizeObservedPath(entry.Path).Equals(normalizedRequestedPath, StringComparison.Ordinal));

        if (matchedEntry == null)
        {
            matchedEntry = successfulEntries.FirstOrDefault(entry =>
                NormalizeObservedPath(entry.Path).Equals(normalizedRequestedPath, StringComparison.OrdinalIgnoreCase));
        }

        if (matchedEntry != null)
        {
            if (string.IsNullOrWhiteSpace(matchedEntry.Name))
            {
                return NoRetry(
                    MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName,
                    "The matching FileDirectory entry did not contain a usable observed FileName identity.");
            }

            var candidate = matchedEntry.Name;
            string normalizedCandidate;
            try
            {
                normalizedCandidate = MmsFileNameEncoding.Normalize(candidate);
            }
            catch (ArgumentException)
            {
                return NoRetry(
                    MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName,
                    "The FileDirectory entry contained an observed FileName that could not be represented safely for FileOpen.");
            }

            if (normalizedCandidate.Equals(normalizedRequestedPath, StringComparison.Ordinal))
            {
                return NoRetry(
                    MmsObservedDirectoryIdentityRecoveryStatus.NoDistinctObservedFileName,
                    "FileDirectory returned the same complete FileName identity that has already been tried by FileOpen; replaying it would add no evidence.");
            }

            return new MmsObservedDirectoryIdentityDecision
            {
                ShouldRetry = true,
                CandidateFileName = candidate,
                ObservedCatalogPath = matchedEntry.Path,
                Reason = $"FileDirectory exposed the matching catalog entry using the distinct server-returned FileName identity '{candidate}'."
            };
        }

        var failedPage = pages.FirstOrDefault(page => !page.IsSuccess);
        if (failedPage != null)
        {
            return NoRetry(
                MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                $"Observed FileName recovery is inconclusive because FileDirectory failed: {failedPage.Message}");
        }

        if (pages.Count == 0)
        {
            return NoRetry(
                MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                "Observed FileName recovery is inconclusive because no FileDirectory response was obtained.");
        }

        if (pages[^1].MoreFollows)
        {
            return NoRetry(
                MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                "Observed FileName recovery is inconclusive because the bounded FileDirectory read ended while moreFollows remained true.");
        }

        return NoRetry(
            MmsObservedDirectoryIdentityRecoveryStatus.EntryNoLongerPresent,
            $"The requested catalog path '{normalizedRequestedPath}' was no longer present in the completed FileDirectory re-list.");
    }

    private static MmsObservedDirectoryIdentityDecision NoRetry(
        MmsObservedDirectoryIdentityRecoveryStatus status,
        string reason)
        => new()
        {
            ShouldRetry = false,
            NoRetryStatus = status,
            Reason = reason
        };

    private static string NormalizeObservedPath(string path)
    {
        try
        {
            return MmsFileNameEncoding.Normalize(path);
        }
        catch (ArgumentException)
        {
            return string.Join('/', path
                .Trim()
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
}

public sealed partial class MmsClientSession
{
    public MmsObservedDirectoryIdentityRecoveryEvidence? LastObservedDirectoryIdentityRecovery { get; private set; }

    /// <summary>
    /// Adds a bounded P3 recovery after the established P0/P1/P2 path sequence.
    /// When FileDirectory still exposes the requested catalog path but returns a
    /// distinct FileName identity for that entry, replays that server-returned
    /// identity exactly once through the existing adaptive FileOpen pipeline.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileObservedDirectoryIdentityRecoveredAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastObservedDirectoryIdentityRecovery = null;

        var requestedPath = MmsFileNameEncoding.Normalize(remotePath);
        var initial = await DownloadFileCanonicalPathRecoveredAsync(
            requestedPath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (initial.IsSuccess)
            return initial;

        var p1 = LastRemoteFileRevalidation;
        var p2 = LastRemoteFileRecovery;
        if (p1?.Status != MmsRemoteFileRevalidationStatus.PresentExactPath ||
            p2?.Status != MmsRemoteFileRecoveryStatus.ExactPathStillUnopenable)
        {
            return initial;
        }

        if (!IsMmsInitiated)
        {
            var evidence = new MmsObservedDirectoryIdentityRecoveryEvidence
            {
                Status = MmsObservedDirectoryIdentityRecoveryStatus.AssociationUnavailable,
                RequestedPath = requestedPath,
                ObservedCatalogPath = p1.MatchedPath,
                RetryAttempted = false,
                Message = "The exact catalog path remains known, but the MMS association is no longer initiated before observed FileName recovery."
            };
            LastObservedDirectoryIdentityRecovery = evidence;
            AppendObservedIdentityDiagnostic(initial, evidence);
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
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
            var evidence = new MmsObservedDirectoryIdentityRecoveryEvidence
            {
                Status = IsMmsInitiated
                    ? MmsObservedDirectoryIdentityRecoveryStatus.DirectoryReadFailed
                    : MmsObservedDirectoryIdentityRecoveryStatus.AssociationUnavailable,
                RequestedPath = requestedPath,
                ObservedCatalogPath = p1.MatchedPath,
                RetryAttempted = false,
                Message = $"Observed FileName recovery could not re-list '{(string.IsNullOrWhiteSpace(parentDirectory) ? "/" : parentDirectory)}': {ex.GetType().Name}: {ex.Message}"
            };
            LastObservedDirectoryIdentityRecovery = evidence;
            AppendObservedIdentityDiagnostic(initial, evidence);
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        var decision = MmsObservedDirectoryIdentityRecoveryPolicy.Decide(requestedPath, pages);
        if (!decision.ShouldRetry)
        {
            var evidence = new MmsObservedDirectoryIdentityRecoveryEvidence
            {
                Status = decision.NoRetryStatus,
                RequestedPath = requestedPath,
                ObservedCatalogPath = decision.ObservedCatalogPath,
                RetryAttempted = false,
                PagesRead = pages.Count,
                Message = decision.Reason
            };
            LastObservedDirectoryIdentityRecovery = evidence;
            AppendObservedIdentityDiagnostic(initial, evidence);
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        if (!destination.CanSeek || !destination.CanWrite)
        {
            var evidence = new MmsObservedDirectoryIdentityRecoveryEvidence
            {
                Status = MmsObservedDirectoryIdentityRecoveryStatus.RetryUnavailable,
                RequestedPath = requestedPath,
                ObservedFileName = decision.CandidateFileName,
                ObservedCatalogPath = decision.ObservedCatalogPath,
                RetryAttempted = false,
                PagesRead = pages.Count,
                Message = "A distinct server-returned FileName identity was observed, but the destination stream cannot be safely reset for the bounded retry."
            };
            LastObservedDirectoryIdentityRecovery = evidence;
            AppendObservedIdentityDiagnostic(initial, evidence);
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        try
        {
            destination.Position = 0;
            destination.SetLength(0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            var evidence = new MmsObservedDirectoryIdentityRecoveryEvidence
            {
                Status = MmsObservedDirectoryIdentityRecoveryStatus.RetryUnavailable,
                RequestedPath = requestedPath,
                ObservedFileName = decision.CandidateFileName,
                ObservedCatalogPath = decision.ObservedCatalogPath,
                RetryAttempted = false,
                PagesRead = pages.Count,
                Message = $"The local stream could not be reset for observed FileName recovery: {ex.GetType().Name}: {ex.Message}"
            };
            LastObservedDirectoryIdentityRecovery = evidence;
            AppendObservedIdentityDiagnostic(initial, evidence);
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        var preRetryDiagnostic = LastFileTransferDiagnosticText;
        var retry = await DownloadFileCanonicalPathAdaptiveAsync(
            decision.CandidateFileName,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        var retryDiagnostic = LastFileTransferDiagnosticText;

        var recovery = new MmsObservedDirectoryIdentityRecoveryEvidence
        {
            Status = retry.IsSuccess
                ? MmsObservedDirectoryIdentityRecoveryStatus.RecoveredByObservedFileName
                : MmsObservedDirectoryIdentityRecoveryStatus.ObservedFileNameRetryFailed,
            RequestedPath = requestedPath,
            ObservedFileName = decision.CandidateFileName,
            ObservedCatalogPath = decision.ObservedCatalogPath,
            RetryAttempted = true,
            PagesRead = pages.Count,
            Message = retry.IsSuccess
                ? $"Recovered the transfer by replaying the FileName identity returned by FileDirectory: '{decision.CandidateFileName}'."
                : $"The FileName identity returned by FileDirectory was replayed once, but FileOpen still failed: '{decision.CandidateFileName}'."
        };
        LastObservedDirectoryIdentityRecovery = recovery;

        AppendAdaptiveDiagnostic(
            preRetryDiagnostic.TrimEnd() + "\n\n" +
            BuildObservedIdentityDiagnostic(recovery) + "\n\n" +
            "OBSERVED FILENAME RETRY DIAGNOSTIC\n" +
            new string('-', 72) + "\n" +
            retryDiagnostic);

        return CloneWithMessage(retry, retry.Message + " " + recovery.Message);
    }

    private void AppendObservedIdentityDiagnostic(
        MmsFileTransferResult initial,
        MmsObservedDirectoryIdentityRecoveryEvidence evidence)
    {
        AppendAdaptiveDiagnostic(
            LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
            BuildObservedIdentityDiagnostic(evidence));
    }

    private static string BuildObservedIdentityDiagnostic(MmsObservedDirectoryIdentityRecoveryEvidence evidence)
        =>
            "OBSERVED DIRECTORY FILE-NAME RECOVERY\n" +
            new string('=', 72) + "\n" +
            $"Requested path      : {evidence.RequestedPath}\n" +
            $"Observed FileName   : {(string.IsNullOrWhiteSpace(evidence.ObservedFileName) ? "-" : evidence.ObservedFileName)}\n" +
            $"Observed catalog    : {(string.IsNullOrWhiteSpace(evidence.ObservedCatalogPath) ? "-" : evidence.ObservedCatalogPath)}\n" +
            $"Status              : {evidence.Status}\n" +
            $"Retry attempted     : {evidence.RetryAttempted}\n" +
            $"Pages read          : {evidence.PagesRead}\n" +
            $"Interpretation      : {evidence.Message}";
}
