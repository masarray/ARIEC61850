using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsFileOpenResult
{
    public bool IsSuccess { get; init; }
    public int FileReadStateMachineId { get; init; } = -1;
    public uint? FileSizeBytes { get; init; }
    public byte[] LastModifiedRaw { get; init; } = Array.Empty<byte>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsFileReadResult
{
    public bool IsSuccess { get; init; }
    public int FileReadStateMachineId { get; init; } = -1;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public bool MoreFollows { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsFileCloseResult
{
    public bool IsSuccess { get; init; }
    public int FileReadStateMachineId { get; init; } = -1;
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsFileTransferOptions
{
    public long MaximumBytes { get; init; } = 512L * 1024L * 1024L;
    public int MaximumReadOperations { get; init; } = 100_000;
    public bool RequireDeclaredSizeMatch { get; init; }
    public bool FlushDestinationOnSuccess { get; init; } = true;
}

public sealed class MmsFileTransferProgress
{
    public string RemotePath { get; init; } = string.Empty;
    public long BytesTransferred { get; init; }
    public long? ExpectedBytes { get; init; }
    public int ReadOperations { get; init; }
    public bool IsComplete { get; init; }
}

public sealed class MmsFileTransferResult
{
    public bool IsSuccess { get; init; }
    public string RemotePath { get; init; } = string.Empty;
    public long BytesTransferred { get; init; }
    public long? ExpectedBytes { get; init; }
    public int ReadOperations { get; init; }
    public bool RemoteFileClosed { get; init; }
    public string Message { get; init; } = string.Empty;
}

public static class MmsFileOpenRequest
{
    public static byte[] Build(int invokeId, string remotePath, uint initialPosition = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));

        var fileName = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: true,
            tagNumber: 0,
            MmsFileNameEncoding.EncodeContent(remotePath));
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

public static class MmsFileReadRequest
{
    public static byte[] Build(int invokeId, int fileReadStateMachineId)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));
        if (fileReadStateMachineId < 0)
            throw new ArgumentOutOfRangeException(nameof(fileReadStateMachineId));

        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: false,
            tagNumber: 73,
            BerWriter.EncodeSignedInteger(fileReadStateMachineId));
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            MmsPresentation.Concat(MmsPresentation.Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }
}

public static class MmsFileCloseRequest
{
    public static byte[] Build(int invokeId, int fileReadStateMachineId)
    {
        if (invokeId < 0)
            throw new ArgumentOutOfRangeException(nameof(invokeId));
        if (fileReadStateMachineId < 0)
            throw new ArgumentOutOfRangeException(nameof(fileReadStateMachineId));

        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed: false,
            tagNumber: 74,
            BerWriter.EncodeSignedInteger(fileReadStateMachineId));
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            MmsPresentation.Concat(MmsPresentation.Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }
}

public static class MmsFileOpenResponseDecoder
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
                    if (value is >= 0 and <= int.MaxValue)
                        stateMachineId = (int)value.Value;
                }
                else if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1 && field.Constructed)
                {
                    MmsFileResponseEnvelope.DecodeFileAttributes(field, ref fileSize, ref lastModified);
                }
            }

            if (!stateMachineId.HasValue)
                return Fail("FileOpen response did not contain a valid file read state machine identifier.", hex);

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

public static class MmsFileReadResponseDecoder
{
    public static MmsFileReadResult Decode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        int fileReadStateMachineId)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);
        if (!MmsFileResponseEnvelope.TryDecode(
                presentationPayload,
                expectedInvokeId,
                serviceTagNumber: 73,
                operationName: "FileRead",
                out var service,
                out var error))
        {
            return Fail(fileReadStateMachineId, error, hex);
        }

        try
        {
            byte[] data = Array.Empty<byte>();
            var moreFollows = true;

            foreach (var field in BerReader.ReadChildren(service.Value))
            {
                if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0)
                    data = field.Value.ToArray();
                else if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1)
                    moreFollows = BerReader.ReadBoolean(field) ?? moreFollows;
            }

            return new MmsFileReadResult
            {
                IsSuccess = true,
                FileReadStateMachineId = fileReadStateMachineId,
                Data = data,
                MoreFollows = moreFollows,
                Message = $"MMS FileRead decoded {data.Length} byte(s), moreFollows={moreFollows}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(
                fileReadStateMachineId,
                $"FileRead response decode failed: {ex.GetType().Name}: {ex.Message}",
                hex);
        }
    }

    private static MmsFileReadResult Fail(int stateMachineId, string message, string hex)
        => new()
        {
            IsSuccess = false,
            FileReadStateMachineId = stateMachineId,
            Message = message,
            ResponseHexPreview = hex
        };
}

public static class MmsFileCloseResponseDecoder
{
    public static MmsFileCloseResult Decode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        int fileReadStateMachineId)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);
        if (!MmsFileResponseEnvelope.TryDecode(
                presentationPayload,
                expectedInvokeId,
                serviceTagNumber: 74,
                operationName: "FileClose",
                out _,
                out var error))
        {
            return new MmsFileCloseResult
            {
                IsSuccess = false,
                FileReadStateMachineId = fileReadStateMachineId,
                Message = error,
                ResponseHexPreview = hex
            };
        }

        return new MmsFileCloseResult
        {
            IsSuccess = true,
            FileReadStateMachineId = fileReadStateMachineId,
            Message = $"MMS FileClose succeeded for FRSM={fileReadStateMachineId}.",
            ResponseHexPreview = hex
        };
    }
}

internal static class MmsFileIntegerEncoding
{
    public static byte[] EncodeUnsigned32(uint value)
    {
        var encoded = BerWriter.EncodeUnsignedInteger(value);
        if (encoded.Length > 0 && (encoded[0] & 0x80) != 0)
            return MmsPresentation.Concat([0x00], encoded);

        return encoded;
    }
}

internal static class MmsFileNameEncoding
{
    public static byte[] EncodeContent(string path)
    {
        var normalized = Normalize(path);
        var body = Array.Empty<byte>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            body = MmsPresentation.Concat(body, BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(segment)));

        if (body.Length == 0)
            throw new ArgumentException("Remote file path has no usable path segment.", nameof(path));

        return body;
    }

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0'))
            throw new ArgumentException("Remote file path contains a null character.", nameof(path));

        var segments = path
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
            throw new ArgumentException("Remote file path has no usable path segment.", nameof(path));
        if (segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Remote file path contains a traversal segment.", nameof(path));

        return string.Join('/', segments);
    }
}

internal static class MmsFileResponseEnvelope
{
    public static bool TryDecode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        int serviceTagNumber,
        string operationName,
        out BerTlv service,
        out string error)
    {
        service = default;
        error = string.Empty;

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
            {
                error = $"Empty MMS {operationName} response payload.";
                return false;
            }

            if (mms[0] == 0xA2)
            {
                error = $"MMS Confirmed-Error PDU during {operationName}: {HexDump.ToCompactString(mms)}";
                return false;
            }

            if (mms[0] is 0xA3 or 0xA4)
            {
                error = $"MMS Reject/Abort PDU during {operationName}: {HexDump.ToCompactString(mms)}";
                return false;
            }

            if (mms[0] != 0xA1)
            {
                error = $"Expected MMS Confirmed-Response PDU [1] (0xA1) during {operationName}, received 0x{mms[0]:X2}.";
                return false;
            }

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
            {
                error = $"MMS {operationName} response could not be decoded as BER.";
                return false;
            }

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0 || children[0].EncodedTag != 0x02)
            {
                error = $"MMS {operationName} response did not start with invokeID.";
                return false;
            }

            var actualInvokeId = BerReader.ReadUnsignedInteger(children[0]);
            if (actualInvokeId != (ulong)expectedInvokeId)
            {
                error = $"MMS {operationName} invokeID mismatch. Expected {expectedInvokeId}, received {actualInvokeId}.";
                return false;
            }

            service = children
                .Skip(1)
                .FirstOrDefault(item =>
                    item.Class == BerClass.ContextSpecific &&
                    item.TagNumber == serviceTagNumber);
            if (service.EncodedTag == 0)
            {
                error = $"MMS response has no {operationName} service response node [{serviceTagNumber}].";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            error = $"{operationName} response envelope decode failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public static void DecodeFileAttributes(BerTlv attributes, ref uint? fileSize, ref byte[] lastModified)
    {
        foreach (var attribute in BerReader.ReadChildren(attributes.Value))
        {
            if (attribute.Class == BerClass.ContextSpecific && attribute.TagNumber == 0)
                fileSize = BerReader.ReadUInt32(attribute);
            else if (attribute.Class == BerClass.ContextSpecific && attribute.TagNumber == 1)
                lastModified = attribute.Value.ToArray();
        }
    }
}

public sealed partial class MmsClientSession
{
    public async Task<MmsFileOpenResult> FileOpenAsync(
        string remotePath,
        uint initialPosition = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        var normalizedPath = MmsFileNameEncoding.Normalize(remotePath);
        var invokeId = NextInvokeId();
        var request = MmsFileOpenRequest.Build(invokeId, normalizedPath, initialPosition);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsFileOpenResponseDecoder.Decode(response, invokeId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileOpenResult
            {
                IsSuccess = false,
                Message = $"FileOpen transport fault: {ex.GetType().Name}: {ex.Message}",
                ResponseHexPreview = LastDiscoveryResponseHex
            };
        }
    }

    public async Task<MmsFileReadResult> FileReadAsync(
        int fileReadStateMachineId,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        var invokeId = NextInvokeId();
        var request = MmsFileReadRequest.Build(invokeId, fileReadStateMachineId);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsFileReadResponseDecoder.Decode(response, invokeId, fileReadStateMachineId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileReadResult
            {
                IsSuccess = false,
                FileReadStateMachineId = fileReadStateMachineId,
                Message = $"FileRead transport fault: {ex.GetType().Name}: {ex.Message}",
                ResponseHexPreview = LastDiscoveryResponseHex
            };
        }
    }

    public async Task<MmsFileCloseResult> FileCloseAsync(
        int fileReadStateMachineId,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        var invokeId = NextInvokeId();
        var request = MmsFileCloseRequest.Build(invokeId, fileReadStateMachineId);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsFileCloseResponseDecoder.Decode(response, invokeId, fileReadStateMachineId);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsFileCloseResult
            {
                IsSuccess = false,
                FileReadStateMachineId = fileReadStateMachineId,
                Message = $"FileClose transport fault: {ex.GetType().Name}: {ex.Message}",
                ResponseHexPreview = LastDiscoveryResponseHex
            };
        }
    }

    public async Task<MmsFileTransferResult> DownloadFileAsync(
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
        var stateMachineId = -1;
        long bytesTransferred = 0;
        long? expectedBytes = null;
        var readOperations = 0;
        var remoteFileClosed = false;
        string? failure = null;

        try
        {
            var opened = await FileOpenAsync(normalizedPath, initialPosition: 0, cancellationToken).ConfigureAwait(false);
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
                            break;
                        }

                        var chunk = await FileReadAsync(stateMachineId, cancellationToken).ConfigureAwait(false);
                        if (!chunk.IsSuccess)
                        {
                            failure = chunk.Message;
                            break;
                        }

                        if (chunk.Data.Length == 0 && chunk.MoreFollows)
                        {
                            failure = "FileRead returned an empty block while moreFollows remained true.";
                            break;
                        }

                        if (bytesTransferred + chunk.Data.LongLength > options.MaximumBytes)
                        {
                            failure = $"File transfer exceeded the configured limit of {options.MaximumBytes} byte(s).";
                            break;
                        }

                        if (chunk.Data.Length > 0)
                        {
                            await destination.WriteAsync(chunk.Data.AsMemory(), cancellationToken).ConfigureAwait(false);
                            bytesTransferred += chunk.Data.LongLength;
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
                    }

                    if (failure == null && options.FlushDestinationOnSuccess)
                        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            failure = $"File transfer failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            if (stateMachineId >= 0 && IsMmsInitiated)
            {
                try
                {
                    using var closeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var close = await FileCloseAsync(stateMachineId, closeCancellation.Token).ConfigureAwait(false);
                    remoteFileClosed = close.IsSuccess;
                    if (!close.IsSuccess && failure == null)
                        failure = close.Message;
                }
                catch (OperationCanceledException)
                {
                    if (failure == null)
                        failure = "FileClose did not complete within the bounded close timeout.";
                }
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

        return new MmsFileTransferResult
        {
            IsSuccess = success,
            RemotePath = normalizedPath,
            BytesTransferred = bytesTransferred,
            ExpectedBytes = expectedBytes,
            ReadOperations = readOperations,
            RemoteFileClosed = remoteFileClosed,
            Message = success
                ? $"Downloaded {bytesTransferred} byte(s) from '{normalizedPath}' in {readOperations} FileRead operation(s)."
                : failure ?? "MMS file transfer failed."
        };
    }
}
