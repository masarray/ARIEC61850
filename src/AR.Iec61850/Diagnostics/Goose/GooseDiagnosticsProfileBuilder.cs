using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Diagnostics.Goose;

public sealed class GooseDiagnosticsProfileBuilder
{
    public GooseDiagnosticsProfile Build(SclEngineeringProfile expectedProfile, IReadOnlyCollection<ProcessBusStreamSummary> observedSummaries, string sourceName = "")
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        observedSummaries ??= Array.Empty<ProcessBusStreamSummary>();

        var gooseSummaries = observedSummaries
            .Where(s => s.Kind == ProcessBusEventKind.Goose)
            .ToList();

        var usedObserved = new HashSet<ProcessBusStreamSummary>();
        var allFindings = new List<GooseDiagnosticsFinding>();
        var streams = new List<GooseDiagnosticsStream>();

        foreach (var expected in expectedProfile.ProcessBus.GooseStreams.OrderBy(s => s.ControlBlockReference, StringComparer.OrdinalIgnoreCase))
        {
            var stream = BuildExpectedStream(expected, gooseSummaries.Where(s => !usedObserved.Contains(s)).ToList());
            if (stream.ObservedPacketCount > 0)
            {
                var observed = gooseSummaries.FirstOrDefault(s => SameObservedStream(stream, s));
                if (observed is not null)
                    usedObserved.Add(observed);
            }

            streams.Add(stream);
            allFindings.AddRange(stream.Findings);
        }

        foreach (var unexpected in gooseSummaries.Where(s => !usedObserved.Contains(s)).OrderBy(s => s.AppId).ThenBy(s => s.StreamId, StringComparer.OrdinalIgnoreCase))
        {
            var finding = CreateFinding(
                "Warning",
                "GOOSE_UNEXPECTED_STREAM",
                unexpected.StreamId,
                $"Observed GOOSE stream is not described by the SCL engineering profile: APPID=0x{unexpected.AppId:X4}, dst={Dash(unexpected.Destination)}, confRev={unexpected.ConfigurationRevision?.ToString() ?? "-"}, packets={unexpected.PacketCount}.",
                "Confirm whether the PCAP came from the intended VLAN/substation segment. If valid, update the SCL or add this publisher to the expected model.");

            var streamFindings = new[] { finding };
            streams.Add(new GooseDiagnosticsStream
            {
                ExpectedControlBlockReference = string.Empty,
                Status = GooseDiagnosticsStreamStatus.Unexpected,
                ObservedStreamId = unexpected.StreamId,
                ObservedAppId = unexpected.AppId,
                ObservedSourceMac = unexpected.Source,
                ObservedDestinationMac = unexpected.Destination,
                ObservedVlanId = unexpected.VlanId,
                ObservedVlanPriority = unexpected.VlanPriority,
                ObservedConfigurationRevision = unexpected.ConfigurationRevision,
                ObservedPacketCount = unexpected.PacketCount,
                ObservedDecodedValueCount = unexpected.LastDecodedValueCount,
                LastStateNumber = unexpected.LastStateNumber,
                LastSequenceNumber = unexpected.LastSequenceNumber,
                LastTimeAllowedToLiveMilliseconds = unexpected.LastTimeAllowedToLiveMilliseconds,
                MaxArrivalGapMilliseconds = unexpected.MaxArrivalGapMilliseconds,
                StateChangeCount = unexpected.GooseStateChangeCount,
                RetransmissionCount = unexpected.GooseRetransmissionCount,
                SequenceGapCount = unexpected.GooseSequenceGapCount,
                DuplicateCount = unexpected.GooseDuplicateCount,
                RegressionCount = unexpected.GooseSequenceRegressionCount + unexpected.GooseStateRegressionCount,
                TimeoutCount = unexpected.GooseTimeoutCount,
                ValueChangeCount = unexpected.GooseValueChangeCount,
                LastChangedSummary = unexpected.LastChangedSummary,
                LastDiagnostics = unexpected.LastDiagnostics,
                Findings = streamFindings,
                HealthScore = 70
            });
            allFindings.Add(finding);
        }

        var orderedFindings = allFindings
            .OrderByDescending(f => SeverityRank(f.Severity))
            .ThenBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var orderedStreams = streams
            .OrderBy(s => s.Status == GooseDiagnosticsStreamStatus.Unexpected ? 1 : 0)
            .ThenBy(s => string.IsNullOrWhiteSpace(s.ExpectedControlBlockReference) ? s.ObservedStreamId : s.ExpectedControlBlockReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GooseDiagnosticsProfile
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? expectedProfile.SourceName : sourceName,
            ExpectedStreamCount = expectedProfile.GooseStreamCount,
            ObservedStreamCount = gooseSummaries.Count,
            BoundStreamCount = orderedStreams.Count(s => s.Status != GooseDiagnosticsStreamStatus.Missing && s.Status != GooseDiagnosticsStreamStatus.Unexpected),
            HealthyStreamCount = orderedStreams.Count(s => s.Status == GooseDiagnosticsStreamStatus.Healthy),
            Streams = orderedStreams,
            Findings = orderedFindings
        };
    }

    private static GooseDiagnosticsStream BuildExpectedStream(SclGooseStream expected, IReadOnlyList<ProcessBusStreamSummary> candidates)
    {
        var observed = FindBestMatch(expected, candidates);
        var findings = new List<GooseDiagnosticsFinding>();

        if (observed is null)
        {
            findings.Add(CreateFinding(
                "High",
                "GOOSE_EXPECTED_MISSING",
                expected.ControlBlockReference,
                $"Expected GOOSE stream was not observed: APPID={FormatAppId(expected.Address.AppId)}, dst={Dash(expected.Address.DestinationMacText)}, confRev={expected.ConfigurationRevision}.",
                "Verify publisher is online, VLAN/mirror-port selection is correct, and the capture interface can see EtherType 0x88b8 frames."));

            return new GooseDiagnosticsStream
            {
                ExpectedControlBlockReference = expected.ControlBlockReference,
                ExpectedDataSetReference = expected.DataSetReference,
                ExpectedAppId = expected.Address.AppId,
                ExpectedDestinationMac = expected.Address.DestinationMacText,
                ExpectedVlanId = expected.Address.VlanId,
                ExpectedVlanPriority = expected.Address.VlanPriority,
                ExpectedConfigurationRevision = expected.ConfigurationRevision,
                ExpectedDataSetMemberCount = expected.Entries.Count,
                Status = GooseDiagnosticsStreamStatus.Missing,
                Findings = findings,
                HealthScore = 0
            };
        }

        AddCommonFinding(expected, observed, findings);
        AddSequenceFindings(expected, observed, findings);
        AddFrameDiagnostics(expected, observed, findings);

        var status = ResolveStatus(findings);
        return new GooseDiagnosticsStream
        {
            ExpectedControlBlockReference = expected.ControlBlockReference,
            ExpectedDataSetReference = expected.DataSetReference,
            ExpectedAppId = expected.Address.AppId,
            ExpectedDestinationMac = expected.Address.DestinationMacText,
            ExpectedVlanId = expected.Address.VlanId,
            ExpectedVlanPriority = expected.Address.VlanPriority,
            ExpectedConfigurationRevision = expected.ConfigurationRevision,
            ExpectedDataSetMemberCount = expected.Entries.Count,
            Status = status,
            ObservedStreamId = observed.StreamId,
            ObservedAppId = observed.AppId,
            ObservedSourceMac = observed.Source,
            ObservedDestinationMac = observed.Destination,
            ObservedVlanId = observed.VlanId,
            ObservedVlanPriority = observed.VlanPriority,
            ObservedConfigurationRevision = observed.ConfigurationRevision,
            ObservedPacketCount = observed.PacketCount,
            ObservedDecodedValueCount = observed.LastDecodedValueCount,
            LastStateNumber = observed.LastStateNumber,
            LastSequenceNumber = observed.LastSequenceNumber,
            LastTimeAllowedToLiveMilliseconds = observed.LastTimeAllowedToLiveMilliseconds,
            MaxArrivalGapMilliseconds = observed.MaxArrivalGapMilliseconds,
            StateChangeCount = observed.GooseStateChangeCount,
            RetransmissionCount = observed.GooseRetransmissionCount,
            SequenceGapCount = observed.GooseSequenceGapCount,
            DuplicateCount = observed.GooseDuplicateCount,
            RegressionCount = observed.GooseSequenceRegressionCount + observed.GooseStateRegressionCount,
            TimeoutCount = observed.GooseTimeoutCount,
            ValueChangeCount = observed.GooseValueChangeCount,
            LastChangedSummary = observed.LastChangedSummary,
            LastDiagnostics = observed.LastDiagnostics,
            Findings = findings.ToArray(),
            HealthScore = CalculateScore(findings, observed)
        };
    }

    private static ProcessBusStreamSummary? FindBestMatch(SclGooseStream expected, IReadOnlyList<ProcessBusStreamSummary> candidates)
    {
        var expectedMac = NormalizeMac(expected.Address.DestinationMacText);
        return candidates
            .Select(candidate => new { Candidate = candidate, Score = Score(expected, expectedMac, candidate) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Candidate.PacketCount)
            .Select(x => x.Candidate)
            .FirstOrDefault();
    }

    private static int Score(SclGooseStream expected, string expectedMac, ProcessBusStreamSummary candidate)
    {
        var score = 0;
        if (string.Equals(candidate.StreamId, expected.ControlBlockReference, StringComparison.OrdinalIgnoreCase))
            score += 70;
        if (expected.Address.AppId.HasValue && candidate.AppId == expected.Address.AppId.Value)
            score += 30;
        if (!string.IsNullOrWhiteSpace(expectedMac) && string.Equals(NormalizeMac(candidate.Destination), expectedMac, StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (expected.ConfigurationRevision == candidate.ConfigurationRevision)
            score += 10;
        return score;
    }

    private static bool SameObservedStream(GooseDiagnosticsStream stream, ProcessBusStreamSummary summary)
        => stream.ObservedAppId == summary.AppId &&
           string.Equals(stream.ObservedStreamId, summary.StreamId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeMac(stream.ObservedDestinationMac), NormalizeMac(summary.Destination), StringComparison.OrdinalIgnoreCase) &&
           stream.ObservedConfigurationRevision == summary.ConfigurationRevision;

    private static void AddCommonFinding(SclGooseStream expected, ProcessBusStreamSummary observed, ICollection<GooseDiagnosticsFinding> findings)
    {
        if (expected.Address.AppId.HasValue && observed.AppId != expected.Address.AppId.Value)
            findings.Add(CreateFinding("High", "GOOSE_APPID_MISMATCH", expected.ControlBlockReference, $"APPID mismatch. SCL={FormatAppId(expected.Address.AppId)}, observed=0x{observed.AppId:X4}.", "Fix SCL Communication/GSE APPID or check whether the observed frame belongs to another publisher."));

        if (!string.IsNullOrWhiteSpace(expected.Address.DestinationMacText) && !string.Equals(NormalizeMac(expected.Address.DestinationMacText), NormalizeMac(observed.Destination), StringComparison.OrdinalIgnoreCase))
            findings.Add(CreateFinding("High", "GOOSE_DESTINATION_MAC_MISMATCH", expected.ControlBlockReference, $"Destination MAC mismatch. SCL={Dash(expected.Address.DestinationMacText)}, observed={Dash(observed.Destination)}.", "Fix SCL multicast MAC or capture the correct network segment."));

        if (expected.Address.VlanId.HasValue && observed.VlanId != expected.Address.VlanId)
            findings.Add(CreateFinding("Warning", "GOOSE_VLAN_MISMATCH", expected.ControlBlockReference, $"VLAN mismatch. SCL={expected.Address.VlanId}, observed={observed.VlanId?.ToString() ?? "-"}.", "Verify switch VLAN tagging, mirror-port configuration, and SCL Communication/GSE VLAN-ID."));

        if (expected.ConfigurationRevision != (observed.ConfigurationRevision ?? 0))
            findings.Add(CreateFinding("High", "GOOSE_CONFREV_MISMATCH", expected.ControlBlockReference, $"confRev mismatch. SCL={expected.ConfigurationRevision}, observed={observed.ConfigurationRevision?.ToString() ?? "-"}.", "Re-export SCL or update the subscribing IED/gateway mapping before relying on this GOOSE DataSet layout."));

        if (expected.Entries.Count > 0 && observed.LastDecodedValueCount > 0 && expected.Entries.Count != observed.LastDecodedValueCount)
            findings.Add(CreateFinding("High", "GOOSE_DATASET_COUNT_MISMATCH", expected.ControlBlockReference, $"DataSet value count mismatch. SCL={expected.Entries.Count}, observed={observed.LastDecodedValueCount}.", "Check DataSet member order/count and ensure publisher and subscriber use the same SCL revision."));
    }

    private static void AddSequenceFindings(SclGooseStream expected, ProcessBusStreamSummary observed, ICollection<GooseDiagnosticsFinding> findings)
    {
        if (observed.GooseSequenceGapCount > 0)
            findings.Add(CreateFinding("Warning", "GOOSE_SEQUENCE_GAP", expected.ControlBlockReference, $"Observed {observed.GooseSequenceGapCount} sqNum jump(s).", "Check capture loss, switch congestion, publisher retransmission behavior, or frame drops in the capture path."));

        var regressions = observed.GooseSequenceRegressionCount + observed.GooseStateRegressionCount;
        if (regressions > 0)
            findings.Add(CreateFinding("High", "GOOSE_STATE_OR_SEQUENCE_REGRESSION", expected.ControlBlockReference, $"Observed {regressions} stNum/sqNum regression(s).", "Treat this as a serious sequence integrity finding; check duplicate publishers, replay, firmware restart behavior, or capture ordering."));

        if (observed.GooseTimeoutCount > 0)
            findings.Add(CreateFinding("Warning", "GOOSE_SUPERVISION_TIMEOUT", expected.ControlBlockReference, $"Observed {observed.GooseTimeoutCount} interval(s) longer than the previous TimeAllowedToLive.", "Check publisher health, network congestion, supervision parameter, and capture completeness."));

        if (observed.GooseDuplicateCount > 0)
            findings.Add(CreateFinding("Info", "GOOSE_DUPLICATE_FRAME", expected.ControlBlockReference, $"Observed {observed.GooseDuplicateCount} duplicate GOOSE frame(s).", "Usually harmless in captures; review only if duplicate count is excessive."));

        if (observed.LastTimeAllowedToLiveMilliseconds == 0)
            findings.Add(CreateFinding("Warning", "GOOSE_TAL_ZERO", expected.ControlBlockReference, "TimeAllowedToLive is zero; subscriber supervision cannot be evaluated.", "Fix publisher GOOSE configuration so subscribers can supervise stream loss."));
    }

    private static void AddFrameDiagnostics(SclGooseStream expected, ProcessBusStreamSummary observed, ICollection<GooseDiagnosticsFinding> findings)
    {
        foreach (var diagnostic in observed.LastDiagnostics)
        {
            var text = diagnostic ?? string.Empty;
            if (text.Contains("test flag", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("Warning", "GOOSE_TEST_FLAG_SET", expected.ControlBlockReference, text, "Do not use this stream as operational evidence unless the test condition is intentional and documented."));
            }
            else if (text.Contains("ndsCom", StringComparison.OrdinalIgnoreCase) || text.Contains("NeedsCommissioning", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("Warning", "GOOSE_NDSCOM_SET", expected.ControlBlockReference, text, "Complete commissioning and clear needs-commissioning before operational use."));
            }
            else if (text.Contains("values changed without a state-number increment", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("High", "GOOSE_VALUE_CHANGE_WITHOUT_STATE_INCREMENT", expected.ControlBlockReference, text, "Publisher sequence semantics are suspicious; verify implementation, capture order, and possible replay/masquerade conditions."));
            }
            else if (text.Contains("state number changed but decoded DataSet values did not change", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("Warning", "GOOSE_STATE_CHANGE_WITHOUT_VALUE_CHANGE", expected.ControlBlockReference, text, "Confirm whether timestamp/quality-only changes are expected; otherwise review publisher state-change logic."));
            }
            else if (text.Contains("confRev mismatch", StringComparison.OrdinalIgnoreCase) || text.Contains("DataSet value count mismatch", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("High", "GOOSE_FRAME_DIAGNOSTIC_HIGH", expected.ControlBlockReference, text, "Fix SCL/publisher/subscriber model alignment."));
            }
            else
            {
                findings.Add(CreateFinding("Warning", "GOOSE_FRAME_DIAGNOSTIC", expected.ControlBlockReference, text, "Review the decoded GOOSE frame diagnostic and confirm whether it is intentional."));
            }
        }
    }

    private static GooseDiagnosticsStreamStatus ResolveStatus(IReadOnlyList<GooseDiagnosticsFinding> findings)
    {
        if (findings.Any(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            return GooseDiagnosticsStreamStatus.Critical;
        if (findings.Any(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase)))
            return GooseDiagnosticsStreamStatus.Warning;
        return GooseDiagnosticsStreamStatus.Healthy;
    }

    private static int CalculateScore(IReadOnlyList<GooseDiagnosticsFinding> findings, ProcessBusStreamSummary observed)
    {
        var score = 100;
        score -= findings.Count(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)) * 35;
        score -= findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase)) * 12;
        score -= Math.Min(10, observed.GooseSequenceGapCount * 2);
        score -= Math.Min(10, observed.GooseTimeoutCount * 2);
        return Math.Clamp(score, 0, 100);
    }

    private static GooseDiagnosticsFinding CreateFinding(string severity, string code, string objectReference, string message, string recommendation)
        => new()
        {
            Severity = severity,
            Code = code,
            ObjectReference = objectReference,
            Message = message,
            Recommendation = recommendation
        };

    private static int SeverityRank(string severity)
        => severity.Equals("High", StringComparison.OrdinalIgnoreCase) ? 3 :
           severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? 2 :
           severity.Equals("Info", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static string Dash(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    private static string NormalizeMac(string? value) => (value ?? string.Empty).Trim().Replace('-', ':').ToUpperInvariant();
    private static string FormatAppId(ushort? appId) => appId.HasValue ? $"0x{appId.Value:X4}" : "-";
}
