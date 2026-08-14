namespace AR.Iec61850.Mms;

public enum MmsRawDirectoryIdentityRecoveryStatus
{
    NotChecked = 0,
    RecoveredByRawObservedFileName = 1,
    RawObservedFileNameRetryFailed = 2,
    NoRawSingleGraphicString = 3,
    RawIdentityAlreadyTried = 4,
    EntryNoLongerPresent = 5,
    DirectoryReadFailed = 6,
    AssociationUnavailable = 7,
    RetryUnavailable = 8
}

public sealed class MmsRawDirectoryIdentityRecoveryEvidence
{
    public MmsRawDirectoryIdentityRecoveryStatus Status { get; init; }
    public string RequestedPath { get; init; } = string.Empty;
    public string RawObservedFileName { get; init; } = string.Empty;
    public string ObservedCatalogPath { get; init; } = string.Empty;
    public IReadOnlyList<string> RawNameComponents { get; init; } = Array.Empty<string>();
    public bool RetryAttempted { get; init; }
    public int PagesRead { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class MmsRawDirectoryIdentityDecision
{
    public bool ShouldRetry { get; init; }
    public string CandidateFileName { get; init; } = string.Empty;
    public string ObservedCatalogPath { get; init; } = string.Empty;
    public IReadOnlyList<string> RawNameComponents { get; init; } = Array.Empty<string>();
    public MmsRawDirectoryIdentityRecoveryStatus NoRetryStatus { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal static class MmsRawDirectoryIdentityRecoveryPolicy
{
    public static MmsRawDirectoryIdentityDecision Decide(
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
        matchedEntry ??= successfulEntries.FirstOrDefault(entry =>
            NormalizeObservedPath(entry.Path).Equals(normalizedRequestedPath, StringComparison.OrdinalIgnoreCase));

        if (matchedEntry != null)
        {
            var rawComponents = matchedEntry.RawNameComponents ?? Array.Empty<string>();
            if (rawComponents.Count != 1 || string.IsNullOrWhiteSpace(matchedEntry.RawName))
            {
                return NoRetry(
                    MmsRawDirectoryIdentityRecoveryStatus.NoRawSingleGraphicString,
                    matchedEntry.Path,
                    rawComponents,
                    rawComponents.Count == 0
                        ? "The matching FileDirectory entry has no preserved raw GraphicString identity."
                        : $"The matching FileDirectory entry used {rawComponents.Count} GraphicString components; no single raw filename identity can be replayed without inventing a representation.");
            }

            var rawName = matchedEntry.RawName;
            try
            {
                MmsRawObservedFileOpenRequest.ValidateObservedIdentity(rawName);
            }
            catch (ArgumentException ex)
            {
                return NoRetry(
                    MmsRawDirectoryIdentityRecoveryStatus.NoRawSingleGraphicString,
                    matchedEntry.Path,
                    rawComponents,
                    $"The preserved raw FileDirectory identity is not safe to replay: {ex.Message}");
            }

            if (WasAlreadyTriedAsSingleGraphicString(normalizedRequestedPath, rawName))
            {
                return NoRetry(
                    MmsRawDirectoryIdentityRecoveryStatus.RawIdentityAlreadyTried,
                    matchedEntry.Path,
                    rawComponents,
                    $"The exact raw FileDirectory GraphicString '{DisplayRaw(rawName)}' is identical to a single-GraphicString FileOpen representation already attempted by P0-P3.");
            }

            return new MmsRawDirectoryIdentityDecision
            {
                ShouldRetry = true,
                CandidateFileName = rawName,
                ObservedCatalogPath = matchedEntry.Path,
                RawNameComponents = rawComponents.ToArray(),
                Reason = $"FileDirectory returned the matching entry as the distinct raw GraphicString '{DisplayRaw(rawName)}'; replaying that exact wire identity adds evidence not covered by the normalized, segmented, or rooted representations."
            };
        }

        var failedPage = pages.FirstOrDefault(page => !page.IsSuccess);
        if (failedPage != null)
        {
            return NoRetry(
                MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                string.Empty,
                Array.Empty<string>(),
                $"Raw FileDirectory identity recovery is inconclusive because FileDirectory failed: {failedPage.Message}");
        }

        if (pages.Count == 0)
        {
            return NoRetry(
                MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                string.Empty,
                Array.Empty<string>(),
                "Raw FileDirectory identity recovery is inconclusive because no FileDirectory response was obtained.");
        }

        if (pages[^1].MoreFollows)
        {
            return NoRetry(
                MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed,
                string.Empty,
                Array.Empty<string>(),
                "Raw FileDirectory identity recovery is inconclusive because the bounded FileDirectory read ended while moreFollows remained true.");
        }

        return NoRetry(
            MmsRawDirectoryIdentityRecoveryStatus.EntryNoLongerPresent,
            string.Empty,
            Array.Empty<string>(),
            $"The requested catalog path '{normalizedRequestedPath}' was no longer present in the completed FileDirectory re-list.");
    }

    internal static bool WasAlreadyTriedAsSingleGraphicString(string normalizedRequestedPath, string rawName)
    {
        if (rawName.Equals(normalizedRequestedPath, StringComparison.Ordinal))
            return true;

        var rootedBackslash = "\\" + normalizedRequestedPath.Replace('/', '\\');
        return rawName.Equals(rootedBackslash, StringComparison.Ordinal);
    }

    internal static string DisplayRaw(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static MmsRawDirectoryIdentityDecision NoRetry(
        MmsRawDirectoryIdentityRecoveryStatus status,
        string observedCatalogPath,
        IReadOnlyList<string> rawNameComponents,
        string reason)
        => new()
        {
            ShouldRetry = false,
            ObservedCatalogPath = observedCatalogPath,
            RawNameComponents = rawNameComponents.ToArray(),
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
