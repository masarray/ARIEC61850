using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Diagnostics.SampledValues;

public sealed class SampledValuesDiagnosticsProfileBuilder
{
    public SampledValuesDiagnosticsProfile Build(SclEngineeringProfile expectedProfile, IReadOnlyCollection<ProcessBusStreamSummary> observedSummaries, string sourceName = "")
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        observedSummaries ??= Array.Empty<ProcessBusStreamSummary>();

        var svSummaries = observedSummaries
            .Where(s => s.Kind == ProcessBusEventKind.SampledValues)
            .ToList();

        var usedObserved = new HashSet<ProcessBusStreamSummary>();
        var allFindings = new List<SampledValuesDiagnosticsFinding>();
        var streams = new List<SampledValuesDiagnosticsStream>();

        foreach (var expected in expectedProfile.ProcessBus.SampledValuesStreams.OrderBy(s => s.ControlBlockReference, StringComparer.OrdinalIgnoreCase))
        {
            var stream = BuildExpectedStream(expected, svSummaries.Where(s => !usedObserved.Contains(s)).ToList());
            if (stream.ObservedPacketCount > 0)
            {
                var observed = svSummaries.FirstOrDefault(s => SameObservedStream(stream, s));
                if (observed is not null)
                    usedObserved.Add(observed);
            }

            streams.Add(stream);
            allFindings.AddRange(stream.Findings);
        }

        foreach (var unexpected in svSummaries.Where(s => !usedObserved.Contains(s)).OrderBy(s => s.AppId).ThenBy(s => s.StreamId, StringComparer.OrdinalIgnoreCase))
        {
            var finding = CreateFinding(
                "Warning",
                "SV_UNEXPECTED_STREAM",
                unexpected.StreamId,
                $"Observed SV stream is not described by the SCL engineering profile: APPID=0x{unexpected.AppId:X4}, dst={Dash(unexpected.Destination)}, svID={Dash(unexpected.StreamId)}, confRev={unexpected.ConfigurationRevision?.ToString() ?? "-"}, packets={unexpected.PacketCount}.",
                "Confirm whether the PCAP came from the intended process-bus VLAN. If valid, update the SCL or add this publisher to the expected model.");

            var streamFindings = new[] { finding };
            streams.Add(new SampledValuesDiagnosticsStream
            {
                ExpectedControlBlockReference = string.Empty,
                Status = SampledValuesDiagnosticsStreamStatus.Unexpected,
                ObservedStreamId = unexpected.StreamId,
                ObservedAppId = unexpected.AppId,
                ObservedSourceMac = unexpected.Source,
                ObservedDestinationMac = unexpected.Destination,
                ObservedVlanId = unexpected.VlanId,
                ObservedVlanPriority = unexpected.VlanPriority,
                ObservedConfigurationRevision = unexpected.ConfigurationRevision,
                ObservedPacketCount = unexpected.PacketCount,
                ObservedDecodedValueCount = unexpected.LastDecodedValueCount,
                FirstSampleCount = unexpected.FirstSampleCount,
                LastSampleCount = unexpected.LastSampleCount,
                LastSampleRate = unexpected.LastSampleRate,
                LastSampleMode = unexpected.LastSampleMode,
                LastSampleSynchronization = unexpected.LastSampleSynchronization,
                LastAsduCount = unexpected.LastAsduCount,
                ObservedPayloadBytes = unexpected.LastPayloadBytes,
                PayloadLengthChangeCount = unexpected.PayloadLengthChangeCount,
                SampleSynchronizationIssueCount = unexpected.SampleSynchronizationIssueCount,
                SequenceGapCount = unexpected.SequenceGapCount,
                MissedSampleCount = unexpected.MissedSampleCount,
                DuplicateSampleCount = unexpected.DuplicateSampleCount,
                OutOfOrderSampleCount = unexpected.OutOfOrderSampleCount,
                WrapCount = unexpected.WrapCount,
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
            .OrderBy(s => s.Status == SampledValuesDiagnosticsStreamStatus.Unexpected ? 1 : 0)
            .ThenBy(s => string.IsNullOrWhiteSpace(s.ExpectedControlBlockReference) ? s.ObservedStreamId : s.ExpectedControlBlockReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SampledValuesDiagnosticsProfile
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? expectedProfile.SourceName : sourceName,
            ExpectedStreamCount = expectedProfile.SampledValuesStreamCount,
            ObservedStreamCount = svSummaries.Count,
            BoundStreamCount = orderedStreams.Count(s => s.Status != SampledValuesDiagnosticsStreamStatus.Missing && s.Status != SampledValuesDiagnosticsStreamStatus.Unexpected),
            HealthyStreamCount = orderedStreams.Count(s => s.Status == SampledValuesDiagnosticsStreamStatus.Healthy),
            Streams = orderedStreams,
            Findings = orderedFindings
        };
    }

    private static SampledValuesDiagnosticsStream BuildExpectedStream(SclSampledValuesStream expected, IReadOnlyList<ProcessBusStreamSummary> candidates)
    {
        var observed = FindBestMatch(expected, candidates);
        var findings = new List<SampledValuesDiagnosticsFinding>();
        var expectedPayloadBytes = ExpectedPayloadBytes(expected);
        var expectedSampleMode = TryMapSampleMode(expected.SampleMode);

        if (observed is null)
        {
            findings.Add(CreateFinding(
                "High",
                "SV_EXPECTED_MISSING",
                expected.ControlBlockReference,
                $"Expected SV stream was not observed: APPID={FormatAppId(expected.Address.AppId)}, dst={Dash(expected.Address.DestinationMacText)}, svID={Dash(expected.SvId)}, confRev={expected.ConfigurationRevision}.",
                "Verify publisher is online, the mirror port is on the correct process-bus VLAN, and the capture interface can see EtherType 0x88ba frames."));

            return new SampledValuesDiagnosticsStream
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
                ExpectedSampleRate = expected.SampleRate == 0 ? null : expected.SampleRate,
                ExpectedSampleMode = expectedSampleMode,
                ExpectedNoAsdu = expected.NoAsdu,
                ExpectedPayloadBytes = expectedPayloadBytes,
                Status = SampledValuesDiagnosticsStreamStatus.Missing,
                Findings = findings,
                HealthScore = 0
            };
        }

        AddCommonFindings(expected, observed, expectedPayloadBytes, expectedSampleMode, findings);
        AddSequenceFindings(expected, observed, findings);
        AddFrameDiagnostics(expected, observed, findings);

        var status = ResolveStatus(findings);
        return new SampledValuesDiagnosticsStream
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
            ExpectedSampleRate = expected.SampleRate == 0 ? null : expected.SampleRate,
            ExpectedSampleMode = expectedSampleMode,
            ExpectedNoAsdu = expected.NoAsdu,
            ExpectedPayloadBytes = expectedPayloadBytes,
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
            FirstSampleCount = observed.FirstSampleCount,
            LastSampleCount = observed.LastSampleCount,
            LastSampleRate = observed.LastSampleRate,
            LastSampleMode = observed.LastSampleMode,
            LastSampleSynchronization = observed.LastSampleSynchronization,
            LastAsduCount = observed.LastAsduCount,
            ObservedPayloadBytes = observed.LastPayloadBytes,
            PayloadLengthChangeCount = observed.PayloadLengthChangeCount,
            SampleSynchronizationIssueCount = observed.SampleSynchronizationIssueCount,
            SequenceGapCount = observed.SequenceGapCount,
            MissedSampleCount = observed.MissedSampleCount,
            DuplicateSampleCount = observed.DuplicateSampleCount,
            OutOfOrderSampleCount = observed.OutOfOrderSampleCount,
            WrapCount = observed.WrapCount,
            LastDiagnostics = observed.LastDiagnostics,
            Findings = findings.ToArray(),
            HealthScore = CalculateScore(findings, observed)
        };
    }

    private static ProcessBusStreamSummary? FindBestMatch(SclSampledValuesStream expected, IReadOnlyList<ProcessBusStreamSummary> candidates)
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

    private static int Score(SclSampledValuesStream expected, string expectedMac, ProcessBusStreamSummary candidate)
    {
        var score = 0;
        if (string.Equals(candidate.StreamId, expected.SvId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.StreamId, expected.SmvId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.StreamId, expected.ControlBlockReference, StringComparison.OrdinalIgnoreCase))
            score += 70;
        if (expected.Address.AppId.HasValue && candidate.AppId == expected.Address.AppId.Value)
            score += 30;
        if (!string.IsNullOrWhiteSpace(expectedMac) && string.Equals(NormalizeMac(candidate.Destination), expectedMac, StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (expected.ConfigurationRevision == candidate.ConfigurationRevision)
            score += 10;
        return score;
    }

    private static bool SameObservedStream(SampledValuesDiagnosticsStream stream, ProcessBusStreamSummary summary)
        => stream.ObservedAppId == summary.AppId &&
           string.Equals(stream.ObservedStreamId, summary.StreamId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeMac(stream.ObservedDestinationMac), NormalizeMac(summary.Destination), StringComparison.OrdinalIgnoreCase) &&
           stream.ObservedConfigurationRevision == summary.ConfigurationRevision;

    private static void AddCommonFindings(
        SclSampledValuesStream expected,
        ProcessBusStreamSummary observed,
        int expectedPayloadBytes,
        ushort? expectedSampleMode,
        ICollection<SampledValuesDiagnosticsFinding> findings)
    {
        if (expected.Address.AppId.HasValue && observed.AppId != expected.Address.AppId.Value)
            findings.Add(CreateFinding("High", "SV_APPID_MISMATCH", expected.ControlBlockReference, $"APPID mismatch. SCL={FormatAppId(expected.Address.AppId)}, observed=0x{observed.AppId:X4}.", "Fix SCL Communication/SMV APPID or check whether the observed frame belongs to another publisher."));

        if (!string.IsNullOrWhiteSpace(expected.Address.DestinationMacText) && !string.Equals(NormalizeMac(expected.Address.DestinationMacText), NormalizeMac(observed.Destination), StringComparison.OrdinalIgnoreCase))
            findings.Add(CreateFinding("High", "SV_DESTINATION_MAC_MISMATCH", expected.ControlBlockReference, $"Destination MAC mismatch. SCL={Dash(expected.Address.DestinationMacText)}, observed={Dash(observed.Destination)}.", "Fix SCL multicast MAC or capture the correct process-bus segment."));

        if (expected.Address.VlanId.HasValue && observed.VlanId != expected.Address.VlanId)
            findings.Add(CreateFinding("Warning", "SV_VLAN_MISMATCH", expected.ControlBlockReference, $"VLAN mismatch. SCL={expected.Address.VlanId}, observed={observed.VlanId?.ToString() ?? "-"}.", "Verify switch VLAN tagging, mirror-port configuration, and SCL Communication/SMV VLAN-ID."));

        if (expected.ConfigurationRevision != (observed.ConfigurationRevision ?? 0))
            findings.Add(CreateFinding("High", "SV_CONFREV_MISMATCH", expected.ControlBlockReference, $"confRev mismatch. SCL={expected.ConfigurationRevision}, observed={observed.ConfigurationRevision?.ToString() ?? "-"}.", "Re-export SCL or update the subscribing IED/gateway mapping before relying on this SV DataSet layout."));

        if (expected.Entries.Count > 0 && observed.LastDecodedValueCount > 0 && expected.Entries.Count != observed.LastDecodedValueCount)
            findings.Add(CreateFinding("High", "SV_DATASET_COUNT_MISMATCH", expected.ControlBlockReference, $"DataSet value count mismatch. SCL={expected.Entries.Count}, observed decoded values={observed.LastDecodedValueCount}.", "Check DataSet member order/count and payload layout compatibility."));

        if (expected.SampleRate != 0 && observed.LastSampleRate.HasValue && expected.SampleRate != observed.LastSampleRate.Value)
            findings.Add(CreateFinding("High", "SV_SAMPLE_RATE_MISMATCH", expected.ControlBlockReference, $"sampleRate mismatch. SCL={expected.SampleRate}, observed={observed.LastSampleRate.Value}.", "Align SmpRate/SmpMod between SCL and publisher before using this stream for process-bus validation."));

        if (expectedSampleMode.HasValue && observed.LastSampleMode.HasValue && expectedSampleMode.Value != observed.LastSampleMode.Value)
            findings.Add(CreateFinding("Warning", "SV_SAMPLE_MODE_MISMATCH", expected.ControlBlockReference, $"sampleMode mismatch. SCL={expectedSampleMode.Value}, observed={observed.LastSampleMode.Value}.", "Verify SmpMod semantics: samples-per-period, samples-per-second, or seconds-per-sample."));

        if (expected.NoAsdu != 0 && observed.LastAsduCount > 0 && expected.NoAsdu != observed.LastAsduCount)
            findings.Add(CreateFinding("High", "SV_NOFASDU_MISMATCH", expected.ControlBlockReference, $"nofASDU mismatch. SCL={expected.NoAsdu}, observed={observed.LastAsduCount}.", "Align the SV publisher ASDU packing with the SCL SampledValueControl."));

        if (expectedPayloadBytes > 0 && observed.LastPayloadBytes > 0 && expectedPayloadBytes != observed.LastPayloadBytes)
            findings.Add(CreateFinding("High", "SV_PAYLOAD_LENGTH_MISMATCH", expected.ControlBlockReference, $"payload length mismatch. SCL layout={expectedPayloadBytes} byte(s), observed={observed.LastPayloadBytes} byte(s).", "Check DataSet member types/order and whether the publisher payload matches the SCL DataTypeTemplates."));
    }

    private static void AddSequenceFindings(SclSampledValuesStream expected, ProcessBusStreamSummary observed, ICollection<SampledValuesDiagnosticsFinding> findings)
    {
        if (observed.SequenceGapCount > 0)
            findings.Add(CreateFinding("Warning", "SV_SAMPLE_COUNT_GAP", expected.ControlBlockReference, $"Observed {observed.SequenceGapCount} smpCnt jump(s) with {observed.MissedSampleCount} missed sample(s).", "Check capture loss, switch congestion, MU health, NIC performance, or process-bus packet drops."));

        if (observed.DuplicateSampleCount > 0)
            findings.Add(CreateFinding("Warning", "SV_DUPLICATE_SAMPLE_COUNT", expected.ControlBlockReference, $"Observed {observed.DuplicateSampleCount} duplicate smpCnt value(s).", "Review duplicate frame forwarding, capture duplication, publisher restart behavior, or process-bus loop risk."));

        if (observed.OutOfOrderSampleCount > 0)
            findings.Add(CreateFinding("High", "SV_OUT_OF_ORDER_SAMPLE_COUNT", expected.ControlBlockReference, $"Observed {observed.OutOfOrderSampleCount} out-of-order smpCnt value(s).", "Treat as sequence integrity evidence; check capture ordering, duplicate publishers, replay, or unstable network path."));

        if (observed.WrapCount > 0)
            findings.Add(CreateFinding("Info", "SV_SAMPLE_COUNT_WRAP", expected.ControlBlockReference, $"Observed {observed.WrapCount} smpCnt wrap(s).", "Normal when the expected wrap boundary is configured correctly; confirm against SmpRate/SmpMod."));

        if (observed.PayloadLengthChangeCount > 0)
            findings.Add(CreateFinding("High", "SV_PAYLOAD_LENGTH_CHANGED", expected.ControlBlockReference, $"Observed {observed.PayloadLengthChangeCount} payload length change(s) inside one stream.", "A stable SV stream should not change payload size during one capture; check publisher configuration or malformed frames."));

        if (observed.SampleSynchronizationIssueCount > 0)
            findings.Add(CreateFinding("Warning", "SV_SAMPLE_SYNCHRONIZATION_ISSUE", expected.ControlBlockReference, $"Observed {observed.SampleSynchronizationIssueCount} frame(s) with smpSynch not equal to synchronized value 2.", "Verify PTP/time synchronization and publisher smpSynch behavior before using phasor/process-bus evidence."));
    }

    private static void AddFrameDiagnostics(SclSampledValuesStream expected, ProcessBusStreamSummary observed, ICollection<SampledValuesDiagnosticsFinding> findings)
    {
        foreach (var diagnostic in observed.LastDiagnostics)
        {
            var text = diagnostic ?? string.Empty;
            if (text.Contains("confRev mismatch", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("payload is too short", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("payload length", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("DataSet", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("High", "SV_FRAME_DIAGNOSTIC_HIGH", expected.ControlBlockReference, text, "Fix SCL/publisher/subscriber model alignment before relying on decoded SV values."));
            }
            else if (text.Contains("smpSynch", StringComparison.OrdinalIgnoreCase) || text.Contains("sample-rate", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(CreateFinding("Warning", "SV_TIMING_DIAGNOSTIC", expected.ControlBlockReference, text, "Review time synchronization and sample-rate configuration."));
            }
            else
            {
                findings.Add(CreateFinding("Warning", "SV_FRAME_DIAGNOSTIC", expected.ControlBlockReference, text, "Review the decoded SV frame diagnostic and confirm whether it is intentional."));
            }
        }
    }

    private static SampledValuesDiagnosticsStreamStatus ResolveStatus(IReadOnlyList<SampledValuesDiagnosticsFinding> findings)
    {
        if (findings.Any(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            return SampledValuesDiagnosticsStreamStatus.Critical;
        if (findings.Any(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase)))
            return SampledValuesDiagnosticsStreamStatus.Warning;
        return SampledValuesDiagnosticsStreamStatus.Healthy;
    }

    private static int CalculateScore(IReadOnlyList<SampledValuesDiagnosticsFinding> findings, ProcessBusStreamSummary observed)
    {
        var score = 100;
        score -= findings.Count(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)) * 35;
        score -= findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase)) * 12;
        score -= Math.Min(12, observed.SequenceGapCount * 3);
        score -= Math.Min(12, observed.OutOfOrderSampleCount * 4);
        score -= Math.Min(8, observed.DuplicateSampleCount * 2);
        return Math.Clamp(score, 0, 100);
    }

    private static int ExpectedPayloadBytes(SclSampledValuesStream stream)
        => SampledValuesPayloadLayout.FromDataSet(stream.Entries).PayloadByteLength;

    private static ushort? TryMapSampleMode(string sampleMode)
    {
        if (string.IsNullOrWhiteSpace(sampleMode))
            return null;

        return sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };
    }

    private static SampledValuesDiagnosticsFinding CreateFinding(string severity, string code, string objectReference, string message, string recommendation)
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
