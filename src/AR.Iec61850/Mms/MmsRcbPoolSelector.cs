namespace AR.Iec61850.Mms;

public enum MmsRcbSelectionMode
{
    StaticDataSet,
    DynamicDataSet
}

public enum MmsRcbAvailabilityKind
{
    AvailableStatic,
    AvailableDynamicEmpty,
    BusyEnabled,
    BusyReserved,
    ContendedFlapping,
    ClaimCooldown,
    UnknownNeedsProbe,
    NotApplicable,
    NotUsable
}

public enum MmsRcbSelectionDecision
{
    Selected,
    Candidate,
    Skipped,
    BlockedPreferred,
    FilteredOut
}

public sealed class MmsRcbContentionProbeObservation
{
    public int ProbeNumber { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string RptEna { get; init; } = string.Empty;
    public string Resv { get; init; } = string.Empty;
    public string ResvTms { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string Summary =>
        $"probe#{ProbeNumber} RptEna={TextOrDash(RptEna)} Resv={TextOrDash(Resv)} ResvTms={TextOrDash(ResvTms)} DatSet={TextOrDash(DataSetReference)} ConfRev={TextOrDash(ConfRev)}";

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

public sealed class MmsRcbContentionProbeResult
{
    public string RcbReference { get; init; } = string.Empty;
    public bool IsContended { get; init; }
    public bool IsBusyAtProbe { get; init; }
    public bool IsFlapping { get; init; }
    public int CooldownSeconds { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public IReadOnlyList<MmsRcbContentionProbeObservation> Observations { get; init; } = Array.Empty<MmsRcbContentionProbeObservation>();

    public string Summary
    {
        get
        {
            var cooldown = CooldownSeconds > 0 ? $", cooldown={CooldownSeconds}s" : string.Empty;
            return $"RCB contention probe: rcb={RcbReference}, decision={Decision}, contended={IsContended.ToString().ToLowerInvariant()}, busy={IsBusyAtProbe.ToString().ToLowerInvariant()}, flapping={IsFlapping.ToString().ToLowerInvariant()}{cooldown} - {Reason}";
        }
    }
}

public sealed class MmsRcbCandidateEvaluation
{
    public string Reference { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string RptEna { get; init; } = string.Empty;
    public string Resv { get; init; } = string.Empty;
    public string ResvTms { get; init; } = string.Empty;
    public bool IsBuffered { get; init; }
    public bool IsPreferred { get; init; }
    public bool IsSameDataSet { get; init; }
    public bool IsSameLogicalDevice { get; init; }
    public bool HasDataSetDirectory { get; init; }
    public int Score { get; init; }
    public MmsRcbAvailabilityKind Availability { get; init; }
    public MmsRcbSelectionDecision Decision { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;

    public string Summary =>
        $"{Decision} {Mode} {Reference} score={Score} availability={Availability} reason={Reason}";
}

public sealed class MmsRcbSelectionEvidence
{
    public MmsRcbSelectionMode Mode { get; init; }
    public string PreferredRcbReference { get; init; } = string.Empty;
    public bool StrictRcb { get; init; }
    public bool AllowUrCbFallback { get; init; } = true;
    public bool AllowPollingFallback { get; init; } = true;
    public string RequestedDataSetReference { get; init; } = string.Empty;
    public string RequestedLogicalDevice { get; init; } = string.Empty;
    public string SelectedRcbReference { get; init; } = string.Empty;
    public bool FallbackUsed { get; init; }
    public IReadOnlyList<MmsRcbCandidateEvaluation> Candidates { get; init; } = Array.Empty<MmsRcbCandidateEvaluation>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public string Summary
    {
        get
        {
            var selected = string.IsNullOrWhiteSpace(SelectedRcbReference) ? "-" : SelectedRcbReference;
            var preferred = string.IsNullOrWhiteSpace(PreferredRcbReference) ? "-" : PreferredRcbReference;
            var fallback = FallbackUsed ? "yes" : "no";
            return $"RCB selection: mode={Mode}, selected={selected}, preferred={preferred}, strict={StrictRcb.ToString().ToLowerInvariant()}, fallbackUsed={fallback}, candidates={Candidates.Count}";
        }
    }
}

public static class MmsRcbPoolSelector
{
    public static MmsRcbSelectionEvidence BuildStaticSelection(
        MmsReportInventory inventory,
        IReadOnlyDictionary<string, MmsDataSetDirectoryResult> dataSetMap,
        string? preferredRcbReference = null,
        string? preferredDataSetReference = null,
        bool strictRcb = false,
        bool allowUrCbFallback = true,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(dataSetMap);

        var excluded = BuildExcludedSet(excludedRcbReferences);
        var preferredRcb = NormalizeReference(preferredRcbReference);
        var requestedDataSet = NormalizeReference(preferredDataSetReference);
        if (string.IsNullOrWhiteSpace(requestedDataSet) && !string.IsNullOrWhiteSpace(preferredRcb))
        {
            var preferred = inventory.ReportControls.FirstOrDefault(x => IsSameReference(x.Reference, preferredRcbReference));
            if (preferred != null && !string.IsNullOrWhiteSpace(preferred.DataSetReference))
                requestedDataSet = NormalizeReference(preferred.DataSetReference);
        }

        var evaluations = inventory.ReportControls
            .Select(rcb => EvaluateStatic(rcb, dataSetMap, preferredRcb, requestedDataSet, strictRcb, allowUrCbFallback, excluded))
            .OrderBy(x => x.Decision == MmsRcbSelectionDecision.FilteredOut ? 1 : 0)
            .ThenByDescending(x => IsSelectable(x))
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.IsBuffered)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = evaluations.FirstOrDefault(IsSelectable);
        if (selected != null)
        {
            evaluations = evaluations
                .Select(x => WithDecision(x, IsSameReference(x.Reference, selected.Reference)
                    ? MmsRcbSelectionDecision.Selected
                    : x.Decision == MmsRcbSelectionDecision.FilteredOut
                        ? MmsRcbSelectionDecision.FilteredOut
                        : IsSelectable(x)
                            ? MmsRcbSelectionDecision.Candidate
                            : MmsRcbSelectionDecision.Skipped))
                .ToList();
        }

        var warnings = new List<string>();
        var blockers = new List<string>();
        if (selected == null)
        {
            blockers.Add(strictRcb && !string.IsNullOrWhiteSpace(preferredRcbReference)
                ? $"Strict RCB selection blocked the session because {preferredRcbReference} is not available for static reporting."
                : "No available static RCB matched the requested DataSet/filter.");

            if (allowPollingFallback)
                warnings.Add("No safe RCB was selected. Smart polling fallback is allowed by policy, but report monitor will remain blocked until polling fallback mode is implemented by the caller.");
        }
        else if (!string.IsNullOrWhiteSpace(preferredRcbReference) && !IsSameReference(selected.Reference, preferredRcbReference))
        {
            warnings.Add($"Preferred RCB {preferredRcbReference} was not selected; smart fallback selected {selected.Reference} to avoid an unsafe/busy RCB.");
        }

        return new MmsRcbSelectionEvidence
        {
            Mode = MmsRcbSelectionMode.StaticDataSet,
            PreferredRcbReference = preferredRcbReference ?? string.Empty,
            StrictRcb = strictRcb,
            AllowUrCbFallback = allowUrCbFallback,
            AllowPollingFallback = allowPollingFallback,
            RequestedDataSetReference = preferredDataSetReference ?? string.Empty,
            SelectedRcbReference = selected?.Reference ?? string.Empty,
            FallbackUsed = !string.IsNullOrWhiteSpace(preferredRcbReference) && selected != null && !IsSameReference(selected.Reference, preferredRcbReference),
            Candidates = evaluations,
            Warnings = warnings,
            Blockers = blockers
        };
    }

    public static MmsRcbSelectionEvidence BuildDynamicSelection(
        MmsReportInventory inventory,
        string? preferredLogicalDevice = null,
        string? preferredRcbReference = null,
        bool strictRcb = false,
        bool allowUrCbFallback = true,
        bool allowPollingFallback = true,
        IReadOnlySet<string>? excludedRcbReferences = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var excluded = BuildExcludedSet(excludedRcbReferences);
        var preferredRcb = NormalizeReference(preferredRcbReference);
        var requestedLd = preferredLogicalDevice?.Trim() ?? string.Empty;
        var evaluations = inventory.ReportControls
            .Select(rcb => EvaluateDynamic(rcb, requestedLd, preferredRcb, strictRcb, allowUrCbFallback, excluded))
            .OrderBy(x => x.Decision == MmsRcbSelectionDecision.FilteredOut ? 1 : 0)
            .ThenByDescending(x => IsSelectable(x))
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.IsBuffered)
            .ThenBy(x => x.IsSameLogicalDevice ? 0 : 1)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = evaluations.FirstOrDefault(IsSelectable);
        if (selected != null)
        {
            evaluations = evaluations
                .Select(x => WithDecision(x, IsSameReference(x.Reference, selected.Reference)
                    ? MmsRcbSelectionDecision.Selected
                    : x.Decision == MmsRcbSelectionDecision.FilteredOut
                        ? MmsRcbSelectionDecision.FilteredOut
                        : IsSelectable(x)
                            ? MmsRcbSelectionDecision.Candidate
                            : MmsRcbSelectionDecision.Skipped))
                .ToList();
        }

        var warnings = new List<string>();
        var blockers = new List<string>();
        if (selected == null)
        {
            blockers.Add(strictRcb && !string.IsNullOrWhiteSpace(preferredRcbReference)
                ? $"Strict RCB selection blocked the session because {preferredRcbReference} is not an available empty dynamic slot."
                : "No available empty dynamic RCB slot matched the requested filter.");

            if (allowPollingFallback)
                warnings.Add("No dynamic RCB slot was selected. Smart polling fallback is allowed by policy, but dynamic reporting remains blocked until the caller chooses polling fallback.");
        }
        else if (!string.IsNullOrWhiteSpace(preferredRcbReference) && !IsSameReference(selected.Reference, preferredRcbReference))
        {
            warnings.Add($"Preferred RCB {preferredRcbReference} was not selected; smart fallback selected {selected.Reference} to avoid an unsafe/busy RCB.");
        }

        return new MmsRcbSelectionEvidence
        {
            Mode = MmsRcbSelectionMode.DynamicDataSet,
            PreferredRcbReference = preferredRcbReference ?? string.Empty,
            StrictRcb = strictRcb,
            AllowUrCbFallback = allowUrCbFallback,
            AllowPollingFallback = allowPollingFallback,
            RequestedLogicalDevice = preferredLogicalDevice ?? string.Empty,
            SelectedRcbReference = selected?.Reference ?? string.Empty,
            FallbackUsed = !string.IsNullOrWhiteSpace(preferredRcbReference) && selected != null && !IsSameReference(selected.Reference, preferredRcbReference),
            Candidates = evaluations,
            Warnings = warnings,
            Blockers = blockers
        };
    }

    public static MmsReportControlCandidate? SelectReportControl(MmsReportInventory inventory, MmsRcbSelectionEvidence selection)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(selection);

        if (string.IsNullOrWhiteSpace(selection.SelectedRcbReference))
            return null;

        return inventory.ReportControls.FirstOrDefault(x => IsSameReference(x.Reference, selection.SelectedRcbReference));
    }

    private static MmsRcbCandidateEvaluation EvaluateStatic(
        MmsReportControlCandidate rcb,
        IReadOnlyDictionary<string, MmsDataSetDirectoryResult> dataSetMap,
        string preferredRcb,
        string requestedDataSet,
        bool strictRcb,
        bool allowUrCbFallback,
        IReadOnlySet<string> excludedRcbReferences)
    {
        var isPreferred = !string.IsNullOrWhiteSpace(preferredRcb) && IsSameReference(rcb.Reference, preferredRcb);
        var excludedByPreviousClaim = excludedRcbReferences.Contains(NormalizeReference(rcb.Reference));
        var normalizedDataSet = NormalizeReference(rcb.DataSetReference);
        var requestedMatches = string.IsNullOrWhiteSpace(requestedDataSet) || IsSameReference(rcb.DataSetReference, requestedDataSet);
        var hasDataSet = !string.IsNullOrWhiteSpace(normalizedDataSet);
        var hasDataSetDirectory = hasDataSet && dataSetMap.TryGetValue(normalizedDataSet, out var directory) && directory.IsSuccess && directory.Members.Count > 0;
        var availability = Classify(rcb, requireEmptyDataSet: false, requireDataSet: true);
        var filteredOut = strictRcb && !string.IsNullOrWhiteSpace(preferredRcb) && !isPreferred;
        if (!allowUrCbFallback && !rcb.Buffered)
            filteredOut = true;
        if (!requestedMatches)
            filteredOut = true;
        if (excludedByPreviousClaim)
            filteredOut = true;

        var selectable = !filteredOut && availability == MmsRcbAvailabilityKind.AvailableStatic && hasDataSetDirectory;
        var score = 0;
        if (isPreferred)
            score += 500;
        if (requestedMatches)
            score += 120;
        if (hasDataSetDirectory)
            score += 100;
        if (rcb.Buffered)
            score += 40;
        if (rcb.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase))
            score += 15;
        if (IsExplicitlyDisabled(rcb))
            score += 25;
        if (IsReservationFree(rcb))
            score += 25;
        if (!string.IsNullOrWhiteSpace(rcb.ConfRev))
            score += 10;
        if (!string.IsNullOrWhiteSpace(rcb.ReportId))
            score += 10;
        if (availability == MmsRcbAvailabilityKind.BusyEnabled || availability == MmsRcbAvailabilityKind.BusyReserved)
            score -= 1000;
        if (!hasDataSetDirectory)
            score -= 250;

        return CreateEvaluation(
            rcb,
            isPreferred,
            requestedMatches,
            true,
            hasDataSetDirectory,
            score,
            availability,
            filteredOut ? MmsRcbSelectionDecision.FilteredOut : selectable ? MmsRcbSelectionDecision.Candidate : MmsRcbSelectionDecision.Skipped,
            BuildReason(rcb, availability, hasDataSetDirectory, requestedMatches, strictRcb, filteredOut, excludedByPreviousClaim, staticMode: true),
            selectable ? "Safe static report candidate." : "Do not claim this RCB automatically.");
    }

    private static MmsRcbCandidateEvaluation EvaluateDynamic(
        MmsReportControlCandidate rcb,
        string requestedLogicalDevice,
        string preferredRcb,
        bool strictRcb,
        bool allowUrCbFallback,
        IReadOnlySet<string> excludedRcbReferences)
    {
        var isPreferred = !string.IsNullOrWhiteSpace(preferredRcb) && IsSameReference(rcb.Reference, preferredRcb);
        var excludedByPreviousClaim = excludedRcbReferences.Contains(NormalizeReference(rcb.Reference));
        var sameLogicalDevice = string.IsNullOrWhiteSpace(requestedLogicalDevice) || rcb.Domain.Equals(requestedLogicalDevice, StringComparison.OrdinalIgnoreCase);
        var availability = Classify(rcb, requireEmptyDataSet: true, requireDataSet: false);
        var filteredOut = strictRcb && !string.IsNullOrWhiteSpace(preferredRcb) && !isPreferred;
        if (!allowUrCbFallback && !rcb.Buffered)
            filteredOut = true;
        if (excludedByPreviousClaim)
            filteredOut = true;

        var selectable = !filteredOut && availability == MmsRcbAvailabilityKind.AvailableDynamicEmpty;
        var score = 0;
        if (isPreferred)
            score += 500;
        if (string.IsNullOrWhiteSpace(rcb.DataSetReference) && rcb.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded)
            score += 140;
        if (sameLogicalDevice)
            score += 100;
        if (rcb.Buffered)
            score += 40;
        if (rcb.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase))
            score += 15;
        if (IsExplicitlyDisabled(rcb))
            score += 25;
        if (IsReservationFree(rcb))
            score += 25;
        if (!string.IsNullOrWhiteSpace(rcb.ConfRev))
            score += 10;
        if (availability == MmsRcbAvailabilityKind.BusyEnabled || availability == MmsRcbAvailabilityKind.BusyReserved)
            score -= 1000;
        if (!string.IsNullOrWhiteSpace(rcb.DataSetReference))
            score -= 600;

        return CreateEvaluation(
            rcb,
            isPreferred,
            string.IsNullOrWhiteSpace(rcb.DataSetReference) && rcb.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded,
            sameLogicalDevice,
            false,
            score,
            availability,
            filteredOut ? MmsRcbSelectionDecision.FilteredOut : selectable ? MmsRcbSelectionDecision.Candidate : MmsRcbSelectionDecision.Skipped,
            BuildReason(rcb, availability, false, sameLogicalDevice, strictRcb, filteredOut, excludedByPreviousClaim, staticMode: false),
            selectable ? "Safe dynamic empty-slot candidate." : "Do not bind a dynamic DataSet to this RCB automatically.");
    }

    private static MmsRcbAvailabilityKind Classify(MmsReportControlCandidate rcb, bool requireEmptyDataSet, bool requireDataSet)
    {
        if (IsExplicitlyEnabled(rcb))
            return MmsRcbAvailabilityKind.BusyEnabled;

        if (IsReservedByOtherClient(rcb))
            return MmsRcbAvailabilityKind.BusyReserved;

        var hasDataSet = !string.IsNullOrWhiteSpace(rcb.DataSetReference);
        var rptEna = ParseBool(rcb.EnabledState);
        var wasProbed = WasProbed(rcb);
        var dataSetProbeSucceeded = rcb.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded;
        var dataSetProbeFailed = rcb.DataSetProbeState == MmsRcbDataSetProbeState.ReadFailed;

        // Never turn an unread/unproven blank DatSet into a dynamic empty slot. Likewise,
        // a current DatSet read failure must prevent automatic static claim even if a stale
        // reference from earlier discovery is still available as diagnostic evidence.
        if (requireEmptyDataSet && !hasDataSet && !dataSetProbeSucceeded)
            return MmsRcbAvailabilityKind.UnknownNeedsProbe;

        if (requireDataSet && dataSetProbeFailed)
            return MmsRcbAvailabilityKind.UnknownNeedsProbe;

        if (requireDataSet && hasDataSet && rptEna != true && wasProbed)
            return MmsRcbAvailabilityKind.AvailableStatic;

        if (requireEmptyDataSet && !hasDataSet && dataSetProbeSucceeded && rptEna != true && wasProbed)
            return MmsRcbAvailabilityKind.AvailableDynamicEmpty;

        if (!wasProbed || rptEna == null)
            return MmsRcbAvailabilityKind.UnknownNeedsProbe;

        if (requireDataSet && !hasDataSet)
            return dataSetProbeSucceeded
                ? MmsRcbAvailabilityKind.NotApplicable
                : MmsRcbAvailabilityKind.UnknownNeedsProbe;

        if (requireEmptyDataSet && hasDataSet)
            return MmsRcbAvailabilityKind.NotApplicable;

        return MmsRcbAvailabilityKind.NotUsable;
    }

    private static MmsRcbCandidateEvaluation CreateEvaluation(
        MmsReportControlCandidate rcb,
        bool isPreferred,
        bool isSameDataSet,
        bool isSameLogicalDevice,
        bool hasDataSetDirectory,
        int score,
        MmsRcbAvailabilityKind availability,
        MmsRcbSelectionDecision decision,
        string reason,
        string action)
        => new()
        {
            Reference = rcb.Reference,
            Mode = rcb.Mode,
            Domain = rcb.Domain,
            LogicalNode = rcb.LogicalNode,
            Name = rcb.Name,
            DataSetReference = rcb.DataSetReference,
            ReportId = rcb.ReportId,
            ConfRev = rcb.ConfRev,
            RptEna = rcb.EnabledState,
            Resv = rcb.ReservationState,
            ResvTms = rcb.ReservationTimeSeconds,
            IsBuffered = rcb.Buffered,
            IsPreferred = isPreferred,
            IsSameDataSet = isSameDataSet,
            IsSameLogicalDevice = isSameLogicalDevice,
            HasDataSetDirectory = hasDataSetDirectory,
            Score = score,
            Availability = availability,
            Decision = decision,
            Reason = reason,
            RecommendedAction = action
        };

    private static MmsRcbCandidateEvaluation WithDecision(MmsRcbCandidateEvaluation item, MmsRcbSelectionDecision decision)
        => new()
        {
            Reference = item.Reference,
            Mode = item.Mode,
            Domain = item.Domain,
            LogicalNode = item.LogicalNode,
            Name = item.Name,
            DataSetReference = item.DataSetReference,
            ReportId = item.ReportId,
            ConfRev = item.ConfRev,
            RptEna = item.RptEna,
            Resv = item.Resv,
            ResvTms = item.ResvTms,
            IsBuffered = item.IsBuffered,
            IsPreferred = item.IsPreferred,
            IsSameDataSet = item.IsSameDataSet,
            IsSameLogicalDevice = item.IsSameLogicalDevice,
            HasDataSetDirectory = item.HasDataSetDirectory,
            Score = item.Score,
            Availability = item.Availability,
            Decision = decision,
            Reason = item.Reason,
            RecommendedAction = decision == MmsRcbSelectionDecision.Selected ? "Selected by Smart RCB policy." : item.RecommendedAction
        };

    private static bool IsSelectable(MmsRcbCandidateEvaluation item)
        => item.Decision == MmsRcbSelectionDecision.Candidate || item.Decision == MmsRcbSelectionDecision.Selected;

    private static string BuildReason(
        MmsReportControlCandidate rcb,
        MmsRcbAvailabilityKind availability,
        bool hasDataSetDirectory,
        bool requestedMatches,
        bool strictRcb,
        bool filteredOut,
        bool excludedByPreviousClaim,
        bool staticMode)
    {
        if (excludedByPreviousClaim)
            return "Excluded after a previous claim/write failure or pre-claim contention/cooldown in this command; trying the next Smart RCB candidate.";

        if (filteredOut)
            return strictRcb ? "Filtered out by strict preferred RCB policy." : "Filtered out by user policy/filter.";

        return availability switch
        {
            MmsRcbAvailabilityKind.AvailableStatic when !hasDataSetDirectory => "Static RCB is free, but DataSet directory is missing/empty; value mapping would be unsafe.",
            MmsRcbAvailabilityKind.AvailableStatic => "Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable.",
            MmsRcbAvailabilityKind.AvailableDynamicEmpty => "Dynamic slot has a live-verified empty DatSet, RptEna=false, and no active reservation.",
            MmsRcbAvailabilityKind.BusyEnabled => "RptEna=true; another client or previous session appears to own this RCB. Do not disable automatically.",
            MmsRcbAvailabilityKind.BusyReserved => rcb.Buffered
                ? $"BRCB ResvTms={TextOrDash(rcb.ReservationTimeSeconds)} before claim; treat as reserved/busy."
                : $"URCB Resv={TextOrDash(rcb.ReservationState)} before claim; treat as reserved/busy.",
            MmsRcbAvailabilityKind.ContendedFlapping => "RCB state flips across probes; treat as contended/flapping and do not claim automatically.",
            MmsRcbAvailabilityKind.ClaimCooldown => "RCB is in command-local claim cooldown after contention/write rejection; do not claim automatically.",
            MmsRcbAvailabilityKind.UnknownNeedsProbe when rcb.DataSetProbeState == MmsRcbDataSetProbeState.ReadFailed => "Live DatSet binding read failed; do not infer either a populated static binding or an empty dynamic slot.",
            MmsRcbAvailabilityKind.UnknownNeedsProbe when string.IsNullOrWhiteSpace(rcb.DataSetReference) => "DatSet binding absence is not positively verified; probe DatSet before selecting this RCB automatically.",
            MmsRcbAvailabilityKind.UnknownNeedsProbe => "RCB runtime state is not explicit; probe attributes before selecting automatically.",
            MmsRcbAvailabilityKind.NotApplicable => staticMode ? "Live DatSet was verified empty, so this RCB is not a static report candidate." : "RCB already has a DatSet, so it is not an empty dynamic slot.",
            _ => requestedMatches ? "RCB state is incomplete or not safe for automatic claim." : "RCB does not match the requested scope."
        };
    }

    private static bool WasProbed(MmsReportControlCandidate rcb)
        => rcb.Status.Contains("probe", StringComparison.OrdinalIgnoreCase) ||
           rcb.Status.Contains("Attribute", StringComparison.OrdinalIgnoreCase) ||
           !string.IsNullOrWhiteSpace(rcb.EnabledState) ||
           !string.IsNullOrWhiteSpace(rcb.ReportId) ||
           !string.IsNullOrWhiteSpace(rcb.ConfRev);

    private static bool IsExplicitlyEnabled(MmsReportControlCandidate rcb)
        => ParseBool(rcb.EnabledState) == true;

    private static bool IsExplicitlyDisabled(MmsReportControlCandidate rcb)
        => ParseBool(rcb.EnabledState) == false;

    private static bool IsReservationFree(MmsReportControlCandidate rcb)
        => ParseBool(rcb.ReservationState) != true && ParsePositiveInteger(rcb.ReservationTimeSeconds) != true;

    private static bool IsReservedByOtherClient(MmsReportControlCandidate rcb)
        => ParseBool(rcb.ReservationState) == true || ParsePositiveInteger(rcb.ReservationTimeSeconds) == true;

    private static HashSet<string> BuildExcludedSet(IReadOnlySet<string>? excludedRcbReferences)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludedRcbReferences == null)
            return set;

        foreach (var reference in excludedRcbReferences)
        {
            var normalized = NormalizeReference(reference);
            if (!string.IsNullOrWhiteSpace(normalized))
                set.Add(normalized);
        }

        return set;
    }

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

    private static bool IsSameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
