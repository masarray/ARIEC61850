using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

/// <summary>
/// Builds the root-qualified single-GraphicString FileName form used by MMS servers
/// that expose root files through FileDirectory but require a leading backslash for
/// FileOpen. The representation is kept separate from the normal segmented path form
/// so generic servers continue to receive the established request first.
/// </summary>
internal static class MmsRootBackslashFileOpenRequest
{
    public static byte[] Build(int invokeId, string remotePath, uint initialPosition = 0)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));

        var rootedPath = BuildRootedPath(remotePath);
        var fileName = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            tagNumber: 0,
            BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(rootedPath)));
        var position = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: false,
            tagNumber: 1,
            MmsFileIntegerEncoding.EncodeUnsigned32(initialPosition));
        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            tagNumber: 72,
            MmsPresentation.Concat(fileName, position));
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            MmsPresentation.Concat(MmsPresentation.Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }

    public static string BuildRootedPath(string remotePath)
    {
        var normalized = MmsFileNameEncoding.Normalize(remotePath);
        return "\\" + normalized.Replace('/', '\\');
    }
}

internal static class MmsFileOpenPathFallbackPolicy
{
    private const string FileOpenConfirmedError = "Confirmed-Error PDU during FileOpen";
    private const string FileNonExistentSignature = "8B 01 07";

    public static bool ShouldRetryWithRootedBackslash(
        MmsFileTransferResult result,
        string? diagnosticText)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess || result.BytesTransferred != 0 || result.ReadOperations != 0)
            return false;

        var evidence = $"{result.Message}\n{diagnosticText}";
        return evidence.Contains(FileOpenConfirmedError, StringComparison.OrdinalIgnoreCase) &&
               evidence.Contains(FileNonExistentSignature, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Uses the established interoperable transfer first. When the server explicitly
    /// returns MMS file/file-non-existent during FileOpen before any byte is transferred,
    /// retries once with a root-qualified single GraphicString (for example
    /// <c>\\FRA00028.cfg</c>). This matches deployed relay file stores while keeping the
    /// normal standards-oriented request as the primary path.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileAdaptiveAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var primary = await DownloadFileInteroperableAsync(
            remotePath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (primary.IsSuccess)
            return primary;

        var primaryDiagnostic = LastFileTransferDiagnosticText;
        if (!MmsFileOpenPathFallbackPolicy.ShouldRetryWithRootedBackslash(primary, primaryDiagnostic))
            return primary;

        if (!destination.CanSeek)
        {
            var message = primary.Message +
                          " Root-qualified FileOpen retry was not attempted because the destination stream is not seekable.";
            AppendAdaptiveDiagnostic(
                "ADAPTIVE FILEOPEN FALLBACK\n" +
                "Root-qualified retry skipped: destination stream is not seekable.\n\n" +
                primaryDiagnostic);
            return CloneFailure(primary, message);
        }

        try
        {
            destination.Position = 0;
            destination.SetLength(0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            var message = primary.Message +
                          $" Root-qualified FileOpen retry could not reset the local stream: {ex.GetType().Name}: {ex.Message}";
            AppendAdaptiveDiagnostic(
                "ADAPTIVE FILEOPEN FALLBACK\n" +
                $"Root-qualified retry skipped: local stream reset failed ({ex.GetType().Name}: {ex.Message}).\n\n" +
                primaryDiagnostic);
            return CloneFailure(primary, message);
        }

        var rooted = await DownloadFileRootedBackslashAsync(
            remotePath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        var rootedDiagnostic = LastFileTransferDiagnosticText;
        var rootedPath = MmsRootBackslashFileOpenRequest.BuildRootedPath(remotePath);

        AppendAdaptiveDiagnostic(
            "ADAPTIVE FILEOPEN FALLBACK\n" +
            new string('=', 72) + "\n" +
            $"Primary path       : {MmsFileNameEncoding.Normalize(remotePath)}\n" +
            $"Fallback path      : {rootedPath}\n" +
            $"Fallback result    : {(rooted.IsSuccess ? "SUCCESS" : "FAILED")}\n" +
            "Reason             : primary FileOpen returned MMS file/file-non-existent.\n\n" +
            "PRIMARY ATTEMPT\n" +
            new string('-', 72) + "\n" +
            primaryDiagnostic + "\n\n" +
            "ROOTED-BACKSLASH ATTEMPT\n" +
            new string('-', 72) + "\n" +
            rootedDiagnostic);

        return rooted.IsSuccess
            ? rooted withMessage(
                $"{rooted.Message} Adaptive FileOpen fallback succeeded with remote path '{rootedPath}'.")
            : rooted withMessage(
                $"{rooted.Message} Primary bare path also failed with file-non-existent; rooted fallback path was '{rootedPath}'.");
    }

    private async Task<MmsFileTransferResult> DownloadFileRootedBackslashAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options,
        IProgress<MmsFileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));

        options ??= new MmsFileTransferOptions();
        if (options.MaximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBytes must be greater than zero.");
        if (options.MaximumReadOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumReadOperations must be greater than zero.");

        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var rootedPath = MmsRootBackslashFileOpenRequest.BuildRootedPath(normalizedPath);
        BeginFileTransferDiagnostic(rootedPath);

        int? stateMachineId = null;
        long bytesTransferred = 0;
        long? expectedBytes = null;
        var readOperations = 0;
        var remoteFileClosed = false;
        string? failure = null;

        try
        {
            var opened = await FileOpenRootedBackslashAsync(
                normalizedPath,
                cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                failure = opened.Message;
            }
            else
            {
                stateMachineId = opened.FileReadStateMachineId;
                expectedBytes = opened.FileSizeBytes is > 0 ? (long)opened.FileSizeBytes.Value : null;
                if (expectedBytes > options.MaximumBytes)
                {
                    failure = $"Remote file declares {expectedBytes.Value} byte(s), exceeding the configured limit of {options.MaximumBytes}.";
                    RecordFileTransferDiagnostic(
                        stage: "Validation after rooted FileOpen",
                        success: false,
                        message: failure,
                        fileReadStateMachineId: stateMachineId,
                        bytesTransferred: bytesTransferred);
                }
                else
                {
                    var moreFollows = true;
                    while (moreFollows && failure == null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++readOperations > options.MaximumReadOperations)
                        {
                            failure = $"FileRead exceeded the configured operation limit of {options.MaximumReadOperations}.";
                            RecordFileTransferDiagnostic(
                                stage: "FileRead limit",
                                success: false,
                                message: failure,
                                fileReadStateMachineId: stateMachineId,
                                readOperation: readOperations,
                                bytesTransferred: bytesTransferred,
                                moreFollows: moreFollows);
                            break;
                        }

                        var chunk = await FileReadInteroperableAsync(
                            stateMachineId.Value,
                            readOperations,
                            bytesTransferred,
                            cancellationToken).ConfigureAwait(false);
                        if (!chunk.IsSuccess)
                        {
                            failure = chunk.Message;
                            break;
                        }

                        if (chunk.Data.Length == 0 && chunk.MoreFollows)
                        {
                            failure = "FileRead returned an empty block while moreFollows remained true.";
                            RecordFileTransferDiagnostic(
                                stage: $"FileRead #{readOperations} validation",
                                success: false,
                                message: failure,
                                fileReadStateMachineId: stateMachineId,
                                readOperation: readOperations,
                                bytesTransferred: bytesTransferred,
                                moreFollows: chunk.MoreFollows);
                            break;
                        }

                        if (bytesTransferred + chunk.Data.LongLength > options.MaximumBytes)
                        {
                            failure = $"File transfer exceeded the configured limit of {options.MaximumBytes} byte(s).";
                            RecordFileTransferDiagnostic(
                                stage: $"FileRead #{readOperations} size validation",
                                success: false,
                                message: failure,
                                fileReadStateMachineId: stateMachineId,
                                readOperation: readOperations,
                                bytesTransferred: bytesTransferred,
                                moreFollows: chunk.MoreFollows);
                            break;
                        }

                        if (chunk.Data.Length > 0)
                        {
                            try
                            {
                                await destination.WriteAsync(
                                    chunk.Data.AsMemory(),
                                    cancellationToken).ConfigureAwait(false);
                                bytesTransferred += chunk.Data.LongLength;
                            }
                            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                            {
                                failure = $"Local destination write failed: {ex.GetType().Name}: {ex.Message}";
                                RecordFileTransferDiagnostic(
                                    stage: $"Local write after FileRead #{readOperations}",
                                    success: false,
                                    message: failure,
                                    fileReadStateMachineId: stateMachineId,
                                    readOperation: readOperations,
                                    bytesTransferred: bytesTransferred,
                                    moreFollows: chunk.MoreFollows,
                                    exception: ex);
                                break;
                            }
                        }

                        moreFollows = chunk.MoreFollows;
                        progress?.Report(new MmsFileTransferProgress
                        {
                            RemotePath = rootedPath,
                            BytesTransferred = bytesTransferred,
                            ExpectedBytes = expectedBytes,
                            ReadOperations = readOperations,
                            IsComplete = !moreFollows
                        });
                    }

                    if (failure == null &&
                        options.RequireDeclaredSizeMatch &&
                        expectedBytes.HasValue &&
                        expectedBytes.Value != bytesTransferred)
                    {
                        failure = $"Transferred size {bytesTransferred} does not match declared size {expectedBytes.Value}.";
                        RecordFileTransferDiagnostic(
                            stage: "Declared-size validation",
                            success: false,
                            message: failure,
                            fileReadStateMachineId: stateMachineId,
                            readOperation: readOperations,
                            bytesTransferred: bytesTransferred);
                    }

                    if (failure == null && options.FlushDestinationOnSuccess)
                    {
                        try
                        {
                            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                        {
                            failure = $"Local destination flush failed: {ex.GetType().Name}: {ex.Message}";
                            RecordFileTransferDiagnostic(
                                stage: "Local destination flush",
                                success: false,
                                message: failure,
                                fileReadStateMachineId: stateMachineId,
                                readOperation: readOperations,
                                bytesTransferred: bytesTransferred,
                                exception: ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            failure = $"Rooted file transfer pipeline failed: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "Rooted transfer pipeline",
                success: false,
                message: failure,
                fileReadStateMachineId: stateMachineId,
                readOperation: readOperations,
                bytesTransferred: bytesTransferred,
                exception: ex);
        }
        finally
        {
            if (stateMachineId.HasValue && IsMmsInitiated)
            {
                try
                {
                    using var closeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var close = await FileCloseInteroperableAsync(
                        stateMachineId.Value,
                        bytesTransferred,
                        readOperations,
                        closeCancellation.Token).ConfigureAwait(false);
                    remoteFileClosed = close.IsSuccess;
                    if (!close.IsSuccess && failure == null)
                        failure = close.Message;
                }
                catch (OperationCanceledException ex)
                {
                    if (failure == null)
                        failure = "FileClose did not complete within the bounded close timeout.";
                    RecordFileTransferDiagnostic(
                        stage: "FileClose timeout",
                        success: false,
                        message: "FileClose did not complete within the bounded close timeout.",
                        fileReadStateMachineId: stateMachineId,
                        readOperation: readOperations,
                        bytesTransferred: bytesTransferred,
                        exception: ex);
                }
            }
            else if (stateMachineId.HasValue)
            {
                RecordFileTransferDiagnostic(
                    stage: "FileClose skipped",
                    success: false,
                    message: "FileClose was skipped because the MMS association was no longer initiated.",
                    fileReadStateMachineId: stateMachineId,
                    readOperation: readOperations,
                    bytesTransferred: bytesTransferred);
                if (failure == null)
                    failure = "FileClose was skipped because the MMS association was no longer initiated.";
            }
        }

        var success = failure == null;
        if (success)
        {
            progress?.Report(new MmsFileTransferProgress
            {
                RemotePath = rootedPath,
                BytesTransferred = bytesTransferred,
                ExpectedBytes = expectedBytes,
                ReadOperations = readOperations,
                IsComplete = true
            });
        }

        var completionMessage = success
            ? $"Downloaded {bytesTransferred} byte(s) from '{rootedPath}' in {readOperations} FileRead operation(s); FRSM={stateMachineId}."
            : $"{failure ?? "MMS file transfer failed."} RemotePath='{rootedPath}', FRSM={stateMachineId?.ToString() ?? "not-opened"}.";

        CompleteFileTransferDiagnostic(
            success,
            completionMessage,
            bytesTransferred,
            readOperations,
            stateMachineId,
            remoteFileClosed);

        return new MmsFileTransferResult
        {
            IsSuccess = success,
            RemotePath = rootedPath,
            BytesTransferred = bytesTransferred,
            ExpectedBytes = expectedBytes,
            ReadOperations = readOperations,
            RemoteFileClosed = remoteFileClosed,
            Message = completionMessage
        };
    }

    private async Task<MmsFileOpenResult> FileOpenRootedBackslashAsync(
        string remotePath,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsRootBackslashFileOpenRequest.Build(
            invokeId,
            remotePath,
            initialPosition: 0);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(
                request,
                invokeId,
                cancellationToken).ConfigureAwait(false);
            var result = MmsInteroperableFileOpenResponseDecoder.Decode(response, invokeId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            RecordFileTransferDiagnostic(
                stage: "FileOpen rooted-backslash retry",
                success: result.IsSuccess,
                message: result.Message,
                invokeId: invokeId,
                fileReadStateMachineId: result.IsSuccess ? result.FileReadStateMachineId : null,
                requestHex: requestHex,
                responseHex: result.ResponseHexPreview);
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var message = $"Rooted FileOpen transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "FileOpen rooted-backslash retry",
                success: false,
                message: message,
                invokeId: invokeId,
                requestHex: requestHex,
                responseHex: string.Empty,
                exception: ex);
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileOpenResult
            {
                IsSuccess = false,
                Message = message,
                ResponseHexPreview = string.Empty
            };
        }
    }

    private void AppendAdaptiveDiagnostic(string diagnostic)
    {
        lock (_fileTransferDiagnosticSync)
            _lastFileTransferDiagnosticText = diagnostic.TrimEnd();
    }

    private static MmsFileTransferResult CloneFailure(
        MmsFileTransferResult source,
        string message)
        => new()
        {
            IsSuccess = false,
            RemotePath = source.RemotePath,
            BytesTransferred = source.BytesTransferred,
            ExpectedBytes = source.ExpectedBytes,
            ReadOperations = source.ReadOperations,
            RemoteFileClosed = source.RemoteFileClosed,
            Message = message
        };

    private static MmsFileTransferResult withMessage(
        this MmsFileTransferResult source,
        string message)
        => new()
        {
            IsSuccess = source.IsSuccess,
            RemotePath = source.RemotePath,
            BytesTransferred = source.BytesTransferred,
            ExpectedBytes = source.ExpectedBytes,
            ReadOperations = source.ReadOperations,
            RemoteFileClosed = source.RemoteFileClosed,
            Message = message
        };
}
