using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Diagnostics.Binding;

public sealed class ExpectedObservedBindingProfileBuilder
{
    public ExpectedObservedBindingProfile Build(SclEngineeringProfile expectedProfile, IReadOnlyCollection<ProcessBusStreamSummary> observedSummaries, string sourceName = "")
    {
        if (expectedProfile is null)
            throw new ArgumentNullException(nameof(expectedProfile));

        observedSummaries ??= Array.Empty<ProcessBusStreamSummary>();
        var findings = new List<ExpectedObservedFinding>();
        var usedObserved = new HashSet<ProcessBusStreamSummary>();

        var gooseBindings = expectedProfile.ProcessBus.GooseStreams
            .Select(expected => BuildGooseBinding(expected, observedSummaries, usedObserved, findings))
            .ToList();

        var svBindings = expectedProfile.ProcessBus.SampledValuesStreams
            .Select(expected => BuildSampledValuesBinding(expected, observedSummaries, usedObserved, findings))
            .ToList();

        var unexpected = observedSummaries
            .Where(summary => summary.Kind is ProcessBusEventKind.Goose or ProcessBusEventKind.SampledValues)
            .Where(summary => !usedObserved.Contains(summary))
            .Select(summary =>
            {
                findings.Add(Finding("Warning", "PB_UNEXPECTED_OBSERVED_STREAM", summary.StreamId,
                    $"Observed {summary.Kind} stream was not expected by the SCL engineering profile: APPID=0x{summary.AppId:X4}, dst={summary.Destination}, confRev={summary.ConfigurationRevision?.ToString() ?? "-"}."));

                return new UnexpectedObservedProcessBusStream
                {
                    Kind = summary.Kind,
                    StreamId = summary.StreamId,
                    AppId = summary.AppId,
                    SourceMac = summary.Source,
                    DestinationMac = summary.Destination,
                    VlanId = summary.VlanId,
                    VlanPriority = summary.VlanPriority,
                    ConfigurationRevision = summary.ConfigurationRevision,
                    PacketCount = summary.PacketCount
                };
            })
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.AppId)
            .ThenBy(s => s.StreamId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedFindings = findings
            .OrderByDescending(f => SeverityRank(f.Severity))
            .ThenBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExpectedObservedBindingProfile
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? expectedProfile.SourceName : sourceName,
            ExpectedGooseCount = expectedProfile.GooseStreamCount,
            ObservedGooseCount = observedSummaries.Count(s => s.Kind == ProcessBusEventKind.Goose),
            BoundGooseCount = gooseBindings.Count(b => b.MatchKind != ProcessBusBindingMatchKind.Missing),
            ExpectedSampledValuesCount = expectedProfile.SampledValuesStreamCount,
            ObservedSampledValuesCount = observedSummaries.Count(s => s.Kind == ProcessBusEventKind.SampledValues),
            BoundSampledValuesCount = svBindings.Count(b => b.MatchKind != ProcessBusBindingMatchKind.Missing),
            Goose = gooseBindings,
            SampledValues = svBindings,
            UnexpectedObservedStreams = unexpected,
            Findings = orderedFindings
        };
    }

    private static ExpectedObservedGooseBinding BuildGooseBinding(
        SclGooseStream expected,
        IReadOnlyCollection<ProcessBusStreamSummary> observedSummaries,
        ISet<ProcessBusStreamSummary> usedObserved,
        ICollection<ExpectedObservedFinding> globalFindings)
    {
        var observed = FindBestGooseMatch(expected, observedSummaries.Where(s => !usedObserved.Contains(s)).ToList());
        var findings = new List<ExpectedObservedFinding>();
        var match = ClassifyMatch(expected, observed, isGoose: true);

        if (observed is null)
        {
            findings.Add(Finding("High", "PB_GOOSE_EXPECTED_MISSING", expected.ControlBlockReference,
                $"Expected GOOSE stream was not observed: {expected.ControlBlockReference}, APPID={FormatAppId(expected.Address.AppId)}, dst={Dash(expected.Address.DestinationMacText)}."));
        }
        else
        {
            usedObserved.Add(observed);
            AddCommonFindings(expected.ControlBlockReference, expected.Address.AppId, expected.Address.DestinationMacText, expected.Address.VlanId, expected.Address.VlanPriority, expected.ConfigurationRevision, expected.Entries.Count, observed, findings, "GOOSE");

            if (observed.GooseSequenceGapCount > 0)
                findings.Add(Finding("Warning", "PB_GOOSE_SEQUENCE_GAP", expected.ControlBlockReference, $"Observed {observed.GooseSequenceGapCount} GOOSE sequence jump(s)."));
            if (observed.GooseSequenceRegressionCount + observed.GooseStateRegressionCount > 0)
                findings.Add(Finding("High", "PB_GOOSE_SEQUENCE_REGRESSION", expected.ControlBlockReference, $"Observed {observed.GooseSequenceRegressionCount + observed.GooseStateRegressionCount} GOOSE state/sequence regression(s)."));
            if (observed.GooseTimeoutCount > 0)
                findings.Add(Finding("Warning", "PB_GOOSE_SUPERVISION_TIMEOUT", expected.ControlBlockReference, $"Observed {observed.GooseTimeoutCount} supervision timeout(s)."));
            if (observed.GooseDuplicateCount > 0)
                findings.Add(Finding("Info", "PB_GOOSE_DUPLICATE", expected.ControlBlockReference, $"Observed {observed.GooseDuplicateCount} duplicate GOOSE frame(s)."));
            foreach (var diagnostic in observed.LastDiagnostics)
                findings.Add(Finding("Warning", "PB_GOOSE_DIAGNOSTIC", expected.ControlBlockReference, diagnostic));
        }

        AddRange(globalFindings, findings);
        return new ExpectedObservedGooseBinding
        {
            ExpectedControlBlockReference = expected.ControlBlockReference,
            ExpectedDataSetReference = expected.DataSetReference,
            ExpectedStreamId = expected.ControlBlockReference,
            ExpectedAppId = expected.Address.AppId,
            ExpectedDestinationMac = expected.Address.DestinationMacText,
            ExpectedVlanId = expected.Address.VlanId,
            ExpectedVlanPriority = expected.Address.VlanPriority,
            ExpectedConfigurationRevision = expected.ConfigurationRevision,
            ExpectedDataSetMemberCount = expected.Entries.Count,
            MatchKind = match,
            ObservedStreamId = observed?.StreamId ?? string.Empty,
            ObservedAppId = observed?.AppId,
            ObservedDestinationMac = observed?.Destination ?? string.Empty,
            ObservedVlanId = observed?.VlanId,
            ObservedVlanPriority = observed?.VlanPriority,
            ObservedConfigurationRevision = observed?.ConfigurationRevision,
            ObservedPacketCount = observed?.PacketCount ?? 0,
            ObservedDecodedValueCount = observed?.LastDecodedValueCount ?? 0,
            SequenceGapCount = observed?.GooseSequenceGapCount ?? 0,
            DuplicateCount = observed?.GooseDuplicateCount ?? 0,
            RegressionCount = observed is null ? 0 : observed.GooseSequenceRegressionCount + observed.GooseStateRegressionCount,
            TimeoutCount = observed?.GooseTimeoutCount ?? 0,
            LastStateNumber = observed?.LastStateNumber,
            LastSequenceNumber = observed?.LastSequenceNumber,
            LastTimeAllowedToLiveMilliseconds = observed?.LastTimeAllowedToLiveMilliseconds,
            StateChangeCount = observed?.GooseStateChangeCount ?? 0,
            RetransmissionCount = observed?.GooseRetransmissionCount ?? 0,
            ValueChangeCount = observed?.GooseValueChangeCount ?? 0,
            Findings = findings
        };
    }

    private static ExpectedObservedSampledValuesBinding BuildSampledValuesBinding(
        SclSampledValuesStream expected,
        IReadOnlyCollection<ProcessBusStreamSummary> observedSummaries,
        ISet<ProcessBusStreamSummary> usedObserved,
        ICollection<ExpectedObservedFinding> globalFindings)
    {
        var observed = FindBestSampledValuesMatch(expected, observedSummaries.Where(s => !usedObserved.Contains(s)).ToList());
        var findings = new List<ExpectedObservedFinding>();
        var match = ClassifyMatch(expected, observed, isGoose: false);

        if (observed is null)
        {
            findings.Add(Finding("High", "PB_SV_EXPECTED_MISSING", expected.ControlBlockReference,
                $"Expected SV stream was not observed: {expected.ControlBlockReference}, APPID={FormatAppId(expected.Address.AppId)}, dst={Dash(expected.Address.DestinationMacText)}, svID={Dash(expected.SvId)}."));
        }
        else
        {
            usedObserved.Add(observed);
            AddCommonFindings(expected.ControlBlockReference, expected.Address.AppId, expected.Address.DestinationMacText, expected.Address.VlanId, expected.Address.VlanPriority, expected.ConfigurationRevision, expected.Entries.Count, observed, findings, "SV");

            if (observed.SequenceGapCount > 0 || observed.MissedSampleCount > 0)
                findings.Add(Finding("Warning", "PB_SV_SAMPLE_GAP", expected.ControlBlockReference, $"Observed {observed.SequenceGapCount} SV sequence gap(s), missed samples={observed.MissedSampleCount}."));
            if (observed.OutOfOrderSampleCount > 0)
                findings.Add(Finding("High", "PB_SV_OUT_OF_ORDER", expected.ControlBlockReference, $"Observed {observed.OutOfOrderSampleCount} out-of-order SV sample(s)."));
            if (observed.DuplicateSampleCount > 0)
                findings.Add(Finding("Warning", "PB_SV_DUPLICATE_SAMPLE", expected.ControlBlockReference, $"Observed {observed.DuplicateSampleCount} duplicate SV sample(s)."));
            foreach (var diagnostic in observed.LastDiagnostics)
                findings.Add(Finding("Warning", "PB_SV_DIAGNOSTIC", expected.ControlBlockReference, diagnostic));
        }

        AddRange(globalFindings, findings);
        return new ExpectedObservedSampledValuesBinding
        {
            ExpectedControlBlockReference = expected.ControlBlockReference,
            ExpectedDataSetReference = expected.DataSetReference,
            ExpectedStreamId = expected.SvId,
            ExpectedAppId = expected.Address.AppId,
            ExpectedDestinationMac = expected.Address.DestinationMacText,
            ExpectedVlanId = expected.Address.VlanId,
            ExpectedVlanPriority = expected.Address.VlanPriority,
            ExpectedConfigurationRevision = expected.ConfigurationRevision,
            ExpectedDataSetMemberCount = expected.Entries.Count,
            MatchKind = match,
            ObservedStreamId = observed?.StreamId ?? string.Empty,
            ObservedAppId = observed?.AppId,
            ObservedDestinationMac = observed?.Destination ?? string.Empty,
            ObservedVlanId = observed?.VlanId,
            ObservedVlanPriority = observed?.VlanPriority,
            ObservedConfigurationRevision = observed?.ConfigurationRevision,
            ObservedPacketCount = observed?.PacketCount ?? 0,
            ObservedDecodedValueCount = observed?.LastDecodedValueCount ?? 0,
            SequenceGapCount = observed?.SequenceGapCount ?? 0,
            DuplicateCount = observed?.DuplicateSampleCount ?? 0,
            RegressionCount = observed?.OutOfOrderSampleCount ?? 0,
            TimeoutCount = 0,
            FirstSampleCount = observed?.FirstSampleCount,
            LastSampleCount = observed?.LastSampleCount,
            MissedSampleCount = observed?.MissedSampleCount ?? 0,
            OutOfOrderSampleCount = observed?.OutOfOrderSampleCount ?? 0,
            WrapCount = observed?.WrapCount ?? 0,
            Findings = findings
        };
    }

    private static ProcessBusStreamSummary? FindBestGooseMatch(SclGooseStream expected, IReadOnlyCollection<ProcessBusStreamSummary> candidates)
    {
        var goose = candidates.Where(s => s.Kind == ProcessBusEventKind.Goose).ToList();
        return goose.FirstOrDefault(s =>
                SameAppId(expected.Address.AppId, s.AppId) &&
                Same(s.StreamId, expected.ControlBlockReference) &&
                SameMac(expected.Address.DestinationMacText, s.Destination) &&
                SameNullable(expected.ConfigurationRevision, s.ConfigurationRevision))
            ?? goose.FirstOrDefault(s => SameAppId(expected.Address.AppId, s.AppId) && Same(s.StreamId, expected.ControlBlockReference))
            ?? goose.FirstOrDefault(s => SameAppId(expected.Address.AppId, s.AppId) && SameMac(expected.Address.DestinationMacText, s.Destination))
            ?? goose.FirstOrDefault(s => Same(s.StreamId, expected.ControlBlockReference));
    }

    private static ProcessBusStreamSummary? FindBestSampledValuesMatch(SclSampledValuesStream expected, IReadOnlyCollection<ProcessBusStreamSummary> candidates)
    {
        var sv = candidates.Where(s => s.Kind == ProcessBusEventKind.SampledValues).ToList();
        return sv.FirstOrDefault(s =>
                SameAppId(expected.Address.AppId, s.AppId) &&
                Same(s.StreamId, expected.SvId) &&
                SameMac(expected.Address.DestinationMacText, s.Destination) &&
                SameNullable(expected.ConfigurationRevision, s.ConfigurationRevision))
            ?? sv.FirstOrDefault(s => SameAppId(expected.Address.AppId, s.AppId) && Same(s.StreamId, expected.SvId))
            ?? sv.FirstOrDefault(s => SameAppId(expected.Address.AppId, s.AppId) && SameMac(expected.Address.DestinationMacText, s.Destination))
            ?? sv.FirstOrDefault(s => Same(s.StreamId, expected.SvId));
    }

    private static ProcessBusBindingMatchKind ClassifyMatch(SclProcessBusStream expected, ProcessBusStreamSummary? observed, bool isGoose)
    {
        if (observed is null)
            return ProcessBusBindingMatchKind.Missing;

        var expectedId = isGoose
            ? expected.ControlBlockReference
            : expected is SclSampledValuesStream sv ? sv.SvId : expected.ControlBlockReference;

        var exact = SameAppId(expected.Address.AppId, observed.AppId) &&
            SameMac(expected.Address.DestinationMacText, observed.Destination) &&
            SameNullable(expected.Address.VlanId, observed.VlanId) &&
            SameNullable(expected.ConfigurationRevision, observed.ConfigurationRevision) &&
            Same(expectedId, observed.StreamId);
        return exact ? ProcessBusBindingMatchKind.Exact : ProcessBusBindingMatchKind.Partial;
    }

    private static void AddCommonFindings(
        string objectReference,
        ushort? expectedAppId,
        string expectedDestinationMac,
        ushort? expectedVlanId,
        byte? expectedVlanPriority,
        uint expectedConfRev,
        int expectedMemberCount,
        ProcessBusStreamSummary observed,
        ICollection<ExpectedObservedFinding> findings,
        string kind)
    {
        if (!SameAppId(expectedAppId, observed.AppId))
            findings.Add(Finding("High", $"PB_{kind}_APPID_MISMATCH", objectReference, $"Expected APPID={FormatAppId(expectedAppId)}, observed APPID={FormatAppId(observed.AppId)}."));

        if (!SameMac(expectedDestinationMac, observed.Destination))
            findings.Add(Finding("High", $"PB_{kind}_DESTINATION_MAC_MISMATCH", objectReference, $"Expected destination MAC={Dash(expectedDestinationMac)}, observed destination MAC={Dash(observed.Destination)}."));

        if (!SameNullable(expectedVlanId, observed.VlanId))
            findings.Add(Finding("Warning", $"PB_{kind}_VLAN_MISMATCH", objectReference, $"Expected VLAN={expectedVlanId?.ToString() ?? "-"}, observed VLAN={observed.VlanId?.ToString() ?? "-"}."));

        if (expectedVlanPriority.HasValue && observed.VlanPriority.HasValue && expectedVlanPriority.Value != observed.VlanPriority.Value)
            findings.Add(Finding("Warning", $"PB_{kind}_VLAN_PRIORITY_MISMATCH", objectReference, $"Expected VLAN priority={expectedVlanPriority.Value}, observed VLAN priority={observed.VlanPriority.Value}."));

        if (!SameNullable(expectedConfRev, observed.ConfigurationRevision))
            findings.Add(Finding("High", $"PB_{kind}_CONFREV_MISMATCH", objectReference, $"Expected confRev={expectedConfRev}, observed confRev={observed.ConfigurationRevision?.ToString() ?? "-"}."));

        if (expectedMemberCount > 0 && observed.LastDecodedValueCount > 0 && expectedMemberCount != observed.LastDecodedValueCount)
            findings.Add(Finding("High", $"PB_{kind}_DATASET_MEMBER_COUNT_MISMATCH", objectReference, $"Expected DataSet member count={expectedMemberCount}, observed decoded value count={observed.LastDecodedValueCount}."));
    }

    private static void AddRange(ICollection<ExpectedObservedFinding> destination, IEnumerable<ExpectedObservedFinding> source)
    {
        foreach (var item in source)
            destination.Add(item);
    }

    private static ExpectedObservedFinding Finding(string severity, string code, string objectReference, string message)
        => new() { Severity = severity, Code = code, ObjectReference = objectReference, Message = message };

    private static int SeverityRank(string severity)
        => severity.Equals("High", StringComparison.OrdinalIgnoreCase) ? 3
            : severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? 2
            : severity.Equals("Info", StringComparison.OrdinalIgnoreCase) ? 1
            : 0;

    private static bool Same(string? first, string? second)
        => string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameMac(string? first, string? second)
        => NormalizeMac(first).Equals(NormalizeMac(second), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMac(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim().Replace("-", ":", StringComparison.Ordinal).ToUpperInvariant();

    private static bool SameAppId(ushort? first, ushort? second)
        => first.HasValue && second.HasValue && first.Value == second.Value;

    private static bool SameNullable<T>(T? first, T? second) where T : struct, IEquatable<T>
        => first.HasValue && second.HasValue ? first.Value.Equals(second.Value) : !first.HasValue && !second.HasValue;

    private static string Dash(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    private static string FormatAppId(ushort? appId) => appId.HasValue ? $"0x{appId.Value:X4}" : "-";
}
