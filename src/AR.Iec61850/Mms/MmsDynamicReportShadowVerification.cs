namespace AR.Iec61850.Mms;

/// <summary>
/// One report-authoritative observation captured during G2.6 shadow verification.
/// The DataSet index/member identity must be the exact identity already proven by
/// the qualification profile; this model intentionally contains no fuzzy mapping.
/// </summary>
public sealed record MmsDynamicReportShadowReportObservation
{
    public int DataSetIndex { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public DateTimeOffset? DeviceTimestampUtc { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public ulong? SequenceNumber { get; init; }
}

/// <summary>
/// One independent MMS read used only as the reference side of the G2.6 shadow.
/// </summary>
public sealed record MmsDynamicReportShadowPollObservation
{
    public int DataSetIndex { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public DateTimeOffset? DeviceTimestampUtc { get; init; }
    public DateTimeOffset ReadAtUtc { get; init; }
}

/// <summary>
/// Bounded physical shadow evidence. Reconnect and mutation counters are explicit
/// because production acceptance must prove that report recovery does not create a
/// repeated RCB/DataSet mutation loop.
/// </summary>
public sealed record MmsDynamicReportShadowVerificationEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MmsDynamicReportShadowReportObservation> ReportObservations { get; init; } = Array.Empty<MmsDynamicReportShadowReportObservation>();
    public IReadOnlyList<MmsDynamicReportShadowPollObservation> PollObservations { get; init; } = Array.Empty<MmsDynamicReportShadowPollObservation>();
    public int ReconnectAttempts { get; init; }
    public int SuccessfulReconnects { get; init; }
    public int ReportResubscriptionsAfterReconnect { get; init; }
    public int PollReferenceRecoveriesAfterReconnect { get; init; }
    public int DynamicActivationAttempts { get; init; }
}

public sealed record MmsDynamicReportShadowVerificationOptions
{
    public static MmsDynamicReportShadowVerificationOptions ProductionDefaults { get; } = new();

    public int MinimumReportEdges { get; init; } = 1;
    public TimeSpan MaximumReportToPollLag { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaximumPollTransitionToReportLag { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaximumDeviceTimestampDelta { get; init; } = TimeSpan.FromMilliseconds(250);
    public bool RequireQualityEvidence { get; init; }
    public bool RequireDeviceTimestampEvidence { get; init; }
    public bool RequireReconnectCycle { get; init; } = true;
    public int MaximumDynamicActivationAttemptsPerAssociation { get; init; } = 1;
}

public sealed record MmsDynamicReportShadowVerificationResult
{
    public bool IsSuccess { get; init; }
    public bool ExactMemberIdentityPassed { get; init; }
    public bool ValueParityPassed { get; init; }
    public bool QualityParityPassed { get; init; }
    public bool TimestampParityPassed { get; init; }
    public bool ReportOrderPassed { get; init; }
    public bool NoMissingReportEdgesPassed { get; init; }
    public bool NoDuplicateReportEdgesPassed { get; init; }
    public bool PollingAuthorityGuardPassed { get; init; }
    public bool ReconnectRegressionPassed { get; init; }
    public bool NoRepeatedMutationLoopPassed { get; init; }
    public int ReportObservationCount { get; init; }
    public int PollObservationCount { get; init; }
    public int PollTransitionCount { get; init; }
    public int MatchedReportToPollCount { get; init; }
    public int MatchedPollTransitionToReportCount { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Pure G2.6 acceptance evaluator. It does not perform network I/O and does not
/// mutate an RCB, DataSet, or qualification profile. Consumers collect physical
/// observations and pass them here for deterministic fail-closed evaluation.
/// </summary>
public static class MmsDynamicReportShadowVerificationPolicy
{
    public static MmsDynamicReportShadowVerificationResult Evaluate(
        MmsDynamicReportShadowVerificationEvidence evidence,
        MmsDynamicReportShadowVerificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        options ??= MmsDynamicReportShadowVerificationOptions.ProductionDefaults;
        ValidateOptions(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.EvidenceId);

        var failures = new List<string>();
        var members = evidence.MemberReferences
            .Select(NormalizeReference)
            .ToArray();

        var memberSetValid = members.Length > 0 &&
                             members.All(reference => reference.Length > 0) &&
                             members.Distinct(StringComparer.OrdinalIgnoreCase).Count() == members.Length;
        if (!memberSetValid)
            failures.Add("Shadow verification requires one non-empty, duplicate-free exact member sequence.");

        var reportIdentityPassed = memberSetValid && evidence.ReportObservations.All(observation =>
            IsExactMember(observation.DataSetIndex, observation.MemberReference, members));
        var pollIdentityPassed = memberSetValid && evidence.PollObservations.All(observation =>
            IsExactMember(observation.DataSetIndex, observation.MemberReference, members));
        var exactMemberIdentityPassed = reportIdentityPassed && pollIdentityPassed;
        if (!exactMemberIdentityPassed)
            failures.Add("At least one report/poll observation does not match the exact DataSet index/member identity.");

        var originalReportOrder = evidence.ReportObservations.ToArray();
        var reportOrderPassed = true;
        for (var index = 1; index < originalReportOrder.Length; index++)
        {
            if (originalReportOrder[index].ReceivedAtUtc < originalReportOrder[index - 1].ReceivedAtUtc)
            {
                reportOrderPassed = false;
                break;
            }

            var previousSequence = originalReportOrder[index - 1].SequenceNumber;
            var currentSequence = originalReportOrder[index].SequenceNumber;
            // Multiple included DataSet members from one InformationReport legitimately
            // share the same sequence number. Only sequence regression is invalid here;
            // same-sequence/same-index duplication is rejected separately below.
            if (previousSequence.HasValue && currentSequence.HasValue && currentSequence.Value < previousSequence.Value)
            {
                reportOrderPassed = false;
                break;
            }
        }
        if (!reportOrderPassed)
            failures.Add("Report receive/sequence order regressed inside the bounded shadow window.");

        var duplicateKeys = evidence.ReportObservations
            .Where(observation => observation.SequenceNumber.HasValue)
            .GroupBy(observation => $"{observation.SequenceNumber!.Value}:{observation.DataSetIndex}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var noDuplicateReportEdgesPassed = duplicateKeys.Length == 0;
        if (!noDuplicateReportEdgesPassed)
            failures.Add("Duplicate report edge(s) were observed for the same sequence number and DataSet index: " + string.Join(", ", duplicateKeys));

        var reports = evidence.ReportObservations
            .OrderBy(observation => observation.ReceivedAtUtc)
            .ToArray();
        var polls = evidence.PollObservations
            .OrderBy(observation => observation.ReadAtUtc)
            .ToArray();

        var matchedReportToPoll = 0;
        var valueParityPassed = true;
        var qualityParityPassed = true;
        var timestampParityPassed = true;
        var sawQualityPair = false;
        var sawTimestampPair = false;

        foreach (var report in reports)
        {
            var poll = polls.FirstOrDefault(candidate =>
                candidate.DataSetIndex == report.DataSetIndex &&
                candidate.ReadAtUtc >= report.ReceivedAtUtc &&
                candidate.ReadAtUtc - report.ReceivedAtUtc <= options.MaximumReportToPollLag);
            if (poll is null)
                continue;

            matchedReportToPoll++;
            if (!SameValue(report.Value, poll.Value))
                valueParityPassed = false;

            var reportQuality = NormalizeText(report.Quality);
            var pollQuality = NormalizeText(poll.Quality);
            if (reportQuality.Length > 0 || pollQuality.Length > 0)
            {
                sawQualityPair = true;
                if (!reportQuality.Equals(pollQuality, StringComparison.OrdinalIgnoreCase))
                    qualityParityPassed = false;
            }

            if (report.DeviceTimestampUtc.HasValue || poll.DeviceTimestampUtc.HasValue)
            {
                if (!report.DeviceTimestampUtc.HasValue || !poll.DeviceTimestampUtc.HasValue)
                {
                    timestampParityPassed = false;
                }
                else
                {
                    sawTimestampPair = true;
                    var delta = (report.DeviceTimestampUtc.Value - poll.DeviceTimestampUtc.Value).Duration();
                    if (delta > options.MaximumDeviceTimestampDelta)
                        timestampParityPassed = false;
                }
            }
        }

        if (options.RequireQualityEvidence && !sawQualityPair)
            qualityParityPassed = false;
        if (options.RequireDeviceTimestampEvidence && !sawTimestampPair)
            timestampParityPassed = false;

        if (!valueParityPassed)
            failures.Add("At least one report value disagrees with the independent MMS reference read.");
        if (!qualityParityPassed)
            failures.Add(options.RequireQualityEvidence && !sawQualityPair
                ? "Required quality evidence was not captured on both report and polling sides."
                : "At least one report quality disagrees with the independent MMS reference read.");
        if (!timestampParityPassed)
            failures.Add(options.RequireDeviceTimestampEvidence && !sawTimestampPair
                ? "Required device timestamp evidence was not captured on both report and polling sides."
                : "At least one report/poll device timestamp pair exceeds the configured tolerance or is one-sided.");

        var pollTransitions = BuildPollTransitions(polls);
        var matchedPollTransitions = 0;
        foreach (var transition in pollTransitions)
        {
            var matchingReport = reports.FirstOrDefault(report =>
                report.DataSetIndex == transition.Current.DataSetIndex &&
                SameValue(report.Value, transition.Current.Value) &&
                report.ReceivedAtUtc >= transition.Previous.ReadAtUtc &&
                report.ReceivedAtUtc <= transition.Current.ReadAtUtc + options.MaximumPollTransitionToReportLag);
            if (matchingReport is not null)
                matchedPollTransitions++;
        }

        var noMissingReportEdgesPassed = pollTransitions.Count == matchedPollTransitions;
        if (!noMissingReportEdgesPassed)
            failures.Add($"Independent MMS polling observed {pollTransitions.Count} value transition(s), but only {matchedPollTransitions} had a matching report edge in the bounded window.");

        var enoughReportEdges = reports.Length >= options.MinimumReportEdges;
        if (!enoughReportEdges)
            failures.Add($"Shadow verification captured {reports.Length} report edge(s); at least {options.MinimumReportEdges} are required.");

        var pollingAuthorityGuardPassed = polls.Length > 0 &&
                                          matchedReportToPoll == reports.Length &&
                                          noMissingReportEdgesPassed;
        if (!pollingAuthorityGuardPassed)
            failures.Add("Independent MMS polling did not remain an authoritative reference for every report edge.");

        var reconnectRegressionPassed = options.RequireReconnectCycle
            ? evidence.ReconnectAttempts > 0 &&
              evidence.SuccessfulReconnects == evidence.ReconnectAttempts &&
              evidence.ReportResubscriptionsAfterReconnect >= evidence.SuccessfulReconnects &&
              evidence.PollReferenceRecoveriesAfterReconnect >= evidence.SuccessfulReconnects
            : evidence.SuccessfulReconnects <= evidence.ReconnectAttempts;
        if (!reconnectRegressionPassed)
            failures.Add("Reconnect regression did not prove both report resubscription and independent polling-reference recovery.");

        var associationCount = 1 + Math.Max(0, evidence.SuccessfulReconnects);
        var allowedDynamicAttempts = checked(associationCount * options.MaximumDynamicActivationAttemptsPerAssociation);
        var noRepeatedMutationLoopPassed = evidence.DynamicActivationAttempts >= 0 &&
                                           evidence.DynamicActivationAttempts <= allowedDynamicAttempts;
        if (!noRepeatedMutationLoopPassed)
            failures.Add($"Dynamic activation attempts ({evidence.DynamicActivationAttempts}) exceed the bounded allowance ({allowedDynamicAttempts}); repeated mutation loop cannot be excluded.");

        var success = exactMemberIdentityPassed &&
                      enoughReportEdges &&
                      valueParityPassed &&
                      qualityParityPassed &&
                      timestampParityPassed &&
                      reportOrderPassed &&
                      noMissingReportEdgesPassed &&
                      noDuplicateReportEdgesPassed &&
                      pollingAuthorityGuardPassed &&
                      reconnectRegressionPassed &&
                      noRepeatedMutationLoopPassed;

        return new MmsDynamicReportShadowVerificationResult
        {
            IsSuccess = success,
            ExactMemberIdentityPassed = exactMemberIdentityPassed,
            ValueParityPassed = valueParityPassed,
            QualityParityPassed = qualityParityPassed,
            TimestampParityPassed = timestampParityPassed,
            ReportOrderPassed = reportOrderPassed,
            NoMissingReportEdgesPassed = noMissingReportEdgesPassed,
            NoDuplicateReportEdgesPassed = noDuplicateReportEdgesPassed,
            PollingAuthorityGuardPassed = pollingAuthorityGuardPassed,
            ReconnectRegressionPassed = reconnectRegressionPassed,
            NoRepeatedMutationLoopPassed = noRepeatedMutationLoopPassed,
            ReportObservationCount = reports.Length,
            PollObservationCount = polls.Length,
            PollTransitionCount = pollTransitions.Count,
            MatchedReportToPollCount = matchedReportToPoll,
            MatchedPollTransitionToReportCount = matchedPollTransitions,
            Failures = failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Summary = success
                ? $"G2.6 shadow PASS: reports={reports.Length}, polls={polls.Length}, pollTransitions={pollTransitions.Count}, reconnects={evidence.SuccessfulReconnects}, dynamicAttempts={evidence.DynamicActivationAttempts}."
                : $"G2.6 shadow FAIL: {string.Join(" | ", failures.Distinct(StringComparer.OrdinalIgnoreCase))}"
        };
    }

    /// <summary>
    /// Converts a successful typed shadow result into the existing production-acceptance
    /// contract. Control/static-report regressions remain explicit independent inputs.
    /// This helper does not modify a qualification profile.
    /// </summary>
    public static MmsDynamicReportProductionAcceptance BuildProductionAcceptance(
        MmsDynamicReportShadowVerificationEvidence evidence,
        MmsDynamicReportShadowVerificationResult shadow,
        bool controlRegressionPassed,
        bool staticReportingRegressionPassed)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(shadow);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.EvidenceId);
        if (!shadow.IsSuccess)
            throw new InvalidOperationException("A failed G2.6 shadow cannot be converted into production acceptance evidence.");

        return new MmsDynamicReportProductionAcceptance
        {
            FieldEvidenceId = evidence.EvidenceId.Trim(),
            ObservedAtUtc = evidence.ObservedAtUtc,
            ControlRegressionPassed = controlRegressionPassed,
            StaticReportingRegressionPassed = staticReportingRegressionPassed,
            DynamicInformationReportRegressionPassed = shadow.ExactMemberIdentityPassed &&
                                                       shadow.ValueParityPassed &&
                                                       shadow.ReportOrderPassed &&
                                                       shadow.NoMissingReportEdgesPassed &&
                                                       shadow.NoDuplicateReportEdgesPassed,
            PollingAuthorityGuardPassed = shadow.PollingAuthorityGuardPassed,
            ReconnectRegressionPassed = shadow.ReconnectRegressionPassed,
            QualityRegressionPassed = shadow.QualityParityPassed && shadow.TimestampParityPassed,
            NoRepeatedMutationLoopPassed = shadow.NoRepeatedMutationLoopPassed
        };
    }

    private static IReadOnlyList<PollTransition> BuildPollTransitions(
        IReadOnlyList<MmsDynamicReportShadowPollObservation> polls)
    {
        var transitions = new List<PollTransition>();
        foreach (var group in polls.GroupBy(observation => observation.DataSetIndex))
        {
            var ordered = group.OrderBy(observation => observation.ReadAtUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (!SameValue(ordered[index - 1].Value, ordered[index].Value))
                    transitions.Add(new PollTransition(ordered[index - 1], ordered[index]));
            }
        }
        return transitions;
    }

    private static bool IsExactMember(int index, string reference, IReadOnlyList<string> members)
        => index >= 0 &&
           index < members.Count &&
           NormalizeReference(reference).Equals(members[index], StringComparison.OrdinalIgnoreCase);

    private static bool SameValue(string left, string right)
        => NormalizeText(left).Equals(NormalizeText(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => NormalizeText(reference).Replace('$', '.');

    private static string NormalizeText(string? text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

    private static void ValidateOptions(MmsDynamicReportShadowVerificationOptions options)
    {
        if (options.MinimumReportEdges < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumReportEdges));
        if (options.MaximumReportToPollLag <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumReportToPollLag));
        if (options.MaximumPollTransitionToReportLag < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPollTransitionToReportLag));
        if (options.MaximumDeviceTimestampDelta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumDeviceTimestampDelta));
        if (options.MaximumDynamicActivationAttemptsPerAssociation < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumDynamicActivationAttemptsPerAssociation));
    }

    private sealed record PollTransition(
        MmsDynamicReportShadowPollObservation Previous,
        MmsDynamicReportShadowPollObservation Current);
}
