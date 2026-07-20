using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

/// <summary>
/// Compatibility file-transfer path for MMS servers that allocate a negative
/// FileReadStateMachine identifier. ISO 9506 defines frsmID as Integer32, so the
/// full signed range is valid and must be echoed unchanged in FileRead/FileClose.
/// </summary>
public static class MmsInteroperableFileOpenResponseDecoder
{
    public static MmsFileOpenResult Decode(ReadOnlyMemory<byte> presentationPayload, int expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);
        if (!MmsFileResponseEnvelope.TryDecode(
                presentationPayload,
                expectedInvokeId,
                serviceTagNumber: 72,
                operationName: "FileOpen",
                out var service,
                out var error))
        {
            return Fail(error, hex);
        }

        try
        {
            int? stateMachineId = null;
            uint? fileSize = null;
            byte[] lastModified = Array.Empty<byte>();

            foreach (var field in BerReader.ReadChildren(service.Value))
            {
                if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0)
                {
                    var value = BerReader.ReadSignedInteger(field);
                    if (value is >= int.MinValue and <= int.MaxValue)
                        stateMachineId = (int)value.Value;
                }
                else if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1 && field.Constructed)
                {
                    MmsFileResponseEnvelope.DecodeFileAttributes(field, ref fileSize, ref lastModified);
                }
            }

            if (!stateMachineId.HasValue)
                return Fail("FileOpen response did not contain a valid signed Integer32 file read state machine identifier.", hex);

            return new MmsFileOpenResult
            {
                IsSuccess = true,
                FileReadStateMachineId = stateMachineId.Value,
                FileSizeBytes = fileSize,
                LastModifiedRaw = lastModified,
                Message = fileSize.HasValue
                    ? $"MMS FileOpen succeeded. FRSM={stateMachineId.Value}, declaredSize={fileSize.Value}."
                    : $"MMS FileOpen succeeded. FRSM={stateMachineId.Value}, declared size unavailable.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"FileOpen response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsFileOpenResult Fail(string message, string hex)
        => new()
        {
            IsSuccess = false,
            Message = message,
            ResponseHexPreview = hex
        };
}

public static class MmsInteroperableFileReadRequest
{
    public static byte[] Build(int invokeId, int fileReadStateMachineId)
        => BuildFrsmRequest(invokeId, fileReadStateMachineId, serviceTagNumber: 73);

    internal static byte[] BuildFrsmRequest(int invokeId, int fileReadStateMachineId, int serviceTagNumber)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));

        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: false,
            tagNumber: serviceTagNumber,
            BerWriter.EncodeSignedInteger(fileReadStateMachineId));
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            MmsPresentation.Concat(MmsPresentation.Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }
}

public static class MmsInteroperableFileCloseRequest
{
    public static byte[] Build(int invokeId, int fileReadStateMachineId)
        => MmsInteroperableFileReadRequest.BuildFrsmRequest(invokeId, fileReadStateMachineId, serviceTagNumber: 74);
}

public sealed partial class MmsClientSession
{
    public async Task<MmsFileTransferResult> DownloadFileInteroperableAsync(
        string remotePath,
        Stream destination,
        MmsFileTransferOptions? options = null,
        IProgress<MmsFileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
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
            var opened = await FileOpenInteroperableAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
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
                        stage: "Validation after FileOpen",
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
            failure = $"File transfer pipeline failed: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "Transfer pipeline",
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
                RemotePath = normalizedPath,
                BytesTransferred = bytesTransferred,
                ExpectedBytes = expectedBytes,
                ReadOperations = readOperations,
                IsComplete = true
            });
        }

        var completionMessage = success
            ? $"Downloaded {bytesTransferred} byte(s) from '{normalizedPath}' in {readOperations} FileRead operation(s); FRSM={stateMachineId}."
            : $"{failure ?? "MMS file transfer failed."} RemotePath='{normalizedPath}', FRSM={stateMachineId?.ToString() ?? "not-opened"}.";

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

    private async Task<MmsFileOpenResult> FileOpenInteroperableAsync(
        string remotePath,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsFileOpenRequest.Build(invokeId, remotePath, initialPosition: 0);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsInteroperableFileOpenResponseDecoder.Decode(response, invokeId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            RecordFileTransferDiagnostic(
                stage: "FileOpen",
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
            var message = $"FileOpen transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "FileOpen",
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

    private async Task<MmsFileReadResult> FileReadInteroperableAsync(
        int fileReadStateMachineId,
        int readOperation,
        long bytesTransferred,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsInteroperableFileReadRequest.Build(invokeId, fileReadStateMachineId);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsFileReadResponseDecoder.Decode(response, invokeId, fileReadStateMachineId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            RecordFileTransferDiagnostic(
                stage: $"FileRead #{readOperation}",
                success: result.IsSuccess,
                message: result.Message,
                invokeId: invokeId,
                fileReadStateMachineId: fileReadStateMachineId,
                readOperation: readOperation,
                bytesTransferred: bytesTransferred + (result.IsSuccess ? result.Data.LongLength : 0),
                moreFollows: result.IsSuccess ? result.MoreFollows : null,
                requestHex: requestHex,
                responseHex: result.ResponseHexPreview);
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var message = $"FileRead transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: $"FileRead #{readOperation}",
                success: false,
                message: message,
                invokeId: invokeId,
                fileReadStateMachineId: fileReadStateMachineId,
                readOperation: readOperation,
                bytesTransferred: bytesTransferred,
                requestHex: requestHex,
                responseHex: string.Empty,
                exception: ex);
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileReadResult
            {
                IsSuccess = false,
                FileReadStateMachineId = fileReadStateMachineId,
                Message = message,
                ResponseHexPreview = string.Empty
            };
        }
    }

    private async Task<MmsFileCloseResult> FileCloseInteroperableAsync(
        int fileReadStateMachineId,
        long bytesTransferred,
        int readOperations,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsInteroperableFileCloseRequest.Build(invokeId, fileReadStateMachineId);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsFileCloseResponseDecoder.Decode(response, invokeId, fileReadStateMachineId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            RecordFileTransferDiagnostic(
                stage: "FileClose",
                success: result.IsSuccess,
                message: result.Message,
                invokeId: invokeId,
                fileReadStateMachineId: fileReadStateMachineId,
                readOperation: readOperations,
                bytesTransferred: bytesTransferred,
                requestHex: requestHex,
                responseHex: result.ResponseHexPreview);
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var message = $"FileClose transport fault: {ex.GetType().Name}: {ex.Message}";
            RecordFileTransferDiagnostic(
                stage: "FileClose",
                success: false,
                message: message,
                invokeId: invokeId,
                fileReadStateMachineId: fileReadStateMachineId,
                readOperation: readOperations,
                bytesTransferred: bytesTransferred,
                requestHex: requestHex,
                responseHex: string.Empty,
                exception: ex);
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileCloseResult
            {
                IsSuccess = false,
                FileReadStateMachineId = fileReadStateMachineId,
                Message = message,
                ResponseHexPreview = string.Empty
            };
        }
    }
}