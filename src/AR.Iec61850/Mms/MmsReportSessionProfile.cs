namespace AR.Iec61850.Mms;

public sealed class MmsReportSessionProfile
{
    public string SchemaVersion { get; init; } = "mms-report-session-profile-v1";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string IedName { get; init; } = string.Empty;
    public MmsReportSubscriptionPlanMode Mode { get; init; }
    public MmsReportSubscriptionPlanStatus Status { get; init; }
    public string ReportControlReference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<MmsReportSessionMemberProfile> Members { get; init; } = Array.Empty<MmsReportSessionMemberProfile>();
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public bool TriggerGeneralInterrogation { get; init; } = true;
    public int ListenDurationSeconds { get; init; } = 60;
    public string Summary => $"{Mode} report profile: status={Status}, rcb={TextOrDash(ReportControlReference)}, dataset={TextOrDash(DataSetReference)}, members={Members.Count}";

    public static MmsReportSessionProfile FromPlan(
        MmsReportSubscriptionPlan plan,
        string host,
        int port = 102,
        string iedName = "",
        bool triggerGeneralInterrogation = true,
        int listenDurationSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new MmsReportSessionProfile
        {
            Host = host.Trim(),
            Port = port <= 0 ? 102 : port,
            IedName = iedName.Trim(),
            Mode = plan.Mode,
            Status = plan.Status,
            ReportControlReference = plan.ReportControl?.Reference ?? string.Empty,
            Buffered = plan.ReportControl?.Buffered ?? false,
            DataSetReference = plan.DataSetReference,
            Members = plan.Members.Select(MmsReportSessionMemberProfile.FromMember).ToArray(),
            Steps = plan.Steps.ToArray(),
            Warnings = plan.Warnings.ToArray(),
            Blockers = plan.Blockers.ToArray(),
            TriggerGeneralInterrogation = triggerGeneralInterrogation,
            ListenDurationSeconds = listenDurationSeconds <= 0 ? 60 : listenDurationSeconds
        };
    }

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}

public sealed class MmsReportSessionMemberProfile
{
    public int Index { get; init; }
    public string UserReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string DataObjectPath { get; init; } = string.Empty;
    public int Confidence { get; init; }

    public static MmsReportSessionMemberProfile FromMember(MmsDataSetDirectoryMember member, int index)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new MmsReportSessionMemberProfile
        {
            Index = index,
            UserReference = member.UserReference,
            FunctionalConstraint = member.FunctionalConstraint,
            MmsReference = member.MmsReference,
            LogicalNode = member.LogicalNode,
            DataObjectPath = member.DataObjectPath,
            Confidence = member.Confidence
        };
    }
}
