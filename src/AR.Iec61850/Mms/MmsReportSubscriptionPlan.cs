namespace AR.Iec61850.Mms;

public enum MmsReportSubscriptionPlanMode
{
    StaticDataSet,
    DynamicDataSet
}

public enum MmsReportSubscriptionPlanStatus
{
    ReadyReadOnly,
    ReadyRequiresWrite,
    Blocked,
    Incomplete
}

public sealed class MmsReportSubscriptionPlan
{
    public MmsReportSubscriptionPlanMode Mode { get; init; }
    public MmsReportSubscriptionPlanStatus Status { get; init; }
    public MmsReportControlCandidate? ReportControl { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<MmsDataSetDirectoryMember> Members { get; init; } = Array.Empty<MmsDataSetDirectoryMember>();
    public IReadOnlyList<MmsFcResolvedPoint> DynamicPoints { get; init; } = Array.Empty<MmsFcResolvedPoint>();
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public bool IsReady => Status is MmsReportSubscriptionPlanStatus.ReadyReadOnly or MmsReportSubscriptionPlanStatus.ReadyRequiresWrite;

    public string Summary
    {
        get
        {
            var rcb = ReportControl == null ? "-" : ReportControl.Reference;
            var dataset = string.IsNullOrWhiteSpace(DataSetReference) ? "-" : DataSetReference;
            return $"Report {Mode} plan: status={Status}, rcb={rcb}, dataset={dataset}, members={Members.Count}, dynamicPoints={DynamicPoints.Count}";
        }
    }
}

public static class MmsReportSubscriptionPlanner
{
    public static MmsReportSubscriptionPlan BuildStaticPlan(
        MmsReportInventory inventory,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        string? preferredRcbReference = null,
        string? preferredDataSetReference = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(dataSetDirectories);

        var readiness = MmsReportReadinessPlanner.Build(inventory);
        var candidates = readiness.Items
            .Where(x => x.Kind == MmsReportReadinessKind.ReadyStaticDataSet)
            .Select(x => x.ReportControl)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredRcbReference))
            candidates = candidates.Where(x => x.Reference.Equals(preferredRcbReference, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!string.IsNullOrWhiteSpace(preferredDataSetReference))
            candidates = candidates.Where(x => x.DataSetReference.Equals(preferredDataSetReference, StringComparison.OrdinalIgnoreCase)).ToArray();

        var selected = candidates
            .OrderByDescending(x => x.Buffered)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected == null)
        {
            return new MmsReportSubscriptionPlan
            {
                Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
                Status = MmsReportSubscriptionPlanStatus.Blocked,
                DataSetReference = preferredDataSetReference ?? string.Empty,
                Blockers = ["No ReadyStaticDataSet RCB matched the requested filter. Run mms-report-plan --only-safe first."],
                Steps = ["Keep the workflow read-only until at least one RCB has DatSet, RptEna=false, and no active reservation."]
            };
        }

        var dataSet = dataSetDirectories.FirstOrDefault(x => x.IsSuccess && x.DataSetReference.Equals(selected.DataSetReference, StringComparison.OrdinalIgnoreCase));
        var members = dataSet?.Members ?? Array.Empty<MmsDataSetDirectoryMember>();
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (members.Count == 0)
            blockers.Add($"DataSet directory for {selected.DataSetReference} is missing or empty; report values cannot be mapped safely.");

        if (!selected.Buffered)
            warnings.Add("Selected RCB is URCB. It is fine for online monitoring, but BRCB is preferred for buffered event recovery when available.");

        if (string.IsNullOrWhiteSpace(selected.OptionalFields))
            warnings.Add("OptFlds has not been decoded into named flags yet; first live enable should keep current IED settings.");

        return new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = blockers.Count == 0 ? MmsReportSubscriptionPlanStatus.ReadyRequiresWrite : MmsReportSubscriptionPlanStatus.Blocked,
            ReportControl = selected,
            DataSetReference = selected.DataSetReference,
            Members = members,
            Warnings = warnings,
            Blockers = blockers,
            Steps = BuildStaticSteps(selected, members)
        };
    }

    public static MmsReportSubscriptionPlan BuildDynamicPlan(
        MmsReportInventory inventory,
        MmsIedModelDirectory directory,
        IEnumerable<string> requestedPoints,
        string? preferredLogicalDevice = null,
        string? preferredRcbReference = null,
        string? dataSetName = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(requestedPoints);

        var points = requestedPoints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => MmsFcResolver.Resolve(directory, x.Trim()).BestCandidate)
            .Where(x => x != null)
            .Cast<MmsFcResolvedPoint>()
            .DistinctBy(x => x.MmsReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var blockers = new List<string>();
        var warnings = new List<string>();

        if (points.Length == 0)
            blockers.Add("No requested point could be resolved from the live IED directory.");

        var readiness = MmsReportReadinessPlanner.Build(inventory);
        var dynamicSlots = readiness.Items
            .Where(x => x.Kind == MmsReportReadinessKind.EmptyDynamicSlotNeedsDataSet)
            .Select(x => x.ReportControl)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredLogicalDevice))
            dynamicSlots = dynamicSlots.Where(x => x.Domain.Equals(preferredLogicalDevice, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!string.IsNullOrWhiteSpace(preferredRcbReference))
            dynamicSlots = dynamicSlots.Where(x => x.Reference.Equals(preferredRcbReference, StringComparison.OrdinalIgnoreCase)).ToArray();

        var firstPointDomain = points.FirstOrDefault()?.Domain ?? string.Empty;
        var selected = dynamicSlots
            .OrderByDescending(x => x.Buffered)
            .ThenBy(x => firstPointDomain.Length > 0 && x.Domain.Equals(firstPointDomain, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected == null)
            blockers.Add("No free dynamic RCB slot matched the requested filter.");

        var dsName = string.IsNullOrWhiteSpace(dataSetName) ? "AR_DYN_DS01" : SanitizeDataSetName(dataSetName);
        var dsReference = selected == null ? string.Empty : $"{selected.Domain}/LLN0.{dsName}";

        if (points.Any(x => x.FunctionalConstraint.Equals("CO", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("A dynamic report DataSet includes CO/control data. This is unusual for monitoring; verify the use case before creating the DataSet.");

        if (points.Length > 64)
            warnings.Add("Large dynamic DataSets can increase report payload size and report latency. Keep first tests small.");

        return new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.DynamicDataSet,
            Status = blockers.Count == 0 ? MmsReportSubscriptionPlanStatus.ReadyRequiresWrite : MmsReportSubscriptionPlanStatus.Blocked,
            ReportControl = selected,
            DataSetReference = dsReference,
            DynamicPoints = points,
            Members = points.Select(ToDirectoryMember).ToArray(),
            Warnings = warnings,
            Blockers = blockers,
            Steps = selected == null ? Array.Empty<string>() : BuildDynamicSteps(selected, dsReference, points)
        };
    }

    private static IReadOnlyList<string> BuildStaticSteps(MmsReportControlCandidate rcb, IReadOnlyList<MmsDataSetDirectoryMember> members)
    {
        var reserveStep = rcb.Buffered
            ? "Reserve selected BRCB with ResvTms when supported, otherwise keep existing free state."
            : "Reserve selected URCB with Resv=true when supported.";

        return
        [
            $"Use DataSet map {rcb.DataSetReference} with {members.Count} member(s) before enabling report.",
            $"Select RCB {rcb.Reference} ({rcb.Mode}) because DatSet is already assigned and RptEna=false.",
            reserveStep,
            "Install report receiver/dispatcher before enabling RptEna so unsolicited InformationReport is not lost.",
            "Write RptEna=true only after receiver is ready.",
            "Trigger GI=true after RptEna=true if GI is present in TrgOps/current RCB settings.",
            "Map each received report value by DataSet member index, not by guessed object name.",
            "On stop, write RptEna=false and release Resv/ResvTms if this client reserved the RCB."
        ];
    }

    private static IReadOnlyList<string> BuildDynamicSteps(MmsReportControlCandidate rcb, string dataSetReference, IReadOnlyList<MmsFcResolvedPoint> points)
    {
        return
        [
            $"Create dynamic DataSet {dataSetReference} with {points.Count} resolved member(s).",
            $"Write RCB.DatSet={dataSetReference} on free RCB {rcb.Reference}.",
            "Keep current OptFlds/TrgOps for first dynamic test unless the IED requires explicit configuration.",
            rcb.Buffered ? "Reserve BRCB with ResvTms when supported." : "Reserve URCB with Resv=true when supported.",
            "Install report receiver/dispatcher before enabling RptEna.",
            "Write RptEna=true, then write GI=true for first full refresh.",
            "On stop, write RptEna=false, release reservation, and delete dynamic DataSet only if it was created by this client and is deletable."
        ];
    }

    private static MmsDataSetDirectoryMember ToDirectoryMember(MmsFcResolvedPoint point)
        => new()
        {
            Domain = point.Domain,
            MmsItemName = point.MmsItemName,
            UserReference = point.UserReference,
            FunctionalConstraint = point.FunctionalConstraint,
            LogicalNode = point.LogicalNode,
            DataObjectPath = point.DataObjectPath,
            Source = point.Source,
            Confidence = point.Confidence
        };

    private static string SanitizeDataSetName(string name)
    {
        var text = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(text))
            return "AR_DYN_DS01";

        if (char.IsDigit(text[0]))
            text = "DS_" + text;

        return text.Length > 32 ? text[..32] : text;
    }
}
