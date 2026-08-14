namespace AR.Iec61850.Mms;

public enum MmsRemoteFileRevalidationStatus
{
    NotChecked = 0,
    PresentExactPath = 1,
    PresentCaseVariant = 2,
    EntryDisappeared = 3,
    DirectoryReadFailed = 4,
    AssociationUnavailable = 5
}

public sealed class MmsRemoteFileRevalidationEvidence
{
    public MmsRemoteFileRevalidationStatus Status { get; init; }
    public string RemotePath { get; init; } = string.Empty;
    public string ParentDirectory { get; init; } = string.Empty;
    public string ExpectedFileName { get; init; } = string.Empty;
    public string MatchedPath { get; init; } = string.Empty;
    public int PagesRead { get; init; }
    public int ObservedEntryCount { get; init; }
    public IReadOnlyList<string> ObservedPaths { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;

    public bool IsConclusive => Status is
        MmsRemoteFileRevalidationStatus.PresentExactPath or
        MmsRemoteFileRevalidationStatus.PresentCaseVariant or
        MmsRemoteFileRevalidationStatus.EntryDisappeared;
}

internal static class MmsRemoteFileRevalidationClassifier
{
    public static MmsRemoteFileRevalidationEvidence Classify(
        string remotePath,
        IReadOnlyList<MmsFileDirectoryResult> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentNullException.ThrowIfNull(pages);

        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var parentDirectory = GetParentDirectory(normalizedPath);
        var expectedFileName = GetFileName(normalizedPath);
        var successfulEntries = pages
            .Where(page => page.IsSuccess)
            .SelectMany(page => page.Entries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToArray();
        var observedPaths = successfulEntries
            .Select(entry => NormalizeObservedPath(entry.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var exactPath = observedPaths.FirstOrDefault(path =>
            path.Equals(normalizedPath, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(exactPath))
        {
            return Build(
                MmsRemoteFileRevalidationStatus.PresentExactPath,
                normalizedPath,
                parentDirectory,
                expectedFileName,
                exactPath,
                pages,
                observedPaths,
                $"Remote entry '{normalizedPath}' is still present with the exact case-sensitive path after FileOpen returned file-non-existent. The failure is therefore not explained by the directory entry disappearing; FileOpen path/representation interoperability remains suspect.");
        }

        var caseVariant = observedPaths.FirstOrDefault(path =>
            path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(caseVariant))
        {
            return Build(
                MmsRemoteFileRevalidationStatus.PresentCaseVariant,
                normalizedPath,
                parentDirectory,
                expectedFileName,
                caseVariant,
                pages,
                observedPaths,
                $"The parent directory still contains a case-variant entry '{caseVariant}' while FileOpen targeted '{normalizedPath}'. The remote file store appears case-sensitive or the cached catalog identity differs in case.");
        }

        var failedPage = pages.FirstOrDefault(page => !page.IsSuccess);
        if (failedPage != null)
        {
            return Build(
                MmsRemoteFileRevalidationStatus.DirectoryReadFailed,
                normalizedPath,
                parentDirectory,
                expectedFileName,
                string.Empty,
                pages,
                observedPaths,
                $"Remote-entry revalidation is inconclusive because FileDirectory failed for '{DisplayDirectory(parentDirectory)}': {failedPage.Message}");
        }

        if (pages.Count == 0)
        {
            return Build(
                MmsRemoteFileRevalidationStatus.DirectoryReadFailed,
                normalizedPath,
                parentDirectory,
                expectedFileName,
                string.Empty,
                pages,
                observedPaths,
                $"Remote-entry revalidation is inconclusive because no FileDirectory response was obtained for '{DisplayDirectory(parentDirectory)}'.");
        }

        if (pages[^1].MoreFollows)
        {
            return Build(
                MmsRemoteFileRevalidationStatus.DirectoryReadFailed,
                normalizedPath,
                parentDirectory,
                expectedFileName,
                string.Empty,
                pages,
                observedPaths,
                $"Remote-entry revalidation is inconclusive because FileDirectory still reported moreFollows=true after the bounded page limit for '{DisplayDirectory(parentDirectory)}'.");
        }

        return Build(
            MmsRemoteFileRevalidationStatus.EntryDisappeared,
            normalizedPath,
            parentDirectory,
            expectedFileName,
            string.Empty,
            pages,
            observedPaths,
            $"Remote entry '{normalizedPath}' is no longer present in a complete re-list of '{DisplayDirectory(parentDirectory)}'. The earlier fault-record catalog entry is stale or the IED rotated/removed the record before FileOpen.");
    }

    public static MmsRemoteFileRevalidationEvidence AssociationUnavailable(string remotePath)
    {
        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var parentDirectory = GetParentDirectory(normalizedPath);
        return new MmsRemoteFileRevalidationEvidence
        {
            Status = MmsRemoteFileRevalidationStatus.AssociationUnavailable,
            RemotePath = normalizedPath,
            ParentDirectory = parentDirectory,
            ExpectedFileName = GetFileName(normalizedPath),
            Message = "Remote-entry revalidation was not attempted because the MMS association was no longer initiated after FileOpen failure."
        };
    }

    private static MmsRemoteFileRevalidationEvidence Build(
        MmsRemoteFileRevalidationStatus status,
        string remotePath,
        string parentDirectory,
        string expectedFileName,
        string matchedPath,
        IReadOnlyList<MmsFileDirectoryResult> pages,
        IReadOnlyList<string> observedPaths,
        string message)
        => new()
        {
            Status = status,
            RemotePath = remotePath,
            ParentDirectory = parentDirectory,
            ExpectedFileName = expectedFileName,
            MatchedPath = matchedPath,
            PagesRead = pages.Count,
            ObservedEntryCount = observedPaths.Count,
            ObservedPaths = observedPaths.Take(128).ToArray(),
            Message = message
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

    private static string GetParentDirectory(string normalizedPath)
    {
        var index = normalizedPath.LastIndexOf('/');
        return index <= 0 ? string.Empty : normalizedPath[..index];
    }

    private static string GetFileName(string normalizedPath)
    {
        var index = normalizedPath.LastIndexOf('/');
        return index < 0 ? normalizedPath : normalizedPath[(index + 1)..];
    }

    private static string DisplayDirectory(string directory)
        => string.IsNullOrWhiteSpace(directory) ? "/" : directory;
}

internal static class MmsRemoteFileRevalidationPolicy
{
    private const string FileOpenConfirmedError = "Confirmed-Error PDU during FileOpen";
    private const string FileNonExistentSignature = "8B 01 07";

    public static bool ShouldRevalidate(MmsFileTransferResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess || result.BytesTransferred != 0 || result.ReadOperations != 0)
            return false;

        return result.Message.Contains(FileOpenConfirmedError, StringComparison.OrdinalIgnoreCase) &&
               result.Message.Contains(FileNonExistentSignature, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class MmsClientSession
{
    public MmsRemoteFileRevalidationEvidence? LastRemoteFileRevalidation { get; private set; }

    /// <summary>
    /// Runs the canonical/legacy adaptive FileOpen sequence first. If the final failure
    /// is still MMS file/file-non-existent before any data transfer, re-lists the parent
    /// directory to distinguish a stale/rotated entry from a still-visible path identity.
    /// The revalidation is read-only and bounded; it never retries FileOpen indefinitely.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileCanonicalPathRevalidatedAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastRemoteFileRevalidation = null;

        var transfer = await DownloadFileCanonicalPathAdaptiveAsync(
            remotePath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (transfer.IsSuccess || !MmsRemoteFileRevalidationPolicy.ShouldRevalidate(transfer))
            return transfer;

        var transferDiagnostic = LastFileTransferDiagnosticText;
        MmsRemoteFileRevalidationEvidence evidence;
        if (!IsMmsInitiated)
        {
            evidence = MmsRemoteFileRevalidationClassifier.AssociationUnavailable(remotePath);
        }
        else
        {
            evidence = await RevalidateRemoteFileEntryAsync(
                remotePath,
                maxPages: 8,
                cancellationToken).ConfigureAwait(false);
        }

        LastRemoteFileRevalidation = evidence;
        AppendAdaptiveDiagnostic(
            transferDiagnostic.TrimEnd() + "\n\n" +
            BuildRevalidationDiagnostic(evidence));

        return CloneWithMessage(
            transfer,
            transfer.Message + " " + evidence.Message);
    }

    private async Task<MmsRemoteFileRevalidationEvidence> RevalidateRemoteFileEntryAsync(
        string remotePath,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var slash = normalizedPath.LastIndexOf('/');
        var parentDirectory = slash <= 0 ? string.Empty : normalizedPath[..slash];

        try
        {
            var pages = await GetFileDirectoryPagedAsync(
                string.IsNullOrWhiteSpace(parentDirectory) ? null : parentDirectory,
                Math.Max(1, maxPages),
                cancellationToken).ConfigureAwait(false);
            return MmsRemoteFileRevalidationClassifier.Classify(normalizedPath, pages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            return new MmsRemoteFileRevalidationEvidence
            {
                Status = IsMmsInitiated
                    ? MmsRemoteFileRevalidationStatus.DirectoryReadFailed
                    : MmsRemoteFileRevalidationStatus.AssociationUnavailable,
                RemotePath = normalizedPath,
                ParentDirectory = parentDirectory,
                ExpectedFileName = slash < 0 ? normalizedPath : normalizedPath[(slash + 1)..],
                Message = $"Remote-entry revalidation failed while re-listing '{(string.IsNullOrWhiteSpace(parentDirectory) ? "/" : parentDirectory)}': {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static string BuildRevalidationDiagnostic(MmsRemoteFileRevalidationEvidence evidence)
    {
        var observed = evidence.ObservedPaths.Count == 0
            ? "-"
            : string.Join("\n", evidence.ObservedPaths.Take(20).Select(path => "  - " + path));
        var truncated = evidence.ObservedPaths.Count > 20
            ? $"\n  ... {evidence.ObservedPaths.Count - 20} additional path(s) omitted"
            : string.Empty;

        return
            "REMOTE FILE REVALIDATION\n" +
            new string('=', 72) + "\n" +
            $"Remote path        : {evidence.RemotePath}\n" +
            $"Parent directory   : {(string.IsNullOrWhiteSpace(evidence.ParentDirectory) ? "/" : evidence.ParentDirectory)}\n" +
            $"Expected file      : {evidence.ExpectedFileName}\n" +
            $"Status             : {evidence.Status}\n" +
            $"Matched path       : {(string.IsNullOrWhiteSpace(evidence.MatchedPath) ? "-" : evidence.MatchedPath)}\n" +
            $"Pages read         : {evidence.PagesRead}\n" +
            $"Observed entries   : {evidence.ObservedEntryCount}\n" +
            $"Interpretation     : {evidence.Message}\n" +
            "Observed paths      :\n" + observed + truncated;
    }
}
