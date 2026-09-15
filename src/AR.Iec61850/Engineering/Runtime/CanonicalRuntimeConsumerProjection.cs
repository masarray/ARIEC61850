using System.Globalization;
using System.Text;

namespace AR.Iec61850.Engineering.Runtime;

/// <summary>
/// Bounded consumer facade over one canonical model generation plus its live value plane.
/// Consumers page rows instead of materializing a second signal graph.
/// </summary>
public static class CanonicalRuntimeConsumerProjection
{
    public const int MaximumUiPageSize = 1_000;
    public const int MaximumCliPageSize = 5_000;
    public const int MaximumExporterPageSize = CanonicalSignalQueryIndex.MaximumPageSize;

    public static CanonicalRuntimeQueryResult ForUi(
        CanonicalRuntimeValuePlane plane,
        string referencePrefix = "",
        string functionalConstraint = "",
        int offset = 0,
        int limit = 500)
        => Query(plane, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, limit), MaximumUiPageSize));

    public static CanonicalRuntimeQueryResult ForCli(
        CanonicalRuntimeValuePlane plane,
        string referencePrefix = "",
        string functionalConstraint = "",
        int offset = 0,
        int limit = 2_000)
        => Query(plane, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, limit), MaximumCliPageSize));

    public static CanonicalRuntimeQueryResult ForExporter(
        CanonicalRuntimeValuePlane plane,
        int offset,
        int pageSize = MaximumExporterPageSize,
        string referencePrefix = "",
        string functionalConstraint = "")
        => Query(plane, referencePrefix, functionalConstraint, offset, Math.Min(Math.Max(1, pageSize), MaximumExporterPageSize));

    public static IEnumerable<CanonicalRuntimeSignalProjection> EnumerateForExporter(
        CanonicalRuntimeValuePlane plane,
        int pageSize = MaximumExporterPageSize,
        string referencePrefix = "",
        string functionalConstraint = "")
    {
        ArgumentNullException.ThrowIfNull(plane);
        var offset = 0;
        while (true)
        {
            var page = ForExporter(plane, offset, pageSize, referencePrefix, functionalConstraint);
            foreach (var row in page.Rows)
                yield return row;

            if (!page.HasMore || page.Rows.Count == 0)
                yield break;

            offset += page.Rows.Count;
        }
    }

    private static CanonicalRuntimeQueryResult Query(
        CanonicalRuntimeValuePlane plane,
        string referencePrefix,
        string functionalConstraint,
        int offset,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(plane);
        return plane.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = referencePrefix,
            FunctionalConstraint = functionalConstraint,
            Offset = Math.Max(0, offset),
            Limit = limit
        });
    }
}

public sealed class CanonicalRuntimeExportResult
{
    public long ModelGeneration { get; init; }
    public long StartValueGeneration { get; init; }
    public long EndValueGeneration { get; init; }
    public int RowCount { get; init; }
    public bool ValuesChangedDuringExport => StartValueGeneration != EndValueGeneration;

    public string Summary => ValuesChangedDuringExport
        ? $"Canonical runtime export wrote {RowCount} row(s) from model generation {ModelGeneration}; live values advanced from generation {StartValueGeneration} to {EndValueGeneration} during export."
        : $"Canonical runtime export wrote {RowCount} row(s) from model generation {ModelGeneration} at stable value generation {StartValueGeneration}.";
}

/// <summary>
/// Streams a canonical model/value projection page-by-page. Model identity is pinned by
/// the value plane. Runtime values are intentionally live; start/end value generations
/// are recorded so an exporter can prove whether values changed while the file was being
/// written instead of silently pretending to have an immutable value snapshot.
/// </summary>
public static class CanonicalRuntimeCsvExporter
{
    public static CanonicalRuntimeExportResult Write(
        CanonicalRuntimeValuePlane plane,
        TextWriter writer,
        int pageSize = CanonicalRuntimeConsumerProjection.MaximumExporterPageSize,
        string referencePrefix = "",
        string functionalConstraint = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(writer);

        var boundedPageSize = Math.Min(
            Math.Max(1, pageSize),
            CanonicalRuntimeConsumerProjection.MaximumExporterPageSize);
        var startValueGeneration = plane.ValueGeneration;
        var rowCount = 0;
        var offset = 0;

        writer.WriteLine("SignalId,Reference,FC,BasicType,MmsType,CDC,Value,Quality,Timestamp,Reason,Source,ValueVersion,UpdatedAtUtc");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = CanonicalRuntimeConsumerProjection.ForExporter(
                plane,
                offset,
                boundedPageSize,
                referencePrefix,
                functionalConstraint);

            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(string.Join(",",
                    row.SignalId.ToString(CultureInfo.InvariantCulture),
                    Csv(row.Reference),
                    Csv(row.FunctionalConstraint),
                    Csv(row.BasicType),
                    Csv(row.MmsType),
                    Csv(row.Cdc),
                    Csv(row.HasValue ? row.Value : string.Empty),
                    Csv(row.HasQuality ? row.Quality : string.Empty),
                    Csv(row.HasTimestamp ? row.Timestamp : string.Empty),
                    Csv(row.HasReason ? row.Reason : string.Empty),
                    Csv(row.Source),
                    row.ValueVersion.ToString(CultureInfo.InvariantCulture),
                    Csv(row.UpdatedAtUtc == default ? string.Empty : row.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture))));
                rowCount++;
            }

            if (!page.HasMore || page.Rows.Count == 0)
                break;

            offset += page.Rows.Count;
        }

        return new CanonicalRuntimeExportResult
        {
            ModelGeneration = plane.ModelGeneration,
            StartValueGeneration = startValueGeneration,
            EndValueGeneration = plane.ValueGeneration,
            RowCount = rowCount
        };
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return text;

        var builder = new StringBuilder(text.Length + 4);
        builder.Append('"');
        foreach (var ch in text)
        {
            if (ch == '"')
                builder.Append("\"\"");
            else
                builder.Append(ch);
        }
        builder.Append('"');
        return builder.ToString();
    }
}
