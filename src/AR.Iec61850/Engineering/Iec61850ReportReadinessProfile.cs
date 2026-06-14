using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Engineering;

public sealed class Iec61850ReportReadinessProfileOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool ProbeReportAttributes { get; init; } = true;
    public int MaxReportAttributeProbes { get; init; } = 286;
    public bool ReadDataSetDirectories { get; init; } = true;
    public int MaxDataSetDirectories { get; init; } = 64;
    public string PreferredRcbReference { get; init; } = string.Empty;
    public string PreferredDataSetReference { get; init; } = string.Empty;
    public bool StrictRcb { get; init; }
    public bool AllowUrCbFallback { get; init; } = true;
    public bool AllowPollingFallback { get; init; } = true;
    public bool TriggerGeneralInterrogation { get; init; } = true;
    public int ListenDurationSeconds { get; init; } = 60;
}

public enum Iec61850ReportCandidateSafety
{
    Preferred,
    Usable,
    Attention,
    Blocked
}

public sealed class Iec61850ReportCandidateAssessment
{
    public int Rank { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string Readiness { get; init; } = string.Empty;
    public Iec61850ReportCandidateSafety Safety { get; init; } = Iec61850ReportCandidateSafety.Attention;
    public string EnabledState { get; init; } = string.Empty;
    public string ReservationState { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;

    public bool CanBeSelectedAutomatically => Safety is Iec61850ReportCandidateSafety.Preferred or Iec61850ReportCandidateSafety.Usable;

    public string Summary =>
        $"#{Rank} {Mode} {Reference}: safety={Safety}, readiness={Readiness}, dataset={TextOrDash(DataSetReference)}, rptEna={TextOrDash(EnabledState)}, resv={TextOrDash(ReservationState)}";

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}

public sealed class Iec61850ReportReadinessProfile
{
    public string SchemaVersion { get; init; } = "ariec61850-report-readiness-profile-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string DiscoverySummary { get; init; } = string.Empty;
    public MmsReportSubscriptionPlan StaticPlan { get; init; } = new();
    public MmsReportSessionProfile? SessionProfile { get; init; }
    public IReadOnlyList<Iec61850ReportCandidateAssessment> Candidates { get; init; } = Array.Empty<Iec61850ReportCandidateAssessment>();
    public IReadOnlyList<Iec61850DiagnosticMessage> Diagnostics { get; init; } = Array.Empty<Iec61850DiagnosticMessage>();
    public IReadOnlyList<Iec61850DiagnosticMessage> AcceptanceGates { get; init; } = Array.Empty<Iec61850DiagnosticMessage>();
    public int DataSetCount { get; init; }
    public int DataSetDirectorySuccessCount { get; init; }
    public int DataSetMemberCount { get; init; }
    public int ReportControlCount { get; init; }
    public int SafeCandidateCount { get; init; }
    public string PreferredRcbReference { get; init; } = string.Empty;
    public string PreferredDataSetReference { get; init; } = string.Empty;

    public bool IsReadyForGuardedLiveSession =>
        StaticPlan.IsReady &&
        StaticPlan.ReportControl != null &&
        StaticPlan.Members.Count > 0 &&
        !AcceptanceGates.Any(x => x.Severity == Iec61850DiagnosticSeverity.Error);

    public string Summary =>
        $"Report readiness profile: ready={IsReadyForGuardedLiveSession.ToString().ToLowerInvariant()}, " +
        $"rcb={TextOrDash(StaticPlan.ReportControl?.Reference ?? string.Empty)}, " +
        $"dataset={TextOrDash(StaticPlan.DataSetReference)}, members={StaticPlan.Members.Count}, " +
        $"candidates={ReportControlCount}, safe={SafeCandidateCount}.";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ARIEC61850 Report Readiness Profile");
        sb.AppendLine();
        sb.AppendLine($"Generated UTC: `{GeneratedAtUtc:O}`");
        sb.AppendLine($"Endpoint: `{Host}:{Port}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- {Summary}");
        if (!string.IsNullOrWhiteSpace(DiscoverySummary))
            sb.AppendLine($"- Discovery: {DiscoverySummary}");
        if (!string.IsNullOrWhiteSpace(PreferredRcbReference))
            sb.AppendLine($"- Preferred RCB: `{PreferredRcbReference}`");
        if (!string.IsNullOrWhiteSpace(PreferredDataSetReference))
            sb.AppendLine($"- Preferred DataSet: `{PreferredDataSetReference}`");
        sb.AppendLine($"- DataSets: discovered={DataSetCount}, directoryReadOk={DataSetDirectorySuccessCount}, members={DataSetMemberCount}");
        sb.AppendLine();
        sb.AppendLine("## Acceptance gates");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Message | Recommendation |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var gate in AcceptanceGates)
            sb.AppendLine($"| {gate.Severity} | {Escape(gate.Code)} | {Escape(gate.Message)} | {Escape(gate.Recommendation)} |");
        sb.AppendLine();
        sb.AppendLine("## Selected static report plan");
        sb.AppendLine();
        sb.AppendLine($"- {StaticPlan.Summary}");
        if (SessionProfile != null)
            sb.AppendLine($"- Session profile: `{SessionProfile.Summary}`");
        if (StaticPlan.Blockers.Count > 0)
        {
            sb.AppendLine("- Blockers:");
            foreach (var blocker in StaticPlan.Blockers)
                sb.AppendLine($"  - {blocker}");
        }
        if (StaticPlan.Warnings.Count > 0)
        {
            sb.AppendLine("- Warnings:");
            foreach (var warning in StaticPlan.Warnings)
                sb.AppendLine($"  - {warning}");
        }
        if (StaticPlan.Steps.Count > 0)
        {
            sb.AppendLine("- Execution steps:");
            foreach (var step in StaticPlan.Steps)
                sb.AppendLine($"  - {step}");
        }
        sb.AppendLine();
        sb.AppendLine("## RCB candidate matrix");
        sb.AppendLine();
        sb.AppendLine("| Rank | Mode | Reference | DataSet | Safety | Readiness | RptEna | Reservation | ConfRev | Reason | Action |");
        sb.AppendLine("| ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var candidate in Candidates)
        {
            sb.AppendLine($"| {candidate.Rank} | {Escape(candidate.Mode)} | {Escape(candidate.Reference)} | {Escape(candidate.DataSetReference)} | {candidate.Safety} | {Escape(candidate.Readiness)} | {Escape(candidate.EnabledState)} | {Escape(candidate.ReservationState)} | {Escape(candidate.ConfRev)} | {Escape(candidate.Reason)} | {Escape(candidate.RecommendedAction)} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Diagnostics");
        sb.AppendLine();
        if (Diagnostics.Count == 0)
        {
            sb.AppendLine("No diagnostic findings were generated.");
        }
        else
        {
            sb.AppendLine("| Severity | Code | Reference | Message | Recommendation |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var diagnostic in Diagnostics)
                sb.AppendLine($"| {diagnostic.Severity} | {Escape(diagnostic.Code)} | {Escape(diagnostic.Reference)} | {Escape(diagnostic.Message)} | {Escape(diagnostic.Recommendation)} |");
        }

        return sb.ToString();
    }

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

public static class Iec61850ReportReadinessProfileBuilder
{
    public static Iec61850ReportReadinessProfile BuildStatic(
        MmsDiscoveryResult discovery,
        IEnumerable<MmsDataSetDirectoryResult>? dataSetDirectories = null,
        Iec61850ReportReadinessProfileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        options ??= new Iec61850ReportReadinessProfileOptions();
        var directories = (dataSetDirectories ?? Array.Empty<MmsDataSetDirectoryResult>()).ToArray();
        var readiness = MmsReportReadinessPlanner.Build(discovery.ReportInventory);
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(
            discovery.ReportInventory,
            directories,
            NullIfBlank(options.PreferredRcbReference),
            NullIfBlank(options.PreferredDataSetReference),
            options.StrictRcb,
            options.AllowUrCbFallback,
            options.AllowPollingFallback);

        var sessionProfile = MmsReportSessionProfile.FromPlan(
            plan,
            options.Host,
            options.Port,
            triggerGeneralInterrogation: options.TriggerGeneralInterrogation,
            listenDurationSeconds: options.ListenDurationSeconds);

        var candidates = readiness.Items.Select((item, index) => ToCandidate(index + 1, item, plan)).ToArray();
        var gates = BuildAcceptanceGates(discovery, directories, plan).ToArray();
        var diagnostics = BuildDiagnostics(discovery, directories, plan, readiness, gates).ToArray();

        return new Iec61850ReportReadinessProfile
        {
            Host = options.Host,
            Port = options.Port <= 0 ? 102 : options.Port,
            DiscoverySummary = discovery.Summary,
            StaticPlan = plan,
            SessionProfile = sessionProfile,
            Candidates = candidates,
            Diagnostics = diagnostics,
            AcceptanceGates = gates,
            DataSetCount = discovery.ReportInventory.DataSets.Count,
            DataSetDirectorySuccessCount = directories.Count(x => x.IsSuccess),
            DataSetMemberCount = directories.Where(x => x.IsSuccess).Sum(x => x.Members.Count),
            ReportControlCount = discovery.ReportInventory.ReportControls.Count,
            SafeCandidateCount = readiness.SafeCandidates.Count,
            PreferredRcbReference = options.PreferredRcbReference,
            PreferredDataSetReference = options.PreferredDataSetReference
        };
    }

    private static IEnumerable<Iec61850DiagnosticMessage> BuildAcceptanceGates(
        MmsDiscoveryResult discovery,
        IReadOnlyList<MmsDataSetDirectoryResult> directories,
        MmsReportSubscriptionPlan plan)
    {
        yield return Gate(
            discovery.IedDirectory.PointCount > 0 ? Iec61850DiagnosticSeverity.Info : Iec61850DiagnosticSeverity.Error,
            "MODEL_DISCOVERY_GATE",
            discovery.IedDirectory.PointCount > 0
                ? $"Live model has {discovery.IedDirectory.PointCount} resolved FC point(s)."
                : "Live model discovery did not resolve any FC point.",
            discovery.IedDirectory.PointCount > 0
                ? "Use the model snapshot as the report value context."
                : "Fix model discovery before running report tests.");

        yield return Gate(
            directories.Any(x => x.IsSuccess && x.Members.Count > 0) ? Iec61850DiagnosticSeverity.Info : Iec61850DiagnosticSeverity.Error,
            "DATASET_DIRECTORY_GATE",
            directories.Any(x => x.IsSuccess && x.Members.Count > 0)
                ? $"At least one DataSet directory was decoded with member evidence; total members={directories.Where(x => x.IsSuccess).Sum(x => x.Members.Count)}."
                : "No DataSet directory with members is available for report value mapping.",
            directories.Any(x => x.IsSuccess && x.Members.Count > 0)
                ? "Map received report values by DataSet member index."
                : "Read DataSet directories before enabling any RCB.");

        yield return Gate(
            plan.ReportControl != null ? Iec61850DiagnosticSeverity.Info : Iec61850DiagnosticSeverity.Error,
            "RCB_SELECTION_GATE",
            plan.ReportControl != null
                ? $"Selected {plan.ReportControl.Mode} {plan.ReportControl.Reference}."
                : "No RCB was selected by the static report planner.",
            plan.ReportControl != null
                ? "Use the selected RCB only through a guarded session."
                : "Probe RCB attributes and choose another RCB/DataSet filter.");

        yield return Gate(
            plan.Members.Count > 0 ? Iec61850DiagnosticSeverity.Info : Iec61850DiagnosticSeverity.Error,
            "MEMBER_MAP_GATE",
            plan.Members.Count > 0
                ? $"Selected DataSet map has {plan.Members.Count} member(s)."
                : "Selected DataSet map is empty or unavailable.",
            plan.Members.Count > 0
                ? "Use this map as the canonical report payload order."
                : "Do not enable report until the member map is decoded.");

        yield return Gate(
            plan.Status == MmsReportSubscriptionPlanStatus.ReadyRequiresWrite ? Iec61850DiagnosticSeverity.Warning : Iec61850DiagnosticSeverity.Error,
            "LIVE_WRITE_GATE",
            plan.Status == MmsReportSubscriptionPlanStatus.ReadyRequiresWrite
                ? "The plan is ready for a guarded live session, but RptEna/GI writes are still required."
                : $"The static report plan is not ready. Status={plan.Status}.",
            plan.Status == MmsReportSubscriptionPlanStatus.ReadyRequiresWrite
                ? "Start receiver first, then enable RptEna/GI only when the caller explicitly confirms live writes."
                : "Fix blockers before attempting any live write.");
    }

    private static IEnumerable<Iec61850DiagnosticMessage> BuildDiagnostics(
        MmsDiscoveryResult discovery,
        IReadOnlyList<MmsDataSetDirectoryResult> directories,
        MmsReportSubscriptionPlan plan,
        MmsReportReadinessPlan readiness,
        IReadOnlyList<Iec61850DiagnosticMessage> gates)
    {
        if (plan.IsReady)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Info,
                "REPORT_STATIC_PROFILE_READY",
                plan.ReportControl?.Reference ?? string.Empty,
                $"Static report profile is ready with DataSet {plan.DataSetReference} and {plan.Members.Count} member(s).",
                "Run guarded report monitor with explicit confirmation and evidence export.");
        }
        else
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Error,
                "REPORT_STATIC_PROFILE_BLOCKED",
                plan.ReportControl?.Reference ?? string.Empty,
                "Static report profile is blocked or incomplete.",
                plan.Blockers.FirstOrDefault() ?? "Inspect DataSet directories, RCB availability, and selection filters.");
        }

        foreach (var blocker in plan.Blockers)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Error,
                "REPORT_PLAN_BLOCKER",
                plan.ReportControl?.Reference ?? string.Empty,
                blocker,
                "Resolve this blocker before live RptEna/GI writes.");
        }

        foreach (var warning in plan.Warnings)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "REPORT_PLAN_WARNING",
                plan.ReportControl?.Reference ?? string.Empty,
                warning,
                "Review before first guarded live session.");
        }

        var occupied = readiness.Items.Count(x => x.Kind == MmsReportReadinessKind.OccupiedEnabled);
        if (occupied > 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "RCB_OCCUPIED_COUNT",
                string.Empty,
                $"{occupied} RCB candidate(s) already have RptEna=true.",
                "Do not steal active RCBs; use a free indexed instance or coordinate with the existing client.");
        }

        var reserved = readiness.Items.Count(x => x.Kind == MmsReportReadinessKind.ReservedByOtherClient);
        if (reserved > 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "RCB_RESERVED_COUNT",
                string.Empty,
                $"{reserved} RCB candidate(s) appear reserved.",
                "Prefer a non-reserved RCB or wait for reservation timeout.");
        }

        if (directories.Count > 0 && directories.All(x => !x.IsSuccess))
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Error,
                "DATASET_DIRECTORY_ALL_FAILED",
                string.Empty,
                "All requested DataSet directory reads failed.",
                "Fix GetNamedVariableListAttributes handling before report runtime tests.");
        }

        foreach (var gate in gates.Where(x => x.Severity == Iec61850DiagnosticSeverity.Error))
            yield return Diagnostic(gate.Severity, gate.Code, gate.Reference, gate.Message, gate.Recommendation);
    }

    private static Iec61850ReportCandidateAssessment ToCandidate(int rank, MmsReportReadiness item, MmsReportSubscriptionPlan selectedPlan)
    {
        var rcb = item.ReportControl;
        var isSelected = selectedPlan.ReportControl != null && SameReference(selectedPlan.ReportControl.Reference, rcb.Reference);
        var safety = item.Kind switch
        {
            MmsReportReadinessKind.ReadyStaticDataSet when isSelected => Iec61850ReportCandidateSafety.Preferred,
            MmsReportReadinessKind.ReadyStaticDataSet => Iec61850ReportCandidateSafety.Usable,
            MmsReportReadinessKind.ReadyDynamicSlot => Iec61850ReportCandidateSafety.Attention,
            MmsReportReadinessKind.NeedsAttributeProbe => Iec61850ReportCandidateSafety.Attention,
            _ => Iec61850ReportCandidateSafety.Blocked
        };

        var reservation = rcb.Buffered ? rcb.ReservationTimeSeconds : rcb.ReservationState;
        return new Iec61850ReportCandidateAssessment
        {
            Rank = rank,
            Reference = rcb.Reference,
            Mode = rcb.Mode,
            DataSetReference = rcb.DataSetReference,
            Readiness = item.Kind.ToString(),
            Safety = safety,
            EnabledState = rcb.EnabledState,
            ReservationState = reservation,
            ConfRev = rcb.ConfRev,
            TriggerOptions = rcb.TriggerOptions,
            OptionalFields = rcb.OptionalFields,
            Reason = item.Reason,
            RecommendedAction = item.RecommendedAction
        };
    }

    private static Iec61850DiagnosticMessage Gate(Iec61850DiagnosticSeverity severity, string code, string message, string recommendation)
        => Diagnostic(severity, code, string.Empty, message, recommendation);

    private static Iec61850DiagnosticMessage Diagnostic(
        Iec61850DiagnosticSeverity severity,
        string code,
        string reference,
        string message,
        string recommendation)
        => new()
        {
            Severity = severity,
            Code = code,
            Reference = reference,
            Message = message,
            Recommendation = recommendation
        };

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool SameReference(string left, string right)
        => Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
