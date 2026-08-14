using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

/// <summary>
/// Builds the nested MMS FileOpen form observed on relays that expose a full,
/// case-sensitive slash path as one GraphicString. This intentionally coexists
/// with the legacy segmented FileName encoder and the rooted-backslash fallback.
/// </summary>
internal static class MmsSingleGraphicStringFileOpenRequest
{
    public static byte[] Build(int invokeId, string remotePath, uint initialPosition = 0)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));

        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var fileName = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            tagNumber: 0,
            BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(normalizedPath)));
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
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// File transfer strategy for nested paths:
    /// 1) full slash path in one GraphicString,
    /// 2) existing segmented FileName strategy,
    /// 3) existing rooted-backslash fallback when the server reports file-non-existent.
    /// Root-level files keep the established adaptive path unchanged.
    /// </summary>
    public async Task<MmsFileTransferResult> DownloadFileCanonicalPathAdaptiveAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);

        // For a root-level filename the canonical single-GraphicString form and the
        // established primary representation are identical, so avoid a duplicate open.
        if (!normalizedPath.Contains('/', StringComparison.Ordinal))
        {
            return await DownloadFileAdaptiveAsync(
                normalizedPath,
                destination,
                options,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        var canonical = await DownloadFileSingleGraphicStringAsync(
            normalizedPath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (canonical.IsSuccess)
            return canonical;

        var canonicalDiagnostic = LastFileTransferDiagnosticText;
        if (!MmsFileOpenPathFallbackPolicy.ShouldRetryWithRootedBackslash(canonical, canonicalDiagnostic))
            return canonical;

        if (!destination.CanSeek)
        {
            var message = canonical.Message +
                          " Legacy FileOpen compatibility retries were not attempted because the destination stream is not seekable.";
            AppendAdaptiveDiagnostic(
                "CANONICAL NESTED FILEOPEN\n" +
                new string('=', 72) + "\n" +
                $"Canonical path     : {normalizedPath}\n" +
                "Canonical result   : FAILED\n" +
                "Legacy retry       : SKIPPED (destination stream is not seekable)\n\n" +
                canonicalDiagnostic);
            return CloneFailure(canonical, message);
        }

        try
        {
            destination.Position = 0;
            destination.SetLength(0);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            var message = canonical.Message +
                          $" Legacy FileOpen compatibility retries could not reset the local stream: {ex.GetType().Name}: {ex.Message}";
            AppendAdaptiveDiagnostic(
                "CANONICAL NESTED FILEOPEN\n" +
                new string('=', 72) + "\n" +
                $"Canonical path     : {normalizedPath}\n" +
                "Canonical result   : FAILED\n" +
                $"Legacy retry       : SKIPPED (stream reset failed: {ex.GetType().Name}: {ex.Message})\n\n" +
                canonicalDiagnostic);
            return CloneFailure(canonical, message);
        }

        // Existing adaptive flow is deliberately retained as compatibility behavior:
        // its primary attempt is the legacy segmented FileName, followed by the
        // field-proven rooted-backslash representation on file-non-existent.
        var legacy = await DownloadFileAdaptiveAsync(
            normalizedPath,
            destination,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);
        var legacyDiagnostic = LastFileTransferDiagnosticText;

        AppendAdaptiveDiagnostic(
            "CANONICAL NESTED FILEOPEN\n" +
            new string('=', 72) + "\n" +
            $"Canonical path     : {normalizedPath}\n" +
            "Canonical encoding : single GraphicString with '/' separators\n" +
            "Canonical result   : FAILED (file-non-existent)\n" +
            $"Legacy result      : {(legacy.IsSuccess ? "SUCCESS" : "FAILED")}\n" +
            "Legacy sequence    : segmented FileName, then rooted-backslash when eligible\n\n" +
            "CANONICAL ATTEMPT\n" +
            new string('-', 72) + "\n" +
            canonicalDiagnostic + "\n\n" +
            "LEGACY ADAPTIVE ATTEMPT\n" +
            new string('-', 72) + "\n" +
            legacyDiagnostic);

        return legacy.IsSuccess
            ? CloneWithMessage(
                legacy,
                $"{legacy.Message} Canonical nested FileOpen was rejected; legacy compatibility representation succeeded.")
            : CloneWithMessage(
                legacy,
                $"{legacy.Message} Canonical single-GraphicString and legacy FileOpen representations all failed.");
    }

    private async Task<MmsFileTransferResult> DownloadFileSingleGraphicStringAsync(
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
        BeginFileTransferDiagnostic(normalizedPath);

        int? stateMachineId = null;
        long bytesTransferred = 0;
        long? expectedBytes = null;
        var readOperations = 0;
        var remoteFileClosed = false;
        string? failure = null;

        try
        {
            var opened = await FileOpenSingleGraphicStringAsync(
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
                        stage: "Validation after canonical FileOpen",
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
                                stage: "Canonical FileRead limit",
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
                                stage: $"Canonical FileRead #{readOperations} validation",
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
                                stage: $"Canonical FileRead #{readOperations} size validation",
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
                                    stage: $"Local write after canonical FileRead #{readOperations}",
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
                            RemotePath = normalizedPath,
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
                            stage: "Canonical declared-size validation",
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
                                stage: "Canonical local destination flush",
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
            failure = $"Canonical file transfer pipeline failed: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "Canonical transfer pipeline",
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
                        stage: "Canonical FileClose timeout",
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
                    stage: "Canonical FileClose skipped",
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
                RemotePath = normalizedPath,
                BytesTransferred = bytesTransferred,
                ExpectedBytes = expectedBytes,
                ReadOperations = readOperations,
                IsComplete = true
            });
        }

        var completionMessage = success
            ? $"Downloaded {bytesTransferred} byte(s) from '{normalizedPath}' using canonical single-GraphicString FileOpen in {readOperations} FileRead operation(s); FRSM={stateMachineId}."
            : $"{failure ?? "MMS file transfer failed."} RemotePath='{normalizedPath}', canonicalFileOpen=true, FRSM={stateMachineId?.ToString() ?? "not-opened"}.";

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
            RemotePath = normalizedPath,
            BytesTransferred = bytesTransferred,
            ExpectedBytes = expectedBytes,
            ReadOperations = readOperations,
            RemoteFileClosed = remoteFileClosed,
            Message = completionMessage
        };
    }

    private async Task<MmsFileOpenResult> FileOpenSingleGraphicStringAsync(
        string remotePath,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsSingleGraphicStringFileOpenRequest.Build(
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
                stage: "FileOpen canonical-single-GraphicString",
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
            var message = $"Canonical FileOpen transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "FileOpen canonical-single-GraphicString",
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
}
