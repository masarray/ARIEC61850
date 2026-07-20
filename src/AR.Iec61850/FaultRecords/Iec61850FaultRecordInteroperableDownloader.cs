using AR.Iec61850.Mms;

namespace AR.Iec61850.FaultRecords;

/// <summary>
/// Downloads every file in a discovered fault-record bundle while preserving the
/// complete signed Integer32 FRSM returned by the MMS server.
/// </summary>
public static class Iec61850FaultRecordInteroperableDownloader
{
    public static async Task<Iec61850FaultRecordDownloadResult> DownloadAsync(
        MmsClientSession session,
        Iec61850FaultRecordSet record,
        string destinationRoot,
        Iec61850FaultRecordDownloadOptions? options = null,
        IProgress<Iec61850FaultRecordDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        options ??= new Iec61850FaultRecordDownloadOptions();
        ValidateOptions(options);

        if (record.Files.Count == 0)
            return Fail(record, "The selected fault record contains no downloadable file.");
        if (options.RequireCompleteRecord && !record.IsComplete)
            return Fail(record, $"The selected fault record is incomplete: {record.Completeness}.");
        if (!record.HasUnknownSize && record.KnownSizeBytes > options.MaximumTotalBytes)
            return Fail(record, $"The selected fault record declares {record.KnownSizeBytes} byte(s), exceeding the configured bundle limit of {options.MaximumTotalBytes}.");

        var destinationFullPath = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(destinationFullPath);
        var temporaryDirectory = Path.Combine(
            destinationFullPath,
            $".fault-record-{Guid.NewGuid():N}.partial");
        Directory.CreateDirectory(temporaryDirectory);

        var downloadedFiles = new List<Iec61850FaultRecordDownloadedFile>();
        long totalBytes = 0;
        long? expectedBytes = record.HasUnknownSize ? null : record.KnownSizeBytes;
        string? failure = null;

        try
        {
            for (var index = 0; index < record.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = record.Files[index];
                var safeFileName = Iec61850LocalPath.SanitizeFileName(file.Name);
                var localPath = Iec61850LocalPath.CombineUnderRoot(temporaryDirectory, safeFileName);
                var remainingBundleBytes = options.MaximumTotalBytes - totalBytes;
                if (remainingBundleBytes <= 0)
                    throw new InvalidDataException($"Bundle transfer exceeded the configured limit of {options.MaximumTotalBytes} byte(s).");

                var maximumFileBytes = Math.Min(options.MaximumFileBytes, remainingBundleBytes);
                var baseBytes = totalBytes;
                var fileProgress = new InlineProgress<MmsFileTransferProgress>(item =>
                {
                    progress?.Report(new Iec61850FaultRecordDownloadProgress
                    {
                        RecordId = record.RecordId,
                        CurrentFileName = file.Name,
                        CompletedFiles = index,
                        TotalFiles = record.Files.Count,
                        BytesTransferred = baseBytes + item.BytesTransferred,
                        ExpectedBytes = expectedBytes,
                        IsComplete = false
                    });
                });

                await using (var output = new FileStream(
                                 localPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 64 * 1024,
                                 useAsync: true))
                {
                    var transfer = await session.DownloadFileInteroperableAsync(
                        file.RemotePath,
                        output,
                        new MmsFileTransferOptions
                        {
                            MaximumBytes = maximumFileBytes,
                            MaximumReadOperations = options.MaximumReadOperationsPerFile,
                            RequireDeclaredSizeMatch = options.RequireDeclaredSizeMatch,
                            FlushDestinationOnSuccess = true
                        },
                        fileProgress,
                        cancellationToken).ConfigureAwait(false);

                    if (!transfer.IsSuccess)
                    {
                        throw new InvalidDataException(
                            $"{file.RemotePath}: {transfer.Message} " +
                            $"Request={ValueOrDash(session.LastDiscoveryRequestHex)}; " +
                            $"Response={ValueOrDash(session.LastDiscoveryResponseHex)}");
                    }

                    totalBytes += transfer.BytesTransferred;
                    downloadedFiles.Add(new Iec61850FaultRecordDownloadedFile
                    {
                        RemotePath = file.RemotePath,
                        LocalPath = localPath,
                        BytesTransferred = transfer.BytesTransferred
                    });
                }

                progress?.Report(new Iec61850FaultRecordDownloadProgress
                {
                    RecordId = record.RecordId,
                    CurrentFileName = file.Name,
                    CompletedFiles = index + 1,
                    TotalFiles = record.Files.Count,
                    BytesTransferred = totalBytes,
                    ExpectedBytes = expectedBytes,
                    IsComplete = index + 1 == record.Files.Count
                });
            }

            var finalDirectory = Iec61850LocalPath.ResolveAvailableDirectory(
                destinationFullPath,
                Iec61850LocalPath.SanitizeFileName(record.BaseName));
            Directory.Move(temporaryDirectory, finalDirectory);

            var finalFiles = downloadedFiles
                .Select(file => new Iec61850FaultRecordDownloadedFile
                {
                    RemotePath = file.RemotePath,
                    LocalPath = Path.Combine(finalDirectory, Path.GetFileName(file.LocalPath)),
                    BytesTransferred = file.BytesTransferred
                })
                .ToArray();

            return new Iec61850FaultRecordDownloadResult
            {
                IsSuccess = true,
                RecordId = record.RecordId,
                DestinationDirectory = finalDirectory,
                Files = finalFiles,
                BytesTransferred = totalBytes,
                Message = $"Downloaded {finalFiles.Length} fault-record file(s), {totalBytes} byte(s), to '{finalDirectory}'."
            };
        }
        catch (OperationCanceledException)
        {
            Iec61850LocalPath.TryDeleteDirectory(temporaryDirectory);
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            failure = ex.Message;
        }

        Iec61850LocalPath.TryDeleteDirectory(temporaryDirectory);
        return new Iec61850FaultRecordDownloadResult
        {
            IsSuccess = false,
            RecordId = record.RecordId,
            Files = downloadedFiles,
            BytesTransferred = totalBytes,
            Message = failure ?? "Fault-record download failed."
        };
    }

    private static void ValidateOptions(Iec61850FaultRecordDownloadOptions options)
    {
        if (options.MaximumTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTotalBytes must be greater than zero.");
        if (options.MaximumFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumFileBytes must be greater than zero.");
        if (options.MaximumReadOperationsPerFile <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumReadOperationsPerFile must be greater than zero.");
    }

    private static Iec61850FaultRecordDownloadResult Fail(Iec61850FaultRecordSet record, string message)
        => new()
        {
            IsSuccess = false,
            RecordId = record.RecordId,
            Message = message
        };

    private static string ValueOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
