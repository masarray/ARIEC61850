namespace AR.Iec61850.Mms;

public enum MmsRemoteFileRecoveryStatus
{
    NotChecked = 0,
    RecoveredByObservedCasePath = 1,
    ObservedCasePathRetryFailed = 2,
    ExactPathStillUnopenable = 3,
    EntryDisappeared = 4,
    RevalidationInconclusive = 5,
    AssociationUnavailable = 6,
    RetryUnavailable = 7
}

public sealed class MmsRemoteFileRecoveryEvidence
{
    public MmsRemoteFileRecoveryStatus Status { get; init; }
    public string RequestedPath { get; init; } = string.Empty;
    public string EffectivePath { get; init; } = string.Empty;
    public bool RetryAttempted { get; init; }
    public MmsRemoteFileRevalidationEvidence? Revalidation { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class MmsRemoteFileRecoveryDecision
{
    public bool ShouldRetry { get; init; }
    public string CandidatePath { get; init; } = string.Empty;
    public MmsRemoteFileRecoveryStatus NoRetryStatus { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal static class MmsRemoteFileRecoveryPolicy
{
    public static MmsRemoteFileRecoveryDecision Decide(MmsRemoteFileRevalidationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.Status switch
        {
            MmsRemoteFileRevalidationStatus.PresentCaseVariant
                when !string.IsNullOrWhiteSpace(evidence.MatchedPath) &&
                     !evidence.MatchedPath.Equals(evidence.RemotePath, StringComparison.Ordinal)
                => new MmsRemoteFileRecoveryDecision
                {
                    ShouldRetry = true,
                    CandidatePath = evidence.MatchedPath,
                    Reason = "FileDirectory returned a concrete case-sensitive path that differs from the originally requested path."
                },

            MmsRemoteFileRevalidationStatus.PresentExactPath
                => NoRetry(
                    MmsRemoteFileRecoveryStatus.ExactPathStillUnopenable,
                    "The exact path remains visible, but every established FileOpen representation already failed; repeating the same path would add no evidence."),

            MmsRemoteFileRevalidationStatus.EntryDisappeared
                => NoRetry(
                    MmsRemoteFileRecoveryStatus.EntryDisappeared,
                    "The remote entry disappeared from a complete re-list, so retrying the stale path is not justified."),

            MmsRemoteFileRevalidationStatus.AssociationUnavailable
                => NoRetry(
                    MmsRemoteFileRecoveryStatus.AssociationUnavailable,
                    "The MMS association is unavailable, so no recovery retry can be issued."),

            MmsRemoteFileRevalidationStatus.DirectoryReadFailed
                => NoRetry(
                    MmsRemoteFileRecoveryStatus.RevalidationInconclusive,
                    "Directory revalidation was inconclusive, so the engine will not guess a replacement path."),

            _ => NoRetry(
                MmsRemoteFileRecoveryStatus.RevalidationInconclusive,
                "No evidence-backed replacement path is available.")
        };
    }

    private static MmsRemoteFileRecoveryDecision NoRetry(
        MmsRemoteFileRecoveryStatus status,
        string reason)
        => new()
        {
            ShouldRetry = false,
            NoRetryStatus = status,
            Reason = reason
        };
}

public sealed partial class MmsClientSession
{
    public MmsRemoteFileRecoveryEvidence? LastRemoteFileRecovery { get; private set; }

    /// <summary>
    /// Runs canonical FileOpen interoperability plus P1 revalidation. If the re-list
    /// returns a concrete case-variant path, retries exactly once using that observed
    /// path. No retry is made for exact-path-present, disappeared, or inconclusive
    /// evidence. The recovery is evidence-driven and bounded.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileCanonicalPathRecoveredAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastRemoteFileRecovery = null;

        var requestedPath = MmsFileNameEncoding.Normalize(remotePath);
        var initial = await DownloadFileCanonicalPathRevalidatedAsync(
            requestedPath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (initial.IsSuccess)
            return initial;

        var revalidation = LastRemoteFileRevalidation;
        if (revalidation == null)
            return initial;

        var decision = MmsRemoteFileRecoveryPolicy.Decide(revalidation);
        if (!decision.ShouldRetry)
        {
            var evidence = new MmsRemoteFileRecoveryEvidence
            {
                Status = decision.NoRetryStatus,
                RequestedPath = requestedPath,
                EffectivePath = revalidation.MatchedPath,
                RetryAttempted = false,
                Revalidation = revalidation,
                Message = decision.Reason
            };
            LastRemoteFileRecovery = evidence;
            AppendAdaptiveDiagnostic(
                LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
                BuildRecoveryDiagnostic(evidence));
            return CloneWithMessage(initial, initial.Message + " " + decision.Reason);
        }

        if (!IsMmsInitiated)
        {
            var evidence = new MmsRemoteFileRecoveryEvidence
            {
                Status = MmsRemoteFileRecoveryStatus.AssociationUnavailable,
                RequestedPath = requestedPath,
                EffectivePath = decision.CandidatePath,
                RetryAttempted = false,
                Revalidation = revalidation,
                Message = "A case-variant path was observed, but the MMS association is no longer initiated."
            };
            LastRemoteFileRecovery = evidence;
            AppendAdaptiveDiagnostic(
                LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
                BuildRecoveryDiagnostic(evidence));
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        if (!destination.CanSeek)
        {
            var evidence = new MmsRemoteFileRecoveryEvidence
            {
                Status = MmsRemoteFileRecoveryStatus.RetryUnavailable,
                RequestedPath = requestedPath,
                EffectivePath = decision.CandidatePath,
                RetryAttempted = false,
                Revalidation = revalidation,
                Message = "A case-variant path was observed, but the destination stream is not seekable and cannot be safely reset for one bounded retry."
            };
            LastRemoteFileRecovery = evidence;
            AppendAdaptiveDiagnostic(
                LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
                BuildRecoveryDiagnostic(evidence));
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        try
        {
            destination.Position = 0;
            destination.SetLength(0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            var evidence = new MmsRemoteFileRecoveryEvidence
            {
                Status = MmsRemoteFileRecoveryStatus.RetryUnavailable,
                RequestedPath = requestedPath,
                EffectivePath = decision.CandidatePath,
                RetryAttempted = false,
                Revalidation = revalidation,
                Message = $"A case-variant path was observed, but the local stream could not be reset: {ex.GetType().Name}: {ex.Message}"
            };
            LastRemoteFileRecovery = evidence;
            AppendAdaptiveDiagnostic(
                LastFileTransferDiagnosticText.TrimEnd() + "\n\n" +
                BuildRecoveryDiagnostic(evidence));
            return CloneWithMessage(initial, initial.Message + " " + evidence.Message);
        }

        var initialDiagnostic = LastFileTransferDiagnosticText;
        var retry = await DownloadFileCanonicalPathAdaptiveAsync(
            decision.CandidatePath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        var retryDiagnostic = LastFileTransferDiagnosticText;

        var recovery = new MmsRemoteFileRecoveryEvidence
        {
            Status = retry.IsSuccess
                ? MmsRemoteFileRecoveryStatus.RecoveredByObservedCasePath
                : MmsRemoteFileRecoveryStatus.ObservedCasePathRetryFailed,
            RequestedPath = requestedPath,
            EffectivePath = decision.CandidatePath,
            RetryAttempted = true,
            Revalidation = revalidation,
            Message = retry.IsSuccess
                ? $"Recovered the transfer by retrying the exact case-sensitive path observed from FileDirectory: '{decision.CandidatePath}'."
                : $"The exact case-sensitive path observed from FileDirectory was retried once, but FileOpen still failed: '{decision.CandidatePath}'."
        };
        LastRemoteFileRecovery = recovery;

        AppendAdaptiveDiagnostic(
            initialDiagnostic.TrimEnd() + "\n\n" +
            BuildRecoveryDiagnostic(recovery) + "\n\n" +
            "CASE-PATH RETRY DIAGNOSTIC\n" +
            new string('-', 72) + "\n" +
            retryDiagnostic);

        return CloneWithMessage(
            retry,
            retry.Message + " " + recovery.Message);
    }

    private static string BuildRecoveryDiagnostic(MmsRemoteFileRecoveryEvidence evidence)
        =>
            "REMOTE FILE RECOVERY\n" +
            new string('=', 72) + "\n" +
            $"Requested path     : {evidence.RequestedPath}\n" +
            $"Effective path     : {(string.IsNullOrWhiteSpace(evidence.EffectivePath) ? "-" : evidence.EffectivePath)}\n" +
            $"Status             : {evidence.Status}\n" +
            $"Retry attempted    : {evidence.RetryAttempted}\n" +
            $"Revalidation       : {evidence.Revalidation?.Status.ToString() ?? "-"}\n" +
            $"Interpretation     : {evidence.Message}";
}
