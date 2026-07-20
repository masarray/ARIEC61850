using System.Globalization;
using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.FaultRecords;

public enum Iec61850FaultRecordFileKind
{
    Configuration,
    Data,
    Header,
    Information,
    Combined,
    Archive,
    VendorPackage
}

public sealed class Iec61850FaultRecordFile
{
    public string Name { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
    public string RemoteDirectory { get; init; } = string.Empty;
    public string BaseName { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public Iec61850FaultRecordFileKind Kind { get; init; }
    public uint? SizeBytes { get; init; }
    public byte[] LastModifiedRaw { get; init; } = Array.Empty<byte>();
    public DateTimeOffset? LastModifiedUtc { get; init; }
}

public sealed class Iec61850FaultRecordSet
{
    public string RecordId { get; init; } = string.Empty;
    public string RemoteDirectory { get; init; } = string.Empty;
    public string BaseName { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850FaultRecordFile> Files { get; init; } = Array.Empty<Iec61850FaultRecordFile>();
    public bool IsComplete { get; init; }
    public string Completeness { get; init; } = string.Empty;
    public long KnownSizeBytes { get; init; }
    public bool HasUnknownSize { get; init; }
    public DateTimeOffset? LastModifiedUtc { get; init; }
    public bool CanDownload => Files.Count > 0;
}

public sealed class Iec61850FaultRecordCatalog
{
    public IReadOnlyList<Iec61850FaultRecordSet> Records { get; init; } = Array.Empty<Iec61850FaultRecordSet>();
    public IReadOnlyList<string> DirectoriesVisited { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public int FileCount => Records.Sum(record => record.Files.Count);
    public int CompleteRecordCount => Records.Count(record => record.IsComplete);
    public string Summary => $"Fault records={Records.Count}, complete={CompleteRecordCount}, files={FileCount}, directories={DirectoriesVisited.Count}.";
}

public sealed class Iec61850FaultRecordDiscoveryOptions
{
    public bool TraverseSubdirectories { get; init; } = true;
    public int MaximumDirectoryDepth { get; init; } = 4;
    public int MaximumDirectoryCount { get; init; } = 128;
    public int MaximumEntries { get; init; } = 20_000;
    public int MaximumPagesPerDirectory { get; init; } = 16;
    public IReadOnlyList<string> AdditionalRootDirectories { get; init; } = Array.Empty<string>();
}

public sealed class Iec61850FaultRecordDownloadOptions
{
    public long MaximumTotalBytes { get; init; } = 1024L * 1024L * 1024L;
    public long MaximumFileBytes { get; init; } = 512L * 1024L * 1024L;
    public int MaximumReadOperationsPerFile { get; init; } = 100_000;
    public bool RequireCompleteRecord { get; init; } = true;
    public bool RequireDeclaredSizeMatch { get; init; }
}

public sealed class Iec61850FaultRecordDownloadProgress
{
    public string RecordId { get; init; } = string.Empty;
    public string CurrentFileName { get; init; } = string.Empty;
    public int CompletedFiles { get; init; }
    public int TotalFiles { get; init; }
    public long BytesTransferred { get; init; }
    public long? ExpectedBytes { get; init; }
    public bool IsComplete { get; init; }
}

public sealed class Iec61850FaultRecordDownloadedFile
{
    public string RemotePath { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public long BytesTransferred { get; init; }
}

public sealed class Iec61850FaultRecordDownloadResult
{
    public bool IsSuccess { get; init; }
    public string RecordId { get; init; } = string.Empty;
    public string DestinationDirectory { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850FaultRecordDownloadedFile> Files { get; init; } = Array.Empty<Iec61850FaultRecordDownloadedFile>();
    public long BytesTransferred { get; init; }
    public string Message { get; init; } = string.Empty;
}

public static class Iec61850FaultRecordCatalogBuilder
{
    private static readonly IReadOnlyDictionary<string, Iec61850FaultRecordFileKind> SupportedExtensions =
        new Dictionary<string, Iec61850FaultRecordFileKind>(StringComparer.OrdinalIgnoreCase)
        {
            [".cfg"] = Iec61850FaultRecordFileKind.Configuration,
            [".dat"] = Iec61850FaultRecordFileKind.Data,
            [".hdr"] = Iec61850FaultRecordFileKind.Header,
            [".inf"] = Iec61850FaultRecordFileKind.Information,
            [".cff"] = Iec61850FaultRecordFileKind.Combined,
            [".zip"] = Iec61850FaultRecordFileKind.Archive
        };

    private static readonly string[] VendorPackagePrefixes =
    [
        "FRA", "FAULT", "DIST", "COMTRADE", "OSC", "RECORD", "REC", "DR"
    ];

    public static bool IsSupportedFile(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        return SupportedExtensions.ContainsKey(Path.GetExtension(normalized)) || LooksLikeVendorFaultRecordPackage(normalized);
    }

    public static Iec61850FaultRecordCatalog Build(
        IEnumerable<MmsFileDirectoryEntry> entries,
        IEnumerable<string>? directoriesVisited = null,
        IEnumerable<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var files = entries
            .Where(entry => !entry.IsLikelyDirectory && IsSupportedFile(entry.Path))
            .Select(ToFaultRecordFile)
            .DistinctBy(file => file.RemotePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var records = files
            .GroupBy(
                file => BuildRecordId(file.RemoteDirectory, file.BaseName),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildRecord(group.Key, group))
            .OrderByDescending(record => record.LastModifiedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(record => record.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Iec61850FaultRecordCatalog
        {
            Records = records,
            DirectoriesVisited = (directoriesVisited ?? Array.Empty<string>())
                .Select(Iec61850RemotePath.NormalizeDirectory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = (diagnostics ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray()
        };
    }

    private static Iec61850FaultRecordFile ToFaultRecordFile(MmsFileDirectoryEntry entry)
    {
        var remotePath = Iec61850RemotePath.NormalizeFile(entry.Path);
        var name = Iec61850RemotePath.GetFileName(remotePath);
        var directory = Iec61850RemotePath.GetDirectoryName(remotePath);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(name);
        var kind = SupportedExtensions.TryGetValue(extension, out var knownKind)
            ? knownKind
            : Iec61850FaultRecordFileKind.VendorPackage;

        return new Iec61850FaultRecordFile
        {
            Name = name,
            RemotePath = remotePath,
            RemoteDirectory = directory,
            BaseName = baseName,
            Extension = extension,
            Kind = kind,
            SizeBytes = entry.SizeBytes,
            LastModifiedRaw = entry.LastModifiedRaw.ToArray(),
            LastModifiedUtc = MmsGeneralizedTime.TryParse(entry.LastModifiedRaw)
        };
    }

    private static Iec61850FaultRecordSet BuildRecord(
        string recordId,
        IEnumerable<Iec61850FaultRecordFile> source)
    {
        var files = source
            .OrderBy(file => file.Kind)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasConfiguration = files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Configuration);
        var hasData = files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Data);
        var hasCombined = files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Combined);
        var hasArchive = files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Archive);
        var hasVendorPackage = files.Any(file => file.Kind == Iec61850FaultRecordFileKind.VendorPackage);
        var complete = hasVendorPackage || hasCombined || hasArchive || (hasConfiguration && hasData);
        var first = files[0];
        var modifiedTimes = files
            .Where(file => file.LastModifiedUtc.HasValue)
            .Select(file => file.LastModifiedUtc!.Value)
            .ToArray();

        return new Iec61850FaultRecordSet
        {
            RecordId = recordId,
            RemoteDirectory = first.RemoteDirectory,
            BaseName = first.BaseName,
            Files = files,
            IsComplete = complete,
            Completeness = DescribeCompleteness(hasConfiguration, hasData, hasCombined, hasArchive, hasVendorPackage),
            KnownSizeBytes = files.Sum(file => (long)(file.SizeBytes ?? 0)),
            HasUnknownSize = files.Any(file => !file.SizeBytes.HasValue || file.SizeBytes.Value == 0),
            LastModifiedUtc = modifiedTimes.Length == 0 ? null : modifiedTimes.Max()
        };
    }

    private static string DescribeCompleteness(
        bool hasConfiguration,
        bool hasData,
        bool hasCombined,
        bool hasArchive,
        bool hasVendorPackage)
    {
        if (hasVendorPackage)
            return "IED fault-record package";
        if (hasCombined)
            return "Combined COMTRADE file";
        if (hasArchive)
            return "COMTRADE archive";
        if (hasConfiguration && hasData)
            return "CFG + DAT";
        if (hasConfiguration)
            return "Missing DAT";
        if (hasData)
            return "Missing CFG";
        return "Incomplete";
    }

    private static bool LooksLikeVendorFaultRecordPackage(string path)
    {
        var name = Iec61850RemotePath.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(Path.GetExtension(name)))
            return false;

        var compact = new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (!compact.Any(char.IsDigit))
            return false;

        return VendorPackagePrefixes.Any(prefix => compact.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string BuildRecordId(string directory, string baseName)
        => string.IsNullOrWhiteSpace(directory) ? baseName : $"{directory}/{baseName}";
}

public sealed class Iec61850FaultRecordService
{
    private readonly MmsClientSession _session;

    public Iec61850FaultRecordService(MmsClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<Iec61850FaultRecordCatalog> DiscoverAsync(
        string? remoteDirectory = null,
        Iec61850FaultRecordDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new Iec61850FaultRecordDiscoveryOptions();
        ValidateDiscoveryOptions(options);

        var diagnostics = new List<string>();
        var entries = new List<MmsFileDirectoryEntry>();
        var visited = new List<string>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<DirectoryWorkItem>();

        EnqueueRoot(remoteDirectory);
        foreach (var root in options.AdditionalRootDirectories)
            EnqueueRoot(root);

        while (queue.Count > 0 &&
               visited.Count < options.MaximumDirectoryCount &&
               entries.Count < options.MaximumEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var work = queue.Dequeue();
            visited.Add(work.Directory);

            var pages = await _session.GetFileDirectoryPagedAsync(
                string.IsNullOrWhiteSpace(work.Directory) ? null : work.Directory,
                options.MaximumPagesPerDirectory,
                cancellationToken).ConfigureAwait(false);

            foreach (var page in pages)
            {
                if (!page.IsSuccess)
                {
                    diagnostics.Add($"{DisplayDirectory(work.Directory)}: {page.Message}");
                    break;
                }

                foreach (var entry in page.Entries)
                {
                    if (entries.Count >= options.MaximumEntries)
                        break;

                    entries.Add(entry);
                    if (!options.TraverseSubdirectories ||
                        work.Depth >= options.MaximumDirectoryDepth ||
                        !IsDirectoryCandidate(entry))
                    {
                        continue;
                    }

                    try
                    {
                        var child = Iec61850RemotePath.NormalizeDirectory(entry.Path);
                        if (queued.Add(child))
                            queue.Enqueue(new DirectoryWorkItem(child, work.Depth + 1));
                    }
                    catch (ArgumentException ex)
                    {
                        diagnostics.Add($"Skipped unsafe remote directory '{entry.Path}': {ex.Message}");
                    }
                }
            }

            if (!_session.IsMmsInitiated)
            {
                diagnostics.Add("MMS association closed while browsing the remote file store.");
                break;
            }
        }

        if (queue.Count > 0 && visited.Count >= options.MaximumDirectoryCount)
            diagnostics.Add($"Directory traversal stopped at the configured limit of {options.MaximumDirectoryCount}.");
        if (entries.Count >= options.MaximumEntries)
            diagnostics.Add($"File directory collection stopped at the configured limit of {options.MaximumEntries} entries.");

        return Iec61850FaultRecordCatalogBuilder.Build(entries, visited, diagnostics);

        void EnqueueRoot(string? root)
        {
            var normalized = Iec61850RemotePath.NormalizeDirectory(root);
            if (queued.Add(normalized))
                queue.Enqueue(new DirectoryWorkItem(normalized, 0));
        }
    }

    public async Task<Iec61850FaultRecordDownloadResult> DownloadAsync(
        Iec61850FaultRecordSet record,
        string destinationRoot,
        Iec61850FaultRecordDownloadOptions? options = null,
        IProgress<Iec61850FaultRecordDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        options ??= new Iec61850FaultRecordDownloadOptions();
        ValidateDownloadOptions(options);

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
        var expectedBytes = TryGetExpectedBytes(record);
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
                    var transfer = await _session.DownloadFileAsync(
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
                        throw new InvalidDataException($"{file.RemotePath}: {transfer.Message}");

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

    private static bool IsDirectoryCandidate(MmsFileDirectoryEntry entry)
        => entry.IsLikelyDirectory && (!entry.SizeBytes.HasValue || entry.SizeBytes.Value == 0);

    private static long? TryGetExpectedBytes(Iec61850FaultRecordSet record)
        => record.HasUnknownSize ? null : record.KnownSizeBytes;

    private static Iec61850FaultRecordDownloadResult Fail(Iec61850FaultRecordSet record, string message)
        => new()
        {
            IsSuccess = false,
            RecordId = record.RecordId,
            Message = message
        };

    private static string DisplayDirectory(string directory)
        => string.IsNullOrWhiteSpace(directory) ? "/" : directory;

    private static void ValidateDiscoveryOptions(Iec61850FaultRecordDiscoveryOptions options)
    {
        if (options.MaximumDirectoryDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumDirectoryDepth cannot be negative.");
        if (options.MaximumDirectoryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumDirectoryCount must be greater than zero.");
        if (options.MaximumEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumEntries must be greater than zero.");
        if (options.MaximumPagesPerDirectory <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumPagesPerDirectory must be greater than zero.");
    }

    private static void ValidateDownloadOptions(Iec61850FaultRecordDownloadOptions options)
    {
        if (options.MaximumTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTotalBytes must be greater than zero.");
        if (options.MaximumFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumFileBytes must be greater than zero.");
        if (options.MaximumReadOperationsPerFile <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumReadOperationsPerFile must be greater than zero.");
    }

    private readonly record struct DirectoryWorkItem(string Directory, int Depth);
}

internal static class Iec61850RemotePath
{
    public static string NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed is "/" or "*")
            return string.Empty;

        return NormalizeSegments(trimmed, allowEmpty: true);
    }

    public static string NormalizeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = NormalizeSegments(path.Trim().Replace('\\', '/'), allowEmpty: false);
        if (string.IsNullOrWhiteSpace(GetFileName(normalized)))
            throw new ArgumentException("Remote file path has no usable filename.", nameof(path));

        return normalized;
    }

    public static string GetFileName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    public static string GetDirectoryName(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? string.Empty : path[..index];
    }

    private static string NormalizeSegments(string path, bool allowEmpty)
    {
        if (path.Contains('\0'))
            throw new ArgumentException("Remote path contains a null character.", nameof(path));

        var segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Remote path contains a traversal segment.", nameof(path));

        var normalized = string.Join('/', segments);
        if (!allowEmpty && string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Remote path has no usable segment.", nameof(path));

        return normalized;
    }
}

internal static class Iec61850LocalPath
{
    public static string SanitizeFileName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "fault-record" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);

        foreach (var character in source)
        {
            builder.Append(character < ' ' || invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "fault-record" : sanitized;
    }

    public static string CombineUnderRoot(string root, string fileName)
    {
        var rootFullPath = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(rootFullPath, fileName));
        var rootPrefix = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("Resolved local file path escapes the destination directory.");

        return candidate;
    }

    public static string ResolveAvailableDirectory(string root, string preferredName)
    {
        var safeName = SanitizeFileName(preferredName);
        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var name = suffix == 0 ? safeName : $"{safeName}-{suffix}";
            var candidate = CombineUnderRoot(root, name);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not allocate an available fault-record destination directory.");
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class MmsGeneralizedTime
{
    private static readonly string[] UtcFormats =
    [
        "yyyyMMddHHmmss'Z'",
        "yyyyMMddHHmmss.FFFFFFF'Z'",
        "yyyyMMddHHmm'Z'",
        "yyyyMMddHHmm.FFFFFFF'Z'"
    ];

    public static DateTimeOffset? TryParse(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty)
            return null;

        var text = Encoding.ASCII.GetString(raw).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTimeOffset.TryParseExact(
                text,
                UtcFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var utc))
        {
            return utc;
        }

        var normalizedOffset = NormalizeOffset(text);
        if (DateTimeOffset.TryParse(
                normalizedOffset,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string NormalizeOffset(string value)
    {
        if (value.Length < 5)
            return value;

        var signIndex = Math.Max(value.LastIndexOf('+'), value.LastIndexOf('-'));
        if (signIndex < 0 || value.Length - signIndex != 5 || value[^3] == ':')
            return value;

        return value.Insert(value.Length - 2, ":");
    }
}

internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public InlineProgress(Action<T> report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void Report(T value)
        => _report(value);
}
