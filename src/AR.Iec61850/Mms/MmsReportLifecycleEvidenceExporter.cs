using System.Text.Json;

namespace AR.Iec61850.Mms;

public sealed class MmsSanitizedReportLifecycleEvidence
{
    public int SchemaVersion { get; init; } = 1;
    public string ReportControlKind { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string DataSetOwnership { get; init; } = string.Empty;
    public bool IsReserved { get; init; }
    public bool IsEnabled { get; init; }
    public bool GeneralInterrogationRequested { get; init; }
    public bool CleanupHasResidue { get; init; }
    public int FailureCount { get; init; }
    public int AcceptedReportCount { get; init; }
    public int DuplicateReportCount { get; init; }
    public int SequenceGapCount { get; init; }
    public int SequenceResetCount { get; init; }
    public int BufferOverflowCount { get; init; }
    public int TimestampRegressionCount { get; init; }
    public int GeneralInterrogationReportCount { get; init; }
    public int IntegrityReportCount { get; init; }
    public IReadOnlyList<MmsSanitizedReportLifecycleEvent> Events { get; init; } = Array.Empty<MmsSanitizedReportLifecycleEvent>();
}

public sealed class MmsSanitizedReportLifecycleEvent
{
    public long Sequence { get; init; }
    public string Phase { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
}

/// <summary>
/// Writes repeatable report-session evidence without IED names, IP addresses, object
/// references, EntryIDs, timestamps, raw payloads, or diagnostic free text. The export is
/// intentionally evidence-minimal so it can be attached to public regression reports
/// without leaking customer or live-network identifiers.
/// </summary>
public static class MmsReportLifecycleEvidenceExporter
{
    public static MmsSanitizedReportLifecycleEvidence Project(MmsReportLifecycleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MmsSanitizedReportLifecycleEvidence
        {
            ReportControlKind = snapshot.Kind.ToString(),
            Phase = snapshot.Phase.ToString(),
            DataSetOwnership = snapshot.DataSetOwnership.ToString(),
            IsReserved = snapshot.IsReserved,
            IsEnabled = snapshot.IsEnabled,
            GeneralInterrogationRequested = snapshot.GeneralInterrogationRequested,
            CleanupHasResidue = snapshot.CleanupHasResidue,
            FailureCount = snapshot.FailureCount,
            AcceptedReportCount = snapshot.Replay.AcceptedReportCount,
            DuplicateReportCount = snapshot.Replay.DuplicateReportCount,
            SequenceGapCount = snapshot.Replay.SequenceGapCount,
            SequenceResetCount = snapshot.Replay.SequenceResetCount,
            BufferOverflowCount = snapshot.Replay.BufferOverflowCount,
            TimestampRegressionCount = snapshot.Replay.TimestampRegressionCount,
            GeneralInterrogationReportCount = snapshot.Replay.GeneralInterrogationReportCount,
            IntegrityReportCount = snapshot.Replay.IntegrityReportCount,
            Events = snapshot.Events
                .OrderBy(x => x.Sequence)
                .Select(x => new MmsSanitizedReportLifecycleEvent
                {
                    Sequence = x.Sequence,
                    Phase = x.Phase.ToString(),
                    Kind = x.Kind.ToString(),
                    IsSuccess = x.IsSuccess
                })
                .ToArray()
        };
    }

    public static void WriteJson(MmsReportLifecycleSnapshot snapshot, TextWriter writer, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var evidence = Project(snapshot);
        writer.Write(JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = indented
        }));
    }
}
