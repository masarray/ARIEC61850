using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AR.Iec61850.SampledValues.Field;

public enum SvSupportBundlePrivacyMode
{
    FullEvidence,
    CaptureExcerpt,
    Anonymized,
    MetadataOnly
}

public sealed record SvSupportBundleManifest
{
    public const string CurrentSchemaVersion = "ariec61850.sv-support-bundle/v1";
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset GeneratedAt { get; init; }
    public string Application { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string ApplicationCommit { get; init; } = string.Empty;
    public string EngineCommit { get; init; } = string.Empty;
    public string CaptureSource { get; init; } = string.Empty;
    public string SclSha256 { get; init; } = string.Empty;
    public SvSupportBundlePrivacyMode PrivacyMode { get; init; }
    public IReadOnlyList<SvSupportBundleFile> Files { get; init; } = Array.Empty<SvSupportBundleFile>();
}

public sealed record SvSupportBundleFile(
    string Path,
    long Length,
    string Sha256,
    string Purpose);

public sealed record SvSupportBundleContent(
    string Path,
    ReadOnlyMemory<byte> Content,
    string Purpose);

public static class SvSupportBundleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static void Write(
        string zipPath,
        SvSupportBundleManifest manifest,
        IEnumerable<SvSupportBundleContent> contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(contents);
        if (manifest.SchemaVersion != SvSupportBundleManifest.CurrentSchemaVersion || manifest.GeneratedAt == default)
            throw new InvalidOperationException("Support bundle manifest is incomplete or uses an unsupported schema.");

        var materialized = contents.ToArray();
        ValidatePaths(materialized.Select(item => item.Path));
        var files = materialized.Select(item => new SvSupportBundleFile(
            item.Path,
            item.Content.Length,
            Convert.ToHexString(SHA256.HashData(item.Content.Span)).ToLowerInvariant(),
            item.Purpose)).ToArray();
        var completedManifest = manifest with { Files = files };

        var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var item in materialized)
        {
            var entry = archive.CreateEntry(item.Path.Replace('\\', '/'), CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(item.Content.Span);
        }

        var manifestJson = JsonSerializer.Serialize(completedManifest, JsonOptions);
        WriteText(archive, "manifest.json", manifestJson);
        var checksums = string.Join(Environment.NewLine, files.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"{file.Sha256}  {file.Path.Replace('\\', '/') }")) + Environment.NewLine;
        WriteText(archive, "sha256sums.txt", checksums);
    }

    public static SvSupportBundleContent Text(string path, string content, string purpose)
        => new(path, Encoding.UTF8.GetBytes(content ?? string.Empty), purpose);

    private static void ValidatePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
                throw new InvalidOperationException($"Unsafe support-bundle path '{path}'.");
            if (normalized is "manifest.json" or "sha256sums.txt" || !seen.Add(normalized))
                throw new InvalidOperationException($"Duplicate or reserved support-bundle path '{normalized}'.");
        }
    }

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
