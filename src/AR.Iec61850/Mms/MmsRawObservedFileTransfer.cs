using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

internal static class MmsRawObservedFileOpenRequest
{
    public static byte[] Build(int invokeId, string observedFileName, uint initialPosition = 0)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));

        ValidateObservedIdentity(observedFileName);

        var fileName = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            tagNumber: 0,
            BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(observedFileName)));
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

    internal static void ValidateObservedIdentity(string observedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observedFileName);

        if (observedFileName.Any(char.IsControl))
            throw new ArgumentException("Observed MMS FileName contains a control character.", nameof(observedFileName));

        var segments = observedFileName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Observed MMS FileName contains no usable path segment.", nameof(observedFileName));
        if (segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Observed MMS FileName contains a traversal segment.", nameof(observedFileName));
    }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Downloads a file using one exact GraphicString identity observed from
    /// FileDirectory. No separator, leading-root marker, case, or whitespace is
    /// normalized before FileOpen. This API is intended only for evidence-backed
    /// recovery after a FileDirectory response supplied the exact string.
    /// </summary>
    internal async Task<MmsFileTransferResult> DownloadFileRawObservedIdentityAsync(
        string observedFileName,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));

        MmsRawObservedFileOpenRequest.ValidateObservedIdentity(observedFileName);

        options ??= new MmsFileTransferOptions();
        if (options.MaximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBytes must be greater than zero.");
        if (options.MaximumReadOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumReadOperations must be greater than zero.");

        BeginFileTransferDiagnostic(observedFileName);

        int? stateMachineId = null;
        long bytesTransferred = 0;
        long? expectedBytes = null;
        var readOperations = 0;
        var remoteFileClosed = false;
        string? failure = null;

        try
        {
            var opened = await FileOpenRawObservedAsync(observedFileName, cancellationToken).ConfigureAwait(false);
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
                        stage: "Validation after raw-observed FileOpen",
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
                                await destination.WriteAsync(chunk.Data.AsMemory(), cancellationToken).ConfigureAwait(false);
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
                            RemotePath = observedFileName,
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
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            failure = $"Raw-observed file transfer pipeline failed: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "Raw-observed transfer pipeline",
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
                RemotePath = observedFileName,
                BytesTransferred = bytesTransferred,
                ExpectedBytes = expectedBytes,
                ReadOperations = readOperations,
                IsComplete = true
            });
        }

        var completionMessage = success
            ? $"Downloaded {bytesTransferred} byte(s) using exact FileDirectory GraphicString '{observedFileName}' in {readOperations} FileRead operation(s); FRSM={stateMachineId}."
            : $"{failure ?? "MMS file transfer failed."} RawObservedFileName='{observedFileName}', FRSM={stateMachineId?.ToString() ?? "not-opened"}.";

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
            RemotePath = observedFileName,
            BytesTransferred = bytesTransferred,
            ExpectedBytes = expectedBytes,
            ReadOperations = readOperations,
            RemoteFileClosed = remoteFileClosed,
            Message = completionMessage
        };
    }

    private async Task<MmsFileOpenResult> FileOpenRawObservedAsync(
        string observedFileName,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsRawObservedFileOpenRequest.Build(invokeId, observedFileName, initialPosition: 0);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsInteroperableFileOpenResponseDecoder.Decode(response, invokeId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            RecordFileTransferDiagnostic(
                stage: "FileOpen raw-observed-GraphicString",
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
            var message = $"Raw-observed FileOpen transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "FileOpen raw-observed-GraphicString",
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
