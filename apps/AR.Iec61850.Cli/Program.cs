using AR.Iec61850.Capture;
using AR.Iec61850.Discovery;
using AR.Iec61850.Diagnostics.Binding;
using AR.Iec61850.Diagnostics.Goose;
using AR.Iec61850.Diagnostics.SampledValues;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Engineering;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;
using AR.Iec61850.Scl.Analysis;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Simulation;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "inspect-scl" => InspectScl(args[1..]),
                "scl-diff" => SclDiff(args[1..]),
                "scl-engineering-profile" => SclEngineeringProfile(args[1..]),
                "process-bus-binding-profile" => ProcessBusBindingProfile(args[1..]),
                "goose-diagnostics-profile" => GooseDiagnosticsProfile(args[1..]),
                "sv-diagnostics-profile" => SampledValuesDiagnosticsProfile(args[1..]),
                "generate-pcap" => await GeneratePcapAsync(args[1..]).ConfigureAwait(false),
                "inspect-pcap" => InspectPcap(args[1..]),
                "stream-pcap" => await StreamPcapAsync(args[1..]).ConfigureAwait(false),
                "list-adapters" => ListAdapters(),
                "goose-subscribe-live" => await GooseSubscribeLiveAsync(args[1..]).ConfigureAwait(false),
                "mms-discover" => await MmsDiscoverAsync(args[1..]).ConfigureAwait(false),
                "mms-engine-profile" => await MmsEngineProfileAsync(args[1..]).ConfigureAwait(false),
                "mms-report-readiness-profile" => await MmsReportReadinessProfileAsync(args[1..]).ConfigureAwait(false),
                "mms-server-readonly-profile" => MmsServerReadOnlyProfile(args[1..]),
                "mms-listener-skeleton-profile" => await MmsListenerSkeletonProfileAsync(args[1..]).ConfigureAwait(false),
                "mms-handshake-codec-profile" => MmsHandshakeCodecProfile(args[1..]),
                "mms-handshake-listener-profile" => await MmsHandshakeListenerProfileAsync(args[1..]).ConfigureAwait(false),
                "mms-association-response-profile" => await MmsAssociationResponseProfileAsync(args[1..]).ConfigureAwait(false),
                "mms-directory" => await MmsDirectoryAsync(args[1..]).ConfigureAwait(false),
                "mms-model-discover" => await MmsModelDiscoverAsync(args[1..]).ConfigureAwait(false),
                "mms-scl-export" => await MmsSclExportAsync(args[1..]).ConfigureAwait(false),
                "mms-service-discover" => await MmsServiceDiscoverAsync(args[1..]).ConfigureAwait(false),
                "mms-find" => await MmsFindAsync(args[1..]).ConfigureAwait(false),
                "mms-resolve" => await MmsResolveAsync(args[1..]).ConfigureAwait(false),
                "mms-read-smart" => await MmsReadSmartAsync(args[1..]).ConfigureAwait(false),
                "mms-report-plan" => await MmsReportPlanAsync(args[1..]).ConfigureAwait(false),
                "mms-report-static-plan" => await MmsReportStaticPlanAsync(args[1..]).ConfigureAwait(false),
                "mms-report-dynamic-plan" => await MmsReportDynamicPlanAsync(args[1..]).ConfigureAwait(false),
                "mms-rcb-probe" => await MmsRcbProbeAsync(args[1..]).ConfigureAwait(false),
                "mms-report-static-live" => await MmsReportStaticLiveAsync(args[1..]).ConfigureAwait(false),
                "mms-report-monitor" => await MmsReportStaticLiveAsync(args[1..], monitorMode: true).ConfigureAwait(false),
                "mms-report-dynamic-live" => await MmsReportDynamicLiveAsync(args[1..]).ConfigureAwait(false),
                "mms-dataset-directory" => await MmsDataSetDirectoryAsync(args[1..]).ConfigureAwait(false),
                "publish-sv-live" => await PublishSvLiveAsync(args[1..]).ConfigureAwait(false),
                "publish-goose-live" => await PublishGooseLiveAsync(args[1..]).ConfigureAwait(false),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int InspectScl(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("inspect-scl requires exactly one SCL file path.");

        var document = new SclParser().Load(args[0]);

        Console.WriteLine($"SCL: {document.SourceName}");
        Console.WriteLine($"Header: {document.HeaderId} version={document.HeaderVersion} revision={document.HeaderRevision}");
        Console.WriteLine($"Edition: {document.Edition} namespace={document.NamespaceUri}");
        Console.WriteLine($"IEDs: {document.Ieds.Count}");
        foreach (var ied in document.Ieds)
            Console.WriteLine($"  IED {ied.Name} manufacturer={TextOrDash(ied.Manufacturer)} type={TextOrDash(ied.Type)}");

        Console.WriteLine($"DataSets: {document.DataSets.Count}");
        foreach (var dataSet in document.DataSets)
            Console.WriteLine($"  {dataSet.Reference} entries={dataSet.Entries.Count}");

        Console.WriteLine($"SV streams: {document.SampledValuesStreams.Count}");
        foreach (var sv in document.SampledValuesStreams)
        {
            Console.WriteLine(
                $"  {sv.ControlBlockReference} APPID={FormatAppId(sv.Address.AppId)} MAC={TextOrDash(sv.Address.DestinationMacText)} VLAN={FormatVlan(sv.Address)} svID={TextOrDash(sv.SvId)} confRev={sv.ConfigurationRevision} entries={sv.Entries.Count}");
        }

        Console.WriteLine($"GOOSE streams: {document.GooseStreams.Count}");
        foreach (var goose in document.GooseStreams)
        {
            Console.WriteLine(
                $"  {goose.ControlBlockReference} APPID={FormatAppId(goose.Address.AppId)} MAC={TextOrDash(goose.Address.DestinationMacText)} VLAN={FormatVlan(goose.Address)} goID={TextOrDash(goose.GoId)} confRev={goose.ConfigurationRevision} entries={goose.Entries.Count}");
        }

        Console.WriteLine($"Reports: {document.ReportControls.Count}");
        foreach (var report in document.ReportControls)
        {
            var kind = report.Buffered ? "BRCB" : "URCB";
            Console.WriteLine($"  {report.ControlBlockReference} kind={kind} datSet={TextOrDash(report.DataSetReference)} entries={report.Entries.Count}");
        }

        Console.WriteLine($"Warnings: {document.Warnings.Count}");
        foreach (var warning in document.Warnings)
            Console.WriteLine($"  WARNING {warning}");

        Console.WriteLine($"Conflicts: {document.Conflicts.Count}");
        foreach (var conflict in document.Conflicts)
            Console.WriteLine($"  CONFLICT {conflict.Kind} {conflict.Key}: {conflict.Description}");

        return 0;
    }


    private static int SclEngineeringProfile(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("scl-engineering-profile requires <scl-file>.");

        var options = CliOptions.Parse(args[1..]);
        var profile = new SclEngineeringProfileBuilder().Load(args[0]);
        var rawLimit = options.GetInt("raw-limit", 20);

        Console.WriteLine("SCL engineering profile complete.");
        Console.WriteLine($"  Source: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"  IEDs: {profile.Ieds.Count}");
        Console.WriteLine($"  Access points: {profile.AccessPoints.Count}");
        Console.WriteLine($"  Logical devices: {profile.LogicalDevices.Count}");
        Console.WriteLine($"  Logical nodes: {profile.LogicalNodes.Count}");
        Console.WriteLine($"  DataSets: {profile.DataSetCount}");
        Console.WriteLine($"  Reports: {profile.ReportControlCount}");
        Console.WriteLine($"  GOOSE: {profile.GooseStreamCount}");
        Console.WriteLine($"  SV: {profile.SampledValuesStreamCount}");
        Console.WriteLine($"  ExtRef: {profile.ExternalReferenceCount}");
        Console.WriteLine();
        Console.WriteLine("Capability matrix:");
        Console.WriteLine($"  Server model: {FormatBool(profile.Capabilities.HasServerModel)}");
        Console.WriteLine($"  DataSet engineering: {FormatBool(profile.Capabilities.HasDataSets)}");
        Console.WriteLine($"  Report engineering: {FormatBool(profile.Capabilities.HasReports)}");
        Console.WriteLine($"  GOOSE engineering: {FormatBool(profile.Capabilities.HasGoose)}");
        Console.WriteLine($"  SV engineering: {FormatBool(profile.Capabilities.HasSampledValues)}");
        Console.WriteLine($"  ExtRef mapping: {FormatBool(profile.Capabilities.HasExternalReferences)}");
        Console.WriteLine($"  Control objects: {FormatBool(profile.Capabilities.HasControlObjects)}");
        Console.WriteLine($"  Setting groups: {FormatBool(profile.Capabilities.HasSettingGroups)}");
        Console.WriteLine();
        Console.WriteLine("Findings:");
        foreach (var finding in TakeWithLimit(profile.Findings, rawLimit))
            Console.WriteLine($"  {finding.Severity} {finding.Code}: {finding.Message}");
        WriteLimitNotice(profile.Findings.Count, rawLimit, "finding(s)");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown engineering profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON engineering profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.Findings.Any(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)) ? 3 : 0;
    }

    private static string FormatBool(bool value) => value ? "yes" : "no";


    private static int SclDiff(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("scl-diff requires <golden.scl> <candidate.scl>.");

        var options = CliOptions.Parse(args[2..]);
        var output = options.Get("output", Path.Combine("out", "scl-diff"));
        var files = SclGoldenDiffAnalyzer.WriteReport(args[0], args[1], output);
        var report = SclGoldenDiffAnalyzer.Analyze(args[0], args[1]);

        Console.WriteLine("SCL golden diff complete.");
        Console.WriteLine($"  Golden: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"  Candidate: {Path.GetFullPath(args[1])}");
        Console.WriteLine($"  Output: {Path.GetFullPath(output)}");
        Console.WriteLine("  Files:");
        foreach (var file in files)
            Console.WriteLine($"    {Path.GetFullPath(file)}");

        Console.WriteLine("Summary:");
        Console.WriteLine($"  LD missing/extra: {report.LogicalDevices.MissingInCandidate.Count}/{report.LogicalDevices.ExtraInCandidate.Count}");
        Console.WriteLine($"  LN missing/extra: {report.LogicalNodes.MissingInCandidate.Count}/{report.LogicalNodes.ExtraInCandidate.Count}");
        Console.WriteLine($"  DataSets missing/extra: {report.DataSets.MissingInCandidate.Count}/{report.DataSets.ExtraInCandidate.Count}");
        Console.WriteLine($"  Reports missing/extra: {report.Reports.MissingInCandidate.Count}/{report.Reports.ExtraInCandidate.Count}");
        Console.WriteLine($"  GOOSE missing/extra: {report.GooseControls.MissingInCandidate.Count}/{report.GooseControls.ExtraInCandidate.Count}");
        Console.WriteLine($"  SV missing/extra: {report.SampledValueControls.MissingInCandidate.Count}/{report.SampledValueControls.ExtraInCandidate.Count}");
        Console.WriteLine($"  Setting groups missing/extra: {report.SettingControls.MissingInCandidate.Count}/{report.SettingControls.ExtraInCandidate.Count}");
        Console.WriteLine($"  CDC differences: {report.CdcDifferences.Count}");
        Console.WriteLine($"  Service capability differences: {report.ServiceCapabilityDifferences.Count}");

        return 0;
    }

    private static async Task<int> GeneratePcapAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("generate-pcap requires <scl-file> <output.pcap>.");

        var options = CliOptions.Parse(args[2..]);
        var sourceMac = MacAddress.Parse(options.Get("source-mac", "02:00:00:00:99:01"));
        var svFrames = options.GetInt("sv-frames", 16);
        var gooseFrames = options.GetInt("goose-frames", 4);
        var gooseScenario = options.Get("goose-scenario", "normal");
        var svScenario = options.Get("sv-scenario", "normal");
        var startTime = DateTimeOffset.UtcNow;

        var document = new SclParser().Load(args[0]);
        var packets = new List<PcapPacket>();

        if (svScenario.Equals("diagnostic", StringComparison.OrdinalIgnoreCase) || svScenario.Equals("anomaly", StringComparison.OrdinalIgnoreCase))
            AppendSampledValuesDiagnosticPackets(document, sourceMac, Math.Max(svFrames, 6), startTime, packets);
        else
            AppendSampledValuesPackets(document, sourceMac, svFrames, startTime, packets);
        if (gooseScenario.Equals("diagnostic", StringComparison.OrdinalIgnoreCase) || gooseScenario.Equals("anomaly", StringComparison.OrdinalIgnoreCase))
            AppendGooseDiagnosticPackets(document, sourceMac, Math.Max(gooseFrames, 6), startTime.AddMilliseconds(1), packets);
        else
            await AppendGoosePacketsAsync(document, sourceMac, gooseFrames, startTime.AddMilliseconds(1), packets).ConfigureAwait(false);

        packets.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        PcapWriter.WriteAll(args[1], packets);

        Console.WriteLine($"Wrote {packets.Count} Ethernet frames to {Path.GetFullPath(args[1])}");
        Console.WriteLine($"  SV frames: {packets.Count(p => IsEtherType(p.Frame, EthernetConstants.SampledValuesEtherType))}");
        Console.WriteLine($"  GOOSE frames: {packets.Count(p => IsEtherType(p.Frame, EthernetConstants.GooseEtherType))}");
        Console.WriteLine($"  SV scenario: {svScenario}");
        Console.WriteLine($"  GOOSE scenario: {gooseScenario}");
        Console.WriteLine("Open the PCAP in Wireshark or feed it to a playback/analyzer tool.");
        return 0;
    }

    private static int ProcessBusBindingProfile(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("process-bus-binding-profile requires <scl-file> <pcap-file>.");

        var sclPath = args[0];
        var pcapPath = args[1];
        var options = CliOptions.Parse(args[2..]);
        var rawLimit = options.GetInt("raw-limit", 30);
        var nominalHz = options.GetDouble("nominal-hz", 50);

        var engineeringProfile = new SclEngineeringProfileBuilder().Load(sclPath);
        var document = new SclParser().Load(sclPath);
        var monitor = new ProcessBusStreamMonitor(document, nominalHz);
        var packets = PcapReader.ReadAll(pcapPath);
        var decodedFrames = 0;
        var otherFrames = 0;

        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Unknown)
                otherFrames++;
            else
                decodedFrames++;
        }

        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, monitor.Summaries, Path.GetFileName(sclPath));

        Console.WriteLine("Expected-vs-observed process-bus binding complete.");
        Console.WriteLine($"  SCL: {Path.GetFullPath(sclPath)}");
        Console.WriteLine($"  PCAP: {Path.GetFullPath(pcapPath)}");
        Console.WriteLine($"  Packets: {packets.Count}");
        Console.WriteLine($"  Decoded process-bus frames: {decodedFrames}");
        Console.WriteLine($"  Other frames: {otherFrames}");
        Console.WriteLine($"  Expected GOOSE: {profile.ExpectedGooseCount}, observed={profile.ObservedGooseCount}, bound={profile.BoundGooseCount}");
        Console.WriteLine($"  Expected SV: {profile.ExpectedSampledValuesCount}, observed={profile.ObservedSampledValuesCount}, bound={profile.BoundSampledValuesCount}");
        Console.WriteLine($"  Missing expected: {profile.MissingExpectedCount}");
        Console.WriteLine($"  Unexpected observed: {profile.UnexpectedObservedCount}");
        Console.WriteLine($"  Mismatches: {profile.MismatchCount}");
        Console.WriteLine($"  Sequence anomalies: {profile.SequenceAnomalyCount}");
        Console.WriteLine($"  Ready: {FormatBool(profile.IsReady)}");
        Console.WriteLine();

        Console.WriteLine("Findings:");
        foreach (var finding in TakeWithLimit(profile.Findings, rawLimit))
            Console.WriteLine($"  {finding.Severity} {finding.Code}: {finding.Message}");
        WriteLimitNotice(profile.Findings.Count, rawLimit, "finding(s)");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown binding profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON binding profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsReady ? 0 : 3;
    }


    private static int GooseDiagnosticsProfile(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("goose-diagnostics-profile requires <scl-file> <pcap-file>.");

        var sclPath = args[0];
        var pcapPath = args[1];
        var options = CliOptions.Parse(args[2..]);
        var rawLimit = options.GetInt("raw-limit", 30);
        var nominalHz = options.GetDouble("nominal-hz", 50);

        var engineeringProfile = new SclEngineeringProfileBuilder().Load(sclPath);
        var document = new SclParser().Load(sclPath);
        var monitor = new ProcessBusStreamMonitor(document, nominalHz);
        var packets = PcapReader.ReadAll(pcapPath);
        var gooseFrames = 0;
        var otherFrames = 0;

        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Goose)
                gooseFrames++;
            else
                otherFrames++;
        }

        var profile = new GooseDiagnosticsProfileBuilder().Build(engineeringProfile, monitor.Summaries, Path.GetFileName(sclPath));

        Console.WriteLine("GOOSE diagnostics profile complete.");
        Console.WriteLine($"  SCL: {Path.GetFullPath(sclPath)}");
        Console.WriteLine($"  PCAP: {Path.GetFullPath(pcapPath)}");
        Console.WriteLine($"  Packets: {packets.Count}");
        Console.WriteLine($"  Decoded GOOSE frames: {gooseFrames}");
        Console.WriteLine($"  Non-GOOSE/other frames: {otherFrames}");
        Console.WriteLine($"  Expected streams: {profile.ExpectedStreamCount}");
        Console.WriteLine($"  Observed streams: {profile.ObservedStreamCount}");
        Console.WriteLine($"  Bound streams: {profile.BoundStreamCount}");
        Console.WriteLine($"  Healthy streams: {profile.HealthyStreamCount}");
        Console.WriteLine($"  High findings: {profile.HighCount}");
        Console.WriteLine($"  Warning findings: {profile.WarningCount}");
        Console.WriteLine($"  Sequence anomalies: {profile.SequenceAnomalyCount}");
        Console.WriteLine($"  Supervision issues: {profile.SupervisionIssueCount}");
        Console.WriteLine($"  Healthy: {FormatBool(profile.IsHealthy)}");
        Console.WriteLine();

        Console.WriteLine("Findings:");
        foreach (var finding in TakeWithLimit(profile.Findings, rawLimit))
            Console.WriteLine($"  {finding.Severity} {finding.Code}: {finding.Message} Recommendation: {finding.Recommendation}");
        WriteLimitNotice(profile.Findings.Count, rawLimit, "finding(s)");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown GOOSE diagnostics profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON GOOSE diagnostics profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsHealthy ? 0 : 3;
    }

    private static int SampledValuesDiagnosticsProfile(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("sv-diagnostics-profile requires <scl-file> <pcap-file>.");

        var sclPath = args[0];
        var pcapPath = args[1];
        var options = CliOptions.Parse(args[2..]);
        var rawLimit = options.GetInt("raw-limit", 30);
        var nominalHz = options.GetDouble("nominal-hz", 50);

        var engineeringProfile = new SclEngineeringProfileBuilder().Load(sclPath);
        var document = new SclParser().Load(sclPath);
        var monitor = new ProcessBusStreamMonitor(document, nominalHz);
        var packets = PcapReader.ReadAll(pcapPath);
        var decodedFrames = 0;
        var otherFrames = 0;

        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Unknown)
                otherFrames++;
            else
                decodedFrames++;
        }

        var profile = new SampledValuesDiagnosticsProfileBuilder().Build(engineeringProfile, monitor.Summaries, Path.GetFileName(sclPath));

        Console.WriteLine("Sampled Values diagnostics profile complete.");
        Console.WriteLine($"  SCL: {Path.GetFullPath(sclPath)}");
        Console.WriteLine($"  PCAP: {Path.GetFullPath(pcapPath)}");
        Console.WriteLine($"  Packets: {packets.Count}");
        Console.WriteLine($"  Decoded process-bus frames: {decodedFrames}");
        Console.WriteLine($"  Other frames: {otherFrames}");
        Console.WriteLine($"  Expected SV streams: {profile.ExpectedStreamCount}");
        Console.WriteLine($"  Observed SV streams: {profile.ObservedStreamCount}");
        Console.WriteLine($"  Bound SV streams: {profile.BoundStreamCount}");
        Console.WriteLine($"  Healthy SV streams: {profile.HealthyStreamCount}");
        Console.WriteLine($"  High findings: {profile.HighCount}");
        Console.WriteLine($"  Warning findings: {profile.WarningCount}");

        foreach (var stream in profile.Streams.Take(rawLimit))
        {
            Console.WriteLine(
                $"  [{stream.Status}] expected={TextOrDash(stream.ExpectedControlBlockReference)} observed={TextOrDash(stream.ObservedStreamId)} APPID={FormatAppId(stream.ObservedAppId)} packets={stream.ObservedPacketCount} smpCnt={FormatCounterRange(stream.FirstSampleCount, stream.LastSampleCount)} gaps={stream.SequenceGapCount} missed={stream.MissedSampleCount} dup={stream.DuplicateSampleCount} late={stream.OutOfOrderSampleCount} wraps={stream.WrapCount} payload={stream.ObservedPayloadBytes} sync={stream.LastSampleSynchronization?.ToString(CultureInfo.InvariantCulture) ?? "-"} score={stream.HealthScore} findings={stream.Findings.Count}");
        }
        WriteLimitNotice(profile.Streams.Count, rawLimit, "SV stream row(s)");

        foreach (var finding in profile.Findings.Take(rawLimit))
            Console.WriteLine($"  {finding.Severity} {finding.Code}: {finding.Message} Recommendation: {finding.Recommendation}");
        WriteLimitNotice(profile.Findings.Count, rawLimit, "finding(s)");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown SV diagnostics profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON SV diagnostics profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsHealthy ? 0 : 3;
    }


    private static int MmsHandshakeCodecProfile(string[] args)
    {
        var options = CliOptions.Parse(args);
        var profile = new MmsHandshakeCodecProfileBuilder().BuildDefault();

        Console.WriteLine("MMS handshake codec profile");
        Console.WriteLine($"Server transport readiness: {(profile.IsServerTransportReady ? "READY" : "BLOCKED")}");
        Console.WriteLine();
        Console.WriteLine("Codec steps:");
        foreach (var step in profile.Steps)
            Console.WriteLine($"  {(step.IsPass ? "OK" : "FAIL")} {step.Area} - {step.Message}");

        if (profile.Findings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Findings:");
            foreach (var finding in profile.Findings)
                Console.WriteLine($"  - {finding}");
        }

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown MMS handshake codec profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, profile.ToJson());
            Console.WriteLine($"JSON MMS handshake codec profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsServerTransportReady ? 0 : 3;
    }


    private static async Task<int> MmsHandshakeListenerProfileAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        var port = options.GetInt("port", 0);
        if (port is < 0 or > 65535)
            throw new ArgumentException("--port must be 0..65535. Use 0 for an ephemeral loopback port.");

        var timeoutMs = options.GetInt("timeout-ms", 5000);
        if (timeoutMs <= 0)
            throw new ArgumentException("--timeout-ms must be greater than 0.");

        var associationProfile = options.Get("association-profile", "BalancedApTitle");
        var profile = await new MmsHandshakeListenerProfileBuilder().RunLoopbackProbeAsync(
            new MmsHandshakeListenerOptions
            {
                Port = port,
                ProbeTimeoutMilliseconds = timeoutMs,
                AssociationProfileName = associationProfile
            }).ConfigureAwait(false);

        Console.WriteLine("MMS handshake listener profile");
        Console.WriteLine("Mode: loopback OSI listener probe (TPKT/COTP handshake + ACSE/MMS association payload inspection; no AARE/MMS server response yet).");
        Console.WriteLine($"Listener readiness: {(profile.IsReady ? "READY" : "BLOCKED")}");
        Console.WriteLine($"Bound port: {profile.BoundPort}");
        Console.WriteLine($"Accepted connections: {profile.AcceptedConnectionCount}");
        Console.WriteLine();
        Console.WriteLine("Transport gates:");
        Console.WriteLine($"  TPKT exchange verified: {profile.TpktExchangeVerified.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  COTP connection confirmed: {profile.CotpConnectionConfirmed.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  COTP data observed: {profile.CotpDataObserved.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Association payload observed: {profile.AssociationPayloadObserved.ToString().ToLowerInvariant()}");
        Console.WriteLine();
        Console.WriteLine("Handshake steps:");
        foreach (var step in profile.Steps)
            Console.WriteLine($"  {(step.IsPass ? "OK" : "FAIL")} {step.Side} {step.Layer} - {step.Message}");

        if (profile.Findings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Findings:");
            foreach (var finding in profile.Findings)
                Console.WriteLine($"  - {finding}");
        }

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown MMS handshake listener profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, profile.ToJson());
            Console.WriteLine($"JSON MMS handshake listener profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsReady ? 0 : 3;
    }


    private static async Task<int> MmsAssociationResponseProfileAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        var port = options.GetInt("port", 0);
        if (port is < 0 or > 65535)
            throw new ArgumentException("--port must be 0..65535. Use 0 for an ephemeral loopback port.");

        var timeoutMs = options.GetInt("timeout-ms", 5000);
        if (timeoutMs <= 0)
            throw new ArgumentException("--timeout-ms must be greater than 0.");

        var associationProfile = options.Get("association-profile", "BalancedApTitle");
        var responseProfile = options.Get("response-profile", "DeterministicInitiateResponse");
        var profile = await new MmsAssociationResponseProfileBuilder().RunLoopbackProbeAsync(
            new MmsAssociationResponseOptions
            {
                Port = port,
                ProbeTimeoutMilliseconds = timeoutMs,
                AssociationProfileName = associationProfile,
                ResponseProfileName = responseProfile
            }).ConfigureAwait(false);

        Console.WriteLine("MMS association response profile");
        Console.WriteLine("Mode: loopback OSI listener probe (TPKT/COTP + ACSE AARE + MMS InitiateResponse profile; no confirmed MMS request dispatch yet).");
        Console.WriteLine($"Association readiness: {(profile.IsReady ? "READY" : "BLOCKED")}");
        Console.WriteLine($"Bound port: {profile.BoundPort}");
        Console.WriteLine($"Accepted connections: {profile.AcceptedConnectionCount}");
        Console.WriteLine($"Client association profile: {profile.AssociationProfileName}");
        Console.WriteLine($"Server response profile: {profile.ResponseProfileName} ({profile.ResponseLength} byte)");
        Console.WriteLine();
        Console.WriteLine("Association gates:");
        Console.WriteLine($"  TPKT exchange verified: {profile.TpktExchangeVerified.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  COTP connection confirmed: {profile.CotpConnectionConfirmed.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Client associate request observed: {profile.ClientAssociateRequestObserved.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Server associate response sent: {profile.ServerAssociateResponseSent.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Client accepted associate response: {profile.ClientAssociateResponseAccepted.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  MMS initiate response marker observed: {profile.MmsInitiateResponseObserved.ToString().ToLowerInvariant()}");
        Console.WriteLine();
        Console.WriteLine("Association steps:");
        foreach (var step in profile.Steps)
            Console.WriteLine($"  {(step.IsPass ? "OK" : "FAIL")} {step.Side} {step.Layer} - {step.Message}");

        if (profile.Findings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Findings:");
            foreach (var finding in profile.Findings)
                Console.WriteLine($"  - {finding}");
        }

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown MMS association response profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, profile.ToJson());
            Console.WriteLine($"JSON MMS association response profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsReady ? 0 : 3;
    }

    private static int MmsServerReadOnlyProfile(string[] args)
    {
        var options = CliOptions.Parse(args);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var steps = options.GetInt("steps", 0);
        var profileName = options.Get("name", "ARIEC61850 Virtual IED");
        var readTarget = options.Get("read", "IED1LD0/XCBR1.Pos.stVal");
        var dataSetTarget = options.Get("dataset", "IED1LD0/LLN0.dsStatus");
        var rawLimit = options.GetInt("raw-limit", 30);

        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var engine = new IedSimulatorEngine(simulatorProfile);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < steps; i++)
            engine.Step(now.AddMilliseconds(i * 20));

        var snapshot = engine.CreateSnapshot(DateTimeOffset.UtcNow);
        var profile = new MmsReadOnlyServerModelBuilder().Build(
            simulatorProfile,
            snapshot,
            new MmsReadOnlyServerProfileOptions
            {
                ServerName = profileName,
                Port = port,
                IncludeSelfTest = true
            });

        var session = new MmsReadOnlyServerSession(profile);
        var directory = session.Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory });
        var read = session.Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = readTarget });
        var dataSet = session.Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = dataSetTarget });
        var writeReject = session.Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = readTarget, Value = "test" });

        Console.WriteLine(profile.Summary);
        Console.WriteLine($"Mode: read-only virtual MMS server model (offline alpha; no TCP listener).");
        Console.WriteLine($"Port profile: {port}");
        Console.WriteLine();
        Console.WriteLine("Directory probe:");
        Console.WriteLine($"  {directory.Summary}");
        foreach (var item in directory.Items.Take(rawLimit))
            Console.WriteLine($"  LD {item}");
        WriteLimitNotice(directory.Items.Count, rawLimit, "logical device row(s)");

        Console.WriteLine();
        Console.WriteLine("Read probe:");
        Console.WriteLine($"  {read.Summary}");
        foreach (var value in read.Values)
            Console.WriteLine($"  {value.Reference} = {value.Value} q={value.Quality} t={value.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff}Z");

        Console.WriteLine();
        Console.WriteLine("DataSet probe:");
        Console.WriteLine($"  {dataSet.Summary}");
        foreach (var value in dataSet.Values.Take(rawLimit))
            Console.WriteLine($"  {value.Reference} = {value.Value} q={value.Quality}");
        WriteLimitNotice(dataSet.Values.Count, rawLimit, "DataSet value row(s)");

        Console.WriteLine();
        Console.WriteLine("Write guard:");
        Console.WriteLine($"  {writeReject.Summary}");

        Console.WriteLine();
        Console.WriteLine("Self-test:");
        foreach (var step in profile.SelfTestSteps)
            Console.WriteLine($"  {(step.IsSuccess ? "OK" : "FAIL")} {step.Operation} {TextOrDash(step.Target)} - {step.Message}");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown MMS server profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON MMS server profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsReady && read.IsSuccess && dataSet.IsSuccess && !writeReject.IsSuccess ? 0 : 3;
    }

    private static async Task<int> MmsListenerSkeletonProfileAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        var port = options.GetInt("port", 0);
        if (port is < 0 or > 65535)
            throw new ArgumentException("--port must be 0..65535. Use 0 for an ephemeral loopback port.");

        var host = options.Get("host", "127.0.0.1");
        var steps = options.GetInt("steps", 0);
        var timeoutMs = options.GetInt("timeout-ms", 5000);
        if (timeoutMs <= 0)
            throw new ArgumentException("--timeout-ms must be greater than 0.");

        var profileName = options.Get("name", "ARIEC61850 Virtual IED");
        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var engine = new IedSimulatorEngine(simulatorProfile);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < steps; i++)
            engine.Step(now.AddMilliseconds(i * 20));

        var snapshot = engine.CreateSnapshot(DateTimeOffset.UtcNow);
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(
            simulatorProfile,
            snapshot,
            new MmsReadOnlyServerProfileOptions
            {
                ServerName = profileName,
                Port = port == 0 ? 102 : port,
                IncludeSelfTest = true
            });

        var listener = new MmsReadOnlyListenerSkeleton(serverProfile);
        var profile = await listener.RunSelfProbeAsync(new MmsReadOnlyListenerSkeletonOptions
        {
            Host = host,
            Port = port,
            ProbeTimeoutMilliseconds = timeoutMs
        }).ConfigureAwait(false);

        Console.WriteLine(profile.Summary);
        Console.WriteLine("Mode: read-only TCP listener skeleton (loopback self-probe; JSON-line harness; no live MMS PDU decoder yet)." );
        Console.WriteLine($"Bound endpoint: {profile.Host}:{profile.BoundPort}");
        Console.WriteLine();
        Console.WriteLine("Probe steps:");
        foreach (var step in profile.ProbeSteps)
            Console.WriteLine($"  {(step.IsTransportSuccess ? "OK" : "FAIL")} {step.Operation} {TextOrDash(step.Target)} serverSuccess={step.IsServerSuccess.ToString().ToLowerInvariant()} - {step.Message}");

        if (profile.Diagnostics.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Diagnostics:");
            foreach (var diagnostic in profile.Diagnostics)
                Console.WriteLine($"  {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown MMS listener skeleton profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON MMS listener skeleton profile: {Path.GetFullPath(jsonPath)}");
        }

        return profile.IsReady ? 0 : 3;
    }

    private static int InspectPcap(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("inspect-pcap requires a PCAP file path.");

        var options = CliOptions.Parse(args[1..]);
        var packets = PcapReader.ReadAll(args[0]);
        var monitor = CreateProcessBusMonitor(options);
        var otherFrames = 0;

        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Unknown)
                otherFrames++;
        }

        var svSummaries = monitor.Summaries.Where(s => s.Kind == ProcessBusEventKind.SampledValues).ToArray();
        var gooseSummaries = monitor.Summaries.Where(s => s.Kind == ProcessBusEventKind.Goose).ToArray();

        Console.WriteLine($"PCAP: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"Packets: {packets.Count}");
        Console.WriteLine($"Decoded process-bus frames: {svSummaries.Sum(s => s.PacketCount) + gooseSummaries.Sum(s => s.PacketCount)}");
        Console.WriteLine($"SV streams: {svSummaries.Length} frames={svSummaries.Sum(s => s.PacketCount)}");
        foreach (var summary in svSummaries.OrderBy(s => s.AppId))
        {
            Console.WriteLine(
                $"  APPID=0x{summary.AppId:X4} src={summary.Source} dst={summary.Destination} VLAN={FormatVlan(summary.VlanId, summary.VlanPriority)} svID={TextOrDash(summary.StreamId)} confRev={summary.ConfigurationRevision ?? 0} packets={summary.PacketCount} smpCnt={FormatCounterRange(summary.FirstSampleCount, summary.LastSampleCount)} values={summary.LastDecodedValueCount} gaps={summary.SequenceGapCount} missed={summary.MissedSampleCount} dup={summary.DuplicateSampleCount} late={summary.OutOfOrderSampleCount} wraps={summary.WrapCount}");
        }

        Console.WriteLine($"GOOSE streams: {gooseSummaries.Length} frames={gooseSummaries.Sum(s => s.PacketCount)}");
        foreach (var summary in gooseSummaries.OrderBy(s => s.AppId))
        {
            Console.WriteLine(
                $"  APPID=0x{summary.AppId:X4} src={summary.Source} dst={summary.Destination} VLAN={FormatVlan(summary.VlanId, summary.VlanPriority)} goCB={TextOrDash(summary.StreamId)} confRev={summary.ConfigurationRevision ?? 0} packets={summary.PacketCount} stNum={summary.LastStateNumber} sqNum={summary.LastSequenceNumber} TAL={summary.LastTimeAllowedToLiveMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms stateChanges={summary.GooseStateChangeCount} retrans={summary.GooseRetransmissionCount} gaps={summary.GooseSequenceGapCount} dup={summary.GooseDuplicateCount} regress={summary.GooseSequenceRegressionCount + summary.GooseStateRegressionCount} timeouts={summary.GooseTimeoutCount} valueChanges={summary.GooseValueChangeCount}{FormatChangedSummary(summary.LastChangedSummary)}{FormatDiagnostics(summary.LastDiagnostics)}");
        }

        Console.WriteLine($"Other frames: {otherFrames}");
        return 0;
    }

    private static async Task<int> StreamPcapAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("stream-pcap requires a PCAP file path.");

        var options = CliOptions.Parse(args[1..]);
        var delayMs = options.GetInt("delay-ms", 50);
        var limit = options.GetInt("limit", 0);
        var packets = PcapReader.ReadAll(args[0]);
        var monitor = CreateProcessBusMonitor(options);
        var emitted = 0;

        Console.WriteLine($"Streaming {Path.GetFullPath(args[0])}");
        Console.WriteLine($"Delay: {delayMs} ms per decoded process-bus event");

        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Unknown)
                continue;

            Console.WriteLine(FormatStreamEvent(streamEvent));
            emitted++;

            if (limit > 0 && emitted >= limit)
                break;

            if (delayMs > 0)
                await Task.Delay(delayMs).ConfigureAwait(false);
        }

        Console.WriteLine($"Stream complete: emitted={emitted} streams={monitor.Summaries.Count}");
        foreach (var summary in monitor.Summaries.OrderBy(s => s.Kind).ThenBy(s => s.AppId))
            Console.WriteLine(FormatMonitorSummary(summary));

        return 0;
    }

    private static async Task<int> GooseSubscribeLiveAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        var adapterSelector = options.GetRequired("adapter");
        var adapter = NpcapAdapterCatalog.ResolveAdapterInfo(adapterSelector);
        var monitor = CreateProcessBusMonitor(options);
        var filter = options.Get("filter", "ether proto 0x88b8");
        var continuous = options.GetBool("continuous", fallback: false);
        var durationSeconds = options.GetDouble("duration-sec", continuous ? 0 : 60);
        if (durationSeconds < 0)
            throw new ArgumentException("--duration-sec must be greater than or equal to 0.");

        var frameLimit = options.GetInt("frames", 0);
        if (frameLimit < 0)
            throw new ArgumentException("--frames must be greater than or equal to 0.");

        var statusIntervalMs = options.GetInt("status-ms", 1000);
        if (statusIntervalMs < 0)
            throw new ArgumentException("--status-ms must be greater than or equal to 0.");

        var readTimeoutMs = options.GetInt("read-timeout-ms", 1000);
        if (readTimeoutMs <= 0)
            throw new ArgumentException("--read-timeout-ms must be greater than 0.");

        var bufferCapacity = options.GetInt("buffer-capacity", 4096);
        if (bufferCapacity <= 0)
            throw new ArgumentException("--buffer-capacity must be greater than 0.");

        Console.WriteLine("Mode: live GOOSE subscriber (read-only raw Ethernet capture).");
        Console.WriteLine($"Adapter: [{adapter.Index}] MAC={adapter.MacAddress?.ToString() ?? "-"} {TextOrDash(adapter.Description)}");
        Console.WriteLine($"Filter: {filter}");
        Console.WriteLine($"Duration: {(durationSeconds <= 0 ? "continuous" : $"{durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s")} frameLimit={(frameLimit <= 0 ? "none" : frameLimit.ToString(CultureInfo.InvariantCulture))}");
        if (options.TryGet("scl", out var sclPath) && !string.IsNullOrWhiteSpace(sclPath))
            Console.WriteLine($"SCL binding: {Path.GetFullPath(sclPath)}");
        else
            Console.WriteLine("SCL binding: none; GOOSE values will be semantically anonymous.");

        using var source = new NpcapProcessBusFrameSource(adapterSelector);
        using var stop = new CancellationTokenSource();
        if (durationSeconds > 0)
            stop.CancelAfter(TimeSpan.FromSeconds(durationSeconds));

        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stop requested; finishing current GOOSE capture loop.");
        };
        Console.CancelKeyPress += cancelHandler;

        long capturedFrames = 0;
        long gooseFrames = 0;
        long otherFrames = 0;
        var startedTicks = Stopwatch.GetTimestamp();
        var nextStatusTicks = startedTicks + StatusIntervalTicks(statusIntervalMs);

        try
        {
            var captureOptions = new ProcessBusCaptureOptions
            {
                Filter = filter,
                ReadTimeoutMilliseconds = readTimeoutMs,
                BufferCapacity = bufferCapacity
            };

            await foreach (var captured in source.CaptureAsync(captureOptions, stop.Token).ConfigureAwait(false))
            {
                capturedFrames++;
                var streamEvent = monitor.Observe(captured.Timestamp, captured.Frame);
                if (streamEvent.Kind != ProcessBusEventKind.Goose)
                {
                    otherFrames++;
                    continue;
                }

                gooseFrames++;
                Console.WriteLine(FormatStreamEvent(streamEvent));

                if (frameLimit > 0 && gooseFrames >= frameLimit)
                    break;

                var nowTicks = Stopwatch.GetTimestamp();
                if (statusIntervalMs > 0 && nowTicks >= nextStatusTicks)
                {
                    Console.WriteLine($"  status captured={capturedFrames} goose={gooseFrames} streams={monitor.Summaries.Count(s => s.Kind == ProcessBusEventKind.Goose)} elapsed={Stopwatch.GetElapsedTime(startedTicks).TotalSeconds:0.###}s");
                    nextStatusTicks = nowTicks + StatusIntervalTicks(statusIntervalMs);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        Console.WriteLine($"GOOSE live subscriber complete: captured={capturedFrames} goose={gooseFrames} other={otherFrames} elapsed={elapsed.TotalSeconds:0.###}s streams={monitor.Summaries.Count}");
        foreach (var summary in monitor.Summaries.Where(s => s.Kind == ProcessBusEventKind.Goose).OrderBy(s => s.AppId))
            Console.WriteLine(FormatMonitorSummary(summary));

        return 0;
    }

    private static ProcessBusStreamMonitor CreateProcessBusMonitor(CliOptions options)
    {
        if (!options.TryGet("scl", out var sclPath) || string.IsNullOrWhiteSpace(sclPath))
            return new ProcessBusStreamMonitor();

        var nominalHz = options.GetDouble("nominal-hz", 50);
        var document = new SclParser().Load(sclPath);
        return new ProcessBusStreamMonitor(document, nominalHz);
    }

    private static int ListAdapters()
    {
        var adapters = NpcapAdapterCatalog.ListAdapters();

        Console.WriteLine($"Adapters: {adapters.Count}");
        foreach (var adapter in adapters)
        {
            Console.WriteLine($"  [{adapter.Index}] MAC={adapter.MacAddress?.ToString() ?? "-"} Name={TextOrDash(adapter.Name)}");
            Console.WriteLine($"      {TextOrDash(adapter.Description)}");
        }

        Console.WriteLine();
        Console.WriteLine("Use the adapter index with publish-sv-live, publish-goose-live, or goose-subscribe-live --adapter <index>.");
        return 0;
    }

    private static async Task<int> MmsDiscoverAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-discover requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var probeReports = !options.GetBool("no-report-probe", fallback: false);
        var maxReportProbes = options.GetInt("max-report-probes", 32);
        var rawLimit = options.GetInt("raw-limit", 50);
        var showRaw = options.GetBool("show-raw", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine($"Mode: native clean-room TCP/TPKT/COTP/ACSE/MMS discovery; reportProbe={probeReports} maxReportProbes={maxReportProbes}");

        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");
        Console.WriteLine($"Receive pump: {(session.IsReceivePumpRunning ? "running" : "stopped")}");

        var discovery = await session.DiscoverAsync(probeReports, maxReportProbes, timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.Summary);

        Console.WriteLine();
        Console.WriteLine($"Logical devices: {discovery.Snapshot.DomainVariables.Count}");
        foreach (var domain in discovery.Snapshot.DomainVariables.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            discovery.Snapshot.DomainVariables.TryGetValue(domain, out var variables);
            discovery.Snapshot.DomainVariableLists.TryGetValue(domain, out var lists);
            Console.WriteLine($"  {domain}: variables={variables?.Count ?? 0} datasets={lists?.Count ?? 0}");
        }

        Console.WriteLine();
        Console.WriteLine(discovery.ReportInventory.Summary);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine($"FC index: {FormatFcCounts(discovery.IedDirectory.CountByFunctionalConstraint())}");

        if (discovery.ReportInventory.DataSets.Count > 0)
        {
            Console.WriteLine("DataSets:");
            foreach (var dataSet in TakeWithLimit(discovery.ReportInventory.DataSets, rawLimit))
                Console.WriteLine($"  {dataSet.Reference} raw={TextOrDash(dataSet.RawMmsName)}");
            WriteLimitNotice(discovery.ReportInventory.DataSets.Count, rawLimit, "DataSets");
        }

        if (discovery.ReportInventory.ReportControls.Count > 0)
        {
            Console.WriteLine("Report controls:");
            foreach (var report in TakeWithLimit(discovery.ReportInventory.ReportControls, rawLimit))
            {
                Console.WriteLine(
                    $"  {report.Mode} {report.Reference} datSet={TextOrDash(report.DataSetReference)} rptID={TextOrDash(report.ReportId)} confRev={TextOrDash(report.ConfRev)} intgPd={TextOrDash(report.IntegrityPeriodMs)} rptEna={TextOrDash(report.EnabledState)} resv={TextOrDash(report.Buffered ? report.ReservationTimeSeconds : report.ReservationState)} bufTm={TextOrDash(report.BufferTimeMs)} trgOps={TextOrDash(report.TriggerOptions)} status={report.Status}");

                if (report.Attributes.Count > 0)
                    Console.WriteLine($"      attrs={string.Join(",", report.Attributes)}");
            }
            WriteLimitNotice(discovery.ReportInventory.ReportControls.Count, rawLimit, "Report controls");
        }

        if (showRaw)
        {
            Console.WriteLine();
            Console.WriteLine("Raw MMS names:");
            foreach (var domain in discovery.Snapshot.DomainVariables.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  [{domain}] NamedVariable");
                foreach (var item in TakeWithLimit(discovery.Snapshot.DomainVariables[domain], rawLimit))
                    Console.WriteLine($"    {item}");
                WriteLimitNotice(discovery.Snapshot.DomainVariables[domain].Count, rawLimit, $"{domain} variables");

                if (discovery.Snapshot.DomainVariableLists.TryGetValue(domain, out var lists))
                {
                    Console.WriteLine($"  [{domain}] NamedVariableList");
                    foreach (var item in TakeWithLimit(lists, rawLimit))
                        Console.WriteLine($"    {item}");
                    WriteLimitNotice(lists.Count, rawLimit, $"{domain} variable lists");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(session.LastAssociationAttemptSummary))
        {
            Console.WriteLine();
            Console.WriteLine("Association attempts:");
            Console.WriteLine($"  {session.LastAssociationAttemptSummary}");
        }

        return 0;
    }


    private static async Task<int> MmsEngineProfileAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-engine-profile requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var profileOptions = new Iec61850EngineeringProfileOptions
        {
            Host = host,
            Port = port,
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            ProbeReportAttributes = !options.GetBool("no-report-probe", false),
            MaxReportAttributeProbes = options.GetInt("max-report-probes", 32),
            ReadDataSetDirectories = options.GetBool("read-datasets", true),
            MaxDataSetDirectories = options.GetInt("max-datasets", 32)
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var client = new Iec61850Client();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: engineering-profile discovery (read-only capability assessment, no RCB writes).");

        var result = await client.DiscoverEngineeringProfileAsync(profileOptions, timeout.Token).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value == null)
        {
            foreach (var diagnostic in result.Diagnostics)
                Console.Error.WriteLine(diagnostic.Summary);
            return 2;
        }

        var profile = result.Value;
        Console.WriteLine(profile.Summary);
        Console.WriteLine();
        Console.WriteLine("Capabilities:");
        foreach (var capability in profile.Capabilities)
            Console.WriteLine($"  {capability.Summary}");

        Console.WriteLine();
        Console.WriteLine("Diagnostics:");
        foreach (var diagnostic in profile.Diagnostics)
            Console.WriteLine($"  {diagnostic.Summary}");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON profile: {Path.GetFullPath(jsonPath)}");
        }

        return 0;
    }


    private static async Task<int> MmsReportReadinessProfileAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-readiness-profile requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var durationSec = options.GetInt("duration-sec", 60);
        if (durationSec < 1)
            throw new ArgumentException("--duration-sec must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 20);
        var profileOptions = new Iec61850ReportReadinessProfileOptions
        {
            Host = host,
            Port = port,
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            ProbeReportAttributes = !options.GetBool("no-report-probe", false),
            MaxReportAttributeProbes = options.GetInt("max-report-probes", 286),
            ReadDataSetDirectories = options.GetBool("read-datasets", true),
            MaxDataSetDirectories = options.GetInt("max-datasets", 64),
            PreferredRcbReference = options.Get("rcb", string.Empty),
            PreferredDataSetReference = options.Get("dataset", string.Empty),
            StrictRcb = options.GetBool("strict-rcb", false),
            AllowUrCbFallback = options.GetBool("allow-urcb-fallback", true),
            AllowPollingFallback = options.GetBool("allow-polling-fallback", true),
            TriggerGeneralInterrogation = options.GetBool("gi", true),
            ListenDurationSeconds = durationSec
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var client = new Iec61850Client();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: static report readiness profile (read-only planning; no RCB writes).");

        var result = await client.DiscoverStaticReportReadinessProfileAsync(profileOptions, timeout.Token).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value == null)
        {
            foreach (var diagnostic in result.Diagnostics)
                Console.Error.WriteLine(diagnostic.Summary);
            return 2;
        }

        var profile = result.Value;
        Console.WriteLine(profile.Summary);
        Console.WriteLine();
        Console.WriteLine("Acceptance gates:");
        foreach (var gate in profile.AcceptanceGates)
            Console.WriteLine($"  {gate.Summary}");

        Console.WriteLine();
        Console.WriteLine("Selected plan:");
        Console.WriteLine($"  {profile.StaticPlan.Summary}");
        foreach (var blocker in profile.StaticPlan.Blockers)
            Console.WriteLine($"  BLOCKER {blocker}");
        foreach (var warning in profile.StaticPlan.Warnings)
            Console.WriteLine($"  WARNING {warning}");

        Console.WriteLine();
        Console.WriteLine("RCB candidates:");
        foreach (var candidate in TakeWithLimit(profile.Candidates, rawLimit))
            Console.WriteLine($"  {candidate.Summary}");
        WriteLimitNotice(profile.Candidates.Count, rawLimit, "RCB candidate(s)");

        Console.WriteLine();
        Console.WriteLine("Diagnostics:");
        foreach (var diagnostic in profile.Diagnostics)
            Console.WriteLine($"  {diagnostic.Summary}");

        if (options.TryGet("output", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        {
            EnsureOutputDirectory(markdownPath);
            File.WriteAllText(markdownPath, profile.ToMarkdown());
            Console.WriteLine($"Markdown report-readiness profile: {Path.GetFullPath(markdownPath)}");
        }

        if (options.TryGet("json", out var jsonPath) && !string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureOutputDirectory(jsonPath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON report-readiness profile: {Path.GetFullPath(jsonPath)}");
        }

        if (options.TryGet("session-json", out var sessionPath) && !string.IsNullOrWhiteSpace(sessionPath))
        {
            EnsureOutputDirectory(sessionPath);
            File.WriteAllText(sessionPath, JsonSerializer.Serialize(profile.SessionProfile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"JSON guarded session profile: {Path.GetFullPath(sessionPath)}");
        }

        return profile.IsReadyForGuardedLiveSession ? 0 : 3;
    }

    private static void EnsureOutputDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }



    private static async Task<int> MmsModelDiscoverAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-model-discover requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        var timeoutMs = options.GetInt("timeout-ms", 120000);
        var maxReportProbes = options.GetInt("max-report-probes", 286);
        var readDataSets = options.GetBool("read-datasets", true);
        var readTypes = options.GetBool("read-types", true);
        var maxTypeReads = options.GetInt("max-type-reads", 256);
        var typeReadSource = options.Get("type-read-source", "datasets");
        var output = options.Get("output", Path.Combine("out", "ied-model-discovery"));
        var iedName = options.Get("ied-name", string.Empty);
        var goldenProfileName = options.Get("golden-profile-name", string.IsNullOrWhiteSpace(iedName) ? "generic-safe-learned" : iedName);
        var apName = options.Get("ap-name", "AP1");

        await using var session = new MmsClientSession();
        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: live IED model discovery (read-only, no RCB writes).");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), cts.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        if (!string.IsNullOrWhiteSpace(session.LastHandshakeMessage))
            Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(
            probeReportAttributes: true,
            maxReportAttributeProbes: maxReportProbes,
            cancellationToken: cts.Token).ConfigureAwait(false);

        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);
        Console.WriteLine($"FC counts: {FormatFcCounts(discovery.IedDirectory.CountByFunctionalConstraint())}");

        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
        if (readDataSets && discovery.ReportInventory.DataSets.Count > 0)
        {
            Console.WriteLine($"Reading DataSet directories: {discovery.ReportInventory.DataSets.Count} candidate(s).");
            dataSetDirectories = await session.GetDataSetDirectoriesAsync(
                discovery.ReportInventory.DataSets.Select(x => x.Reference),
                discovery.IedDirectory,
                cts.Token).ConfigureAwait(false);

            foreach (var dataSet in dataSetDirectories.Take(10))
                Console.WriteLine($"  {(dataSet.IsSuccess ? "OK" : "FAIL")} {dataSet.Summary}");

            if (dataSetDirectories.Count > 10)
                Console.WriteLine($"  ... {dataSetDirectories.Count - 10} more DataSet result(s).");
        }

        IReadOnlyList<MmsVariableAccessAttributesResult> variableTypes = Array.Empty<MmsVariableAccessAttributesResult>();
        if (readTypes)
        {
            var typeCandidates = BuildTypeReadCandidates(discovery, dataSetDirectories, typeReadSource)
                .Take(maxTypeReads <= 0 ? int.MaxValue : maxTypeReads)
                .ToArray();

            Console.WriteLine($"Reading MMS variable access attributes: {typeCandidates.Length} candidate(s), source={typeReadSource}, max={maxTypeReads}.");
            variableTypes = await session.GetVariableAccessAttributesBatchAsync(typeCandidates, maxTypeReads, cts.Token).ConfigureAwait(false);
            foreach (var type in variableTypes.Take(10))
                Console.WriteLine($"  {(type.IsSuccess ? "OK" : "FAIL")} {type.Summary}");

            if (variableTypes.Count > 10)
                Console.WriteLine($"  ... {variableTypes.Count - 10} more type result(s).");
        }

        var document = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions
            {
                Host = host,
                Port = port,
                IedName = iedName,
                AccessPointName = apName
            },
            dataSetDirectories,
            variableTypes);

        var files = LiveIedModelDiscoveryExporter.WriteBundle(document, output);
        Console.WriteLine(document.Summary);
        Console.WriteLine(
            $"Coverage: LD={document.Coverage.LogicalDeviceCount}, LN={document.Coverage.LogicalNodeCount}, DO={document.Coverage.DataObjectCount}, DA={document.Coverage.DataAttributeCount}, exactTypes={document.Coverage.ExactMmsTypeCount}/{document.Coverage.VariableTypeReadAttemptCount}, highCDC={document.Coverage.HighConfidenceCdcCount}, mediumCDC={document.Coverage.MediumConfidenceCdcCount}, unknownCDC={document.Coverage.UnknownCdcCount}, GoCB={document.Coverage.GooseControlBlockCount}, SVCB={document.Coverage.SampledValueControlBlockCount}, SGCB={document.Coverage.SettingGroupControlCount}, LCB={document.Coverage.LogControlCount}.");
        Console.WriteLine("Discovery evidence written:");
        foreach (var file in files)
            Console.WriteLine($"  {Path.GetFullPath(file)}");

        return 0;
    }




    private static async Task<int> MmsServiceDiscoverAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-service-discover requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        var timeoutMs = options.GetInt("timeout-ms", 120000);
        var maxReportProbes = options.GetInt("max-report-probes", 286);
        var readDataSets = options.GetBool("read-datasets", true);
        var readTypes = options.GetBool("read-types", false);
        var maxTypeReads = options.GetInt("max-type-reads", 128);
        var typeReadSource = options.Get("type-read-source", "datasets");
        var typeReadStrategy = options.Get("type-read-strategy", "safe");
        var typeReadDelayMs = options.GetInt("type-read-delay-ms", 20);
        var typeReadIsolated = options.GetBool("type-read-isolated", true);
        var typeReadQuarantine = options.GetBool("type-read-quarantine", true);
        var learnTypesFromGolden = options.GetBool("learn-types-from-golden", true);
        var goldenScl = options.Get("golden-scl", string.Empty);
        var goldenConflictPolicy = options.Get("golden-learning-conflict-policy", "review-only");
        var readFiles = options.GetBool("read-files", true);
        var fileDirectory = options.Get("file-directory", string.Empty);
        var maxFilePages = options.GetInt("max-file-pages", 8);
        var readSettingGroups = options.GetBool("read-setting-groups", true);
        var readSettingValues = options.GetBool("read-setting-values", false);
        var maxSettingReads = options.GetInt("max-setting-reads", 256);
        var settingReadDelayMs = options.GetInt("setting-read-delay-ms", 10);
        var iedName = options.Get("ied-name", string.Empty);
        var goldenProfileName = options.Get("golden-profile-name", string.IsNullOrWhiteSpace(iedName) ? "generic-safe-learned" : iedName);
        var apName = options.Get("ap-name", "AP1");
        var output = options.Get("output", Path.Combine("out", "service-discovery"));

        await using var session = new MmsClientSession();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: full online service discovery coverage (read-only; no RCB writes).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), cts.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        if (!string.IsNullOrWhiteSpace(session.LastHandshakeMessage))
            Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(
            probeReportAttributes: true,
            maxReportAttributeProbes: maxReportProbes,
            cancellationToken: cts.Token).ConfigureAwait(false);

        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);
        Console.WriteLine($"FC counts: {FormatFcCounts(discovery.IedDirectory.CountByFunctionalConstraint())}");

        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
        if (readDataSets && discovery.ReportInventory.DataSets.Count > 0)
        {
            Console.WriteLine($"Reading DataSet directories: {discovery.ReportInventory.DataSets.Count} candidate(s).");
            dataSetDirectories = await session.GetDataSetDirectoriesAsync(
                discovery.ReportInventory.DataSets.Select(x => x.Reference),
                discovery.IedDirectory,
                cts.Token).ConfigureAwait(false);
            foreach (var dataSet in dataSetDirectories.Take(10))
                Console.WriteLine($"  {(dataSet.IsSuccess ? "OK" : "FAIL")} {dataSet.Summary}");
            if (dataSetDirectories.Count > 10)
                Console.WriteLine($"  ... {dataSetDirectories.Count - 10} more DataSet result(s).");
        }

        LiveIedFileServiceEvidence fileEvidence = new();
        if (readFiles)
        {
            Console.WriteLine($"Reading MMS FileDirectory: dir='{(string.IsNullOrWhiteSpace(fileDirectory) ? "/" : fileDirectory)}', maxPages={maxFilePages}.");
            var fileResults = await session.GetFileDirectoryPagedAsync(fileDirectory, maxFilePages, cts.Token).ConfigureAwait(false);
            fileEvidence = BuildFileServiceEvidence(fileDirectory, fileResults);
            Console.WriteLine($"  {(fileEvidence.IsSuccess ? "OK" : "FAIL")} FileDirectory entries={fileEvidence.Entries.Count}, pages={fileEvidence.PageCount}: {fileEvidence.Message}");
        }

        IReadOnlyList<MmsVariableAccessAttributesResult> variableTypes = Array.Empty<MmsVariableAccessAttributesResult>();
        LiveIedVariableTypeProbeEvidence variableTypeProbe = new()
        {
            Attempted = false,
            Source = typeReadSource,
            Strategy = typeReadStrategy,
            MaxReads = maxTypeReads,
            DelayMs = typeReadDelayMs,
            Summary = "Variable specification probe was not attempted in this run."
        };
        LiveIedVariableSpecQuarantineEvidence variableSpecQuarantine = new()
        {
            IsEnabled = typeReadQuarantine,
            TargetKey = $"{host}:{port}/{iedName}",
            Summary = typeReadQuarantine
                ? "Variable specification quarantine is enabled but has not been triggered."
                : "Variable specification quarantine is disabled for this run."
        };

        var document = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions
            {
                Host = host,
                Port = port,
                IedName = iedName,
                AccessPointName = apName
            },
            dataSetDirectories,
            variableTypes);

        IReadOnlyList<LiveIedSettingGroupReadbackEvidence> settingGroupReadbacks = Array.Empty<LiveIedSettingGroupReadbackEvidence>();
        if (readSettingGroups && document.SettingGroupControls.Count > 0 && session.IsMmsInitiated)
        {
            Console.WriteLine($"Reading Setting Group control attributes: {document.SettingGroupControls.Count} candidate(s).");
            settingGroupReadbacks = await ReadSettingGroupReadbacksAsync(session, document.SettingGroupControls, cts.Token).ConfigureAwait(false);
            foreach (var readback in settingGroupReadbacks.Take(10))
                Console.WriteLine($"  SG {(readback.HasAnySuccess ? "OK" : "INFO")} {readback.Reference}: {readback.Attributes.Count(x => x.IsSuccess)}/{readback.Attributes.Count} readable");
        }

        var settingGroupMap = await BuildSettingGroupMapAsync(
            session,
            document,
            settingGroupReadbacks,
            readSettingValues,
            maxSettingReads,
            settingReadDelayMs,
            cts.Token).ConfigureAwait(false);
        if (settingGroupMap.EntryCount > 0)
        {
            var readText = settingGroupMap.ReadAttemptCount > 0
                ? $", setting value reads={settingGroupMap.ReadSuccessCount}/{settingGroupMap.ReadAttemptCount}"
                : ", setting value reads=not attempted";
            Console.WriteLine($"Setting Group map: entries={settingGroupMap.EntryCount}{readText}.");
        }

        if (readTypes)
        {
            var rawTypeCandidates = BuildTypeReadCandidates(discovery, dataSetDirectories, typeReadSource).ToArray();
            var typeEvaluations = BuildTypeReadCandidateEvaluations(rawTypeCandidates, typeReadStrategy).ToArray();
            var typeCandidates = typeEvaluations
                .Where(x => x.IsSelected)
                .Select(x => x.Reference)
                .Take(maxTypeReads <= 0 ? int.MaxValue : maxTypeReads)
                .ToArray();
            Console.WriteLine($"Reading MMS variable access attributes {(typeReadIsolated ? "in isolated association" : "on main association")}: {typeCandidates.Length} selected from {rawTypeCandidates.Length} candidate(s), source={typeReadSource}, strategy={typeReadStrategy}, max={maxTypeReads}, delay={typeReadDelayMs}ms.");
            variableTypes = typeReadIsolated
                ? await ReadVariableAccessAttributesIsolatedAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), typeCandidates, maxTypeReads, typeReadDelayMs, cts.Token).ConfigureAwait(false)
                : await ReadVariableAccessAttributesSafelyAsync(session, typeCandidates, maxTypeReads, typeReadDelayMs, cts.Token).ConfigureAwait(false);
            variableTypeProbe = BuildVariableTypeProbeEvidence(typeReadSource, typeReadStrategy, maxTypeReads, typeReadDelayMs, typeEvaluations, typeCandidates, variableTypes);
            variableSpecQuarantine = BuildVariableSpecQuarantineEvidence(variableTypeProbe, typeReadQuarantine, host, port, iedName, typeReadIsolated);
            Console.WriteLine($"  Type probe: {variableTypeProbe.Summary}");
            if (variableSpecQuarantine.IsQuarantined)
                Console.WriteLine($"  Variable specification quarantined for this IED/session: {variableSpecQuarantine.TriggerReference} ({variableSpecQuarantine.Reason}).");
            foreach (var type in variableTypes.Take(10))
                Console.WriteLine($"  {(type.IsSuccess ? "OK" : "FAIL")} {type.Summary}");
            if (variableTypes.Count > 10)
                Console.WriteLine($"  ... {variableTypes.Count - 10} more type result(s).");

            if (variableTypes.Any(x => x.IsSuccess))
            {
                document = LiveIedModelDiscoveryBuilder.Build(
                    discovery,
                    new LiveIedModelDiscoveryBuildOptions
                    {
                        Host = host,
                        Port = port,
                        IedName = iedName,
                        AccessPointName = apName
                    },
                    dataSetDirectories,
                    variableTypes);
            }
        }

        var goldenLearning = learnTypesFromGolden
            ? BuildGoldenSclTypeLearningEvidence(document, ResolveGoldenSclPath(goldenScl, iedName))
            : new LiveIedGoldenSclTypeLearningEvidence
            {
                Attempted = false,
                Message = "Golden SCL type learning was disabled by --learn-types-from-golden false.",
                Summary = "Golden SCL type learning disabled."
            };
        if (goldenLearning.Attempted)
            Console.WriteLine($"Golden SCL type learning: {goldenLearning.Summary}");

        var goldenPromotion = BuildGoldenSclRegistryPromotionEvidence(goldenLearning, goldenProfileName, goldenConflictPolicy);
        if (goldenPromotion.Attempted)
            Console.WriteLine($"Golden registry promotion: {goldenPromotion.Summary}");

        var onlineEvidence = new LiveIedOnlineServiceEvidence
        {
            FileService = fileEvidence,
            SettingGroupReadbacks = settingGroupReadbacks,
            SettingGroupMap = settingGroupMap,
            VariableTypeProbe = variableTypeProbe,
            VariableSpecQuarantine = variableSpecQuarantine,
            GoldenSclTypeLearning = goldenLearning,
            GoldenSclRegistryPromotion = goldenPromotion
        };

        var files = new List<string>();
        files.AddRange(LiveIedModelDiscoveryExporter.WriteBundle(document, output));
        files.AddRange(WriteOnlineServiceEvidenceFiles(onlineEvidence, output));
        files.AddRange(LiveIedServiceDiscoveryReportBuilder.WriteFiles(document, output, onlineEvidence));

        var coverage = LiveIedServiceDiscoveryReportBuilder.Build(document, onlineEvidence);
        Console.WriteLine("Service discovery complete.");
        Console.WriteLine(document.Summary);
        foreach (var item in coverage.Services)
            Console.WriteLine($"  {item.Name}: {item.Status} count={item.Count} evidence={item.Evidence}");
        Console.WriteLine("Evidence written:");
        foreach (var file in files)
            Console.WriteLine($"  {Path.GetFullPath(file)}");

        return 0;
    }


    private static LiveIedFileServiceEvidence BuildFileServiceEvidence(string directoryName, IReadOnlyList<MmsFileDirectoryResult> results)
    {
        var last = results.LastOrDefault();
        var success = results.Count > 0 && results.Any(x => x.IsSuccess);
        var entries = results
            .Where(x => x.IsSuccess)
            .SelectMany(x => x.Entries)
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => new LiveIedFileEntryEvidence
            {
                Name = x.Name,
                Path = x.Path,
                SizeBytes = x.SizeBytes,
                LastModifiedRaw = x.LastModifiedDisplay,
                IsLikelyDirectory = x.IsLikelyDirectory
            })
            .ToArray();

        return new LiveIedFileServiceEvidence
        {
            DirectoryName = directoryName,
            Attempted = true,
            IsSuccess = success,
            PageCount = results.Count,
            MoreFollows = last?.MoreFollows ?? false,
            Entries = entries,
            Message = last?.Message ?? "FileDirectory was attempted but no response was recorded."
        };
    }

    private static IReadOnlyList<string> WriteOnlineServiceEvidenceFiles(LiveIedOnlineServiceEvidence evidence, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var files = new List<string>();

        var filePath = Path.Combine(outputDirectory, "file-inventory.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(evidence.FileService, jsonOptions));
        files.Add(filePath);

        var settingPath = Path.Combine(outputDirectory, "setting-group-readback.json");
        File.WriteAllText(settingPath, JsonSerializer.Serialize(evidence.SettingGroupReadbacks, jsonOptions));
        files.Add(settingPath);

        var settingMapPath = Path.Combine(outputDirectory, "setting-group-map.json");
        File.WriteAllText(settingMapPath, JsonSerializer.Serialize(evidence.SettingGroupMap, jsonOptions));
        files.Add(settingMapPath);

        var settingMapMarkdownPath = Path.Combine(outputDirectory, "setting-group-map.md");
        File.WriteAllText(settingMapMarkdownPath, BuildSettingGroupMapMarkdown(evidence.SettingGroupMap));
        files.Add(settingMapMarkdownPath);

        var typeProbePath = Path.Combine(outputDirectory, "safe-variable-spec-probe.json");
        File.WriteAllText(typeProbePath, JsonSerializer.Serialize(evidence.VariableTypeProbe, jsonOptions));
        files.Add(typeProbePath);

        var typeProbeMarkdownPath = Path.Combine(outputDirectory, "safe-variable-spec-probe.md");
        File.WriteAllText(typeProbeMarkdownPath, BuildVariableTypeProbeMarkdown(evidence.VariableTypeProbe));
        files.Add(typeProbeMarkdownPath);

        var quarantinePath = Path.Combine(outputDirectory, "variable-spec-quarantine.json");
        File.WriteAllText(quarantinePath, JsonSerializer.Serialize(evidence.VariableSpecQuarantine, jsonOptions));
        files.Add(quarantinePath);

        var quarantineMarkdownPath = Path.Combine(outputDirectory, "variable-spec-quarantine.md");
        File.WriteAllText(quarantineMarkdownPath, BuildVariableSpecQuarantineMarkdown(evidence.VariableSpecQuarantine));
        files.Add(quarantineMarkdownPath);

        var goldenLearningPath = Path.Combine(outputDirectory, "golden-scl-type-learning.json");
        File.WriteAllText(goldenLearningPath, JsonSerializer.Serialize(evidence.GoldenSclTypeLearning, jsonOptions));
        files.Add(goldenLearningPath);

        var goldenLearningMarkdownPath = Path.Combine(outputDirectory, "golden-scl-type-learning.md");
        File.WriteAllText(goldenLearningMarkdownPath, BuildGoldenSclTypeLearningMarkdown(evidence.GoldenSclTypeLearning));
        files.Add(goldenLearningMarkdownPath);

        var goldenPromotionPath = Path.Combine(outputDirectory, "golden-learning-registry-promotion.json");
        File.WriteAllText(goldenPromotionPath, JsonSerializer.Serialize(evidence.GoldenSclRegistryPromotion, jsonOptions));
        files.Add(goldenPromotionPath);

        var goldenPromotionMarkdownPath = Path.Combine(outputDirectory, "golden-learning-registry-promotion.md");
        File.WriteAllText(goldenPromotionMarkdownPath, BuildGoldenSclRegistryPromotionMarkdown(evidence.GoldenSclRegistryPromotion));
        files.Add(goldenPromotionMarkdownPath);

        var goldenRegistryPath = Path.Combine(outputDirectory, "golden-learned-cdc-registry.json");
        File.WriteAllText(goldenRegistryPath, JsonSerializer.Serialize(BuildGoldenLearnedCdcRegistryDocument(evidence.GoldenSclRegistryPromotion), jsonOptions));
        files.Add(goldenRegistryPath);

        return files;
    }



    private static string BuildVariableSpecQuarantineMarkdown(LiveIedVariableSpecQuarantineEvidence quarantine)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# IEC 61850 Variable Specification Quarantine");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {quarantine.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Enabled: {(quarantine.IsEnabled ? "true" : "false")}");
        sb.AppendLine($"- Quarantined: {(quarantine.IsQuarantined ? "true" : "false")}");
        sb.AppendLine($"- Scope: {EscapeMarkdown(quarantine.Scope)}");
        sb.AppendLine($"- Target: {EscapeMarkdown(quarantine.TargetKey)}");
        sb.AppendLine($"- Trigger reference: {EscapeMarkdown(quarantine.TriggerReference)}");
        sb.AppendLine($"- Reason: {EscapeMarkdown(quarantine.Reason)}");
        sb.AppendLine($"- Core discovery preserved: {(quarantine.CoreDiscoveryPreserved ? "true" : "false")}");
        sb.AppendLine($"- Summary: {EscapeMarkdown(quarantine.Summary)}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(quarantine.TriggerMessage))
        {
            sb.AppendLine("## Trigger message");
            sb.AppendLine();
            sb.AppendLine(EscapeMarkdown(quarantine.TriggerMessage));
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(quarantine.Recommendation))
        {
            sb.AppendLine("## Recommendation");
            sb.AppendLine();
            sb.AppendLine(EscapeMarkdown(quarantine.Recommendation));
        }
        return sb.ToString();
    }

    private static string BuildGoldenSclTypeLearningMarkdown(LiveIedGoldenSclTypeLearningEvidence learning)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# IEC 61850 Golden SCL Type Learning");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {learning.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Attempted: {(learning.Attempted ? "true" : "false")}");
        sb.AppendLine($"- Success: {(learning.IsSuccess ? "true" : "false")}");
        sb.AppendLine($"- Golden SCL: {EscapeMarkdown(learning.GoldenSclPath)}");
        sb.AppendLine($"- Golden bindings: {learning.GoldenBindingCount}");
        sb.AppendLine($"- Live data objects: {learning.LiveDataObjectCount}");
        sb.AppendLine($"- Live unknown/medium: {learning.LiveUnknownOrMediumCount}");
        sb.AppendLine($"- Exact key matches: {learning.ExactKeyMatchCount}");
        sb.AppendLine($"- Candidate improvements: {learning.CandidateImprovementCount}");
        sb.AppendLine($"- CDC conflicts: {learning.CdcConflictCount}");
        sb.AppendLine($"- Summary: {EscapeMarkdown(learning.Summary)}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(learning.Message))
        {
            sb.AppendLine("## Message");
            sb.AppendLine();
            sb.AppendLine(EscapeMarkdown(learning.Message));
            sb.AppendLine();
        }
        if (learning.Candidates.Count > 0)
        {
            sb.AppendLine("## Learning candidates");
            sb.AppendLine();
            sb.AppendLine("| Reference | Current CDC | Golden CDC | Golden DOType | Confidence | Action |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var candidate in learning.Candidates.Take(200))
                sb.AppendLine($"| {EscapeMarkdown(candidate.Reference)} | {EscapeMarkdown(candidate.CurrentCdc)} | {EscapeMarkdown(candidate.GoldenCdc)} | {EscapeMarkdown(candidate.GoldenDoTypeId)} | {EscapeMarkdown(candidate.CurrentConfidence)} | {EscapeMarkdown(candidate.SuggestedAction)} |");
            sb.AppendLine();
        }
        if (learning.Conflicts.Count > 0)
        {
            sb.AppendLine("## CDC conflicts");
            sb.AppendLine();
            sb.AppendLine("| Key | Live CDC | Golden CDC | Reference | Notes |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var conflict in learning.Conflicts.Take(120))
                sb.AppendLine($"| {EscapeMarkdown(conflict.Key)} | {EscapeMarkdown(conflict.LiveCdc)} | {EscapeMarkdown(conflict.GoldenCdc)} | {EscapeMarkdown(conflict.Reference)} | {EscapeMarkdown(conflict.Notes)} |");
        }
        return sb.ToString();
    }

    private static string BuildGoldenSclRegistryPromotionMarkdown(LiveIedGoldenSclRegistryPromotionEvidence promotion)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# IEC 61850 Golden Learning Registry Promotion");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {promotion.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Attempted: {(promotion.Attempted ? "true" : "false")}");
        sb.AppendLine($"- Success: {(promotion.IsSuccess ? "true" : "false")}");
        sb.AppendLine($"- Profile: {EscapeMarkdown(promotion.ProfileName)}");
        sb.AppendLine($"- Conflict policy: {EscapeMarkdown(promotion.ConflictPolicy)}");
        sb.AppendLine($"- Candidates: {promotion.CandidateCount}");
        sb.AppendLine($"- Applied promotions: {promotion.AppliedPromotionCount}");
        sb.AppendLine($"- Review conflicts: {promotion.ReviewConflictCount}");
        sb.AppendLine($"- Generated registry entries: {promotion.GeneratedRegistryEntryCount}");
        sb.AppendLine($"- Summary: {EscapeMarkdown(promotion.Summary)}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(promotion.Message))
        {
            sb.AppendLine("## Message");
            sb.AppendLine();
            sb.AppendLine(EscapeMarkdown(promotion.Message));
            sb.AppendLine();
        }

        if (promotion.AppliedPromotions.Count > 0)
        {
            sb.AppendLine("## Applied promotions");
            sb.AppendLine();
            sb.AppendLine("| Key | LN class | DO | Previous CDC | Promoted CDC | Confidence | Golden DOType | Action | Reference |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var entry in promotion.AppliedPromotions.Take(200))
            {
                sb.AppendLine($"| {EscapeMarkdown(entry.Key)} | {EscapeMarkdown(entry.LogicalNodeClass)} | {EscapeMarkdown(entry.DataObjectName)} | {EscapeMarkdown(entry.PreviousCdc)} | {EscapeMarkdown(entry.PromotedCdc)} | {EscapeMarkdown(entry.PromotedConfidence)} | {EscapeMarkdown(entry.GoldenDoTypeId)} | {EscapeMarkdown(entry.Action)} | {EscapeMarkdown(entry.Reference)} |");
            }

            if (promotion.AppliedPromotions.Count > 200)
                sb.AppendLine($"| ... | ... | ... | ... | ... | ... | ... | ... | {promotion.AppliedPromotions.Count - 200} more promotion(s) in golden-learning-registry-promotion.json |");

            sb.AppendLine();
        }

        if (promotion.ReviewConflicts.Count > 0)
        {
            sb.AppendLine("## Review conflicts");
            sb.AppendLine();
            sb.AppendLine("| Key | Reference | Live CDC | Golden CDC | Policy | Recommendation |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var conflict in promotion.ReviewConflicts.Take(120))
            {
                sb.AppendLine($"| {EscapeMarkdown(conflict.Key)} | {EscapeMarkdown(conflict.Reference)} | {EscapeMarkdown(conflict.LiveCdc)} | {EscapeMarkdown(conflict.GoldenCdc)} | {EscapeMarkdown(conflict.Policy)} | {EscapeMarkdown(conflict.Recommendation)} |");
            }

            if (promotion.ReviewConflicts.Count > 120)
                sb.AppendLine($"| ... | ... | ... | ... | ... | {promotion.ReviewConflicts.Count - 120} more conflict(s) in golden-learning-registry-promotion.json |");
        }

        if (promotion.AppliedPromotions.Count == 0 && promotion.ReviewConflicts.Count == 0)
        {
            sb.AppendLine("## Registry output");
            sb.AppendLine();
            sb.AppendLine("No learned CDC registry entries were generated for this run.");
        }

        return sb.ToString();
    }

    private static GoldenLearnedCdcRegistryDocument BuildGoldenLearnedCdcRegistryDocument(LiveIedGoldenSclRegistryPromotionEvidence promotion)
    {
        var entries = promotion.AppliedPromotions
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => new GoldenLearnedCdcRegistryEntry
            {
                Key = x.Key,
                ProfileName = promotion.ProfileName,
                LogicalNodeClass = x.LogicalNodeClass,
                DataObjectName = x.DataObjectName,
                Cdc = x.PromotedCdc,
                Confidence = x.PromotedConfidence,
                GoldenDoTypeId = x.GoldenDoTypeId,
                SourceReference = x.Reference,
                PreviousCdc = x.PreviousCdc,
                PreviousConfidence = x.PreviousConfidence,
                Action = x.Action,
                Source = "GoldenSclRegistryPromotion"
            })
            .ToArray();

        return new GoldenLearnedCdcRegistryDocument
        {
            GeneratedAtUtc = promotion.GeneratedAtUtc,
            Attempted = promotion.Attempted,
            IsSuccess = promotion.IsSuccess,
            ProfileName = promotion.ProfileName,
            ConflictPolicy = promotion.ConflictPolicy,
            CandidateCount = promotion.CandidateCount,
            EntryCount = entries.Length,
            ReviewConflictCount = promotion.ReviewConflictCount,
            Entries = entries,
            ReviewConflicts = promotion.ReviewConflicts
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Message = promotion.Message,
            Summary = promotion.Summary
        };
    }

    private static string BuildVariableTypeProbeMarkdown(LiveIedVariableTypeProbeEvidence probe)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# IEC 61850 Safe Variable Specification Probe");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {probe.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Attempted: {(probe.Attempted ? "true" : "false")}");
        sb.AppendLine($"- Source: {EscapeMarkdown(probe.Source)}");
        sb.AppendLine($"- Strategy: {EscapeMarkdown(probe.Strategy)}");
        sb.AppendLine($"- Max reads: {probe.MaxReads}");
        sb.AppendLine($"- Delay: {probe.DelayMs} ms");
        sb.AppendLine($"- Raw candidates: {probe.RawCandidateCount}");
        sb.AppendLine($"- Selected candidates: {probe.SelectedCandidateCount}");
        sb.AppendLine($"- Skipped candidates: {probe.SkippedCandidateCount}");
        sb.AppendLine($"- Results: {probe.SuccessCount}/{probe.AttemptCount} successful, failures={probe.FailureCount}");
        sb.AppendLine($"- Exact scalar types: {probe.ExactScalarTypeCount}");
        sb.AppendLine($"- Exact structure types: {probe.ExactStructureTypeCount}");
        sb.AppendLine($"- Stopped early: {(probe.StoppedBeforeCandidateExhausted ? "true" : "false")}");
        sb.AppendLine($"- Protocol fault suspected: {(probe.ProtocolFaultSuspected ? "true" : "false")}");
        sb.AppendLine($"- Summary: {EscapeMarkdown(probe.Summary)}");
        sb.AppendLine();

        if (probe.SkippedByReason.Count > 0)
        {
            sb.AppendLine("## Skipped candidates by reason");
            sb.AppendLine();
            sb.AppendLine("| Reason | Count |");
            sb.AppendLine("| --- | ---: |");
            foreach (var item in probe.SkippedByReason)
                sb.AppendLine($"| {EscapeMarkdown(item.Reason)} | {item.Count} |");
            sb.AppendLine();
        }

        if (probe.Results.Count > 0)
        {
            sb.AppendLine("## Probe results");
            sb.AppendLine();
            sb.AppendLine("| Reference | Result | MMS type | SCL bType | Message |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var result in probe.Results.Take(120))
            {
                var status = result.IsSuccess ? "OK" : "FAIL";
                sb.AppendLine($"| {EscapeMarkdown(result.Reference)} | {status} | {EscapeMarkdown(result.MmsType)} | {EscapeMarkdown(result.SclBType)} | {EscapeMarkdown(result.Message)} |");
            }

            if (probe.Results.Count > 120)
                sb.AppendLine($"| ... | ... | ... | ... | {probe.Results.Count - 120} more result(s) in safe-variable-spec-probe.json | ");
        }

        return sb.ToString();
    }

    private static async Task<LiveIedSettingGroupMapDocument> BuildSettingGroupMapAsync(
        MmsClientSession session,
        LiveIedModelDiscoveryDocument document,
        IReadOnlyList<LiveIedSettingGroupReadbackEvidence> readbacks,
        bool readValues,
        int maxReads,
        int readDelayMs,
        CancellationToken cancellationToken)
    {
        var entries = new List<LiveIedSettingGroupMapEntry>();
        var limit = maxReads <= 0 ? int.MaxValue : maxReads;
        var attempted = 0;
        var success = 0;
        var failure = 0;

        foreach (var entry in EnumerateSettingGroupMapEntries(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = entry;
            if (readValues && attempted < limit && session.IsMmsInitiated)
            {
                var reference = new MmsObjectReference(entry.Domain, entry.MmsItemName, entry.FunctionalConstraint);
                var read = await session.ReadSingleVariableAsync(reference, cancellationToken).ConfigureAwait(false);
                attempted++;
                if (read.IsSuccess)
                    success++;
                else
                    failure++;

                mapped = CopySettingGroupMapEntry(entry, read.IsSuccess, read.IsSuccess ? MmsDataValueRenderer.ToCompactString(read.Value, reference.ToString()) : string.Empty, read.Message);

                if (readDelayMs > 0 && attempted < limit)
                    await Task.Delay(readDelayMs, cancellationToken).ConfigureAwait(false);
            }

            entries.Add(mapped);
        }

        var coreComplete = readbacks.Count(IsSettingGroupCoreReadbackCompleteForMap);
        var numOfSg = TryGetFirstIntReadback(readbacks, "NumOfSG");
        var actSg = TryGetFirstIntReadback(readbacks, "ActSG");
        var editSg = TryGetFirstIntReadback(readbacks, "EditSG");
        var cnfEdit = TryGetFirstBoolReadback(readbacks, "CnfEdit");
        var summary = $"SGCB core complete={coreComplete}/{Math.Max(readbacks.Count, document.Coverage.SettingGroupControlCount)}, SG/SE entries={entries.Count}, reads={success}/{attempted}.";

        return new LiveIedSettingGroupMapDocument
        {
            Summary = summary,
            SettingGroupControlCount = document.Coverage.SettingGroupControlCount,
            CoreReadbackCompleteCount = coreComplete,
            NumberOfSettingGroups = numOfSg ?? 0,
            ActiveSettingGroup = actSg ?? 0,
            EditSettingGroup = editSg ?? 0,
            ConfirmEdit = cnfEdit,
            EntryCount = entries.Count,
            ReadAttemptCount = attempted,
            ReadSuccessCount = success,
            ReadFailureCount = failure,
            Entries = entries
                .OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DataObject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.AttributePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IEnumerable<LiveIedSettingGroupMapEntry> EnumerateSettingGroupMapEntries(LiveIedModelDiscoveryDocument document)
    {
        foreach (var ld in document.LogicalDevices)
        {
            foreach (var ln in ld.LogicalNodes)
            {
                foreach (var dataObject in ln.DataObjects)
                {
                    foreach (var attribute in dataObject.Attributes.Where(IsSettingGroupAttribute))
                    {
                        var category = string.Equals(attribute.FunctionalConstraint, "SE", StringComparison.OrdinalIgnoreCase)
                            ? "EditableSettingValue"
                            : "SettingGroupValue";
                        yield return new LiveIedSettingGroupMapEntry
                        {
                            Reference = attribute.ObjectReference,
                            Domain = ld.MmsDomain,
                            LogicalNode = ln.Name,
                            LogicalNodeClass = ln.LnClass,
                            DataObject = dataObject.Name,
                            AttributePath = attribute.AttributePath,
                            FunctionalConstraint = attribute.FunctionalConstraint,
                            Category = category,
                            MmsReference = attribute.MmsReference,
                            MmsItemName = attribute.MmsItemName,
                            InferredCdc = dataObject.InferredCdc,
                            CdcConfidence = dataObject.CdcConfidence,
                            SclBType = attribute.SclBType,
                            TypeSource = attribute.TypeSource,
                            Message = "Setting value read not attempted."
                        };
                    }
                }
            }
        }
    }

    private static bool IsSettingGroupAttribute(LiveIedDataAttributeModel attribute)
        => string.Equals(attribute.FunctionalConstraint, "SG", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(attribute.FunctionalConstraint, "SE", StringComparison.OrdinalIgnoreCase);

    private static LiveIedSettingGroupMapEntry CopySettingGroupMapEntry(
        LiveIedSettingGroupMapEntry source,
        bool isSuccess,
        string value,
        string message)
        => new()
        {
            Reference = source.Reference,
            Domain = source.Domain,
            LogicalNode = source.LogicalNode,
            LogicalNodeClass = source.LogicalNodeClass,
            DataObject = source.DataObject,
            AttributePath = source.AttributePath,
            FunctionalConstraint = source.FunctionalConstraint,
            Category = source.Category,
            MmsReference = source.MmsReference,
            MmsItemName = source.MmsItemName,
            InferredCdc = source.InferredCdc,
            CdcConfidence = source.CdcConfidence,
            SclBType = source.SclBType,
            TypeSource = source.TypeSource,
            ReadAttempted = true,
            IsReadSuccess = isSuccess,
            Value = value,
            Message = message
        };

    private static bool IsSettingGroupCoreReadbackCompleteForMap(LiveIedSettingGroupReadbackEvidence readback)
    {
        var required = new[] { "NumOfSG", "ActSG", "EditSG", "CnfEdit", "LActTm" };
        return required.All(name => readback.Attributes.Any(attribute =>
            attribute.IsSuccess && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static int? TryGetFirstIntReadback(IReadOnlyList<LiveIedSettingGroupReadbackEvidence> readbacks, string attributeName)
    {
        foreach (var value in readbacks
            .SelectMany(x => x.Attributes)
            .Where(x => x.IsSuccess && string.Equals(x.Name, attributeName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool? TryGetFirstBoolReadback(IReadOnlyList<LiveIedSettingGroupReadbackEvidence> readbacks, string attributeName)
    {
        foreach (var value in readbacks
            .SelectMany(x => x.Attributes)
            .Where(x => x.IsSuccess && string.Equals(x.Name, attributeName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value))
        {
            if (bool.TryParse(value, out var parsed))
                return parsed;
        }

        return null;
    }

    private static string BuildSettingGroupMapMarkdown(LiveIedSettingGroupMapDocument map)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# IEC 61850 Setting Group Map");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {map.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Summary: {EscapeMarkdown(map.Summary)}");
        sb.AppendLine($"- NumOfSG: {map.NumberOfSettingGroups}");
        sb.AppendLine($"- ActSG: {map.ActiveSettingGroup}");
        sb.AppendLine($"- EditSG: {map.EditSettingGroup}");
        sb.AppendLine($"- CnfEdit: {(map.ConfirmEdit.HasValue ? (map.ConfirmEdit.Value ? "true" : "false") : "-")}");
        sb.AppendLine($"- Entries: {map.EntryCount}");
        sb.AppendLine($"- Setting value reads: {map.ReadSuccessCount}/{map.ReadAttemptCount}");
        sb.AppendLine();
        sb.AppendLine("| Reference | FC | CDC | bType | Read | Value |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var entry in map.Entries.Take(200))
        {
            var read = !entry.ReadAttempted ? "not attempted" : entry.IsReadSuccess ? "OK" : "FAIL";
            sb.AppendLine($"| {EscapeMarkdown(entry.Reference)} | {EscapeMarkdown(entry.FunctionalConstraint)} | {EscapeMarkdown(entry.InferredCdc)} | {EscapeMarkdown(entry.SclBType)} | {read} | {EscapeMarkdown(entry.Value)} |");
        }

        if (map.Entries.Count > 200)
        {
            sb.AppendLine();
            sb.AppendLine($"_Only the first 200 setting entries are shown. Full evidence is available in `setting-group-map.json`._");
        }

        return sb.ToString();
    }

    private static string EscapeMarkdown(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private sealed class GoldenLearnedCdcRegistryDocument
    {
        public string SchemaVersion { get; init; } = "ariec61850.golden-learned-cdc-registry.v1";
        public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
        public bool Attempted { get; init; }
        public bool IsSuccess { get; init; }
        public string ProfileName { get; init; } = string.Empty;
        public string ConflictPolicy { get; init; } = "review-only";
        public int CandidateCount { get; init; }
        public int EntryCount { get; init; }
        public int ReviewConflictCount { get; init; }
        public IReadOnlyList<GoldenLearnedCdcRegistryEntry> Entries { get; init; } = Array.Empty<GoldenLearnedCdcRegistryEntry>();
        public IReadOnlyList<LiveIedGoldenSclRegistryPromotionConflict> ReviewConflicts { get; init; } = Array.Empty<LiveIedGoldenSclRegistryPromotionConflict>();
        public string Message { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
    }

    private sealed class GoldenLearnedCdcRegistryEntry
    {
        public string Key { get; init; } = string.Empty;
        public string ProfileName { get; init; } = string.Empty;
        public string LogicalNodeClass { get; init; } = string.Empty;
        public string DataObjectName { get; init; } = string.Empty;
        public string Cdc { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
        public string GoldenDoTypeId { get; init; } = string.Empty;
        public string SourceReference { get; init; } = string.Empty;
        public string PreviousCdc { get; init; } = string.Empty;
        public string PreviousConfidence { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
    }

    private sealed class TypeReadCandidateEvaluation
    {
        public MmsObjectReference Reference { get; init; }
        public bool IsSelected { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    private static IEnumerable<MmsObjectReference> ApplyTypeReadStrategy(IEnumerable<MmsObjectReference> candidates, string strategy)
        => BuildTypeReadCandidateEvaluations(candidates, strategy)
            .Where(x => x.IsSelected)
            .Select(x => x.Reference);

    private static IEnumerable<TypeReadCandidateEvaluation> BuildTypeReadCandidateEvaluations(IEnumerable<MmsObjectReference> candidates, string strategy)
    {
        var normalized = string.IsNullOrWhiteSpace(strategy) ? "safe" : strategy.Trim().ToLowerInvariant();
        foreach (var candidate in candidates)
            yield return EvaluateTypeReadCandidate(candidate, normalized);
    }

    private static TypeReadCandidateEvaluation EvaluateTypeReadCandidate(MmsObjectReference reference, string normalizedStrategy)
    {
        if (normalizedStrategy is "all" or "full")
        {
            return new TypeReadCandidateEvaluation
            {
                Reference = reference,
                IsSelected = true,
                Reason = "selected-by-full-strategy"
            };
        }

        var item = reference.Item ?? string.Empty;
        if (string.IsNullOrWhiteSpace(item))
            return ExcludedTypeCandidate(reference, "empty-mms-item");

        var upper = item.ToUpperInvariant();
        if (upper.Contains("$CO$", StringComparison.Ordinal) ||
            upper.Contains("$GO$", StringComparison.Ordinal) ||
            upper.Contains("$MS$", StringComparison.Ordinal) ||
            upper.Contains("$US$", StringComparison.Ordinal))
        {
            return ExcludedTypeCandidate(reference, "unsafe-functional-constraint");
        }

        var blockedSegments = new[]
        {
            "$OPER$", "$SBOW$", "$CANCEL$", "$ORIGIN", "$UNITS", "$CTLVAL", "$CTLNUM",
            "$CHECK", "$TEST", "$DB", "$ANGREF", "$SBOTIMEOUT", "$STSELD"
        };
        if (blockedSegments.Any(segment => upper.Contains(segment, StringComparison.Ordinal)))
            return ExcludedTypeCandidate(reference, "unsafe-control-or-optional-structure");

        // Leaf-only guard. Some IEDs close the association when GetVariableAccessAttributes
        // is requested on an FCD/DO level object such as PTOC1$ST$Op.
        if (item.Count(c => c == '$') < 3)
            return ExcludedTypeCandidate(reference, "not-leaf-variable");

        if (normalizedStrategy is "dataset-leaf" or "datasets-leaf" or "safe" or "leaf" or "leaf-only")
        {
            return new TypeReadCandidateEvaluation
            {
                Reference = reference,
                IsSelected = true,
                Reason = "selected-safe-leaf"
            };
        }

        // Unknown strategies deliberately fall back to safe behavior rather than full behavior.
        return new TypeReadCandidateEvaluation
        {
            Reference = reference,
            IsSelected = true,
            Reason = $"selected-safe-leaf-unknown-strategy-{normalizedStrategy}"
        };
    }

    private static TypeReadCandidateEvaluation ExcludedTypeCandidate(MmsObjectReference reference, string reason)
        => new()
        {
            Reference = reference,
            IsSelected = false,
            Reason = reason
        };

    private static LiveIedVariableTypeProbeEvidence BuildVariableTypeProbeEvidence(
        string source,
        string strategy,
        int maxReads,
        int delayMs,
        IReadOnlyList<TypeReadCandidateEvaluation> evaluations,
        IReadOnlyList<MmsObjectReference> selectedCandidates,
        IReadOnlyList<MmsVariableAccessAttributesResult> results)
    {
        var skipped = evaluations.Where(x => !x.IsSelected).ToArray();
        var maxLimit = maxReads <= 0 ? selectedCandidates.Count : Math.Min(selectedCandidates.Count, maxReads);
        var stoppedBeforeExhausted = results.Count < maxLimit;
        var protocolFault = stoppedBeforeExhausted || results.Any(IsVariableTypeProtocolFault);
        var success = results.Count(x => x.IsSuccess);
        var failure = results.Count(x => !x.IsSuccess);
        var structure = results.Count(x => x.IsSuccess && x.TypeSpecification?.Children.Count > 0);
        var scalar = success - structure;
        var summary = $"attempted={results.Count}, ok={success}, failed={failure}, selected={selectedCandidates.Count}, skipped={skipped.Length}, stoppedEarly={stoppedBeforeExhausted.ToString().ToLowerInvariant()}.";

        return new LiveIedVariableTypeProbeEvidence
        {
            Attempted = true,
            Source = source,
            Strategy = strategy,
            MaxReads = maxReads,
            DelayMs = delayMs,
            RawCandidateCount = evaluations.Count,
            SelectedCandidateCount = selectedCandidates.Count,
            SkippedCandidateCount = skipped.Length,
            AttemptCount = results.Count,
            SuccessCount = success,
            FailureCount = failure,
            ExactScalarTypeCount = scalar,
            ExactStructureTypeCount = structure,
            StoppedBeforeCandidateExhausted = stoppedBeforeExhausted,
            ProtocolFaultSuspected = protocolFault,
            Summary = summary,
            SkippedByReason = skipped
                .GroupBy(x => x.Reason, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new LiveIedVariableTypeProbeSkipSummary { Reason = x.Key, Count = x.Count() })
                .ToArray(),
            SelectedCandidates = selectedCandidates
                .Take(256)
                .Select(x => new LiveIedVariableTypeProbeCandidateEvidence
                {
                    Reference = string.IsNullOrWhiteSpace(x.Domain) ? x.Item : $"{x.Domain}/{x.Item}",
                    Domain = x.Domain,
                    MmsItemName = x.Item,
                    FunctionalConstraint = x.FunctionalConstraint,
                    Reason = "selected-safe-leaf"
                })
                .ToArray(),
            Results = results
                .Select(x => new LiveIedVariableTypeProbeResultEvidence
                {
                    Reference = x.ReferenceKey,
                    IsSuccess = x.IsSuccess,
                    MmsType = x.MmsType,
                    SclBType = x.SclBType,
                    TypeSignature = x.TypeSignature,
                    Message = x.Message
                })
                .ToArray()
        };
    }

    private static bool IsVariableTypeProtocolFault(MmsVariableAccessAttributesResult result)
        => !result.IsSuccess &&
           (result.Message.Contains("transport fault", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("peer closed", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("closed the TCP", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("association", StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<MmsVariableAccessAttributesResult>> ReadVariableAccessAttributesSafelyAsync(
        MmsClientSession session,
        IReadOnlyList<MmsObjectReference> candidates,
        int maxReads,
        int delayMs,
        CancellationToken cancellationToken)
    {
        var results = new List<MmsVariableAccessAttributesResult>();
        var limit = maxReads <= 0 ? candidates.Count : Math.Min(candidates.Count, maxReads);
        for (var i = 0; i < limit; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.IsMmsInitiated)
                break;

            var result = await session.GetVariableAccessAttributesAsync(candidates[i], cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!session.IsMmsInitiated)
                break;

            if (delayMs > 0 && i < limit - 1)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }


    private static async Task<IReadOnlyList<MmsVariableAccessAttributesResult>> ReadVariableAccessAttributesIsolatedAsync(
        string host,
        int port,
        TimeSpan timeout,
        IReadOnlyList<MmsObjectReference> candidates,
        int maxReads,
        int delayMs,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return Array.Empty<MmsVariableAccessAttributesResult>();

        try
        {
            await using var typeSession = new MmsClientSession();
            await typeSession.ConnectAsync(host, port, timeout, cancellationToken).ConfigureAwait(false);
            return await ReadVariableAccessAttributesSafelyAsync(typeSession, candidates, maxReads, delayMs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            var first = candidates.FirstOrDefault();
            return new[]
            {
                new MmsVariableAccessAttributesResult
                {
                    IsSuccess = false,
                    Reference = first,
                    Message = $"GetVariableAccessAttributes isolated association fault: {ex.GetType().Name}: {ex.Message}"
                }
            };
        }
    }

    private static LiveIedVariableSpecQuarantineEvidence BuildVariableSpecQuarantineEvidence(
        LiveIedVariableTypeProbeEvidence probe,
        bool enabled,
        string host,
        int port,
        string iedName,
        bool isolated)
    {
        var target = string.IsNullOrWhiteSpace(iedName)
            ? $"{host}:{port}"
            : $"{host}:{port}/{iedName}";
        if (!enabled)
        {
            return new LiveIedVariableSpecQuarantineEvidence
            {
                IsEnabled = false,
                IsQuarantined = false,
                Scope = isolated ? "IsolatedAssociation" : "MainAssociation",
                TargetKey = target,
                Summary = "Variable specification quarantine was disabled for this run."
            };
        }

        var trigger = probe.Results.FirstOrDefault(x => !x.IsSuccess && IsVariableTypeProtocolFaultMessage(x.Message));
        if (!probe.ProtocolFaultSuspected || probe.SuccessCount != 0 || trigger is null)
        {
            return new LiveIedVariableSpecQuarantineEvidence
            {
                IsEnabled = true,
                IsQuarantined = false,
                Scope = isolated ? "IsolatedAssociation" : "MainAssociation",
                TargetKey = target,
                CoreDiscoveryPreserved = isolated,
                Summary = probe.Attempted
                    ? "Variable specification quarantine was not triggered."
                    : "Variable specification probe was not attempted."
            };
        }

        var quarantineTrigger = trigger;
        return new LiveIedVariableSpecQuarantineEvidence
        {
            IsEnabled = true,
            IsQuarantined = true,
            Scope = isolated ? "IsolatedAssociation" : "MainAssociation",
            TargetKey = target,
            TriggerReference = quarantineTrigger.Reference,
            TriggerMessage = quarantineTrigger.Message,
            Reason = "GetVariableAccessAttributes caused a transport/association fault and the target should not be probed again in this run.",
            CoreDiscoveryPreserved = isolated,
            Recommendation = "Use --read-types false for routine discovery on this IED, or keep --type-read-isolated true with a very small max read count. Prefer golden SCL/type registry learning for CDC/type improvement.",
            Summary = $"VariableAccessAttributes quarantined after peer-close at {quarantineTrigger.Reference}; core discovery preserved={(isolated ? "true" : "false")}."
        };
    }

    private static bool IsVariableTypeProtocolFaultMessage(string message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.Contains("transport fault", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("peer closed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("closed the TCP", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("association", StringComparison.OrdinalIgnoreCase));

    private static string ResolveGoldenSclPath(string explicitPath, string iedName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        if (!string.IsNullOrWhiteSpace(iedName))
        {
            var candidate = Path.Combine("samples", "scl", $"{iedName}.iid");
            if (File.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }



    private static LiveIedGoldenSclRegistryPromotionEvidence BuildGoldenSclRegistryPromotionEvidence(
        LiveIedGoldenSclTypeLearningEvidence learning,
        string profileName,
        string conflictPolicy)
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(profileName) ? "generic-safe-learned" : profileName.Trim();
        var normalizedPolicy = string.IsNullOrWhiteSpace(conflictPolicy) ? "review-only" : conflictPolicy.Trim().ToLowerInvariant();
        if (normalizedPolicy is not "review-only" and not "prefer-live" and not "prefer-golden")
            normalizedPolicy = "review-only";

        if (!learning.Attempted)
        {
            return new LiveIedGoldenSclRegistryPromotionEvidence
            {
                Attempted = false,
                IsSuccess = false,
                ProfileName = normalizedProfile,
                ConflictPolicy = normalizedPolicy,
                Message = "Golden SCL learning was not attempted, so no registry promotion can be generated.",
                Summary = "Golden registry promotion not attempted."
            };
        }

        if (!learning.IsSuccess)
        {
            return new LiveIedGoldenSclRegistryPromotionEvidence
            {
                Attempted = true,
                IsSuccess = false,
                ProfileName = normalizedProfile,
                ConflictPolicy = normalizedPolicy,
                Message = learning.Message,
                Summary = "Golden registry promotion unavailable because learning failed."
            };
        }

        var promotions = new List<LiveIedGoldenSclRegistryPromotionEntry>();
        var conflicts = new List<LiveIedGoldenSclRegistryPromotionConflict>();
        foreach (var candidate in learning.Candidates)
        {
            var key = MakeLnDoKey(candidate.LogicalNodeClass, candidate.DataObjectName);
            var currentCdc = candidate.CurrentCdc.Equals("-", StringComparison.Ordinal) ? string.Empty : candidate.CurrentCdc;
            var sameCdc = !string.IsNullOrWhiteSpace(currentCdc) &&
                currentCdc.Equals(candidate.GoldenCdc, StringComparison.OrdinalIgnoreCase);

            if (sameCdc)
            {
                promotions.Add(new LiveIedGoldenSclRegistryPromotionEntry
                {
                    Key = key,
                    LogicalNodeClass = candidate.LogicalNodeClass,
                    DataObjectName = candidate.DataObjectName,
                    Reference = candidate.Reference,
                    PreviousCdc = currentCdc,
                    PromotedCdc = candidate.GoldenCdc,
                    PreviousConfidence = candidate.CurrentConfidence,
                    PromotedConfidence = "GoldenConfirmedHigh",
                    GoldenDoTypeId = candidate.GoldenDoTypeId,
                    Action = "Promote confidence using matching golden CDC/type key."
                });
                continue;
            }

            if (normalizedPolicy.Equals("prefer-golden", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.GoldenCdc))
            {
                promotions.Add(new LiveIedGoldenSclRegistryPromotionEntry
                {
                    Key = key,
                    LogicalNodeClass = candidate.LogicalNodeClass,
                    DataObjectName = candidate.DataObjectName,
                    Reference = candidate.Reference,
                    PreviousCdc = string.IsNullOrWhiteSpace(currentCdc) ? "-" : currentCdc,
                    PromotedCdc = candidate.GoldenCdc,
                    PreviousConfidence = candidate.CurrentConfidence,
                    PromotedConfidence = "GoldenOverrideHigh",
                    GoldenDoTypeId = candidate.GoldenDoTypeId,
                    Action = "Promote to golden CDC by explicit prefer-golden conflict policy."
                });
            }
            else
            {
                conflicts.Add(new LiveIedGoldenSclRegistryPromotionConflict
                {
                    Key = key,
                    Reference = candidate.Reference,
                    LiveCdc = string.IsNullOrWhiteSpace(currentCdc) ? "-" : currentCdc,
                    GoldenCdc = candidate.GoldenCdc,
                    Policy = normalizedPolicy,
                    Recommendation = normalizedPolicy.Equals("prefer-live", StringComparison.OrdinalIgnoreCase)
                        ? "Keep live inference; do not promote this candidate."
                        : "Review before changing CDC registry because live and golden CDC differ."
                });
            }
        }

        foreach (var conflict in learning.Conflicts)
        {
            if (normalizedPolicy.Equals("prefer-golden", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(conflict.GoldenCdc))
            {
                promotions.Add(new LiveIedGoldenSclRegistryPromotionEntry
                {
                    Key = conflict.Key,
                    LogicalNodeClass = ExtractLnClassFromKey(conflict.Key),
                    DataObjectName = ExtractDoNameFromKey(conflict.Key),
                    Reference = conflict.Reference,
                    PreviousCdc = conflict.LiveCdc,
                    PromotedCdc = conflict.GoldenCdc,
                    PreviousConfidence = "HighOrExact",
                    PromotedConfidence = "GoldenOverrideHigh",
                    GoldenDoTypeId = string.Empty,
                    Action = "Override high/exact live inference by explicit prefer-golden conflict policy."
                });
                continue;
            }

            conflicts.Add(new LiveIedGoldenSclRegistryPromotionConflict
            {
                Key = conflict.Key,
                Reference = conflict.Reference,
                LiveCdc = conflict.LiveCdc,
                GoldenCdc = conflict.GoldenCdc,
                Policy = normalizedPolicy,
                Recommendation = normalizedPolicy.Equals("prefer-live", StringComparison.OrdinalIgnoreCase)
                    ? "Keep high/exact live inference; golden CDC is recorded for audit only."
                    : "Manual review required before overriding high/exact live inference."
            });
        }

        var dedupedPromotions = promotions
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(promotion => PromotionRank(promotion.PromotedConfidence)).ThenBy(promotion => promotion.Reference, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dedupedConflicts = conflicts
            .GroupBy(x => $"{x.Key}|{x.LiveCdc}|{x.GoldenCdc}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LiveIedGoldenSclRegistryPromotionEvidence
        {
            Attempted = true,
            IsSuccess = true,
            ProfileName = normalizedProfile,
            ConflictPolicy = normalizedPolicy,
            CandidateCount = learning.CandidateImprovementCount,
            AppliedPromotionCount = dedupedPromotions.Length,
            ReviewConflictCount = dedupedConflicts.Length,
            GeneratedRegistryEntryCount = dedupedPromotions.Length,
            AppliedPromotions = dedupedPromotions,
            ReviewConflicts = dedupedConflicts,
            Message = "Golden SCL learning candidates were converted into an auditable vendor/profile CDC registry layer.",
            Summary = $"profile={normalizedProfile}, policy={normalizedPolicy}, candidates={learning.CandidateImprovementCount}, applied={dedupedPromotions.Length}, conflicts={dedupedConflicts.Length}."
        };
    }

    private static int PromotionRank(string confidence)
        => confidence.Contains("Override", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

    private static string MakeLnDoKey(string lnClass, string dataObject)
        => string.IsNullOrWhiteSpace(lnClass) ? dataObject : $"{lnClass}.{dataObject}";

    private static string ExtractLnClassFromKey(string key)
    {
        var index = key.IndexOf('.', StringComparison.Ordinal);
        return index <= 0 ? string.Empty : key[..index];
    }

    private static string ExtractDoNameFromKey(string key)
    {
        var index = key.IndexOf('.', StringComparison.Ordinal);
        return index < 0 || index + 1 >= key.Length ? key : key[(index + 1)..];
    }


    private static LiveIedGoldenSclTypeLearningEvidence BuildGoldenSclTypeLearningEvidence(
        LiveIedModelDiscoveryDocument document,
        string goldenSclPath)
    {
        if (string.IsNullOrWhiteSpace(goldenSclPath))
        {
            return new LiveIedGoldenSclTypeLearningEvidence
            {
                Attempted = false,
                Message = "No golden SCL path was supplied and no samples/scl/<IED>.iid file was found.",
                Summary = "Golden SCL learning not attempted."
            };
        }

        try
        {
            if (!File.Exists(goldenSclPath))
            {
                return new LiveIedGoldenSclTypeLearningEvidence
                {
                    Attempted = true,
                    GoldenSclPath = goldenSclPath,
                    IsSuccess = false,
                    Message = "Golden SCL file was not found.",
                    Summary = "Golden SCL learning failed: file not found."
                };
            }

            var snapshot = SclModelSnapshotBuilder.Load(goldenSclPath);
            var goldenByKey = snapshot.DoCdcBindings
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Cdc))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var liveObjects = document.LogicalDevices
                .SelectMany(ld => ld.LogicalNodes.SelectMany(ln => ln.DataObjects.Select(dataObject => new
                {
                    LogicalDevice = ld,
                    LogicalNode = ln,
                    DataObject = dataObject,
                    Key = $"{ln.LnClass}.{dataObject.Name}"
                })))
                .ToArray();

            var unknownOrMedium = liveObjects
                .Where(x => x.DataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Unknown or LiveIedDiscoveryConfidenceLevel.Low or LiveIedDiscoveryConfidenceLevel.Medium)
                .ToArray();

            var candidates = new List<LiveIedGoldenSclTypeLearningEntry>();
            var conflicts = new List<LiveIedGoldenSclTypeLearningConflict>();
            var exactMatches = 0;
            foreach (var item in liveObjects)
            {
                if (!goldenByKey.TryGetValue(item.Key, out var golden))
                    continue;

                exactMatches++;
                var liveCdc = item.DataObject.InferredCdc ?? string.Empty;
                var goldenCdc = golden.Cdc ?? string.Empty;
                var cdcMatches = string.Equals(liveCdc, goldenCdc, StringComparison.OrdinalIgnoreCase);
                if (!cdcMatches && item.DataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.High or LiveIedDiscoveryConfidenceLevel.Exact)
                {
                    conflicts.Add(new LiveIedGoldenSclTypeLearningConflict
                    {
                        Key = item.Key,
                        LiveCdc = liveCdc,
                        GoldenCdc = goldenCdc,
                        Reference = item.DataObject.Reference,
                        Notes = "Live inference is high/exact but golden SCL uses a different CDC. Review before changing the registry."
                    });
                    continue;
                }

                if (!cdcMatches || item.DataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Unknown or LiveIedDiscoveryConfidenceLevel.Low or LiveIedDiscoveryConfidenceLevel.Medium)
                {
                    candidates.Add(new LiveIedGoldenSclTypeLearningEntry
                    {
                        Reference = item.DataObject.Reference,
                        LogicalNodeClass = item.LogicalNode.LnClass,
                        DataObjectName = item.DataObject.Name,
                        CurrentCdc = string.IsNullOrWhiteSpace(liveCdc) ? "-" : liveCdc,
                        GoldenCdc = goldenCdc,
                        GoldenDoTypeId = golden.DoTypeId,
                        CurrentConfidence = item.DataObject.ConfidenceLevel.ToString(),
                        SuggestedAction = cdcMatches ? "Promote confidence using golden SCL key match." : "Review and consider adding LNClass.DO -> CDC to registry."
                    });
                }
            }

            return new LiveIedGoldenSclTypeLearningEvidence
            {
                Attempted = true,
                GoldenSclPath = Path.GetFullPath(goldenSclPath),
                IsSuccess = true,
                Message = "Golden SCL was parsed and compared against the live discovery model.",
                GoldenBindingCount = goldenByKey.Count,
                LiveDataObjectCount = liveObjects.Length,
                LiveUnknownOrMediumCount = unknownOrMedium.Length,
                ExactKeyMatchCount = exactMatches,
                CandidateImprovementCount = candidates.Count,
                CdcConflictCount = conflicts.Count,
                Candidates = candidates
                    .OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
                    .Take(1000)
                    .ToArray(),
                Conflicts = conflicts
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .ToArray(),
                Summary = $"goldenBindings={goldenByKey.Count}, liveDO={liveObjects.Length}, unknownOrMedium={unknownOrMedium.Length}, exactKeyMatches={exactMatches}, candidates={candidates.Count}, conflicts={conflicts.Count}."
            };
        }
        catch (Exception ex)
        {
            return new LiveIedGoldenSclTypeLearningEvidence
            {
                Attempted = true,
                GoldenSclPath = goldenSclPath,
                IsSuccess = false,
                Message = $"Golden SCL learning failed: {ex.GetType().Name}: {ex.Message}",
                Summary = "Golden SCL learning failed."
            };
        }
    }

    private static async Task<IReadOnlyList<LiveIedSettingGroupReadbackEvidence>> ReadSettingGroupReadbacksAsync(
        MmsClientSession session,
        IReadOnlyList<LiveIedControlBlockModel> settingGroupControls,
        CancellationToken cancellationToken)
    {
        var results = new List<LiveIedSettingGroupReadbackEvidence>();
        foreach (var control in settingGroupControls.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.IsMmsInitiated)
                break;

            var attributes = new List<LiveIedSettingGroupAttributeReadback>();
            foreach (var attribute in SettingGroupAttributeCandidates(control))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!session.IsMmsInitiated)
                    break;

                var reference = new MmsObjectReference(control.Domain, $"{control.LogicalNode}${control.FunctionalConstraint}${control.Name}${attribute}", control.FunctionalConstraint);
                var read = await session.ReadSingleVariableAsync(reference, cancellationToken).ConfigureAwait(false);
                attributes.Add(new LiveIedSettingGroupAttributeReadback
                {
                    Name = attribute,
                    MmsReference = $"{reference.Domain}/{reference.Item}",
                    IsSuccess = read.IsSuccess,
                    Value = read.IsSuccess ? MmsDataValueRenderer.ToCompactString(read.Value, reference.ToString()) : string.Empty,
                    Message = read.Message
                });
            }

            results.Add(new LiveIedSettingGroupReadbackEvidence
            {
                Reference = control.Reference,
                Domain = control.Domain,
                LogicalNode = control.LogicalNode,
                Name = control.Name,
                FunctionalConstraint = control.FunctionalConstraint,
                Attributes = attributes
            });
        }

        return results;
    }

    private static IEnumerable<string> SettingGroupAttributeCandidates(LiveIedControlBlockModel control)
    {
        var preferred = new[] { "NumOfSG", "ActSG", "EditSG", "CnfEdit", "LActTm" };
        var available = control.Attributes.Count == 0
            ? new HashSet<string>(preferred, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(control.Attributes, StringComparer.OrdinalIgnoreCase);

        foreach (var attr in preferred)
        {
            if (available.Contains(attr))
                yield return attr;
        }
    }

    private static async Task<int> MmsSclExportAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-scl-export requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        var timeoutMs = options.GetInt("timeout-ms", 120000);
        var maxReportProbes = options.GetInt("max-report-probes", 286);
        var readDataSets = options.GetBool("read-datasets", true);
        var readTypes = options.GetBool("read-types", true);
        var maxTypeReads = options.GetInt("max-type-reads", 512);
        var typeReadSource = options.Get("type-read-source", "both");
        var iedName = options.Get("ied-name", string.Empty);
        var goldenProfileName = options.Get("golden-profile-name", string.IsNullOrWhiteSpace(iedName) ? "generic-safe-learned" : iedName);
        var apName = options.Get("ap-name", "AP1");
        var profile = options.Get("scl-export-profile", options.Get("profile", "safe-connection"));
        var output = options.Get("output", Path.Combine("out", "scl", "live-ied.generated.iid"));
        var subnet = options.Get("ip-subnet", "255.255.255.0");
        var gateway = options.Get("ip-gateway", "0.0.0.0");
        var osiApTitle = options.Get("osi-ap-title", string.Empty);
        var osiAeQualifier = options.Get("osi-ae-qualifier", string.Empty);
        var osiPsel = options.Get("osi-psel", "00000001");
        var osiSsel = options.Get("osi-ssel", "0001");
        var osiTsel = options.Get("osi-tsel", "0001");
        var includeOsi = options.GetBool("include-osi", true);
        var writeDiscoveryBundle = options.GetBool("write-discovery", true);
        var requestedProfile = LiveIedSclExportProfileParser.Parse(profile);
        var writeConnectionCompanion = options.GetBool("write-connection-companion", requestedProfile != LiveIedSclExportProfile.SafeConnection);
        var connectionCompanionOutput = options.Get("connection-output", MakeProfileOutputPath(output, "safe-connection"));
        var ldNameMode = ParseLogicalDeviceNameMode(options.Get("ld-name-mode", "auto"));

        await using var session = new MmsClientSession();
        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: live-to-SCL generic IID/CID-style export (read-only discovery; no RCB writes). ");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), cts.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        if (!string.IsNullOrWhiteSpace(session.LastHandshakeMessage))
            Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(
            probeReportAttributes: true,
            maxReportAttributeProbes: maxReportProbes,
            cancellationToken: cts.Token).ConfigureAwait(false);

        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);
        Console.WriteLine($"FC counts: {FormatFcCounts(discovery.IedDirectory.CountByFunctionalConstraint())}");

        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories = Array.Empty<MmsDataSetDirectoryResult>();
        if (readDataSets && discovery.ReportInventory.DataSets.Count > 0)
        {
            Console.WriteLine($"Reading DataSet directories: {discovery.ReportInventory.DataSets.Count} candidate(s).");
            dataSetDirectories = await session.GetDataSetDirectoriesAsync(
                discovery.ReportInventory.DataSets.Select(x => x.Reference),
                discovery.IedDirectory,
                cts.Token).ConfigureAwait(false);

            foreach (var dataSet in dataSetDirectories.Take(10))
                Console.WriteLine($"  {(dataSet.IsSuccess ? "OK" : "FAIL")} {dataSet.Summary}");

            if (dataSetDirectories.Count > 10)
                Console.WriteLine($"  ... {dataSetDirectories.Count - 10} more DataSet result(s).");
        }

        IReadOnlyList<MmsVariableAccessAttributesResult> variableTypes = Array.Empty<MmsVariableAccessAttributesResult>();
        if (readTypes)
        {
            var typeCandidates = BuildTypeReadCandidates(discovery, dataSetDirectories, typeReadSource)
                .Take(maxTypeReads <= 0 ? int.MaxValue : maxTypeReads)
                .ToArray();

            Console.WriteLine($"Reading MMS variable access attributes: {typeCandidates.Length} candidate(s), source={typeReadSource}, max={maxTypeReads}.");
            variableTypes = await session.GetVariableAccessAttributesBatchAsync(typeCandidates, maxTypeReads, cts.Token).ConfigureAwait(false);
            foreach (var type in variableTypes.Take(10))
                Console.WriteLine($"  {(type.IsSuccess ? "OK" : "FAIL")} {type.Summary}");

            if (variableTypes.Count > 10)
                Console.WriteLine($"  ... {variableTypes.Count - 10} more type result(s).");
        }

        var document = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions
            {
                Host = host,
                Port = port,
                IedName = iedName,
                AccessPointName = apName
            },
            dataSetDirectories,
            variableTypes);

        LiveIedSclExportOptions CreateSclExportOptions(string profileName)
            => new()
            {
                Profile = profileName,
                IpAddress = host,
                IpSubnet = subnet,
                IpGateway = gateway,
                OsiApTitle = osiApTitle,
                OsiAeQualifier = osiAeQualifier,
                OsiPsel = osiPsel,
                OsiSsel = osiSsel,
                OsiTsel = osiTsel,
                IncludeDefaultOsiParameters = includeOsi,
                LogicalDeviceNameMode = ldNameMode
            };

        var export = LiveIedSclExporter.WriteFiles(
            document,
            output,
            CreateSclExportOptions(profile));

        LiveIedSclExportResult? connectionCompanion = null;
        if (writeConnectionCompanion && requestedProfile != LiveIedSclExportProfile.SafeConnection)
        {
            connectionCompanion = LiveIedSclExporter.WriteFiles(
                document,
                connectionCompanionOutput,
                CreateSclExportOptions("safe-connection"));
        }

        if (writeDiscoveryBundle)
        {
            var discoveryDirectory = Path.Combine(Path.GetDirectoryName(output) ?? ".", "discovery-evidence");
            _ = LiveIedModelDiscoveryExporter.WriteBundle(document, discoveryDirectory);
        }

        Console.WriteLine("Live-to-SCL export complete.");
        Console.WriteLine($"  SCL: {Path.GetFullPath(export.SclPath)}");
        Console.WriteLine($"  Report: {Path.GetFullPath(export.ReportPath)}");
        Console.WriteLine($"  Summary: {Path.GetFullPath(export.SummaryPath)}");
        if (!string.IsNullOrWhiteSpace(export.ExcludedAttributesPath))
            Console.WriteLine($"  Excluded: {Path.GetFullPath(export.ExcludedAttributesPath)}");
        Console.WriteLine($"  Counts: LD={export.LogicalDeviceCount}, LN={export.LogicalNodeCount}, DataSets={export.DataSetCount}, RCB={export.ReportControlCount}, GoCB={export.GooseControlBlockCount}, SVCB={export.SampledValueControlBlockCount}, SGCB={export.SettingGroupControlCount}, LCB={export.LogControlCount}, LNodeType={export.LNodeTypeCount}, DOType={export.DoTypeCount}, DAType={export.DaTypeCount}, excludedAttrs={export.ExcludedAttributes.Count}, warnings={export.Warnings.Count}.");
        if (connectionCompanion is not null)
        {
            Console.WriteLine("Safe-connection companion generated.");
            Console.WriteLine($"  Connection SCL: {Path.GetFullPath(connectionCompanion.SclPath)}");
            Console.WriteLine($"  Connection excluded: {Path.GetFullPath(connectionCompanion.ExcludedAttributesPath)}");
            Console.WriteLine($"  Connection counts: DOType={connectionCompanion.DoTypeCount}, DAType={connectionCompanion.DaTypeCount}, excludedAttrs={connectionCompanion.ExcludedAttributes.Count}, warnings={connectionCompanion.Warnings.Count}.");
            Console.WriteLine("  Use the companion file for safe online connect/read-all checks; use the main file for full standard discovery review.");
        }
        Console.WriteLine("Round-trip check:");
        var parsed = new SclParser().Load(output);
        Console.WriteLine($"  Parsed SCL: IED={parsed.Ieds.Count}, DataSets={parsed.DataSets.Count}, Reports={parsed.ReportControls.Count}, GOOSE={parsed.GooseStreams.Count}, SV={parsed.SampledValuesStreams.Count}, warnings={parsed.Warnings.Count}.");

        return 0;
    }

    private static string MakeProfileOutputPath(string outputPath, string profileSuffix)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "live-ied.generated";
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".iid";

        return Path.Combine(directory ?? string.Empty, $"{fileName}.{profileSuffix}{extension}");
    }

    private static LiveIedSclLogicalDeviceNameMode ParseLogicalDeviceNameMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "" or "auto" => LiveIedSclLogicalDeviceNameMode.Auto,
            "keep" or "mms" or "domain" => LiveIedSclLogicalDeviceNameMode.Keep,
            _ => throw new ArgumentException("--ld-name-mode must be auto or keep.")
        };

    private static IEnumerable<MmsObjectReference> BuildTypeReadCandidates(
        MmsDiscoveryResult discovery,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        string source)
    {
        var includeDataSets = string.Equals(source, "datasets", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "both", StringComparison.OrdinalIgnoreCase);
        var includeModel = string.Equals(source, "model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, "both", StringComparison.OrdinalIgnoreCase);

        if (!includeDataSets && !includeModel)
            throw new ArgumentException("--type-read-source must be datasets, model, or both.");

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeDataSets)
        {
            foreach (var member in dataSetDirectories
                .Where(x => x.IsSuccess)
                .SelectMany(x => x.Members)
                .Where(x => !string.IsNullOrWhiteSpace(x.Domain) && !string.IsNullOrWhiteSpace(x.MmsItemName)))
            {
                var expanded = ExpandDataSetMemberTypeCandidates(discovery.IedDirectory, member).ToArray();
                foreach (var point in expanded)
                {
                    var reference = point.ToObjectReference();
                    var key = $"{reference.Domain}/{reference.Item}";
                    if (emitted.Add(key))
                        yield return reference;
                }

                if (expanded.Length == 0)
                {
                    var reference = new MmsObjectReference(member.Domain, member.MmsItemName, member.FunctionalConstraint);
                    var key = $"{reference.Domain}/{reference.Item}";
                    if (emitted.Add(key))
                        yield return reference;
                }
            }
        }

        if (includeModel)
        {
            foreach (var point in discovery.IedDirectory.Points
                .OrderBy(x => TypeReadPriority(x), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UserReference, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(point.Domain) || string.IsNullOrWhiteSpace(point.MmsItemName))
                    continue;

                var reference = new MmsObjectReference(point.Domain, point.MmsItemName, point.FunctionalConstraint);
                var key = $"{reference.Domain}/{reference.Item}";
                if (emitted.Add(key))
                    yield return reference;
            }
        }
    }


    private static IEnumerable<MmsFcResolvedPoint> ExpandDataSetMemberTypeCandidates(
        MmsIedModelDirectory directory,
        MmsDataSetDirectoryMember member)
    {
        if (string.IsNullOrWhiteSpace(member.UserReference))
            yield break;

        var normalizedMember = member.UserReference.Trim();
        var slash = normalizedMember.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0 || slash >= normalizedMember.Length - 1)
            yield break;

        var domain = normalizedMember[..slash];
        var userPath = normalizedMember[(slash + 1)..];
        var dot = userPath.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot >= userPath.Length - 1)
            yield break;

        var logicalNode = userPath[..dot];
        var dataObjectPath = userPath[(dot + 1)..];
        var fc = member.FunctionalConstraint;

        var matches = directory.Points
            .Where(point => string.Equals(point.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .Where(point => string.Equals(point.LogicalNode, logicalNode, StringComparison.OrdinalIgnoreCase))
            .Where(point => string.IsNullOrWhiteSpace(fc) || string.Equals(point.FunctionalConstraint, fc, StringComparison.OrdinalIgnoreCase))
            .Where(point => IsDataSetMemberLeaf(point.DataObjectPath, dataObjectPath))
            .OrderBy(point => TypeReadPriority(point), StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var match in matches)
            yield return match;
    }

    private static bool IsDataSetMemberLeaf(string pointPath, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(pointPath) || string.IsNullOrWhiteSpace(memberPath))
            return false;

        if (pointPath.Equals(memberPath, StringComparison.OrdinalIgnoreCase))
            return !IsFcdLevelTypeCandidate(pointPath);

        return pointPath.StartsWith(memberPath + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFcdLevelTypeCandidate(string dataObjectPath)
        => dataObjectPath.IndexOf('.', StringComparison.Ordinal) < 0;

    private static string TypeReadPriority(MmsFcResolvedPoint point)
    {
        var path = point.DataObjectPath;
        if (path.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
            return "00";
        if (path.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase))
            return "01";
        if (path.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
            return "02";
        if (path.EndsWith(".t", StringComparison.OrdinalIgnoreCase))
            return "03";
        if (string.Equals(point.FunctionalConstraint, "ST", StringComparison.OrdinalIgnoreCase))
            return "04";
        if (string.Equals(point.FunctionalConstraint, "MX", StringComparison.OrdinalIgnoreCase))
            return "05";
        return "99";
    }

    private static async Task<int> MmsDirectoryAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-directory requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var showPoints = options.GetBool("show-points", fallback: false);
        var lnLimit = options.GetInt("ln-limit", 20);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: full live IED directory; FC is parsed from raw MMS names.");

        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: false, maxReportAttributeProbes: 0, timeout.Token).ConfigureAwait(false);
        var directory = discovery.IedDirectory;
        Console.WriteLine(directory.Summary);
        Console.WriteLine($"FC index: {FormatFcCounts(directory.CountByFunctionalConstraint())}");
        Console.WriteLine($"DataSets={discovery.ReportInventory.DataSets.Count}, RCB={discovery.ReportInventory.ReportControls.Count} (BRCB={discovery.ReportInventory.BufferedCount}, URCB={discovery.ReportInventory.UnbufferedCount})");

        Console.WriteLine();
        Console.WriteLine("Logical devices / logical nodes:");
        foreach (var ld in directory.LogicalDevices.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {ld.Name}: LN={ld.LogicalNodes.Count}, points={ld.Points.Count}, FC={FormatFcCounts(ld.Points.GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase))}");
            foreach (var ln in TakeWithLimit(ld.LogicalNodes.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase), lnLimit))
                Console.WriteLine($"      {ln.Name}: points={ln.Points.Count}, FC={FormatFcCounts(ln.CountByFunctionalConstraint())}");
            WriteLimitNotice(ld.LogicalNodes.Count, lnLimit, $"logical nodes in {ld.Name}");
        }

        if (showPoints)
        {
            Console.WriteLine();
            Console.WriteLine("Resolved FC points:");
            foreach (var point in TakeWithLimit(directory.Points, rawLimit))
                Console.WriteLine($"  {point.UserReference} [{point.FunctionalConstraint}] mms={point.MmsItemName}");
            WriteLimitNotice(directory.Points.Count, rawLimit, "FC points");
        }

        return 0;
    }


    private static async Task<int> MmsFindAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("mms-find requires <host-or-ip> <query>.");

        var host = args[0];
        var query = args[1];
        var options = CliOptions.Parse(args[2..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var fc = options.Get("fc", string.Empty).Trim();
        var ld = options.Get("ld", string.Empty).Trim();
        var ln = options.Get("ln", string.Empty).Trim();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine($"Find: {query}");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        var discovery = await session.DiscoverAsync(probeReportAttributes: false, maxReportAttributeProbes: 0, timeout.Token).ConfigureAwait(false);
        var points = discovery.IedDirectory.Points.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(fc))
            points = points.Where(x => x.FunctionalConstraint.Equals(fc, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ld))
            points = points.Where(x => x.Domain.Equals(ld, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ln))
            points = points.Where(x => x.LogicalNode.Equals(ln, StringComparison.OrdinalIgnoreCase));

        var matches = points
            .Where(x => x.UserReference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.MmsReference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.LogicalNode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.DataObjectPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine($"Matches: {matches.Length}");
        foreach (var point in TakeWithLimit(matches, rawLimit))
            Console.WriteLine($"  {point.UserReference} [{point.FunctionalConstraint}] mms={point.MmsReference}");
        WriteLimitNotice(matches.Length, rawLimit, "matches");

        return matches.Length > 0 ? 0 : 1;
    }

    private static async Task<int> MmsResolveAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("mms-resolve requires <host-or-ip> <object-reference>.");

        var host = args[0];
        var reference = args[1];
        var options = CliOptions.Parse(args[2..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 20);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine($"Resolve: {reference}");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        var discovery = await session.DiscoverAsync(probeReportAttributes: false, maxReportAttributeProbes: 0, timeout.Token).ConfigureAwait(false);
        var result = MmsFcResolver.Resolve(discovery.IedDirectory, reference);

        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(result.Message);
        if (result.Candidates.Count > 0)
        {
            Console.WriteLine("Candidates:");
            foreach (var candidate in TakeWithLimit(result.Candidates, rawLimit))
                Console.WriteLine($"  {candidate.UserReference} [{candidate.FunctionalConstraint}] source={candidate.Source} confidence={candidate.Confidence} mms={candidate.MmsReference}");
            WriteLimitNotice(result.Candidates.Count, rawLimit, "candidates");
        }
        else if (result.HeuristicFunctionalConstraints.Count > 0)
        {
            Console.WriteLine($"Heuristic FC: {string.Join(", ", result.HeuristicFunctionalConstraints)}");
        }

        return result.IsResolved ? 0 : 1;
    }

    private static async Task<int> MmsReadSmartAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("mms-read-smart requires <host-or-ip> <object-reference>.");

        var host = args[0];
        var reference = args[1];
        var options = CliOptions.Parse(args[2..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 30000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine($"Smart read: {reference}");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        var discovery = await session.DiscoverAsync(probeReportAttributes: false, maxReportAttributeProbes: 0, timeout.Token).ConfigureAwait(false);
        var result = await session.ReadSmartAsync(discovery.IedDirectory, reference, timeout.Token).ConfigureAwait(false);

        Console.WriteLine(result.ResolveResult.Message);
        if (result.SelectedPoint != null)
            Console.WriteLine($"Selected: {result.SelectedPoint.UserReference} [{result.SelectedPoint.FunctionalConstraint}] mms={result.SelectedPoint.MmsReference}");

        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(session.LastReadAttemptSummary))
            Console.WriteLine($"Read attempts: {session.LastReadAttemptSummary}");

        return result.IsSuccess ? 0 : 1;
    }


    private static async Task<int> MmsDataSetDirectoryAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-dataset-directory requires <host-or-ip> [dataset-reference].");

        var host = args[0];
        var explicitDataSet = args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : string.Empty;
        var optionStart = string.IsNullOrWhiteSpace(explicitDataSet) ? 1 : 2;
        var options = CliOptions.Parse(args[optionStart..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 60000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var showMembers = options.GetBool("show-members", fallback: true);
        var readValues = options.GetBool("read-values", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: DataSet directory / member planner (read-only).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportAttributeProbes: options.GetInt("max-report-probes", 64), timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        var references = string.IsNullOrWhiteSpace(explicitDataSet)
            ? discovery.ReportInventory.DataSets.Select(x => x.Reference).ToArray()
            : [explicitDataSet];

        if (references.Length == 0)
        {
            Console.WriteLine("No DataSet was discovered via MMS NamedVariableList.");
            return 1;
        }

        var results = await session.GetDataSetDirectoriesAsync(references, discovery.IedDirectory, timeout.Token).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"DataSet directories: {results.Count}");
        foreach (var result in results)
        {
            Console.WriteLine($"  {result.Summary}");
            if (!result.IsSuccess)
            {
                Console.WriteLine($"      {result.Message}");
                continue;
            }

            Console.WriteLine($"      FC={FormatFcCounts(result.Members.Where(x => !string.IsNullOrWhiteSpace(x.FunctionalConstraint)).GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase))}");

            if (showMembers)
            {
                foreach (var member in TakeWithLimit(result.Members, rawLimit))
                    Console.WriteLine($"      {member.UserReference} [{TextOrDash(member.FunctionalConstraint)}] mms={member.MmsReference}");
                WriteLimitNotice(result.Members.Count, rawLimit, $"members in {result.DataSetReference}");
            }

            if (readValues && result.Members.Count > 0)
            {
                Console.WriteLine("      Sample member reads:");
                foreach (var member in TakeWithLimit(result.Members.Where(x => !string.IsNullOrWhiteSpace(x.UserReference)), Math.Min(rawLimit <= 0 ? result.Members.Count : rawLimit, 16)))
                {
                    var read = await session.ReadSmartAsync(discovery.IedDirectory, member.UserReference, timeout.Token).ConfigureAwait(false);
                    Console.WriteLine($"        {member.UserReference}: {(read.IsSuccess && read.ReadResult.Value != null ? MmsDataValueRenderer.ToCompactString(read.ReadResult.Value, member.UserReference) : read.Message)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Recommended next safe path:");
        Console.WriteLine("  1. Use this DataSet directory as the report value map before enabling RptEna.");
        Console.WriteLine("  2. Prefer one BRCB/URCB that references this DataSet and is not enabled/reserved.");
        Console.WriteLine("  3. Only proceed to write/reserve/report enable after member mapping is stable.");

        return results.Any(x => x.IsSuccess && x.Members.Count > 0) ? 0 : 1;
    }


    private static async Task<int> MmsReportStaticPlanAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-static-plan requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var preferredRcb = options.Get("rcb", string.Empty);
        var preferredDataSet = options.Get("dataset", string.Empty);
        var strictRcb = options.GetBool("strict-rcb", fallback: false);
        var allowUrCbFallback = options.GetBool("allow-urcb-fallback", fallback: true);
        var allowPollingFallback = options.GetBool("allow-polling-fallback", fallback: true);
        var readValues = options.GetBool("read-values", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: static report subscription planner (read-only, no RptEna write).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");
        Console.WriteLine($"Receive pump: {(session.IsReceivePumpRunning ? "running" : "stopped")}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportAttributeProbes: options.GetInt("max-report-probes", 286), timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        var staticDataSets = discovery.ReportInventory.ReportControls
            .Where(x => !string.IsNullOrWhiteSpace(x.DataSetReference))
            .Select(x => x.DataSetReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredDataSet) && !staticDataSets.Contains(preferredDataSet, StringComparer.OrdinalIgnoreCase))
            staticDataSets = staticDataSets.Append(preferredDataSet).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var directories = await session.GetDataSetDirectoriesAsync(staticDataSets, discovery.IedDirectory, timeout.Token).ConfigureAwait(false);
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet, strictRcb, allowUrCbFallback, allowPollingFallback);

        Console.WriteLine();
        WriteReportSubscriptionPlan(plan, rawLimit);

        if (readValues && plan.Members.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Sample DataSet member reads:");
            foreach (var member in TakeWithLimit(plan.Members.Where(x => !string.IsNullOrWhiteSpace(x.UserReference)), Math.Min(rawLimit <= 0 ? plan.Members.Count : rawLimit, 16)))
            {
                var read = await session.ReadSmartAsync(discovery.IedDirectory, member.UserReference, timeout.Token).ConfigureAwait(false);
                Console.WriteLine($"  {member.UserReference}: {(read.IsSuccess && read.ReadResult.Value != null ? MmsDataValueRenderer.ToCompactString(read.ReadResult.Value, member.UserReference) : read.Message)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Next implementation gate:");
        Console.WriteLine("  After this static plan is stable, implement guarded writes: reserve -> RptEna=true -> GI=true -> receive InformationReport -> cleanup.");
        Console.WriteLine("  Do not enable reporting from an automatic wizard until report receiver/dispatcher is already running.");

        return plan.IsReady ? 0 : 1;
    }

    private static async Task<int> MmsRcbProbeAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("mms-rcb-probe requires <host-or-ip> <LD/LN.BR.name|LD/LN.RP.name>.");

        var host = args[0];
        var rcbReference = args[1];
        var options = CliOptions.Parse(args[2..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: direct selected RCB attribute probe (read-only).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: false, maxReportAttributeProbes: 0, timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        var rcb = discovery.ReportInventory.ReportControls.FirstOrDefault(x =>
            x.Reference.Equals(rcbReference, StringComparison.OrdinalIgnoreCase));
        if (rcb == null)
        {
            Console.WriteLine();
            Console.WriteLine($"RCB not found: {rcbReference}");
            Console.WriteLine("Closest candidates:");
            foreach (var candidate in discovery.ReportInventory.ReportControls
                         .Where(x => x.Reference.Contains(rcbReference, StringComparison.OrdinalIgnoreCase) ||
                                     rcbReference.Contains(x.Name, StringComparison.OrdinalIgnoreCase))
                         .Take(20))
            {
                Console.WriteLine($"  {candidate.Mode} {candidate.Reference}");
            }
            return 1;
        }

        await session.ProbeReportControlAttributesAsync(rcb, timeout.Token).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Selected RCB: {rcb.Mode} {rcb.Reference}");
        Console.WriteLine($"  DatSet={TextOrDash(rcb.DataSetReference)}");
        Console.WriteLine($"  RptID={TextOrDash(rcb.ReportId)}");
        Console.WriteLine($"  ConfRev={TextOrDash(rcb.ConfRev)}");
        Console.WriteLine($"  RptEna={TextOrDash(rcb.EnabledState)}");
        Console.WriteLine($"  Resv={(rcb.Buffered ? TextOrDash(rcb.ReservationTimeSeconds) : TextOrDash(rcb.ReservationState))}");
        Console.WriteLine($"  BufTm={TextOrDash(rcb.BufferTimeMs)}");
        Console.WriteLine($"  IntgPd={TextOrDash(rcb.IntegrityPeriodMs)}");
        Console.WriteLine($"  TrgOps={TextOrDash(rcb.TriggerOptions)}");
        Console.WriteLine($"  OptFlds={TextOrDash(rcb.OptionalFields)}");
        var explicitSafe = MmsReportSubscriptionPlanner.HasExplicitSafeStaticWriteState(rcb);
        Console.WriteLine($"  Explicit-safe for static write: {(explicitSafe ? "YES" : "NO")}");

        if (!explicitSafe && rcb.ProbeDiagnostics.Count > 0)
        {
            Console.WriteLine("  Probe diagnostics:");
            foreach (var line in rcb.ProbeDiagnostics.Take(16))
                Console.WriteLine($"    - {line}");
            if (rcb.ProbeDiagnostics.Count > 16)
                Console.WriteLine($"    ... +{rcb.ProbeDiagnostics.Count - 16} more");
        }

        return 0;
    }

    private static async Task<int> MmsReportStaticLiveAsync(string[] args, bool monitorMode = false)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-static-live requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var preferredRcb = options.Get("rcb", string.Empty);
        var preferredDataSet = options.Get("dataset", string.Empty);
        var strictRcb = options.GetBool("strict-rcb", fallback: false);
        var allowUrCbFallback = options.GetBool("allow-urcb-fallback", fallback: true);
        var allowPollingFallback = options.GetBool("allow-polling-fallback", fallback: true);
        var maxRcbClaimAttempts = options.GetInt("max-rcb-claim-attempts", 6);
        if (maxRcbClaimAttempts < 1)
            throw new ArgumentException("--max-rcb-claim-attempts must be at least 1.");

        var rcbProbeCount = options.GetInt("rcb-probe-count", 1);
        if (rcbProbeCount < 1)
            throw new ArgumentException("--rcb-probe-count must be at least 1.");

        var rcbProbeDelayMs = options.GetInt("rcb-probe-delay-ms", 1000);
        if (rcbProbeDelayMs < 0)
            throw new ArgumentException("--rcb-probe-delay-ms must be greater than or equal to 0.");

        var contentionCooldownSec = options.GetInt("contention-cooldown-sec", 60);
        if (contentionCooldownSec < 0)
            throw new ArgumentException("--contention-cooldown-sec must be greater than or equal to 0.");

        var evidencePath = options.Get("evidence", string.Empty);
        var durationSec = options.GetInt("duration-sec", monitorMode ? 60 : 15);
        if (durationSec < 1)
            throw new ArgumentException("--duration-sec must be at least 1.");

        var pollPoints = monitorMode
            ? SplitCsv(options.Get("poll-points", string.Empty))
            : Array.Empty<string>();
        var pollIntervalMs = options.GetInt("poll-interval-ms", 1000);
        if (pollPoints.Count > 0 && pollIntervalMs < 100)
            throw new ArgumentException("--poll-interval-ms must be at least 100 when --poll-points is used.");

        var giIntervalSec = options.GetInt("gi-interval-sec", 0);
        if (giIntervalSec < 0)
            throw new ArgumentException("--gi-interval-sec must be greater than or equal to 0.");

        var soakSnapshotSec = options.GetInt("soak-snapshot-sec", monitorMode ? 60 : 0);
        if (soakSnapshotSec < 0)
            throw new ArgumentException("--soak-snapshot-sec must be greater than or equal to 0.");

        var reserveSec = options.GetInt("reserve-sec", 30);
        if (reserveSec < 1)
            throw new ArgumentException("--reserve-sec must be at least 1.");

        var triggerGi = options.GetBool("gi", fallback: true);
        var confirmed = options.GetBool("yes", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine(monitorMode
            ? "Mode: guarded static report monitor (receive pump, writes RptEna/GI only when --yes is provided)."
            : "Mode: guarded static report live session (writes RptEna/GI only when --yes is provided).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");
        var associationState = session.State.ToString();
        var associationMessage = session.LastHandshakeMessage;
        Console.WriteLine($"Receive pump: {(session.IsReceivePumpRunning ? "running" : "stopped")}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportAttributeProbes: options.GetInt("max-report-probes", 286), timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        var staticDataSets = discovery.ReportInventory.ReportControls
            .Where(x => !string.IsNullOrWhiteSpace(x.DataSetReference))
            .Select(x => x.DataSetReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredDataSet) && !staticDataSets.Contains(preferredDataSet, StringComparer.OrdinalIgnoreCase))
            staticDataSets = staticDataSets.Append(preferredDataSet).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var directories = await session.GetDataSetDirectoriesAsync(staticDataSets, discovery.IedDirectory, timeout.Token).ConfigureAwait(false);
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet, strictRcb, allowUrCbFallback, allowPollingFallback);

        if (plan.IsReady && plan.ReportControl != null)
        {
            await session.ProbeReportControlAttributesAsync(plan.ReportControl, timeout.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(plan.ReportControl.DataSetReference) &&
                !staticDataSets.Contains(plan.ReportControl.DataSetReference, StringComparer.OrdinalIgnoreCase))
            {
                directories = await session.GetDataSetDirectoriesAsync(
                    staticDataSets.Append(plan.ReportControl.DataSetReference).Distinct(StringComparer.OrdinalIgnoreCase),
                    discovery.IedDirectory,
                    timeout.Token).ConfigureAwait(false);
            }

            plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet, strictRcb, allowUrCbFallback, allowPollingFallback);
        }

        Console.WriteLine();
        WriteReportSubscriptionPlan(plan, rawLimit);

        if (!plan.IsReady || plan.ReportControl == null)
        {
            Console.WriteLine();
            Console.WriteLine("Live static report is blocked. Fix the plan first.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Safety gate:");
        Console.WriteLine("  This command writes RptEna=true and optionally GI=true to the selected RCB, then cleans up with RptEna=false.");
        Console.WriteLine("  Use only on an isolated FAT/test IED or an unused RCB. The command will not proceed without --yes.");
        if (!MmsReportSubscriptionPlanner.HasExplicitSafeStaticWriteState(plan.ReportControl))
        {
            Console.WriteLine("  Live write is blocked because the selected RCB state is not explicit-safe after direct attribute backfill.");
            Console.WriteLine("  Required: DatSet present, RptEna=false, and no active Resv/ResvTms.");
            Console.WriteLine("  Run mms-report-plan --max-report-probes 286 --raw-limit 0 and inspect the selected RCB.");
            return 1;
        }

        if (!confirmed)
        {
            Console.WriteLine("  Dry-run only. Re-run with --yes to execute the guarded live session.");
            return 0;
        }

        var excludedClaimFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimAttempts = new List<MmsRcbClaimAttempt>();
        var contentionProbes = new List<MmsRcbContentionProbeResult>();
        var executedPlan = plan;
        MmsStaticReportSessionResult live = new();

        for (var attempt = 1; ; attempt++)
        {
            if (attempt > 1)
            {
                var retryPlan = BuildStaticPlanForClaimAttempt(
                    discovery.ReportInventory,
                    directories,
                    preferredRcb,
                    preferredDataSet,
                    strictRcb,
                    allowUrCbFallback,
                    allowPollingFallback,
                    excludedClaimFailures);

                if (retryPlan.IsReady && retryPlan.ReportControl != null)
                {
                    await session.ProbeReportControlAttributesAsync(retryPlan.ReportControl, timeout.Token).ConfigureAwait(false);
                    retryPlan = BuildStaticPlanForClaimAttempt(
                        discovery.ReportInventory,
                        directories,
                        preferredRcb,
                        preferredDataSet,
                        strictRcb,
                        allowUrCbFallback,
                        allowPollingFallback,
                        excludedClaimFailures);
                }

                Console.WriteLine();
                Console.WriteLine($"Smart RCB claim fallback attempt {attempt}: excluding previous failed candidate(s): {string.Join(", ", excludedClaimFailures)}");
                WriteReportSubscriptionPlan(retryPlan, rawLimit);

                if (!retryPlan.IsReady || retryPlan.ReportControl == null)
                {
                    live = new MmsStaticReportSessionResult
                    {
                        IsSuccess = false,
                        RcbClaimAttempts = claimAttempts,
                        Message = "All Smart RCB claim candidates failed or no safe fallback candidate remained."
                    };
                    executedPlan = retryPlan;
                    break;
                }

                if (!MmsReportSubscriptionPlanner.HasExplicitSafeStaticWriteState(retryPlan.ReportControl))
                {
                    live = new MmsStaticReportSessionResult
                    {
                        IsSuccess = false,
                        RcbClaimAttempts = claimAttempts,
                        Message = $"Smart RCB fallback candidate {retryPlan.ReportControl.Reference} is not explicit-safe for live write."
                    };
                    executedPlan = retryPlan;
                    break;
                }

                plan = retryPlan;
            }

            if (rcbProbeCount > 1 && plan.ReportControl != null)
            {
                var contentionProbe = await ProbeSelectedRcbContentionAsync(
                    session,
                    plan.ReportControl,
                    rcbProbeCount,
                    TimeSpan.FromMilliseconds(rcbProbeDelayMs),
                    contentionCooldownSec,
                    timeout.Token).ConfigureAwait(false);
                contentionProbes.Add(contentionProbe);

                Console.WriteLine();
                Console.WriteLine(contentionProbe.Summary);
                foreach (var observation in contentionProbe.Observations.Take(8))
                    Console.WriteLine($"  - {observation.Summary}");
                if (contentionProbe.Observations.Count > 8)
                    Console.WriteLine($"  ... +{contentionProbe.Observations.Count - 8} more RCB probe observation(s)");

                if (contentionProbe.IsContended)
                {
                    claimAttempts.Add(ToRcbPreClaimContentionAttempt(attempt, plan, contentionProbe));

                    if (!strictRcb && attempt < maxRcbClaimAttempts && plan.ReportControl != null)
                    {
                        excludedClaimFailures.Add(NormalizeRcbReferenceForCli(plan.ReportControl.Reference));
                        Console.WriteLine($"Smart RCB pre-claim contention detected on {plan.ReportControl.Reference}; putting this candidate in command-local cooldown and trying the next safe RCB.");
                        continue;
                    }

                    var blockedRcbReference = plan.ReportControl?.Reference ?? "-";
                    live = new MmsStaticReportSessionResult
                    {
                        IsSuccess = false,
                        RcbClaimAttempts = claimAttempts,
                        RcbContentionProbes = contentionProbes,
                        StartedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Warnings = [contentionProbe.Reason],
                        Message = $"Smart RCB pre-claim contention blocked the session for {blockedRcbReference}."
                    };
                    break;
                }
            }

            executedPlan = plan;
            Console.WriteLine();
            Console.WriteLine(monitorMode
                ? $"Starting guarded report monitor for {durationSec}s using {plan.ReportControl!.Reference} (claim attempt {attempt})..."
                : $"Starting guarded static report session for {durationSec}s using {plan.ReportControl!.Reference} (claim attempt {attempt})...");
            if (pollPoints.Count > 0)
                Console.WriteLine($"Poll reads: {pollPoints.Count} point(s), interval={pollIntervalMs}ms.");
            if (giIntervalSec > 0)
                Console.WriteLine($"Periodic GI: every {giIntervalSec}s after initial GI.");
            if (soakSnapshotSec > 0)
                Console.WriteLine($"Soak snapshots: every {soakSnapshotSec}s.");

            live = await session.RunGuardedStaticReportSessionAsync(
                plan,
                TimeSpan.FromSeconds(durationSec),
                reserveSec,
                triggerGi,
                timeout.Token,
                pollDirectory: pollPoints.Count > 0 ? discovery.IedDirectory : null,
                pollReferences: pollPoints,
                pollInterval: TimeSpan.FromMilliseconds(pollIntervalMs),
                periodicGeneralInterrogationInterval: giIntervalSec > 0 ? TimeSpan.FromSeconds(giIntervalSec) : null,
                soakSnapshotInterval: soakSnapshotSec > 0 ? TimeSpan.FromSeconds(soakSnapshotSec) : null).ConfigureAwait(false);

            var claimFailed = IsRcbClaimFailure(live);
            claimAttempts.Add(ToRcbClaimAttempt(attempt, plan, live, claimFailed));
            if (!claimFailed || strictRcb || attempt >= maxRcbClaimAttempts || plan.ReportControl == null)
                break;

            excludedClaimFailures.Add(NormalizeRcbReferenceForCli(plan.ReportControl.Reference));
            Console.WriteLine();
            Console.WriteLine($"Smart RCB claim failed on {plan.ReportControl.Reference}; trying the next safe candidate instead of fighting this RCB.");
        }

        live = WithRcbRuntimeEvidence(live, claimAttempts, contentionProbes);
        plan = executedPlan;

        Console.WriteLine();
        Console.WriteLine(live.Message);
        Console.WriteLine($"Receive routing: {TextOrDash(session.LastReceiveRoutingSummary)} pending={session.PendingConfirmedOperationCount} queuedReports={session.QueuedInformationReportCount}");
        WriteReportDiagnostics(live.Diagnostics);
        WriteReportVerification(live.Verification);
        WriteSoakSnapshots(live.SoakSnapshots);

        if (live.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in live.Warnings)
                Console.WriteLine($"  - {warning}");
        }

        Console.WriteLine("Write steps:");
        foreach (var step in live.WriteSteps)
            Console.WriteLine($"  {(step.IsSuccess ? "OK" : "FAIL")} {step.Attribute} {step.Reference}: {step.Message}");

        if (live.PollReads.Count > 0)
        {
            var ok = live.PollReads.Count(x => x.IsSuccess);
            Console.WriteLine($"Poll reads: {ok}/{live.PollReads.Count} succeeded");
            foreach (var poll in TakeWithLimit(live.PollReads, rawLimit))
            {
                var selected = string.IsNullOrWhiteSpace(poll.SelectedReference)
                    ? poll.Reference
                    : $"{poll.SelectedReference} [{poll.FunctionalConstraint}]";
                Console.WriteLine($"  {(poll.IsSuccess ? "OK" : "FAIL")} {poll.ReadAt:yyyy-MM-dd HH:mm:ss.fff} UTC {selected}: {poll.DisplayValue} - {poll.Message}");
            }
            WriteLimitNotice(live.PollReads.Count, rawLimit, "poll read(s)");
        }

        Console.WriteLine($"Reports received: {live.Reports.Count}");
        foreach (var report in TakeWithLimit(live.Reports, rawLimit))
            WriteReportFrame(report);
        WriteLimitNotice(live.Reports.Count, rawLimit, "report frame(s)");

        if (!string.IsNullOrWhiteSpace(evidencePath))
        {
            var files = await WriteReportEvidenceAsync(
                evidencePath,
                $"{host}:{port}",
                monitorMode ? "mms-report-monitor" : "mms-report-static-live",
                associationState,
                associationMessage,
                discovery.IedDirectory.Summary,
                discovery.ReportInventory.Summary,
                session.LastReceiveRoutingSummary,
                plan,
                live).ConfigureAwait(false);

            Console.WriteLine("Evidence written:");
            foreach (var file in files)
                Console.WriteLine($"  {file}");
        }

        return live.IsSuccess ? 0 : 1;
    }

    private static async Task<int> MmsReportDynamicPlanAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-dynamic-plan requires <host-or-ip> --points <point1,point2,...>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var requestedPoints = SplitCsv(options.Get("points", string.Empty));
        if (requestedPoints.Count == 0)
            throw new ArgumentException("--points must contain at least one IEC 61850 point reference separated by comma.");

        var preferredLd = options.Get("ld", string.Empty);
        var preferredRcb = options.Get("rcb", string.Empty);
        var strictRcb = options.GetBool("strict-rcb", fallback: false);
        var allowUrCbFallback = options.GetBool("allow-urcb-fallback", fallback: true);
        var allowPollingFallback = options.GetBool("allow-polling-fallback", fallback: true);
        var dataSetName = options.Get("dataset-name", "AR_DYN_DS01");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: dynamic report workflow planner (read-only, no CreateDataSet/DatSet write).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");
        Console.WriteLine($"Receive pump: {(session.IsReceivePumpRunning ? "running" : "stopped")}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportAttributeProbes: options.GetInt("max-report-probes", 286), timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        Console.WriteLine();
        Console.WriteLine("Requested dynamic DataSet points:");
        foreach (var point in requestedPoints)
        {
            var resolve = MmsFcResolver.Resolve(discovery.IedDirectory, point);
            Console.WriteLine($"  {point}: {resolve.Message}");
            if (resolve.BestCandidate != null)
                Console.WriteLine($"      -> {resolve.BestCandidate.UserReference} [{resolve.BestCandidate.FunctionalConstraint}] mms={resolve.BestCandidate.MmsReference}");
        }

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(discovery.ReportInventory, discovery.IedDirectory, requestedPoints, preferredLd, preferredRcb, dataSetName, strictRcb, allowUrCbFallback, allowPollingFallback);

        Console.WriteLine();
        WriteReportSubscriptionPlan(plan, rawLimit);

        Console.WriteLine();
        Console.WriteLine("Next implementation gate:");
        Console.WriteLine("  Dynamic reporting requires verified MMS DefineNamedVariableList, Write RCB.DatSet, reservation, RptEna, GI, and cleanup.");
        Console.WriteLine("  Keep first dynamic test small: 2-8 ST/MX points, isolated IED, no production SCADA connected to the same RCB pool.");

        return plan.IsReady ? 0 : 1;
    }

    private static async Task<int> MmsReportDynamicLiveAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-dynamic-live requires <host-or-ip> --points <point1,point2,...>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 120000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var rawLimit = options.GetInt("raw-limit", 80);
        var requestedPoints = SplitCsv(options.Get("points", string.Empty));
        if (requestedPoints.Count == 0)
            throw new ArgumentException("--points must contain at least one IEC 61850 point reference separated by comma.");

        var evidencePath = options.Get("evidence", string.Empty);
        var preferredLd = options.Get("ld", string.Empty);
        var preferredRcb = options.Get("rcb", string.Empty);
        var strictRcb = options.GetBool("strict-rcb", fallback: false);
        var allowUrCbFallback = options.GetBool("allow-urcb-fallback", fallback: true);
        var allowPollingFallback = options.GetBool("allow-polling-fallback", fallback: true);
        var dataSetName = options.Get("dataset-name", "AR_DYN_DS01");
        var durationSec = options.GetInt("duration-sec", 15);
        if (durationSec < 1)
            throw new ArgumentException("--duration-sec must be at least 1.");

        var reserveSec = options.GetInt("reserve-sec", 30);
        if (reserveSec < 1)
            throw new ArgumentException("--reserve-sec must be at least 1.");

        var triggerGi = options.GetBool("gi", fallback: true);
        var deleteDataSet = options.GetBool("delete-dataset", fallback: true);
        var confirmed = options.GetBool("yes", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine("Mode: guarded dynamic report live session (creates DataSet, writes DatSet/RptEna/GI only when --yes is provided).");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");
        var associationState = session.State.ToString();
        var associationMessage = session.LastHandshakeMessage;

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportAttributeProbes: options.GetInt("max-report-probes", 286), timeout.Token).ConfigureAwait(false);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);

        Console.WriteLine();
        Console.WriteLine("Requested dynamic DataSet points:");
        foreach (var point in requestedPoints)
        {
            var resolve = MmsFcResolver.Resolve(discovery.IedDirectory, point);
            Console.WriteLine($"  {point}: {resolve.Message}");
            if (resolve.BestCandidate != null)
                Console.WriteLine($"      -> {resolve.BestCandidate.UserReference} [{resolve.BestCandidate.FunctionalConstraint}] mms={resolve.BestCandidate.MmsReference}");
        }

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(discovery.ReportInventory, discovery.IedDirectory, requestedPoints, preferredLd, preferredRcb, dataSetName, strictRcb, allowUrCbFallback, allowPollingFallback);

        Console.WriteLine();
        WriteReportSubscriptionPlan(plan, rawLimit);

        if (!plan.IsReady || plan.ReportControl == null)
        {
            Console.WriteLine();
            Console.WriteLine("Live dynamic report is blocked. Fix the plan first.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Safety gate:");
        Console.WriteLine("  This command creates a dynamic DataSet, points the selected free RCB at it, enables reporting, optionally sends GI, then disables and cleans up.");
        Console.WriteLine("  Use only on an isolated FAT/test IED or a confirmed unused dynamic RCB slot. The command will not proceed without --yes.");
        if (!confirmed)
        {
            Console.WriteLine("  Dry-run only. Re-run with --yes to execute the guarded live session.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Starting guarded dynamic report session for {durationSec}s...");
        var live = await session.RunGuardedDynamicReportSessionAsync(
            plan,
            TimeSpan.FromSeconds(durationSec),
            reserveSec,
            triggerGi,
            deleteDataSet,
            timeout.Token,
            discovery.IedDirectory).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(live.Message);
        Console.WriteLine($"Receive routing: {TextOrDash(session.LastReceiveRoutingSummary)} pending={session.PendingConfirmedOperationCount} queuedReports={session.QueuedInformationReportCount}");
        WriteReportDiagnostics(live.Diagnostics);
        WriteReportVerification(live.Verification);

        if (live.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in live.Warnings)
                Console.WriteLine($"  - {warning}");
        }

        Console.WriteLine("Write steps:");
        foreach (var step in live.WriteSteps)
            Console.WriteLine($"  {(step.IsSuccess ? "OK" : "FAIL")} {step.Attribute} {step.Reference}: {step.Message}");

        Console.WriteLine($"Reports received: {live.Reports.Count}");
        foreach (var report in TakeWithLimit(live.Reports, rawLimit))
            WriteReportFrame(report);
        WriteLimitNotice(live.Reports.Count, rawLimit, "report frame(s)");

        if (!string.IsNullOrWhiteSpace(evidencePath))
        {
            var files = await WriteReportEvidenceAsync(
                evidencePath,
                $"{host}:{port}",
                "mms-report-dynamic-live",
                associationState,
                associationMessage,
                discovery.IedDirectory.Summary,
                discovery.ReportInventory.Summary,
                session.LastReceiveRoutingSummary,
                plan,
                live).ConfigureAwait(false);

            Console.WriteLine("Evidence written:");
            foreach (var file in files)
                Console.WriteLine($"  {file}");
        }

        return live.IsSuccess ? 0 : 1;
    }

    private static async Task<int> MmsReportPlanAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("mms-report-plan requires <host-or-ip>.");

        var host = args[0];
        var options = CliOptions.Parse(args[1..]);
        var port = options.GetInt("port", 102);
        if (port is < 1 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");

        var timeoutMs = options.GetInt("timeout-ms", 60000);
        if (timeoutMs < 1)
            throw new ArgumentException("--timeout-ms must be at least 1.");

        var maxReportProbes = options.GetInt("max-report-probes", 64);
        var rawLimit = options.GetInt("raw-limit", 80);
        var kindFilter = options.Get("kind", string.Empty).Trim();
        var onlySafe = options.GetBool("only-safe", fallback: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        await using var session = new MmsClientSession();

        Console.WriteLine($"MMS target: {host}:{port}");
        Console.WriteLine($"Mode: report readiness planner; maxReportProbes={maxReportProbes}");
        await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
        Console.WriteLine($"Association: {session.State}");
        Console.WriteLine($"  {session.LastHandshakeMessage}");

        var discovery = await session.DiscoverAsync(probeReportAttributes: true, maxReportProbes, timeout.Token).ConfigureAwait(false);
        var plan = MmsReportReadinessPlanner.Build(discovery.ReportInventory);
        Console.WriteLine(discovery.IedDirectory.Summary);
        Console.WriteLine(discovery.ReportInventory.Summary);
        Console.WriteLine(plan.Summary);

        IEnumerable<MmsReportReadiness> items = plan.Items;
        if (onlySafe)
            items = items.Where(x => x.IsReadyForSafeSubscription);
        if (!string.IsNullOrWhiteSpace(kindFilter))
            items = items.Where(x => x.Label.Equals(kindFilter, StringComparison.OrdinalIgnoreCase));

        var filtered = items.ToArray();
        Console.WriteLine();
        Console.WriteLine($"RCB candidates shown: {Math.Min(rawLimit <= 0 ? filtered.Length : rawLimit, filtered.Length)} of {filtered.Length}");
        foreach (var item in TakeWithLimit(filtered, rawLimit))
            Console.WriteLine(FormatReportReadiness(item));
        WriteLimitNotice(filtered.Length, rawLimit, "RCB readiness item(s)");

        Console.WriteLine();
        Console.WriteLine("Recommended next safe path:");
        Console.WriteLine("  1. Prefer ReadyStaticDataSet BRCB for first subscription test.");
        Console.WriteLine("  2. Treat EmptyDynamicSlotNeedsDataSet as future dynamic DataSet workflow, not immediate RptEna target.");
        Console.WriteLine("  3. Do not touch OccupiedEnabled or ReservedByOtherClient automatically.");

        return plan.SafeCandidates.Count > 0 ? 0 : 1;
    }

    private static async Task<int> PublishSvLiveAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("publish-sv-live requires <scl-file> --adapter <index|name>.");

        var options = CliOptions.Parse(args[1..]);
        var adapterSelector = options.GetRequired("adapter");
        var streamIndex = options.GetInt("stream-index", 1);
        if (streamIndex < 1)
            throw new ArgumentException("--stream-index is 1-based and must be at least 1.");

        var dryRun = options.GetBool("dry-run", fallback: false);
        var confirmed = options.GetBool("yes", fallback: false);
        if (!dryRun && !confirmed)
            throw new InvalidOperationException("Live SV publish sends raw Ethernet frames. Re-run with --yes after selecting an isolated test NIC.");

        var document = new SclParser().Load(args[0]);
        var profiles = SampledValuesPublisherProfile.CreateMany(document);
        if (profiles.Count == 0)
            throw new InvalidOperationException("The SCL document does not contain a publishable SampledValueControl stream.");

        if (streamIndex > profiles.Count)
            throw new ArgumentOutOfRangeException(nameof(streamIndex), $"--stream-index must be 1..{profiles.Count} for this SCL.");

        var profile = profiles[streamIndex - 1];
        var adapter = NpcapAdapterCatalog.ResolveAdapterInfo(adapterSelector);
        var sourceMac = ResolveSourceMac(options, adapter);
        var sampleRateHz = options.GetDouble("rate-hz", profile.Stream.SampleRate == 0 ? 4000 : profile.Stream.SampleRate);
        if (sampleRateHz <= 0)
            throw new ArgumentException("--rate-hz must be greater than 0.");

        var nominalHz = options.GetDouble("nominal-hz", 50);
        if (nominalHz <= 0)
            throw new ArgumentException("--nominal-hz must be greater than 0.");

        var continuous = options.GetBool("continuous", fallback: false);
        var durationSeconds = options.GetDouble("duration-sec", 0);
        if (durationSeconds < 0)
            throw new ArgumentException("--duration-sec must be greater than or equal to 0.");

        var frameLimit = ResolveFrameLimit(options, sampleRateHz, continuous, durationSeconds);
        var statusIntervalMs = options.GetInt("status-ms", 1000);
        var sampleCounterWrap = ResolveSampleCounterWrapOption(options, profile, nominalHz);

        Console.WriteLine($"SCL: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"Mode: {(dryRun ? "dry-run (no NIC transmit)" : "live raw Ethernet transmit")}");
        Console.WriteLine($"Adapter: [{adapter.Index}] MAC={adapter.MacAddress?.ToString() ?? "-"} {TextOrDash(adapter.Description)}");
        Console.WriteLine($"SV stream: #{streamIndex}/{profiles.Count} {profile.Stream.ControlBlockReference}");
        Console.WriteLine($"  svID={TextOrDash(profile.Stream.SvId)} APPID=0x{profile.AppId:X4} dst={profile.Destination} VLAN={FormatVlan(profile.Vlan)}");
        Console.WriteLine($"  source={sourceMac} {FormatFrameLimit(frameLimit)} rate={sampleRateHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz nominal={nominalHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz datasetEntries={profile.Entries.Count} payloadBytes={profile.PayloadLayout.PayloadByteLength} smpCntWrap={FormatNullableUShort(sampleCounterWrap)}");
        if (!profile.PayloadLayout.IsFullySupported)
        {
            Console.WriteLine("  Unsupported SV payload entries:");
            foreach (var item in profile.PayloadLayout.UnsupportedElements.Take(8))
                Console.WriteLine($"    - {item.SignalReference} bType={TextOrDash(item.BType)}");
            if (profile.PayloadLayout.UnsupportedElements.Count > 8)
                Console.WriteLine($"    - ... {profile.PayloadLayout.UnsupportedElements.Count - 8} more unsupported entrie(s)");
        }
        if (!frameLimit.HasValue && durationSeconds <= 0)
            Console.WriteLine("  Press Ctrl+C to stop the continuous publisher.");

        IProcessBusTransport transport = dryRun
            ? new InMemoryProcessBusTransport()
            : new NpcapProcessBusTransport(adapterSelector);

        var session = new SampledValuesPublisherSession(profile, sourceMac, transport, sampleCounterWrap: sampleCounterWrap);
        var startedTicks = Stopwatch.GetTimestamp();
        var startedAt = DateTimeOffset.UtcNow;
        var nextStatusTicks = startedTicks;
        var finiteProgressEvery = frameLimit.HasValue ? Math.Max(1, frameLimit.Value / 10) : 0;
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stop requested; finishing current SV publish loop.");
        };
        Console.CancelKeyPress += cancelHandler;

        long sent = 0;
        ushort lastSampleCount = 0;
        var lastPayloadBytes = 0;

        try
        {
            while (!frameLimit.HasValue || sent < frameLimit.Value)
            {
                if (!WaitUntil(startedTicks, sent, sampleRateHz, stop.Token))
                    break;

                var timestamp = startedAt.AddTicks((long)Math.Round(sent * TimeSpan.TicksPerSecond / sampleRateHz));
                var sampleTime = new Iec61850UtcTime(timestamp, Quality: 0);
                var payload = profile.BuildDemoPayload(sent, sampleRateHz, nominalHz, sampleTime);
                lastSampleCount = session.NextSampleCount;
                lastPayloadBytes = payload.Length;
                await session.PublishNextAsync(
                    payload,
                    sampleTime).ConfigureAwait(false);
                sent++;

                var nowTicks = Stopwatch.GetTimestamp();
                var reachedFiniteProgress = frameLimit.HasValue && (sent == 1 || sent == frameLimit.Value || sent % finiteProgressEvery == 0);
                var reachedContinuousStatus = !frameLimit.HasValue && statusIntervalMs > 0 && nowTicks >= nextStatusTicks;
                if (reachedFiniteProgress || reachedContinuousStatus)
                {
                    Console.WriteLine(FormatLivePublishProgress(sent, frameLimit, startedTicks, lastSampleCount, lastPayloadBytes));
                    if (statusIntervalMs > 0)
                        nextStatusTicks = nowTicks + (long)Math.Round(statusIntervalMs * Stopwatch.Frequency / 1000.0);
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            (transport as IDisposable)?.Dispose();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        var effectiveRate = sent / Math.Max(elapsed.TotalSeconds, 0.000001);
        Console.WriteLine($"SV publish complete: frames={sent} elapsed={elapsed.TotalSeconds:0.###}s effectiveRate={effectiveRate:0.###} fps lastSmpCnt={lastSampleCount} payloadBytes={lastPayloadBytes}");
        return 0;
    }

    private static async Task<int> PublishGooseLiveAsync(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("publish-goose-live requires <scl-file> --adapter <index|name>.");

        var options = CliOptions.Parse(args[1..]);
        var adapterSelector = options.GetRequired("adapter");
        var streamIndex = options.GetInt("stream-index", 1);
        if (streamIndex < 1)
            throw new ArgumentException("--stream-index is 1-based and must be at least 1.");

        var dryRun = options.GetBool("dry-run", fallback: false);
        var confirmed = options.GetBool("yes", fallback: false);
        if (!dryRun && !confirmed)
            throw new InvalidOperationException("Live GOOSE publish sends raw Ethernet frames. Re-run with --yes after selecting an isolated test NIC.");

        var document = new SclParser().Load(args[0]);
        var profiles = GoosePublisherProfile.CreateMany(document);
        if (profiles.Count == 0)
            throw new InvalidOperationException("The SCL document does not contain a publishable GSEControl stream.");

        if (streamIndex > profiles.Count)
            throw new ArgumentOutOfRangeException(nameof(streamIndex), $"--stream-index must be 1..{profiles.Count} for this SCL.");

        var profile = profiles[streamIndex - 1];
        var adapter = NpcapAdapterCatalog.ResolveAdapterInfo(adapterSelector);
        var sourceMac = ResolveSourceMac(options, adapter);
        var minTimeMs = (uint)options.GetInt("min-ms", checked((int)profile.Stream.MinTimeMilliseconds));
        var maxTimeMs = (uint)options.GetInt("max-ms", checked((int)profile.Stream.MaxTimeMilliseconds));
        var schedule = new GooseRetransmissionSchedule(minTimeMs, maxTimeMs);
        var continuous = options.GetBool("continuous", fallback: false);
        var durationSeconds = options.GetDouble("duration-sec", 0);
        if (durationSeconds < 0)
            throw new ArgumentException("--duration-sec must be greater than or equal to 0.");

        var frameLimit = ResolveGooseFrameLimit(options, continuous, durationSeconds);
        var statusIntervalMs = options.GetInt("status-ms", 1000);
        var toggleEverySeconds = options.GetDouble("toggle-every-sec", 0);
        if (toggleEverySeconds < 0)
            throw new ArgumentException("--toggle-every-sec must be greater than or equal to 0.");

        var test = options.GetBool("test", fallback: false);
        var needsCommissioning = options.GetBool("nds-com", fallback: false);
        var state = options.GetBool("initial-state", fallback: false);

        Console.WriteLine($"SCL: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"Mode: {(dryRun ? "dry-run (no NIC transmit)" : "live raw Ethernet transmit")}");
        Console.WriteLine($"Adapter: [{adapter.Index}] MAC={adapter.MacAddress?.ToString() ?? "-"} {TextOrDash(adapter.Description)}");
        Console.WriteLine($"GOOSE stream: #{streamIndex}/{profiles.Count} {profile.Stream.ControlBlockReference}");
        Console.WriteLine($"  goID={TextOrDash(profile.Stream.GoId)} APPID=0x{profile.AppId:X4} dst={profile.Destination} VLAN={FormatVlan(profile.Vlan)}");
        Console.WriteLine($"  source={sourceMac} {FormatGooseLimit(frameLimit, durationSeconds)} min={schedule.MinTimeMilliseconds} ms max={schedule.MaxTimeMilliseconds} ms datasetEntries={profile.Entries.Count}");
        Console.WriteLine($"  toggleEvery={(toggleEverySeconds <= 0 ? "off" : $"{toggleEverySeconds.ToString("0.###", CultureInfo.InvariantCulture)}s")} test={test} ndsCom={needsCommissioning}");
        if (!frameLimit.HasValue && durationSeconds <= 0)
            Console.WriteLine("  Press Ctrl+C to stop the continuous publisher.");

        IProcessBusTransport transport = dryRun
            ? new InMemoryProcessBusTransport()
            : new NpcapProcessBusTransport(adapterSelector);

        var session = new GoosePublisherSession(profile, sourceMac, transport);
        var startedTicks = Stopwatch.GetTimestamp();
        var deadlineTicks = durationSeconds > 0
            ? startedTicks + (long)Math.Round(durationSeconds * Stopwatch.Frequency)
            : (long?)null;
        var eventTimestamp = DateTimeOffset.UtcNow;
        long stateGeneration = 0;
        var nextStatusTicks = startedTicks;
        var nextToggleTicks = toggleEverySeconds > 0
            ? startedTicks + (long)Math.Round(toggleEverySeconds * Stopwatch.Frequency)
            : long.MaxValue;
        var finiteProgressEvery = frameLimit.HasValue ? Math.Max(1, frameLimit.Value / 10) : 0;
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stop requested; finishing current GOOSE publish loop.");
        };
        Console.CancelKeyPress += cancelHandler;

        long sent = 0;
        uint lastStateNumber = 0;
        uint lastSequenceNumber = 0;
        var lastPayloadValues = 0;

        try
        {
            while ((!frameLimit.HasValue || sent < frameLimit.Value) &&
                   (!deadlineTicks.HasValue || Stopwatch.GetTimestamp() < deadlineTicks.Value))
            {
                var nowTicks = Stopwatch.GetTimestamp();
                var stateChanged = false;
                if (nowTicks >= nextToggleTicks)
                {
                    state = !state;
                    stateGeneration++;
                    eventTimestamp = DateTimeOffset.UtcNow;
                    schedule.Reset();
                    stateChanged = true;
                    nextToggleTicks = nowTicks + (long)Math.Round(toggleEverySeconds * Stopwatch.Frequency);
                }

                var values = BuildGooseStateValues(profile.Entries, eventTimestamp, state, stateGeneration);
                var frame = await session.PublishAsync(
                    values,
                    new Iec61850UtcTime(DateTimeOffset.UtcNow, Quality: 0),
                    stateChanged,
                    test,
                    needsCommissioning,
                    stop.Token).ConfigureAwait(false);

                sent++;
                lastPayloadValues = values.Count;
                if (GooseFrameParser.TryParseEthernetFrame(frame, out var parsed))
                {
                    lastStateNumber = parsed.Pdu.StateNumber;
                    lastSequenceNumber = parsed.Pdu.SequenceNumber;
                }

                nowTicks = Stopwatch.GetTimestamp();
                var reachedFiniteProgress = frameLimit.HasValue && (sent == 1 || sent == frameLimit.Value || sent % finiteProgressEvery == 0);
                var reachedContinuousStatus = !frameLimit.HasValue && statusIntervalMs > 0 && nowTicks >= nextStatusTicks;
                if (reachedFiniteProgress || reachedContinuousStatus)
                {
                    Console.WriteLine(FormatGoosePublishProgress(sent, frameLimit, startedTicks, lastStateNumber, lastSequenceNumber, state, lastPayloadValues));
                    if (statusIntervalMs > 0)
                        nextStatusTicks = nowTicks + (long)Math.Round(statusIntervalMs * Stopwatch.Frequency / 1000.0);
                }

                var delayMs = schedule.NextDelayMilliseconds();
                if (!WaitForDelay(delayMs, stop.Token, deadlineTicks))
                    break;
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            (transport as IDisposable)?.Dispose();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        var rate = sent / Math.Max(elapsed.TotalSeconds, 0.000001);
        Console.WriteLine($"GOOSE publish complete: frames={sent} elapsed={elapsed.TotalSeconds:0.###}s effectiveRate={rate:0.###} fps stNum={lastStateNumber} sqNum={lastSequenceNumber} values={lastPayloadValues}");
        return 0;
    }

    private static void AppendSampledValuesPackets(
        SclDocument document,
        MacAddress sourceMac,
        int frameCount,
        DateTimeOffset startTime,
        ICollection<PcapPacket> packets)
    {
        if (frameCount <= 0)
            return;

        var profiles = SampledValuesPublisherProfile.CreateMany(document);
        foreach (var profile in profiles)
        {
            var transport = new InMemoryProcessBusTransport();
            var sampleRate = profile.Stream.SampleRate == 0 ? 4000 : profile.Stream.SampleRate;
            var session = new SampledValuesPublisherSession(profile, sourceMac, transport, sampleCounterWrap: profile.ResolveSampleCounterWrap(50));
            var intervalMicros = ResolveSvIntervalMicros((ushort)sampleRate);

            for (var i = 0; i < frameCount; i++)
            {
                var timestamp = startTime.AddTicks(i * intervalMicros * 10L);
                var sampleTime = new Iec61850UtcTime(timestamp, Quality: 0);
                var payload = profile.BuildDemoPayload(i, sampleRate, 50, sampleTime);
                var frame = session.PublishNextAsync(
                    payload,
                    sampleTime).AsTask().GetAwaiter().GetResult();
                packets.Add(new PcapPacket(timestamp, frame));
            }
        }
    }

    private static void AppendSampledValuesDiagnosticPackets(
        SclDocument document,
        MacAddress sourceMac,
        int frameCount,
        DateTimeOffset startTime,
        ICollection<PcapPacket> packets)
    {
        if (frameCount <= 0)
            return;

        var profiles = SampledValuesPublisherProfile.CreateMany(document);
        foreach (var profile in profiles)
        {
            var sampleRate = profile.Stream.SampleRate == 0 ? (ushort)4000 : profile.Stream.SampleRate;
            var sampleMode = TryMapSampleMode(profile.Stream.SampleMode) ?? (ushort)0;
            var intervalMicros = ResolveSvIntervalMicros(sampleRate);
            var nominalPayload = profile.BuildDemoPayload(0, sampleRate, 50, new Iec61850UtcTime(startTime, Quality: 0));
            var shortPayload = nominalPayload.Length > 4 ? nominalPayload.Take(nominalPayload.Length - 4).ToArray() : nominalPayload.Take(Math.Max(0, nominalPayload.Length - 1)).ToArray();

            var scripted = new[]
            {
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 10, timestamp: startTime, samplePayload: nominalPayload, sampleSynchronization: 2, sampleRate: sampleRate, sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 11, timestamp: startTime.AddTicks(intervalMicros * 10L), samplePayload: nominalPayload, sampleSynchronization: 2, sampleRate: sampleRate, sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 14, timestamp: startTime.AddTicks(intervalMicros * 20L), samplePayload: nominalPayload, sampleSynchronization: 2, sampleRate: sampleRate, sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 14, timestamp: startTime.AddTicks(intervalMicros * 30L), samplePayload: nominalPayload, sampleSynchronization: 2, sampleRate: sampleRate, sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 13, timestamp: startTime.AddTicks(intervalMicros * 40L), samplePayload: nominalPayload, sampleSynchronization: 2, sampleRate: sampleRate, sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildSampledValuesPacket(profile, sourceMac, sampleCount: 15, timestamp: startTime.AddTicks(intervalMicros * 50L), samplePayload: shortPayload, sampleSynchronization: 0, sampleRate: (ushort)(sampleRate + 1), sampleMode: sampleMode, configurationRevision: profile.Stream.ConfigurationRevision)
            };

            foreach (var packet in scripted.Take(Math.Max(1, frameCount)))
                packets.Add(packet);
        }
    }

    private static PcapPacket BuildSampledValuesPacket(
        SampledValuesPublisherProfile profile,
        MacAddress sourceMac,
        ushort sampleCount,
        DateTimeOffset timestamp,
        ReadOnlySpan<byte> samplePayload,
        byte sampleSynchronization,
        ushort? sampleRate,
        ushort? sampleMode,
        uint configurationRevision)
    {
        var frame = new SampledValuesFrame
        {
            Destination = profile.Destination,
            Source = sourceMac,
            Vlan = profile.Vlan,
            AppId = profile.AppId,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = profile.Stream.SvId,
                        DataSetReference = profile.Stream.DataSetReference,
                        SampleCount = sampleCount,
                        ConfigurationRevision = configurationRevision,
                        ReferenceTime = new Iec61850UtcTime(timestamp, Quality: 0),
                        SampleSynchronization = sampleSynchronization,
                        SampleRate = sampleRate,
                        SampleMode = sampleMode,
                        SamplePayload = samplePayload.ToArray()
                    }
                ]
            }
        };

        return new PcapPacket(timestamp, SampledValuesFrameBuilder.BuildEthernetFrame(frame));
    }

    private static async Task AppendGoosePacketsAsync(
        SclDocument document,
        MacAddress sourceMac,
        int frameCount,
        DateTimeOffset startTime,
        ICollection<PcapPacket> packets)
    {
        if (frameCount <= 0)
            return;

        var profiles = GoosePublisherProfile.CreateMany(document);
        foreach (var profile in profiles)
        {
            var transport = new InMemoryProcessBusTransport();
            var session = new GoosePublisherSession(profile, sourceMac, transport);
            var state = false;
            long stateGeneration = 0;
            var eventTimestamp = startTime;

            for (var i = 0; i < frameCount; i++)
            {
                var timestamp = startTime.AddMilliseconds(i * 250);
                var stateChanged = i == 0 || i % 3 == 0;
                if (i > 0 && stateChanged)
                {
                    state = !state;
                    stateGeneration++;
                    eventTimestamp = timestamp;
                }

                var values = BuildGooseStateValues(profile.Entries, eventTimestamp, state, stateGeneration);
                var frame = await session.PublishAsync(
                    values,
                    new Iec61850UtcTime(timestamp, Quality: 0),
                    stateChanged: stateChanged).ConfigureAwait(false);
                packets.Add(new PcapPacket(timestamp, frame));
            }
        }
    }


    private static void AppendGooseDiagnosticPackets(
        SclDocument document,
        MacAddress sourceMac,
        int frameCount,
        DateTimeOffset startTime,
        ICollection<PcapPacket> packets)
    {
        if (frameCount <= 0)
            return;

        var profiles = GoosePublisherProfile.CreateMany(document);
        foreach (var profile in profiles)
        {
            var eventTimestamp = startTime;
            var normalValues = BuildGooseStateValues(profile.Entries, eventTimestamp, state: false, stateIndex: 0);
            var changedValues = BuildGooseStateValues(profile.Entries, eventTimestamp.AddMilliseconds(1500), state: true, stateIndex: 1);
            var tailValues = BuildGooseStateValues(profile.Entries, eventTimestamp.AddMilliseconds(1600), state: true, stateIndex: 2);

            var scripted = new[]
            {
                BuildGoosePacket(profile, sourceMac, normalValues, startTime, stateNumber: 2, sequenceNumber: 0, test: false, needsCommissioning: false, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildGoosePacket(profile, sourceMac, normalValues, startTime.AddMilliseconds(100), stateNumber: 2, sequenceNumber: 1, test: false, needsCommissioning: false, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildGoosePacket(profile, sourceMac, normalValues, startTime.AddMilliseconds(200), stateNumber: 2, sequenceNumber: 4, test: false, needsCommissioning: false, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildGoosePacket(profile, sourceMac, changedValues, startTime.AddMilliseconds(1500), stateNumber: 2, sequenceNumber: 5, test: true, needsCommissioning: false, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildGoosePacket(profile, sourceMac, changedValues, startTime.AddMilliseconds(1600), stateNumber: 1, sequenceNumber: 0, test: false, needsCommissioning: true, configurationRevision: profile.Stream.ConfigurationRevision),
                BuildGoosePacket(profile, sourceMac, tailValues.Take(Math.Max(0, tailValues.Count - 1)).ToArray(), startTime.AddMilliseconds(1700), stateNumber: 3, sequenceNumber: 0, test: false, needsCommissioning: false, configurationRevision: profile.Stream.ConfigurationRevision)
            };

            foreach (var packet in scripted.Take(Math.Max(1, frameCount)))
                packets.Add(packet);
        }
    }

    private static PcapPacket BuildGoosePacket(
        GoosePublisherProfile profile,
        MacAddress sourceMac,
        IReadOnlyList<MmsDataValue> values,
        DateTimeOffset timestamp,
        uint stateNumber,
        uint sequenceNumber,
        bool test,
        bool needsCommissioning,
        uint configurationRevision)
    {
        var frame = new GooseFrame
        {
            Destination = profile.Destination,
            Source = sourceMac,
            Vlan = profile.Vlan,
            AppId = profile.AppId,
            Pdu = new GoosePdu
            {
                GoCbRef = profile.Stream.ControlBlockReference,
                TimeAllowedToLiveMilliseconds = profile.Stream.MaxTimeMilliseconds == 0 ? 1000U : profile.Stream.MaxTimeMilliseconds,
                DataSetReference = profile.Stream.DataSetReference,
                GoId = string.IsNullOrWhiteSpace(profile.Stream.GoId) ? profile.Stream.ControlName : profile.Stream.GoId,
                Timestamp = new Iec61850UtcTime(timestamp, Quality: 0),
                StateNumber = stateNumber,
                SequenceNumber = sequenceNumber,
                Test = test,
                ConfigurationRevision = configurationRevision,
                NeedsCommissioning = needsCommissioning,
                Values = values
            }
        };

        return new PcapPacket(timestamp, GooseFrameBuilder.BuildEthernetFrame(frame));
    }

    private static IReadOnlyList<MmsDataValue> BuildGooseStateValues(
        IReadOnlyList<SclDataSetEntry> entries,
        DateTimeOffset eventTimestamp,
        bool state,
        long stateIndex)
    {
        var values = new List<MmsDataValue>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.IsTimestamp)
            {
                values.Add(MmsDataValue.UtcTime(new Iec61850UtcTime(eventTimestamp, Quality: 0)));
            }
            else if (entry.IsQuality)
            {
                values.Add(MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }));
            }
            else if (string.Equals(entry.BType, "BOOLEAN", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.BType, "Bool", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(MmsDataValue.Boolean(state));
            }
            else if (entry.BType.Contains("INT", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(MmsDataValue.Integer(stateIndex + entry.Index));
            }
            else
            {
                values.Add(MmsDataValue.VisibleString($"state-{stateIndex}-{entry.Index}"));
            }
        }

        return values;
    }

    private static long ResolveSvIntervalMicros(ushort sampleRate)
        => sampleRate == 0 ? 250 : Math.Max(1, 1_000_000L / sampleRate);

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

    private static MacAddress ResolveSourceMac(CliOptions options, NpcapAdapterInfo adapter)
    {
        if (options.TryGet("source-mac", out var sourceMacText))
            return MacAddress.Parse(sourceMacText);

        if (adapter.MacAddress.HasValue)
            return adapter.MacAddress.Value;

        throw new InvalidOperationException("The selected adapter did not expose a MAC address. Provide --source-mac XX:XX:XX:XX:XX:XX.");
    }

    private static long? ResolveFrameLimit(CliOptions options, double sampleRateHz, bool continuous, double durationSeconds)
    {
        if (durationSeconds > 0)
            return Math.Max(1, (long)Math.Round(sampleRateHz * durationSeconds));

        if (options.TryGet("frames", out _))
        {
            var frames = options.GetInt("frames", 0);
            if (frames < 1)
                throw new ArgumentException("--frames must be at least 1.");

            return frames;
        }

        return continuous ? null : 4000;
    }

    private static ushort? ResolveSampleCounterWrapOption(CliOptions options, SampledValuesPublisherProfile profile, double nominalHz)
    {
        var text = options.Get("smpcnt-wrap", "auto").Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return profile.ResolveSampleCounterWrap(nominalHz);

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("0", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 1)
            throw new ArgumentException("--smpcnt-wrap must be auto, none, or an integer greater than 1.");

        return parsed;
    }

    private static long? ResolveGooseFrameLimit(
        CliOptions options,
        bool continuous,
        double durationSeconds)
    {
        if (options.TryGet("frames", out _))
        {
            var frames = options.GetInt("frames", 0);
            if (frames < 1)
                throw new ArgumentException("--frames must be at least 1.");

            return frames;
        }

        if (durationSeconds > 0)
            return null;

        return continuous ? null : 16;
    }

    private static string FormatFrameLimit(long? frameLimit)
        => frameLimit.HasValue ? $"frames={frameLimit.Value}" : "frames=continuous";

    private static string FormatNullableUShort(ushort? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";

    private static string FormatGooseLimit(long? frameLimit, double durationSeconds)
    {
        if (frameLimit.HasValue)
            return $"frames={frameLimit.Value}";

        return durationSeconds > 0
            ? $"duration={durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s"
            : "frames=continuous";
    }

    private static long StatusIntervalTicks(int statusIntervalMilliseconds)
        => (long)Math.Round(Math.Max(1, statusIntervalMilliseconds) * Stopwatch.Frequency / 1000.0);

    private static string FormatLivePublishProgress(
        long sent,
        long? frameLimit,
        long startedTicks,
        ushort sampleCount,
        int payloadBytes)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        var rate = sent / Math.Max(elapsed.TotalSeconds, 0.000001);
        var target = frameLimit.HasValue ? $"/{frameLimit.Value}" : string.Empty;
        return $"  sent={sent}{target} elapsed={elapsed.TotalSeconds:0.###}s rate={rate:0.###} fps smpCnt={sampleCount} payloadBytes={payloadBytes}";
    }

    private static string FormatGoosePublishProgress(
        long sent,
        long? frameLimit,
        long startedTicks,
        uint stateNumber,
        uint sequenceNumber,
        bool state,
        int valueCount)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        var rate = sent / Math.Max(elapsed.TotalSeconds, 0.000001);
        var target = frameLimit.HasValue ? $"/{frameLimit.Value}" : string.Empty;
        return $"  sent={sent}{target} elapsed={elapsed.TotalSeconds:0.###}s rate={rate:0.###} fps stNum={stateNumber} sqNum={sequenceNumber} state={state} values={valueCount}";
    }

    private static bool WaitUntil(long startTimestamp, long frameIndex, double rateHz, CancellationToken cancellationToken)
    {
        var targetTimestamp = startTimestamp + (long)Math.Round(frameIndex * Stopwatch.Frequency / rateHz);

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return true;

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > 2)
                Thread.Sleep(1);
            else
                Thread.SpinWait(50);
        }
    }

    private static bool WaitForDelay(int delayMilliseconds, CancellationToken cancellationToken, long? deadlineTicks = null)
    {
        var started = Stopwatch.GetTimestamp();
        var target = started + (long)Math.Round(delayMilliseconds * Stopwatch.Frequency / 1000.0);
        if (deadlineTicks.HasValue)
            target = Math.Min(target, deadlineTicks.Value);

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var remainingTicks = target - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return true;

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > 2)
                Thread.Sleep(1);
            else
                Thread.SpinWait(50);
        }
    }

    private static bool IsEtherType(byte[] frame, ushort etherType)
    {
        if (frame.Length < 14)
            return false;

        var offset = frame.Length >= 18 &&
                     System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12, 2)) == EthernetConstants.VlanTagEtherType
            ? 16
            : 12;

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(offset, 2)) == etherType;
    }

    private static string FormatCounterRange(ushort? first, ushort? last)
        => first.HasValue && last.HasValue ? $"{first.Value}..{last.Value}" : "-";

    private static string FormatStreamEvent(ProcessBusStreamEvent streamEvent)
    {
        var prefix = $"{streamEvent.Timestamp:HH:mm:ss.ffffff} {streamEvent.Kind}";
        var common = $"APPID={FormatAppId(streamEvent.AppId)} src={streamEvent.Source} dst={streamEvent.Destination} VLAN={FormatVlan(streamEvent.VlanId, streamEvent.VlanPriority)} id={TextOrDash(streamEvent.StreamId)} confRev={streamEvent.ConfigurationRevision ?? 0}";

        return streamEvent.Kind switch
        {
            ProcessBusEventKind.SampledValues => $"{prefix} {common} smpCnt={streamEvent.SampleCount?.ToString() ?? "-"} seq={streamEvent.SequenceStatus} payloadBytes={streamEvent.PayloadBytes} bound={(streamEvent.IsBoundToScl ? "SCL" : "anonymous")} values={streamEvent.DecodedValueCount}{FormatDiagnostics(streamEvent.Diagnostics)}",
            ProcessBusEventKind.Goose => $"{prefix} {common} stNum={streamEvent.StateNumber?.ToString() ?? "-"} sqNum={streamEvent.SequenceNumber?.ToString() ?? "-"} seq={streamEvent.GooseSequenceStatus} TAL={streamEvent.TimeAllowedToLiveMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms bound={(streamEvent.IsBoundToScl ? "SCL" : "anonymous")} values={streamEvent.DecodedValueCount} changed={streamEvent.ChangedValueCount}{FormatChangedSummary(streamEvent.ChangedSummary)}{FormatDiagnostics(streamEvent.Diagnostics)}",
            _ => $"{prefix} {streamEvent.Detail}"
        };
    }

    private static string FormatMonitorSummary(ProcessBusStreamSummary summary)
    {
        var common = $"{summary.Kind} APPID=0x{summary.AppId:X4} id={TextOrDash(summary.StreamId)} packets={summary.PacketCount}";
        return summary.Kind == ProcessBusEventKind.SampledValues
            ? $"{common} smpCnt={FormatCounterRange(summary.FirstSampleCount, summary.LastSampleCount)} values={summary.LastDecodedValueCount} gaps={summary.SequenceGapCount} missed={summary.MissedSampleCount} dup={summary.DuplicateSampleCount} late={summary.OutOfOrderSampleCount} wraps={summary.WrapCount}"
            : $"{common} stNum={summary.LastStateNumber} sqNum={summary.LastSequenceNumber} TAL={summary.LastTimeAllowedToLiveMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms stateChanges={summary.GooseStateChangeCount} retrans={summary.GooseRetransmissionCount} gaps={summary.GooseSequenceGapCount} dup={summary.GooseDuplicateCount} regress={summary.GooseSequenceRegressionCount + summary.GooseStateRegressionCount} timeouts={summary.GooseTimeoutCount} valueChanges={summary.GooseValueChangeCount}{FormatChangedSummary(summary.LastChangedSummary)}{FormatDiagnostics(summary.LastDiagnostics)}";
    }

    private static string FormatChangedSummary(string changedSummary)
        => string.IsNullOrWhiteSpace(changedSummary) ? string.Empty : $" change=\"{changedSummary}\"";

    private static string FormatDiagnostics(IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
            return string.Empty;

        var text = string.Join(" | ", diagnostics.Take(2));
        if (diagnostics.Count > 2)
            text += $" | +{diagnostics.Count - 2} more";

        return $" diag=\"{text}\"";
    }

    private static IEnumerable<T> TakeWithLimit<T>(IEnumerable<T> source, int limit)
        => limit <= 0 ? source : source.Take(limit);

    private static void WriteLimitNotice(int total, int limit, string label)
    {
        if (limit > 0 && total > limit)
            Console.WriteLine($"  ... {total - limit} more {label}; use --raw-limit 0 to show all.");
    }


    private static IReadOnlyList<string> SplitCsv(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

    private static void WriteReportSubscriptionPlan(MmsReportSubscriptionPlan plan, int rawLimit)
    {
        Console.WriteLine(plan.Summary);

        if (plan.ReportControl != null)
        {
            var r = plan.ReportControl;
            var reservation = r.Buffered ? TextOrDash(r.ReservationTimeSeconds) : TextOrDash(r.ReservationState);
            Console.WriteLine($"Selected RCB: {r.Mode} {r.Reference}");
            Console.WriteLine($"  DatSet={TextOrDash(r.DataSetReference)} RptEna={TextOrDash(r.EnabledState)} Resv={reservation} RptID={TextOrDash(r.ReportId)} ConfRev={TextOrDash(r.ConfRev)}");
        }

        if (plan.RcbSelection.Candidates.Count > 0)
        {
            Console.WriteLine("Smart RCB selection:");
            Console.WriteLine($"  {plan.RcbSelection.Summary}");
            foreach (var candidate in plan.RcbSelection.Candidates.Take(Math.Min(rawLimit <= 0 ? plan.RcbSelection.Candidates.Count : rawLimit, 12)))
                Console.WriteLine($"  - {candidate.Decision,-10} {candidate.Mode} {candidate.Reference} score={candidate.Score} availability={candidate.Availability} reason={candidate.Reason}");
            WriteLimitNotice(plan.RcbSelection.Candidates.Count, rawLimit <= 0 ? 0 : Math.Min(rawLimit, 12), "RCB candidate(s)");
        }

        if (plan.Blockers.Count > 0)
        {
            Console.WriteLine("Blockers:");
            foreach (var blocker in plan.Blockers)
                Console.WriteLine($"  - {blocker}");
        }

        if (plan.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in plan.Warnings)
                Console.WriteLine($"  - {warning}");
        }

        if (plan.Members.Count > 0)
        {
            Console.WriteLine("Report value map / DataSet members:");
            foreach (var member in TakeWithLimit(plan.Members, rawLimit))
                Console.WriteLine($"  {member.UserReference} [{TextOrDash(member.FunctionalConstraint)}] mms={member.MmsReference}");
            WriteLimitNotice(plan.Members.Count, rawLimit, "report member(s)");
        }

        if (plan.DynamicPoints.Count > 0)
        {
            Console.WriteLine("Dynamic DataSet source points:");
            foreach (var point in TakeWithLimit(plan.DynamicPoints, rawLimit))
                Console.WriteLine($"  {point.UserReference} [{point.FunctionalConstraint}] mms={point.MmsReference}");
            WriteLimitNotice(plan.DynamicPoints.Count, rawLimit, "dynamic point(s)");
        }

        if (plan.Steps.Count > 0)
        {
            Console.WriteLine("Execution steps:");
            var index = 1;
            foreach (var step in plan.Steps)
                Console.WriteLine($"  {index++}. {step}");
        }
    }

    private static string FormatReportReadiness(MmsReportReadiness item)
    {
        var r = item.ReportControl;
        var reservation = r.Buffered ? TextOrDash(r.ReservationTimeSeconds) : TextOrDash(r.ReservationState);
        return $"  {item.Label,-30} {r.Mode} {r.Reference} datSet={TextOrDash(r.DataSetReference)} rptEna={TextOrDash(r.EnabledState)} resv={reservation} rptID={TextOrDash(r.ReportId)} reason={item.Reason}";
    }

    private static void WriteReportFrame(MmsReportFrame report)
    {
        Console.WriteLine($"  {report.ReceivedAt:yyyy-MM-dd HH:mm:ss.fff} UTC - {report.Message}");
        if (report.Header.HasAny)
            Console.WriteLine($"      header: {report.Header.Summary}");
        Console.WriteLine($"      rawAccessResults={report.RawAccessResultCount} inclusionItem={FormatNullableInt(report.InclusionBitstringItemIndex)} included=[{string.Join(",", report.IncludedDataSetIndexes)}]");
        foreach (var value in TakeWithLimit(report.Values, 32))
        {
            var extras = new List<string>();
            if (!string.IsNullOrWhiteSpace(value.DataReference))
                extras.Add($"dataRef={value.DataReference}");
            if (value.ReasonForInclusion.Count > 0)
                extras.Add($"reason={value.ReasonSummary}");

            var suffix = extras.Count == 0 ? string.Empty : $" ({string.Join("; ", extras)})";
            Console.WriteLine($"      [{value.Index}] {value.MemberReference}: {value.DisplayValue}{suffix}");
        }
        WriteLimitNotice(report.Values.Count, 32, "report value(s)");
    }

    private static void WriteSoakSnapshots(IReadOnlyList<MmsReportSoakSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return;

        Console.WriteLine($"Soak snapshots: {snapshots.Count}");
        foreach (var snapshot in snapshots.Take(8))
            Console.WriteLine($"  - {snapshot.Summary}");
        if (snapshots.Count > 8)
            Console.WriteLine($"  ... +{snapshots.Count - 8} more soak snapshot(s)");
    }


    private static MmsReportSubscriptionPlan BuildStaticPlanForClaimAttempt(
        MmsReportInventory inventory,
        IReadOnlyList<MmsDataSetDirectoryResult> directories,
        string preferredRcb,
        string preferredDataSet,
        bool strictRcb,
        bool allowUrCbFallback,
        bool allowPollingFallback,
        IReadOnlySet<string> excludedRcbReferences)
        => MmsReportSubscriptionPlanner.BuildStaticPlan(
            inventory,
            directories,
            preferredRcb,
            preferredDataSet,
            strictRcb,
            allowUrCbFallback,
            allowPollingFallback,
            excludedRcbReferences);

    private static bool IsRcbClaimFailure(MmsStaticReportSessionResult result)
        => result.Message.Contains("RptEna=true failed", StringComparison.OrdinalIgnoreCase) ||
           result.Message.Contains("RCB.DatSet write failed", StringComparison.OrdinalIgnoreCase);

    private static MmsRcbClaimAttempt ToRcbClaimAttempt(
        int attemptNumber,
        MmsReportSubscriptionPlan plan,
        MmsStaticReportSessionResult result,
        bool claimFailed)
    {
        var write = result.WriteSteps.FirstOrDefault(x =>
            x.Attribute.Equals("RptEna", StringComparison.OrdinalIgnoreCase) ||
            x.Attribute.Equals("DatSet", StringComparison.OrdinalIgnoreCase));

        return new MmsRcbClaimAttempt
        {
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            RcbReference = plan.ReportControl?.Reference ?? string.Empty,
            PlanMode = plan.Mode.ToString(),
            DataSetReference = plan.DataSetReference,
            Decision = claimFailed ? "ClaimFailedTryNext" : result.IsSuccess ? "ClaimSucceeded" : "SessionCompletedWithFailure",
            IsSuccess = result.IsSuccess,
            IsFallback = attemptNumber > 1,
            WriteAttribute = write?.Attribute ?? string.Empty,
            WriteReference = write?.Reference ?? string.Empty,
            Message = write?.Message ?? result.Message
        };
    }

    private static MmsRcbClaimAttempt ToRcbPreClaimContentionAttempt(
        int attemptNumber,
        MmsReportSubscriptionPlan plan,
        MmsRcbContentionProbeResult contentionProbe)
        => new()
        {
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            RcbReference = plan.ReportControl?.Reference ?? contentionProbe.RcbReference,
            PlanMode = plan.Mode.ToString(),
            DataSetReference = plan.DataSetReference,
            Decision = contentionProbe.IsContended ? "PreClaimContentionCooldown" : "PreClaimProbeStable",
            IsSuccess = false,
            IsFallback = attemptNumber > 1,
            WriteAttribute = "pre-claim-probe",
            WriteReference = plan.ReportControl?.Reference ?? contentionProbe.RcbReference,
            Message = contentionProbe.Summary
        };

    private static async Task<MmsRcbContentionProbeResult> ProbeSelectedRcbContentionAsync(
        MmsClientSession session,
        MmsReportControlCandidate rcb,
        int probeCount,
        TimeSpan probeDelay,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rcb);

        var observations = new List<MmsRcbContentionProbeObservation>();
        for (var index = 1; index <= probeCount; index++)
        {
            await session.ProbeReportControlAttributesAsync(rcb, cancellationToken).ConfigureAwait(false);
            observations.Add(new MmsRcbContentionProbeObservation
            {
                ProbeNumber = index,
                CapturedAt = DateTimeOffset.UtcNow,
                RcbReference = rcb.Reference,
                RptEna = rcb.EnabledState,
                Resv = rcb.ReservationState,
                ResvTms = rcb.ReservationTimeSeconds,
                DataSetReference = rcb.DataSetReference,
                ConfRev = rcb.ConfRev,
                Message = rcb.ProbeDiagnostics.LastOrDefault() ?? string.Empty
            });

            if (index < probeCount && probeDelay > TimeSpan.Zero)
                await Task.Delay(probeDelay, cancellationToken).ConfigureAwait(false);
        }

        var rptEnaStates = observations
            .Select(x => NormalizeProbeValue(x.RptEna))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resvStates = observations
            .Select(x => NormalizeProbeValue(string.IsNullOrWhiteSpace(x.ResvTms) ? x.Resv : x.ResvTms))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dataSets = observations
            .Select(x => NormalizeProbeValue(x.DataSetReference))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var confRevs = observations
            .Select(x => NormalizeProbeValue(x.ConfRev))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isBusy = observations.Any(x => IsTrueLike(x.RptEna) || IsTrueLike(x.Resv) || IsPositiveInteger(x.ResvTms));
        var isFlapping = rptEnaStates.Length > 1 || resvStates.Length > 1 || dataSets.Length > 1 || confRevs.Length > 1;
        var isContended = isBusy || isFlapping;
        var reason = isFlapping
            ? "RCB state changed across pre-claim probes. Treat as contended/flapping to avoid fighting another client."
            : isBusy
                ? "RCB became busy/reserved during pre-claim probes. Treat as owned by another client and skip."
                : "RCB remained stable and free across pre-claim probes.";

        return new MmsRcbContentionProbeResult
        {
            RcbReference = rcb.Reference,
            IsContended = isContended,
            IsBusyAtProbe = isBusy,
            IsFlapping = isFlapping,
            CooldownSeconds = isContended ? cooldownSeconds : 0,
            Decision = isContended ? "CooldownSkip" : "StableProceed",
            Reason = reason,
            RecommendedAction = isContended
                ? "Do not write RptEna/DatSet on this RCB in the current command; try the next candidate or polling fallback."
                : "Safe to continue with guarded claim attempt.",
            Observations = observations
        };
    }

    private static string NormalizeProbeValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text == "-" ? string.Empty : text;
    }

    private static bool IsTrueLike(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositiveInteger(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0;
    }

    private static MmsStaticReportSessionResult WithRcbRuntimeEvidence(
        MmsStaticReportSessionResult result,
        IReadOnlyList<MmsRcbClaimAttempt> claimAttempts,
        IReadOnlyList<MmsRcbContentionProbeResult> contentionProbes)
        => new()
        {
            IsSuccess = result.IsSuccess,
            WriteSteps = result.WriteSteps,
            Reports = result.Reports,
            PollReads = result.PollReads,
            SoakSnapshots = result.SoakSnapshots,
            RcbClaimAttempts = claimAttempts,
            RcbContentionProbes = contentionProbes,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Warnings = result.Warnings,
            Diagnostics = result.Diagnostics,
            Verification = result.Verification,
            Message = result.Message
        };

    private static string NormalizeRcbReferenceForCli(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static void WriteReportDiagnostics(MmsReportSessionDiagnostics diagnostics)
    {
        Console.WriteLine("Diagnostics:");
        Console.WriteLine($"  {diagnostics.Summary}");
        if (!string.IsNullOrWhiteSpace(diagnostics.FirstEntryIdHex) || !string.IsNullOrWhiteSpace(diagnostics.LastEntryIdHex))
            Console.WriteLine($"  EntryID: {TextOrDash(diagnostics.FirstEntryIdHex)} -> {TextOrDash(diagnostics.LastEntryIdHex)}");
        if (diagnostics.WarningMessages.Count > 0)
        {
            Console.WriteLine("  Diagnostic warnings:");
            foreach (var warning in diagnostics.WarningMessages.Take(8))
                Console.WriteLine($"    - {warning}");
            if (diagnostics.WarningMessages.Count > 8)
                Console.WriteLine($"    ... +{diagnostics.WarningMessages.Count - 8} more diagnostic warning(s)");
        }
        if (diagnostics.ReasonCounts.Count > 0)
            Console.WriteLine($"  Reasons: {string.Join(", ", diagnostics.ReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value}"))}");
    }

    private static void WriteReportVerification(MmsReportSessionVerification verification)
    {
        Console.WriteLine("Verification:");
        Console.WriteLine($"  {verification.Summary}");
        foreach (var check in verification.Checks.Take(20))
        {
            var status = check.Severity.ToString().ToUpperInvariant();
            Console.WriteLine($"  {status} {check.Stage} {check.Target}: expected={TextOrDash(check.Expected)} observed={TextOrDash(check.Observed)} - {check.Message}");
        }
        if (verification.Checks.Count > 20)
            Console.WriteLine($"  ... +{verification.Checks.Count - 20} more verification check(s)");

        if (verification.RcbSnapshots.Count > 0)
        {
            Console.WriteLine("  RCB snapshots:");
            foreach (var snapshot in verification.RcbSnapshots.Take(8))
                Console.WriteLine($"    - {snapshot.Summary}");
            if (verification.RcbSnapshots.Count > 8)
                Console.WriteLine($"    ... +{verification.RcbSnapshots.Count - 8} more RCB snapshot(s)");
        }

        if (verification.DataSetSnapshots.Count > 0)
        {
            Console.WriteLine("  DataSet snapshots:");
            foreach (var snapshot in verification.DataSetSnapshots.Take(8))
                Console.WriteLine($"    - {snapshot.Summary}");
            if (verification.DataSetSnapshots.Count > 8)
                Console.WriteLine($"    ... +{verification.DataSetSnapshots.Count - 8} more DataSet snapshot(s)");
        }
    }

    private static async Task<IReadOnlyList<string>> WriteReportEvidenceAsync(
        string directoryPath,
        string target,
        string mode,
        string associationState,
        string associationMessage,
        string iedSummary,
        string reportInventorySummary,
        string receiveRoutingSummary,
        MmsReportSubscriptionPlan plan,
        MmsStaticReportSessionResult result)
    {
        var directory = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(directory);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var selectedRcb = plan.ReportControl;
        var context = new
        {
                generatedAt = DateTimeOffset.UtcNow,
            target,
            mode,
            association = new
            {
                state = associationState,
                message = associationMessage
            },
            iedSummary,
            reportInventorySummary,
            receiveRoutingSummary,
                plan = new
                {
                    plan.Summary,
                    Mode = plan.Mode.ToString(),
                    plan.DataSetReference,
                plan.IsReady,
                selectedRcb = selectedRcb == null ? null : new
                {
                    selectedRcb.Reference,
                    selectedRcb.Mode,
                    selectedRcb.DataSetReference,
                    selectedRcb.ReportId,
                    selectedRcb.ConfRev,
                    selectedRcb.EnabledState,
                    selectedRcb.ReservationState,
                    selectedRcb.ReservationTimeSeconds,
                    selectedRcb.OptionalFields,
                    selectedRcb.TriggerOptions
                },
                rcbSelection = plan.RcbSelection
            },
            result.IsSuccess,
            result.Message,
            durationSeconds = result.StartedAt == default || result.CompletedAt == default ? 0 : (result.CompletedAt - result.StartedAt).TotalSeconds,
            soakSnapshots = result.SoakSnapshots.Count,
            rcbClaimAttempts = result.RcbClaimAttempts,
            rcbContentionProbes = result.RcbContentionProbes,
            result.Diagnostics,
            verification = result.Verification,
            warnings = result.Warnings
        };

        var summaryJsonPath = Path.Combine(directory, "summary.json");
        var reportsJsonPath = Path.Combine(directory, "reports.json");
        var reportFramesJsonPath = Path.Combine(directory, "report-frames.json");
        var reportStreamsJsonPath = Path.Combine(directory, "report-streams.json");
        var reportValuesCsvPath = Path.Combine(directory, "report-values.csv");
        var rcbCandidatesJsonPath = Path.Combine(directory, "rcb-candidates.json");
        var rcbSelectionJsonPath = Path.Combine(directory, "rcb-selection.json");
        var rcbClaimAttemptsJsonPath = Path.Combine(directory, "rcb-claim-attempts.json");
        var rcbContentionProbesJsonPath = Path.Combine(directory, "rcb-contention-probes.json");
        var reportTimelineJsonPath = Path.Combine(directory, "report-timeline.json");
        var pollReadsJsonPath = Path.Combine(directory, "poll-reads.json");
        var soakSnapshotsJsonPath = Path.Combine(directory, "soak-snapshots.json");
        var writeStepsJsonPath = Path.Combine(directory, "write-steps.json");
        var verificationJsonPath = Path.Combine(directory, "verification.json");
        var rcbSnapshotsJsonPath = Path.Combine(directory, "rcb-snapshots.json");
        var dataSetSnapshotsJsonPath = Path.Combine(directory, "dataset-snapshots.json");
        var summaryMdPath = Path.Combine(directory, "summary.md");

        await File.WriteAllTextAsync(summaryJsonPath, JsonSerializer.Serialize(context, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportsJsonPath, JsonSerializer.Serialize(result.Reports.Select(ToReportEvidence), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportFramesJsonPath, JsonSerializer.Serialize(result.Reports.Select(ToReportFrameEvidence), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportStreamsJsonPath, JsonSerializer.Serialize(ToReportStreamEvidence(result.Reports), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportValuesCsvPath, BuildReportValuesCsv(result.Reports), System.Text.Encoding.UTF8).ConfigureAwait(false);
        await File.WriteAllTextAsync(rcbCandidatesJsonPath, JsonSerializer.Serialize(plan.RcbSelection.Candidates, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(rcbSelectionJsonPath, JsonSerializer.Serialize(plan.RcbSelection, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(rcbClaimAttemptsJsonPath, JsonSerializer.Serialize(ToRcbClaimAttemptEvidence(plan, result), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(rcbContentionProbesJsonPath, JsonSerializer.Serialize(result.RcbContentionProbes, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportTimelineJsonPath, JsonSerializer.Serialize(result.Reports.Select(ToReportTimelineEvidence), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(pollReadsJsonPath, JsonSerializer.Serialize(result.PollReads, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(soakSnapshotsJsonPath, JsonSerializer.Serialize(result.SoakSnapshots, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(writeStepsJsonPath, JsonSerializer.Serialize(result.WriteSteps, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(verificationJsonPath, JsonSerializer.Serialize(result.Verification, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(rcbSnapshotsJsonPath, JsonSerializer.Serialize(result.Verification.RcbSnapshots, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(dataSetSnapshotsJsonPath, JsonSerializer.Serialize(result.Verification.DataSetSnapshots, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryMdPath, BuildReportEvidenceMarkdown(context.generatedAt, target, mode, plan, result), System.Text.Encoding.UTF8).ConfigureAwait(false);

        return [summaryJsonPath, reportsJsonPath, reportFramesJsonPath, reportStreamsJsonPath, reportValuesCsvPath, rcbCandidatesJsonPath, rcbSelectionJsonPath, rcbClaimAttemptsJsonPath, rcbContentionProbesJsonPath, reportTimelineJsonPath, pollReadsJsonPath, soakSnapshotsJsonPath, writeStepsJsonPath, verificationJsonPath, rcbSnapshotsJsonPath, dataSetSnapshotsJsonPath, summaryMdPath];
    }

    private static object ToRcbClaimAttemptEvidence(MmsReportSubscriptionPlan plan, MmsStaticReportSessionResult result)
        => new
        {
            selectedRcb = plan.ReportControl?.Reference ?? string.Empty,
            plan.Mode,
            plan.DataSetReference,
            selection = plan.RcbSelection,
            attempts = result.RcbClaimAttempts,
            preClaimContentionProbes = result.RcbContentionProbes,
            lowLevelWriteSequence = result.WriteSteps.Select((step, index) => new
            {
                index = index + 1,
                step.Attribute,
                step.Reference,
                step.Attempted,
                step.IsSuccess,
                step.Message
            }),
            verification = result.Verification.Checks.Select(check => new
            {
                check.Stage,
                check.Target,
                check.Expected,
                check.Observed,
                check.Severity,
                check.Message
            })
        };

    private static object ToReportEvidence(MmsReportFrame report)
        => new
        {
            report.ReceivedAt,
            report.Message,
            report.RawAccessResultCount,
            report.InclusionBitstringItemIndex,
            report.IncludedDataSetIndexes,
            report.DecoderMode,
            report.StreamKey,
            report.ParseWarnings,
            header = report.Header,
            values = report.Values.Select(value => new
            {
                value.Index,
                value.MemberReference,
                value.DataReference,
                value.ReasonForInclusion,
                value.FailureCode,
                displayValue = value.DisplayValue
            })
        };

    private static object ToReportTimelineEvidence(MmsReportFrame report)
        => new
        {
            report.ReceivedAt,
            RptID = report.Header.ReportId,
            DataSet = report.Header.DataSetReference,
            report.Header.ConfRev,
            report.Header.SequenceNumber,
            report.Header.TimeOfEntry,
            report.Header.BufferOverflow,
            EntryID = report.Header.EntryIdHex,
            OptionalFields = report.Header.OptionalFields.Names,
            OptionalFieldBits = report.Header.OptionalFields.SetBitIndexes,
            OptionalFieldsRaw = report.Header.OptionalFields.RawHex,
            report.DecoderMode,
            report.StreamKey,
            report.ParseWarnings,
            report.IncludedDataSetIndexes,
            IncludedCount = report.IncludedDataSetIndexes.Count,
            MappedCount = report.Values.Count,
            Reasons = report.Values.SelectMany(x => x.ReasonForInclusion).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            Values = report.Values.Select(value => new
            {
                value.Index,
                value.MemberReference,
                value.DataReference,
                Reason = value.ReasonSummary,
                displayValue = value.DisplayValue
            })
        };

    private static object ToReportFrameEvidence(MmsReportFrame report)
        => new
        {
            report.ReceivedAt,
            report.DecoderMode,
            report.StreamKey,
            report.Message,
            report.ParseWarnings,
            raw = new
            {
                report.RawAccessResultCount,
                report.InclusionBitstringItemIndex,
                report.ResponseHexPreview
            },
            header = new
            {
                RptID = report.Header.ReportId,
                DataSet = report.Header.DataSetReference,
                report.Header.ConfRev,
                report.Header.SequenceNumber,
                report.Header.SubSequenceNumber,
                report.Header.MoreSegmentsFollow,
                report.Header.TimeOfEntry,
                report.Header.BufferOverflow,
                EntryID = report.Header.EntryIdHex,
                OptionalFields = report.Header.OptionalFields.Names,
                OptionalFieldBits = report.Header.OptionalFields.SetBitIndexes,
                OptionalFieldsRaw = report.Header.OptionalFields.RawHex
            },
            included = report.IncludedDataSetIndexes,
            values = report.Values.Select(value => new
            {
                value.Index,
                value.MemberReference,
                value.DataReference,
                value.ReasonForInclusion,
                value.FailureCode,
                displayValue = value.DisplayValue
            })
        };

    private static object ToReportStreamEvidence(IReadOnlyList<MmsReportFrame> reports)
        => reports
            .GroupBy(report => report.StreamKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                StreamKey = group.Key,
                RptID = group.Select(x => x.Header.ReportId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                DataSet = group.Select(x => x.Header.DataSetReference).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                ConfRev = group.Select(x => x.Header.ConfRev).FirstOrDefault(x => x.HasValue),
                ReportCount = group.Count(),
                FirstReceivedAt = group.Min(x => x.ReceivedAt),
                LastReceivedAt = group.Max(x => x.ReceivedAt),
                FirstEntryID = group.Select(x => x.Header.EntryIdHex).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                LastEntryID = group.Select(x => x.Header.EntryIdHex).LastOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                SequenceNumbers = group.Select(x => x.Header.SequenceNumber).Where(x => x.HasValue).Select(x => x!.Value).ToArray(),
                BufferOverflowObserved = group.Any(x => x.Header.BufferOverflow == true),
                DecoderModes = group.Select(x => x.DecoderMode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings = group.SelectMany(x => x.ParseWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Reasons = group.SelectMany(x => x.Values).SelectMany(x => x.ReasonForInclusion).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();

    private static string BuildReportValuesCsv(IReadOnlyList<MmsReportFrame> reports)
    {
        var rows = new List<string>
        {
            "ReceivedAtUtc,RptID,DataSet,ConfRev,SqNum,EntryID,BufOvfl,DecoderMode,IncludedIndexes,Index,Reference,DataReference,Reason,Value,TimeOfEntry"
        };

        foreach (var report in reports)
        {
            var included = string.Join(";", report.IncludedDataSetIndexes);
            foreach (var value in report.Values)
            {
                rows.Add(string.Join(",", new[]
                {
                    Csv(report.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                    Csv(report.Header.ReportId),
                    Csv(report.Header.DataSetReference),
                    Csv(report.Header.ConfRev?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(report.Header.SequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(report.Header.EntryIdHex),
                    Csv(report.Header.BufferOverflow?.ToString().ToLowerInvariant() ?? string.Empty),
                    Csv(report.DecoderMode),
                    Csv(included),
                    Csv(value.Index.ToString(CultureInfo.InvariantCulture)),
                    Csv(value.MemberReference),
                    Csv(value.DataReference),
                    Csv(value.ReasonSummary),
                    Csv(value.DisplayValue),
                    Csv(report.Header.TimeOfEntry)
                }));
            }
        }

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static string BuildReportEvidenceMarkdown(
        DateTimeOffset generatedAt,
        string target,
        string mode,
        MmsReportSubscriptionPlan plan,
        MmsStaticReportSessionResult result)
    {
        var diagnostics = result.Diagnostics;
        var lines = new List<string>
        {
            "# IEC 61850 Report Evidence",
            string.Empty,
            $"- Generated: {generatedAt:yyyy-MM-dd HH:mm:ss.fff} UTC",
            $"- Target: {target}",
            $"- Mode: {mode}",
            $"- Plan: {plan.Summary}",
            $"- Result: {(result.IsSuccess ? "PASS" : "FAIL")} - {result.Message}",
            $"- Verification: {result.Verification.OverallStatus} - {result.Verification.Summary}",
            $"- Diagnostics: {diagnostics.Summary}",
            $"- Diagnostic status: {diagnostics.OverallStatus}",
            $"- EntryID: {TextOrDash(diagnostics.FirstEntryIdHex)} -> {TextOrDash(diagnostics.LastEntryIdHex)}",
            $"- Duration: {(result.StartedAt == default || result.CompletedAt == default ? 0 : (result.CompletedAt - result.StartedAt).TotalSeconds):0.###} s",
            $"- Soak snapshots: {result.SoakSnapshots.Count}",
            string.Empty,
            "## Smart RCB Selection",
            string.Empty,
            $"- {plan.RcbSelection.Summary}",
            string.Empty,
            "| Decision | RCB | Score | Availability | Reason |",
            "| --- | --- | ---: | --- | --- |",
        };

        foreach (var candidate in plan.RcbSelection.Candidates.Take(20))
            lines.Add($"| {candidate.Decision} | {candidate.Mode} {candidate.Reference.Replace("|", "\\|")} | {candidate.Score} | {candidate.Availability} | {candidate.Reason.Replace("|", "\\|")} |");
        if (plan.RcbSelection.Candidates.Count > 20)
            lines.Add($"| ... | +{plan.RcbSelection.Candidates.Count - 20} more RCB candidates |  |  | See rcb-candidates.json | ");

        if (result.RcbClaimAttempts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("### RCB Claim Attempts");
            lines.Add(string.Empty);
            lines.Add("| Attempt | RCB | Decision | Success | Write | Message |");
            lines.Add("| ---: | --- | --- | --- | --- | --- |");
            foreach (var attempt in result.RcbClaimAttempts)
            {
                lines.Add($"| {attempt.AttemptNumber} | {attempt.RcbReference.Replace("|", "\\|")} | {attempt.Decision} | {attempt.IsSuccess.ToString().ToLowerInvariant()} | {attempt.WriteAttribute} | {attempt.Message.Replace("|", "\\|")} |");
            }
        }

        if (result.RcbContentionProbes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("### RCB Pre-Claim Contention Probes");
            lines.Add(string.Empty);
            lines.Add("| RCB | Decision | Contended | Busy | Flapping | Cooldown | Reason |");
            lines.Add("| --- | --- | --- | --- | --- | ---: | --- |");
            foreach (var probe in result.RcbContentionProbes)
            {
                lines.Add($"| {probe.RcbReference.Replace("|", "\\|")} | {probe.Decision} | {probe.IsContended.ToString().ToLowerInvariant()} | {probe.IsBusyAtProbe.ToString().ToLowerInvariant()} | {probe.IsFlapping.ToString().ToLowerInvariant()} | {probe.CooldownSeconds} | {probe.Reason.Replace("|", "\\|")} |");
            }

            lines.Add(string.Empty);
            lines.Add("Probe observations are written to `rcb-contention-probes.json`.");
        }

        lines.Add(string.Empty);
        lines.AddRange([
            "## Counts",
            string.Empty,
            $"| Metric | Value |",
            $"| --- | ---: |",
            $"| Reports | {diagnostics.ReportCount} |",
            $"| Report values | {diagnostics.ValueCount} |",
            $"| Mapping failures | {diagnostics.MappingFailureCount} |",
            $"| Partial mappings | {diagnostics.PartialMappingFailureCount} |",
            $"| Poll reads OK | {diagnostics.PollReadSuccessCount}/{diagnostics.PollReadCount} |",
            $"| Write failures | {diagnostics.WriteFailureCount} |",
            $"| Duplicate report keys | {diagnostics.DuplicateReportKeyCount} |",
            $"| Sequence gaps | {diagnostics.SequenceGapCount} |",
            $"| Sequence resets | {diagnostics.SequenceResetCount} |",
            $"| Sequence regressions | {diagnostics.SequenceRegressionCount} |",
            $"| EntryID gaps | {diagnostics.EntryIdGapCount} |",
            $"| EntryID regressions | {diagnostics.EntryIdRegressionCount} |",
            $"| Buffer overflow observed | {diagnostics.BufferOverflowObserved.ToString().ToLowerInvariant()} |",
            string.Empty
        ]);

        if (diagnostics.WarningMessages.Count > 0)
        {
            lines.Add("## Diagnostic Warnings");
            lines.Add(string.Empty);
            foreach (var warning in diagnostics.WarningMessages)
                lines.Add($"- {warning}");
            lines.Add(string.Empty);
        }

        var verification = result.Verification;
        lines.Add("## Verification");
        lines.Add(string.Empty);
        lines.Add($"- Status: {verification.OverallStatus}");
        lines.Add($"- Summary: {verification.Summary}");
        lines.Add(string.Empty);
        lines.Add("| Severity | Stage | Target | Expected | Observed | Message |");
        lines.Add("| --- | --- | --- | --- | --- | --- |");
        foreach (var check in verification.Checks)
            lines.Add($"| {check.Severity} | {check.Stage} | {check.Target} | {check.Expected.Replace("|", "\\|")} | {check.Observed.Replace("|", "\\|")} | {check.Message.Replace("|", "\\|")} |");
        lines.Add(string.Empty);

        if (verification.RcbSnapshots.Count > 0)
        {
            lines.Add("### RCB Snapshots");
            lines.Add(string.Empty);
            foreach (var snapshot in verification.RcbSnapshots)
                lines.Add($"- {snapshot.Summary.Replace("|", "\\|")}");
            lines.Add(string.Empty);
        }

        if (verification.DataSetSnapshots.Count > 0)
        {
            lines.Add("### DataSet Snapshots");
            lines.Add(string.Empty);
            foreach (var snapshot in verification.DataSetSnapshots)
                lines.Add($"- {snapshot.Summary.Replace("|", "\\|")}");
            lines.Add(string.Empty);
        }

        if (result.SoakSnapshots.Count > 0)
        {
            lines.Add("## Soak Snapshots");
            lines.Add(string.Empty);
            lines.Add("| Captured UTC | Elapsed s | Reports | Values | Poll OK | Pending | Queued reports | Routing | ");
            lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ");
            foreach (var snapshot in result.SoakSnapshots)
                lines.Add($"| {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss.fff} | {snapshot.ElapsedSeconds:0.###} | {snapshot.ReportCount} | {snapshot.ValueCount} | {snapshot.PollReadSuccessCount}/{snapshot.PollReadCount} | {snapshot.PendingConfirmedOperationCount} | {snapshot.QueuedInformationReportCount} | {TextOrDash(snapshot.LastReceiveRoutingSummary).Replace("|", "\\|")} |");
            lines.Add(string.Empty);
        }

        if (result.Reports.Count > 0)
        {
            lines.Add("## Report Timeline");
            lines.Add(string.Empty);
            lines.Add("| Received UTC | RptID | Decoder | SqNum | EntryID | BufOvfl | Included | Mapped | Reasons | TimeOfEntry | DataSet |");
            lines.Add("| --- | --- | --- | ---: | --- | --- | --- | ---: | --- | --- | --- |");
            foreach (var report in result.Reports)
            {
                var reasons = string.Join(",", report.Values.SelectMany(x => x.ReasonForInclusion).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                lines.Add($"| {report.ReceivedAt:yyyy-MM-dd HH:mm:ss.fff} | {TextOrDash(report.Header.ReportId).Replace("|", "\\|")} | {TextOrDash(report.DecoderMode)} | {report.Header.SequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"} | {TextOrDash(report.Header.EntryIdHex)} | {report.Header.BufferOverflow?.ToString().ToLowerInvariant() ?? "-"} | [{string.Join(",", report.IncludedDataSetIndexes)}] | {report.Values.Count} | {TextOrDash(reasons)} | {TextOrDash(report.Header.TimeOfEntry).Replace("|", "\\|")} | {TextOrDash(report.Header.DataSetReference).Replace("|", "\\|")} |");
            }
            lines.Add(string.Empty);
        }

        var parseWarnings = result.Reports.SelectMany(x => x.ParseWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (parseWarnings.Length > 0)
        {
            lines.Add("## Report Parse Warnings");
            lines.Add(string.Empty);
            foreach (var warning in parseWarnings)
                lines.Add($"- {warning}");
            lines.Add(string.Empty);
        }

        if (diagnostics.ReasonCounts.Count > 0)
        {
            lines.Add("## Reasons");
            lines.Add(string.Empty);
            lines.Add("| Reason | Count |");
            lines.Add("| --- | ---: |");
            foreach (var reason in diagnostics.ReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add($"| {reason.Key} | {reason.Value} |");
            lines.Add(string.Empty);
        }

        lines.Add("## Write Steps");
        lines.Add(string.Empty);
        lines.Add("| Status | Attribute | Reference | Message |");
        lines.Add("| --- | --- | --- | --- |");
        foreach (var step in result.WriteSteps)
            lines.Add($"| {(step.IsSuccess ? "OK" : "FAIL")} | {step.Attribute} | {step.Reference} | {step.Message.Replace("|", "\\|")} |");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"ERROR: unknown command '{command}'.");
        WriteUsage();
        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("AR.Iec61850.Cli");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  inspect-scl <file.scd|file.cid|file.icd|file.iid>");
        Console.WriteLine("  scl-diff <golden.scl|golden.iid> <candidate.scl|candidate.iid> [--output .artifacts/out/scl-diff]");
        Console.WriteLine("  scl-engineering-profile <scl-file> [--output .artifacts/out/scl-profile.md] [--json .artifacts/out/scl-profile.json] [--raw-limit 20]");
        Console.WriteLine("  process-bus-binding-profile <scl-file> <pcap-file> [--output .artifacts/out/process-bus-binding.md] [--json .artifacts/out/process-bus-binding.json] [--nominal-hz 50] [--raw-limit 30]");
        Console.WriteLine("  goose-diagnostics-profile <scl-file> <pcap-file> [--output .artifacts/out/goose-diagnostics.md] [--json .artifacts/out/goose-diagnostics.json] [--nominal-hz 50] [--raw-limit 30]");
        Console.WriteLine("  sv-diagnostics-profile <scl-file> <pcap-file> [--output .artifacts/out/sv-diagnostics.md] [--json .artifacts/out/sv-diagnostics.json] [--nominal-hz 50] [--raw-limit 30]");
        Console.WriteLine("  generate-pcap <scl-file> <output.pcap> [--source-mac XX:XX:XX:XX:XX:XX] [--sv-frames N] [--goose-frames N] [--sv-scenario normal|diagnostic] [--goose-scenario normal|diagnostic]");
        Console.WriteLine("  inspect-pcap <file.pcap> [--scl file.scd|file.cid|file.icd|file.iid] [--nominal-hz 50]");
        Console.WriteLine("  stream-pcap <file.pcap> [--scl file.scd|file.cid|file.icd|file.iid] [--nominal-hz 50] [--delay-ms N] [--limit N]");
        Console.WriteLine("  list-adapters");
        Console.WriteLine("  goose-subscribe-live --adapter <index|name> [--scl file.scd|file.cid|file.icd|file.iid] [--duration-sec 60] [--frames N] [--filter \"ether proto 0x88b8\"] [--continuous]");
        Console.WriteLine("  mms-discover <host-or-ip> [--port 102] [--timeout-ms 30000] [--no-report-probe] [--max-report-probes N] [--raw-limit N] [--show-raw]");
        Console.WriteLine("  mms-engine-profile <host-or-ip> [--port 102] [--timeout-ms 30000] [--max-report-probes N] [--read-datasets true] [--output profile.md] [--json profile.json]");
        Console.WriteLine("  mms-report-readiness-profile <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--dataset LD/LLN0.DataSet] [--strict-rcb] [--allow-urcb-fallback true|false] [--duration-sec 60] [--gi true|false] [--output report-readiness.md] [--json report-readiness.json] [--session-json session-profile.json]");
        Console.WriteLine("  mms-server-readonly-profile [--port 102] [--name NAME] [--steps N] [--read LD/LN.DO.da] [--dataset LD/LLN0.DataSet] [--output mms-server.md] [--json mms-server.json]");
        Console.WriteLine("  mms-listener-skeleton-profile [--host 127.0.0.1] [--port 0] [--timeout-ms 5000] [--steps N] [--output mms-listener.md] [--json mms-listener.json]");
        Console.WriteLine("  mms-handshake-codec-profile [--output .artifacts/out/mms-handshake-codec.md] [--json .artifacts/out/mms-handshake-codec.json]");
        Console.WriteLine("  mms-handshake-listener-profile [--port 0] [--timeout-ms 5000] [--association-profile BalancedApTitle|LegacyMinimal] [--output .artifacts/out/mms-handshake-listener.md] [--json .artifacts/out/mms-handshake-listener.json]");
        Console.WriteLine("  mms-association-response-profile [--port 0] [--timeout-ms 5000] [--association-profile BalancedApTitle|LegacyMinimal] [--response-profile DeterministicInitiateResponse|CompactInitiateResponse] [--output .artifacts/out/mms-association-response.md] [--json .artifacts/out/mms-association-response.json]");
        Console.WriteLine("  mms-directory <host-or-ip> [--port 102] [--timeout-ms 30000] [--ln-limit N] [--raw-limit N] [--show-points]");
        Console.WriteLine("  mms-model-discover <host-or-ip> [--port 102] [--timeout-ms 120000] [--max-report-probes 286] [--read-datasets true|false] [--read-types true|false] [--max-type-reads 256] [--type-read-source datasets|model|both] [--ied-name NAME] [--ap-name AP1] [--output .artifacts/out/ied-model-discovery]");
        Console.WriteLine("  mms-scl-export <host-or-ip> [--port 102] [--ied-name NAME] [--ap-name AP1] [--scl-export-profile safe-connection|standard-discovery|full-model|simulator-seed] [--write-connection-companion true] [--connection-output .artifacts/out/scl/live-ied.safe-connection.iid] [--ld-name-mode auto|keep] [--output .artifacts/out/scl/live-ied.generated.iid] [--read-datasets true] [--read-types true] [--max-type-reads 512] [--include-osi true]");
        Console.WriteLine("  mms-service-discover <host-or-ip> [--port 102] [--timeout-ms 120000] [--max-report-probes 286] [--read-datasets true] [--read-files true] [--file-directory /] [--read-setting-groups true] [--read-setting-values false] [--max-setting-reads 256] [--read-types false] [--type-read-source datasets|model|both] [--type-read-strategy safe|dataset-leaf|all] [--type-read-isolated true] [--type-read-quarantine true] [--golden-scl samples/scl/minimal-station.scd] [--learn-types-from-golden true] [--golden-profile-name IED1] [--golden-learning-conflict-policy review-only|prefer-live|prefer-golden] [--max-type-reads 32] [--type-read-delay-ms 50] [--ied-name NAME] [--ap-name AP1] [--output .artifacts/out/service-discovery]");
        Console.WriteLine("  mms-find <host-or-ip> <query> [--port 102] [--timeout-ms 30000] [--fc ST|MX|CO|RP|BR] [--ld LD] [--ln LN] [--raw-limit N]");
        Console.WriteLine("  mms-resolve <host-or-ip> <LD/LN.DO.da> [--port 102] [--timeout-ms 30000] [--raw-limit N]");
        Console.WriteLine("  mms-read-smart <host-or-ip> <LD/LN.DO.da> [--port 102] [--timeout-ms 30000]");
        Console.WriteLine("  mms-report-plan <host-or-ip> [--port 102] [--timeout-ms 60000] [--max-report-probes N] [--only-safe] [--kind ReadyStaticDataSet]");
        Console.WriteLine("  mms-report-static-plan <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--strict-rcb] [--allow-urcb-fallback true|false] [--dataset LD/LLN0.DataSet] [--read-values]");
        Console.WriteLine("  mms-report-dynamic-plan <host-or-ip> --points <LD/LN.DO.da,LD/LN.DO.da> [--ld LD] [--rcb LD/LN.RP.name] [--strict-rcb] [--allow-urcb-fallback true|false] [--dataset-name AR_DYN_DS01]");
        Console.WriteLine("  mms-rcb-probe <host-or-ip> <LD/LN.BR.name|LD/LN.RP.name> [--port 102] [--timeout-ms 120000]");
        Console.WriteLine("  mms-report-static-live <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--strict-rcb] [--allow-urcb-fallback true|false] [--max-rcb-claim-attempts 6] [--rcb-probe-count 1] [--rcb-probe-delay-ms 1000] [--contention-cooldown-sec 60] [--duration-sec 15] [--reserve-sec 30] [--gi true|false] [--evidence .artifacts/out/session] [--yes]");
        Console.WriteLine("  mms-report-monitor <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--strict-rcb] [--allow-urcb-fallback true|false] [--max-rcb-claim-attempts 6] [--rcb-probe-count 1] [--rcb-probe-delay-ms 1000] [--contention-cooldown-sec 60] [--duration-sec 60] [--gi true|false] [--gi-interval-sec 0] [--poll-points LD/LN.DO.da,...] [--poll-interval-ms 1000] [--soak-snapshot-sec 60] [--evidence .artifacts/out/session] [--yes]");
        Console.WriteLine("  mms-report-dynamic-live <host-or-ip> --points <LD/LN.DO.da,LD/LN.DO.da> [--ld LD] [--rcb LD/LN.RP.name] [--strict-rcb] [--allow-urcb-fallback true|false] [--dataset-name AR_DYN_DS01] [--duration-sec 15] [--delete-dataset true|false] [--evidence .artifacts/out/session] [--yes]");
        Console.WriteLine("  mms-dataset-directory <host-or-ip> [LD/LLN0.DataSet] [--port 102] [--timeout-ms 60000] [--raw-limit N] [--read-values]");
        Console.WriteLine("  publish-sv-live <scl-file> --adapter <index|name> [--stream-index N] [--source-mac XX:XX:XX:XX:XX:XX] [--frames N] [--duration-sec N] [--continuous] [--status-ms N] [--rate-hz N] [--nominal-hz N] [--smpcnt-wrap auto|none|N] [--dry-run] [--yes]");
        Console.WriteLine("  publish-goose-live <scl-file> --adapter <index|name> [--stream-index N] [--source-mac XX:XX:XX:XX:XX:XX] [--frames N] [--duration-sec N] [--continuous] [--status-ms N] [--min-ms N] [--max-ms N] [--toggle-every-sec N] [--initial-state true|false] [--test] [--nds-com] [--dry-run] [--yes]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl samples/scl/minimal-station.scd");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- scl-diff samples/scl/minimal-station.scd .artifacts/out/scl/demo-ied.standard-discovery.iid --output .artifacts/out/scl-diff/demo-ied");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- scl-engineering-profile samples/scl/minimal-station.scd --output .artifacts/out/scl-profile.md --json .artifacts/out/scl-profile.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- generate-pcap samples/scl/minimal-station.scd .artifacts/out/processbus-demo.pcap");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-pcap .artifacts/out/processbus-demo.pcap --scl samples/scl/minimal-station.scd");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- process-bus-binding-profile samples/scl/minimal-station.scd .artifacts/out/processbus-demo.pcap --output .artifacts/out/process-bus-binding.md --json .artifacts/out/process-bus-binding.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- generate-pcap samples/scl/minimal-station.scd .artifacts/out/goose-diagnostic-demo.pcap --sv-frames 0 --goose-scenario diagnostic");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- goose-diagnostics-profile samples/scl/minimal-station.scd .artifacts/out/goose-diagnostic-demo.pcap --output .artifacts/out/goose-diagnostics.md --json .artifacts/out/goose-diagnostics.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- generate-pcap samples/scl/minimal-station.scd .artifacts/out/sv-diagnostic-demo.pcap --goose-frames 0 --sv-scenario diagnostic");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- sv-diagnostics-profile samples/scl/minimal-station.scd .artifacts/out/sv-diagnostic-demo.pcap --output .artifacts/out/sv-diagnostics.md --json .artifacts/out/sv-diagnostics.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- stream-pcap .artifacts/out/processbus-demo.pcap --scl samples/scl/minimal-station.scd --delay-ms 50 --limit 12");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- list-adapters");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- goose-subscribe-live --adapter 1 --scl samples/scl/minimal-station.scd --duration-sec 30");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-discover 192.0.2.10 --port 102 --max-report-probes 16");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-engine-profile 192.0.2.10 --output .artifacts/out/engineering-profile.md --json .artifacts/out/engineering-profile.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-readiness-profile 192.0.2.10 --output .artifacts/out/report-readiness.md --json .artifacts/out/report-readiness.json --session-json .artifacts/out/report-session-profile.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-server-readonly-profile --steps 5 --output .artifacts/out/mms-server-readonly.md --json .artifacts/out/mms-server-readonly.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-listener-skeleton-profile --port 0 --output .artifacts/out/mms-listener-skeleton.md --json .artifacts/out/mms-listener-skeleton.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-handshake-codec-profile --output .artifacts/out/mms-handshake-codec.md --json .artifacts/out/mms-handshake-codec.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-handshake-listener-profile --port 0 --output .artifacts/out/mms-handshake-listener.md --json .artifacts/out/mms-handshake-listener.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-association-response-profile --port 0 --output .artifacts/out/mms-association-response.md --json .artifacts/out/mms-association-response.json");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-directory 192.0.2.10 --show-points --raw-limit 40");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-model-discover 192.0.2.10 --max-report-probes 286 --read-types true --max-type-reads 256 --output .artifacts/out/ied-model-discovery");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-scl-export 192.0.2.10 --ied-name IED1 --scl-export-profile safe-connection --ld-name-mode auto --output .artifacts/out/scl/demo-ied.generated.iid");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-service-discover 192.0.2.10 --ied-name IED1 --read-files true --read-setting-groups true --read-setting-values false --read-types false --learn-types-from-golden true --output .artifacts/out/service-discovery/demo-ied");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-find 192.0.2.10 XCBR --fc ST --raw-limit 40");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-resolve 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-read-smart 192.0.2.10 IED1LD0/MMXU1.PhV.phsA.cVal.mag.f");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-plan 192.0.2.10 --max-report-probes 64 --only-safe");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-static-plan 192.0.2.10 --read-values");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-dynamic-plan 192.0.2.10 --points IED1LD0/MMXU1.PhV.phsA.cVal.mag.f,IED1LD0/BI6GGIO1.Ind1.stVal");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-static-live 192.0.2.10 --duration-sec 15 --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-monitor 192.0.2.10 --rcb IED1LD0/LLN0.BR.brcbA01 --rcb-probe-count 3 --duration-sec 60 --poll-points IED1LD0/MMXU1.PhV.phsA.cVal.mag.f --evidence .artifacts/out/report-session01 --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-dynamic-live 192.0.2.10 --points IED1LD0/MMXU1.PhV.phsA.cVal.mag.f,IED1LD0/BI6GGIO1.Ind1.stVal --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-dataset-directory 192.0.2.10 IED1LD0/LLN0.DataSet --raw-limit 80");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- publish-sv-live \"samples/scl/01_SV_Stream_4I+4V_(9-2LE).scd\" --adapter 1 --stream-index 1 --frames 4000 --dry-run");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- publish-sv-live \"samples/scl/01_SV_Stream_4I+4V_(9-2LE).scd\" --adapter 1 --stream-index 1 --continuous --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- publish-goose-live samples/scl/minimal-station.scd --adapter 1 --stream-index 1 --duration-sec 10 --toggle-every-sec 2 --yes");
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static string FormatFcCounts(IReadOnlyDictionary<string, int> counts)
        => counts.Count == 0
            ? "-"
            : string.Join(", ", counts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}:{x.Value}"));

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string FormatNullableInt(int? value)
        => value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";

    private static string FormatAppId(ushort? appId)
        => appId.HasValue ? $"0x{appId.Value:X4}" : "-";

    private static string FormatVlan(SclStreamAddress address)
        => address.VlanId.HasValue ? $"{address.VlanId.Value}/prio {address.VlanPriority ?? 0}" : "-";

    private static string FormatVlan(VlanTag? vlan)
        => vlan.HasValue ? $"{vlan.Value.VlanId}/prio {vlan.Value.PriorityCodePoint}" : "-";

    private static string FormatVlan(ushort? vlanId, byte? priority)
        => vlanId.HasValue ? $"{vlanId.Value}/prio {priority ?? 0}" : "-";
}

internal sealed class CliOptions
{
    private readonly Dictionary<string, string> _values;

    private CliOptions(Dictionary<string, string> values)
        => _values = values;

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // Be tolerant of an extra command separator. This can happen when commands are
            // copied from dotnet-run examples or when a shell wrapper forwards the literal
            // separator into the application argument list. Treat it as a separator/no-op
            // instead of failing with an unhelpful "Option name cannot be empty" message.
            if (string.Equals(arg, "--", StringComparison.Ordinal))
                continue;

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must start with --.");

            var keyValue = arg[2..];
            if (string.IsNullOrWhiteSpace(keyValue))
                throw new ArgumentException("Option name cannot be empty. Use --name value, --name=value, or omit the extra -- separator.");

            string key;
            string? inlineValue = null;
            var equalsIndex = keyValue.IndexOf('=');
            if (equalsIndex >= 0)
            {
                key = keyValue[..equalsIndex].Trim();
                inlineValue = keyValue[(equalsIndex + 1)..];
            }
            else
            {
                key = keyValue.Trim();
            }

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Option name cannot be empty. Use --name value, --name=value, or omit the extra -- separator.");

            if (inlineValue is not null)
            {
                values[key] = inlineValue;
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = bool.TrueString;
                continue;
            }

            values[key] = args[++i];
        }

        return new CliOptions(values);
    }

    public string Get(string key, string fallback)
        => _values.TryGetValue(key, out var value) ? value : fallback;

    public bool TryGet(string key, out string value)
        => _values.TryGetValue(key, out value!);

    public string GetRequired(string key)
    {
        if (_values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new ArgumentException($"Option --{key} is required.");
    }

    public int GetInt(string key, int fallback)
    {
        if (!_values.TryGetValue(key, out var value))
            return fallback;

        if (!int.TryParse(value, out var parsed) || parsed < 0)
            throw new ArgumentException($"Option --{key} must be a non-negative integer.");

        return parsed;
    }

    public double GetDouble(string key, double fallback)
    {
        if (!_values.TryGetValue(key, out var value))
            return fallback;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"Option --{key} must be a number.");

        return parsed;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (!_values.TryGetValue(key, out var value))
            return fallback;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        if (value is "1" ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value is "0" ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new ArgumentException($"Option --{key} must be true or false.");
    }
}
