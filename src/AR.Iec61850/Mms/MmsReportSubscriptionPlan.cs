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
    public MmsRcbSelectionEvidence RcbSelection { get; init; } = new();

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
        string? preferredDataSetReference = null,
        bool strictRcb = false,
        bool allowUrCbFallback = true,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(dataSetDirectories);

        var dataSetMap = dataSetDirectories
            .Where(x => x.IsSuccess && !string.IsNullOrWhiteSpace(x.DataSetReference))
            .GroupBy(x => NormalizeReference(x.DataSetReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var selection = MmsRcbPoolSelector.BuildStaticSelection(
            inventory,
            dataSetMap,
            preferredRcbReference,
            preferredDataSetReference,
            strictRcb,
            allowUrCbFallback,
            allowPollingFallback,
            excludedRcbReferences);

        var selected = MmsRcbPoolSelector.SelectReportControl(inventory, selection);
        var selectedDataSetKey = selected == null ? string.Empty : NormalizeReference(selected.DataSetReference);
        var dataSet = selected != null && dataSetMap.TryGetValue(selectedDataSetKey, out var mappedDataSet) ? mappedDataSet : null;
        if (selected == null)
        {
            var knownStatic = inventory.ReportControls.Count(x => !string.IsNullOrWhiteSpace(x.DataSetReference));
            var occupied = inventory.ReportControls.Count(IsExplicitlyEnabled);
            var reserved = inventory.ReportControls.Count(IsReservedByOtherClient);
            return new MmsReportSubscriptionPlan
            {
                Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
                Status = MmsReportSubscriptionPlanStatus.Blocked,
                DataSetReference = preferredDataSetReference ?? string.Empty,
                Blockers = new[]
                {
                    $"No static RCB with a usable DatSet matched the requested filter. Static RCB seen={knownStatic}, occupied={occupied}, reserved={reserved}.",
                    "Run mms-report-plan --max-report-probes 286 --raw-limit 0 and verify at least one RCB has DatSet plus RptEna=false/0."
                }.Concat(selection.Blockers).ToArray(),
                Warnings = selection.Warnings,
                Steps = ["Keep the workflow read-only until at least one RCB has DatSet, is not enabled, and is not actively reserved."],
                RcbSelection = selection
            };
        }

        var members = dataSet?.Members ?? Array.Empty<MmsDataSetDirectoryMember>();
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (members.Count == 0)
            blockers.Add($"DataSet directory for {selected.DataSetReference} is missing or empty; report values cannot be mapped safely.");

        if (!selected.Buffered)
            warnings.Add("Selected RCB is URCB. It is fine for online monitoring, but BRCB is preferred for buffered event recovery when available.");

        if (!IsExplicitlyDisabled(selected))
            warnings.Add("RptEna was not decoded as explicit false/0. The selector accepted this RCB only because it has a valid DataSet map and is not explicitly enabled/reserved. Verify with mms-report-plan before live write.");

        if (string.IsNullOrWhiteSpace(selected.OptionalFields))
            warnings.Add("OptFlds has not been decoded into named flags yet; first live enable should keep current IED settings.");

        return new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = blockers.Count == 0 ? MmsReportSubscriptionPlanStatus.ReadyRequiresWrite : MmsReportSubscriptionPlanStatus.Blocked,
            ReportControl = selected,
            DataSetReference = selected.DataSetReference,
            Members = members,
            Warnings = warnings.Concat(selection.Warnings).ToArray(),
            Blockers = blockers.Concat(selection.Blockers).ToArray(),
            Steps = BuildStaticSteps(selected, members),
            RcbSelection = selection
        };
    }

    public static MmsReportSubscriptionPlan BuildDynamicPlan(
        MmsReportInventory inventory,
        MmsIedModelDirectory directory,
        IEnumerable<string> requestedPoints,
        string? preferredLogicalDevice = null,
        string? preferredRcbReference = null,
        string? dataSetName = null,
        bool strictRcb = false,
        bool allowUrCbFallback = true,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null)
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

        var firstPointDomain = points.FirstOrDefault()?.Domain ?? string.Empty;
        var effectivePreferredLogicalDevice = string.IsNullOrWhiteSpace(preferredLogicalDevice) ? firstPointDomain : preferredLogicalDevice;
        var selection = MmsRcbPoolSelector.BuildDynamicSelection(
            inventory,
            effectivePreferredLogicalDevice,
            preferredRcbReference,
            strictRcb,
            allowUrCbFallback,
            allowPollingFallback,
            excludedRcbReferences);
        var selected = MmsRcbPoolSelector.SelectReportControl(inventory, selection);

        if (selected == null)
            blockers.Add("No free dynamic RCB slot matched the requested filter.");

        var dsName = string.IsNullOrWhiteSpace(dataSetName) ? CreateDefaultDynamicDataSetName() : SanitizeDataSetName(dataSetName);
        var dsLogicalNode = selected == null || string.IsNullOrWhiteSpace(selected.LogicalNode)
            ? "LLN0"
            : selected.LogicalNode;
        var dsReference = selected == null ? string.Empty : $"{selected.Domain}/{dsLogicalNode}.{dsName}";

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
            Warnings = warnings.Concat(selection.Warnings).ToArray(),
            Blockers = blockers.Concat(selection.Blockers).ToArray(),
            Steps = selected == null ? Array.Empty<string>() : BuildDynamicSteps(selected, dsReference, points),
            RcbSelection = selection
        };
    }

    private static IReadOnlyList<string> BuildStaticSteps(MmsReportControlCandidate rcb, IReadOnlyList<MmsDataSetDirectoryMember> members)
    {
        var reserveStep = rcb.Buffered
            ? "Do not pre-write BRCB ResvTms for first live tests; enable RptEna only after the receiver is ready."
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
            rcb.Buffered ? "Do not pre-write BRCB ResvTms for first live tests; enable RptEna after DatSet is configured." : "Reserve URCB with Resv=true when supported.",
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

    private static bool IsSameReference(string left, string right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static int StaticCandidateSafetyScore(MmsReportControlCandidate rcb)
    {
        var score = 0;
        if (IsExplicitlyDisabled(rcb))
            score += 30;
        if (!IsReservedByOtherClient(rcb))
            score += 20;
        if (rcb.Status.Contains("probe", StringComparison.OrdinalIgnoreCase) || rcb.Status.Contains("Attribute", StringComparison.OrdinalIgnoreCase))
            score += 10;
        if (!string.IsNullOrWhiteSpace(rcb.ReportId))
            score += 5;
        if (!string.IsNullOrWhiteSpace(rcb.ConfRev))
            score += 5;
        return score;
    }

    public static bool IsExplicitlyEnabled(MmsReportControlCandidate rcb)
        => ParseBool(rcb.EnabledState) == true;

    public static bool IsExplicitlyDisabled(MmsReportControlCandidate rcb)
        => ParseBool(rcb.EnabledState) == false;

    public static bool IsReservedByOtherClient(MmsReportControlCandidate rcb)
        => ParseBool(rcb.ReservationState) == true || ParsePositiveInteger(rcb.ReservationTimeSeconds) == true;

    public static bool HasExplicitSafeStaticWriteState(MmsReportControlCandidate rcb)
        => !string.IsNullOrWhiteSpace(rcb.DataSetReference) &&
           IsExplicitlyDisabled(rcb) &&
           !IsReservedByOtherClient(rcb);

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        if (bool.TryParse(text, out var parsed))
            return parsed;

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    private static bool? ParsePositiveInteger(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        return ulong.TryParse(text, out var number) ? number > 0 : null;
    }

    private static string CreateDefaultDynamicDataSetName()
        => "AR_DYN_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

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
