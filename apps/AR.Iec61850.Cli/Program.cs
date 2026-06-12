using AR.Iec61850.Capture;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
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
                "generate-pcap" => await GeneratePcapAsync(args[1..]).ConfigureAwait(false),
                "inspect-pcap" => InspectPcap(args[1..]),
                "stream-pcap" => await StreamPcapAsync(args[1..]).ConfigureAwait(false),
                "list-adapters" => ListAdapters(),
                "mms-discover" => await MmsDiscoverAsync(args[1..]).ConfigureAwait(false),
                "mms-directory" => await MmsDirectoryAsync(args[1..]).ConfigureAwait(false),
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

    private static async Task<int> GeneratePcapAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("generate-pcap requires <scl-file> <output.pcap>.");

        var options = CliOptions.Parse(args[2..]);
        var sourceMac = MacAddress.Parse(options.Get("source-mac", "02:00:00:00:99:01"));
        var svFrames = options.GetInt("sv-frames", 16);
        var gooseFrames = options.GetInt("goose-frames", 4);
        var startTime = DateTimeOffset.UtcNow;

        var document = new SclParser().Load(args[0]);
        var packets = new List<PcapPacket>();

        AppendSampledValuesPackets(document, sourceMac, svFrames, startTime, packets);
        await AppendGoosePacketsAsync(document, sourceMac, gooseFrames, startTime.AddMilliseconds(1), packets).ConfigureAwait(false);

        packets.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        PcapWriter.WriteAll(args[1], packets);

        Console.WriteLine($"Wrote {packets.Count} Ethernet frames to {Path.GetFullPath(args[1])}");
        Console.WriteLine($"  SV frames: {packets.Count(p => IsEtherType(p.Frame, EthernetConstants.SampledValuesEtherType))}");
        Console.WriteLine($"  GOOSE frames: {packets.Count(p => IsEtherType(p.Frame, EthernetConstants.GooseEtherType))}");
        Console.WriteLine("Open the PCAP in Wireshark or feed it to a playback/analyzer tool.");
        return 0;
    }

    private static int InspectPcap(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("inspect-pcap requires exactly one PCAP file path.");

        var packets = PcapReader.ReadAll(args[0]);
        var monitor = new ProcessBusStreamMonitor();
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
                $"  APPID=0x{summary.AppId:X4} src={summary.Source} dst={summary.Destination} VLAN={FormatVlan(summary.VlanId, summary.VlanPriority)} svID={TextOrDash(summary.StreamId)} confRev={summary.ConfigurationRevision ?? 0} packets={summary.PacketCount} smpCnt={FormatCounterRange(summary.FirstSampleCount, summary.LastSampleCount)}");
        }

        Console.WriteLine($"GOOSE streams: {gooseSummaries.Length} frames={gooseSummaries.Sum(s => s.PacketCount)}");
        foreach (var summary in gooseSummaries.OrderBy(s => s.AppId))
        {
            Console.WriteLine(
                $"  APPID=0x{summary.AppId:X4} src={summary.Source} dst={summary.Destination} VLAN={FormatVlan(summary.VlanId, summary.VlanPriority)} goCB={TextOrDash(summary.StreamId)} confRev={summary.ConfigurationRevision ?? 0} packets={summary.PacketCount} stNum={summary.LastStateNumber} sqNum={summary.LastSequenceNumber}");
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
        var monitor = new ProcessBusStreamMonitor();
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
        Console.WriteLine("Use the adapter index with publish-sv-live or publish-goose-live --adapter <index>.");
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
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet);

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
        var plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet);

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

            plan = MmsReportSubscriptionPlanner.BuildStaticPlan(discovery.ReportInventory, directories, preferredRcb, preferredDataSet);
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

        Console.WriteLine();
        Console.WriteLine(monitorMode
            ? $"Starting guarded report monitor for {durationSec}s..."
            : $"Starting guarded static report session for {durationSec}s...");
        if (pollPoints.Count > 0)
            Console.WriteLine($"Poll reads: {pollPoints.Count} point(s), interval={pollIntervalMs}ms.");

        var live = await session.RunGuardedStaticReportSessionAsync(
            plan,
            TimeSpan.FromSeconds(durationSec),
            reserveSec,
            triggerGi,
            timeout.Token,
            pollDirectory: pollPoints.Count > 0 ? discovery.IedDirectory : null,
            pollReferences: pollPoints,
            pollInterval: TimeSpan.FromMilliseconds(pollIntervalMs)).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(live.Message);
        Console.WriteLine($"Receive routing: {TextOrDash(session.LastReceiveRoutingSummary)} pending={session.PendingConfirmedOperationCount} queuedReports={session.QueuedInformationReportCount}");
        WriteReportDiagnostics(live.Diagnostics);

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

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(discovery.ReportInventory, discovery.IedDirectory, requestedPoints, preferredLd, preferredRcb, dataSetName);

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

        var plan = MmsReportSubscriptionPlanner.BuildDynamicPlan(discovery.ReportInventory, discovery.IedDirectory, requestedPoints, preferredLd, preferredRcb, dataSetName);

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
            timeout.Token).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(live.Message);
        Console.WriteLine($"Receive routing: {TextOrDash(session.LastReceiveRoutingSummary)} pending={session.PendingConfirmedOperationCount} queuedReports={session.QueuedInformationReportCount}");
        WriteReportDiagnostics(live.Diagnostics);

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

        Console.WriteLine($"SCL: {Path.GetFullPath(args[0])}");
        Console.WriteLine($"Mode: {(dryRun ? "dry-run (no NIC transmit)" : "live raw Ethernet transmit")}");
        Console.WriteLine($"Adapter: [{adapter.Index}] MAC={adapter.MacAddress?.ToString() ?? "-"} {TextOrDash(adapter.Description)}");
        Console.WriteLine($"SV stream: #{streamIndex}/{profiles.Count} {profile.Stream.ControlBlockReference}");
        Console.WriteLine($"  svID={TextOrDash(profile.Stream.SvId)} APPID=0x{profile.AppId:X4} dst={profile.Destination} VLAN={FormatVlan(profile.Vlan)}");
        Console.WriteLine($"  source={sourceMac} {FormatFrameLimit(frameLimit)} rate={sampleRateHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz nominal={nominalHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz datasetEntries={profile.Entries.Count}");
        if (!frameLimit.HasValue && durationSeconds <= 0)
            Console.WriteLine("  Press Ctrl+C to stop the continuous publisher.");

        IProcessBusTransport transport = dryRun
            ? new InMemoryProcessBusTransport()
            : new NpcapProcessBusTransport(adapterSelector);

        var session = new SampledValuesPublisherSession(profile, sourceMac, transport);
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
                var payload = BuildDemoSamplePayload(profile.Entries, sent, sampleRateHz, nominalHz);
                lastSampleCount = session.NextSampleCount;
                lastPayloadBytes = payload.Length;
                await session.PublishNextAsync(
                    payload,
                    new Iec61850UtcTime(timestamp, Quality: 0)).ConfigureAwait(false);
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
                    eventTimestamp = DateTimeOffset.UtcNow;
                    schedule.Reset();
                    stateChanged = true;
                    nextToggleTicks = nowTicks + (long)Math.Round(toggleEverySeconds * Stopwatch.Frequency);
                }

                var values = BuildGooseStateValues(profile.Entries, eventTimestamp, state, sent);
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
            var session = new SampledValuesPublisherSession(profile, sourceMac, transport);
            var intervalMicros = ResolveSvIntervalMicros(profile.Stream.SampleRate);

            for (var i = 0; i < frameCount; i++)
            {
                var timestamp = startTime.AddTicks(i * intervalMicros * 10L);
                var payload = BuildSamplePayload(profile.Entries, i);
                var frame = session.PublishNextAsync(
                    payload,
                    new Iec61850UtcTime(timestamp, Quality: 0)).AsTask().GetAwaiter().GetResult();
                packets.Add(new PcapPacket(timestamp, frame));
            }
        }
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

            for (var i = 0; i < frameCount; i++)
            {
                var timestamp = startTime.AddMilliseconds(i * 250);
                var values = BuildGooseValues(profile.Entries, timestamp, i);
                var frame = await session.PublishAsync(
                    values,
                    new Iec61850UtcTime(timestamp, Quality: 0),
                    stateChanged: i == 0).ConfigureAwait(false);
                packets.Add(new PcapPacket(timestamp, frame));
            }
        }
    }

    private static byte[] BuildSamplePayload(IReadOnlyList<SclDataSetEntry> entries, int sampleIndex)
    {
        var bytes = new List<byte>(Math.Max(entries.Count, 1) * 4);
        Span<byte> buffer = stackalloc byte[4];

        foreach (var entry in entries)
        {
            if (entry.IsQuality)
            {
                bytes.AddRange([0x00, 0x00, 0x00, 0x00]);
                continue;
            }

            if (entry.IsTimestamp)
            {
                bytes.AddRange([0x00, 0x00, 0x00, 0x00]);
                continue;
            }

            var value = 1000 + (sampleIndex * 10) + entry.Index;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            bytes.AddRange(buffer.ToArray());
        }

        return bytes.ToArray();
    }

    private static byte[] BuildDemoSamplePayload(
        IReadOnlyList<SclDataSetEntry> entries,
        long sampleIndex,
        double sampleRateHz,
        double nominalHz)
    {
        var bytes = new List<byte>(Math.Max(entries.Count, 1) * 4);
        Span<byte> buffer = stackalloc byte[4];

        foreach (var entry in entries)
        {
            if (entry.IsQuality)
            {
                bytes.AddRange([0x00, 0x00, 0x00, 0x00]);
                continue;
            }

            if (entry.IsTimestamp)
            {
                bytes.AddRange([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
                continue;
            }

            var amplitude = string.Equals(entry.LnClass, "TVTR", StringComparison.OrdinalIgnoreCase)
                ? 100_000
                : string.Equals(entry.LnClass, "TCTR", StringComparison.OrdinalIgnoreCase)
                    ? 10_000
                    : 1_000;
            var angle = (2.0 * Math.PI * nominalHz * sampleIndex / sampleRateHz) + ResolvePhaseRadians(entry);
            var value = (int)Math.Round(amplitude * Math.Sin(angle));
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            bytes.AddRange(buffer.ToArray());
        }

        return bytes.ToArray();
    }

    private static double ResolvePhaseRadians(SclDataSetEntry entry)
    {
        if (!int.TryParse(entry.LnInst, NumberStyles.Integer, CultureInfo.InvariantCulture, out var instance))
            return 0;

        return instance switch
        {
            2 => -2.0 * Math.PI / 3.0,
            3 => 2.0 * Math.PI / 3.0,
            _ => 0
        };
    }

    private static IReadOnlyList<MmsDataValue> BuildGooseValues(IReadOnlyList<SclDataSetEntry> entries, DateTimeOffset timestamp, int index)
    {
        var values = new List<MmsDataValue>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.IsTimestamp)
            {
                values.Add(MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, Quality: 0)));
            }
            else if (entry.IsQuality)
            {
                values.Add(MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }));
            }
            else if (string.Equals(entry.BType, "BOOLEAN", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.BType, "Bool", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(MmsDataValue.Boolean(index % 2 == 0));
            }
            else if (entry.BType.Contains("INT", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(MmsDataValue.Integer(index + entry.Index));
            }
            else
            {
                values.Add(MmsDataValue.VisibleString($"value-{index}-{entry.Index}"));
            }
        }

        return values;
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

    private static string FormatGooseLimit(long? frameLimit, double durationSeconds)
    {
        if (frameLimit.HasValue)
            return $"frames={frameLimit.Value}";

        return durationSeconds > 0
            ? $"duration={durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s"
            : "frames=continuous";
    }

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
            ProcessBusEventKind.SampledValues => $"{prefix} {common} smpCnt={streamEvent.SampleCount?.ToString() ?? "-"} payloadBytes={streamEvent.PayloadBytes}",
            ProcessBusEventKind.Goose => $"{prefix} {common} stNum={streamEvent.StateNumber?.ToString() ?? "-"} sqNum={streamEvent.SequenceNumber?.ToString() ?? "-"} values={streamEvent.ValueCount}",
            _ => $"{prefix} {streamEvent.Detail}"
        };
    }

    private static string FormatMonitorSummary(ProcessBusStreamSummary summary)
    {
        var common = $"{summary.Kind} APPID=0x{summary.AppId:X4} id={TextOrDash(summary.StreamId)} packets={summary.PacketCount}";
        return summary.Kind == ProcessBusEventKind.SampledValues
            ? $"{common} smpCnt={FormatCounterRange(summary.FirstSampleCount, summary.LastSampleCount)}"
            : $"{common} stNum={summary.LastStateNumber} sqNum={summary.LastSequenceNumber}";
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

    private static void WriteReportDiagnostics(MmsReportSessionDiagnostics diagnostics)
    {
        Console.WriteLine("Diagnostics:");
        Console.WriteLine($"  {diagnostics.Summary}");
        if (!string.IsNullOrWhiteSpace(diagnostics.FirstEntryIdHex) || !string.IsNullOrWhiteSpace(diagnostics.LastEntryIdHex))
            Console.WriteLine($"  EntryID: {TextOrDash(diagnostics.FirstEntryIdHex)} -> {TextOrDash(diagnostics.LastEntryIdHex)}");
        if (diagnostics.ReasonCounts.Count > 0)
            Console.WriteLine($"  Reasons: {string.Join(", ", diagnostics.ReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value}"))}");
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
                }
            },
            result.IsSuccess,
            result.Message,
            result.Diagnostics,
            warnings = result.Warnings
        };

        var summaryJsonPath = Path.Combine(directory, "summary.json");
        var reportsJsonPath = Path.Combine(directory, "reports.json");
        var pollReadsJsonPath = Path.Combine(directory, "poll-reads.json");
        var writeStepsJsonPath = Path.Combine(directory, "write-steps.json");
        var summaryMdPath = Path.Combine(directory, "summary.md");

        await File.WriteAllTextAsync(summaryJsonPath, JsonSerializer.Serialize(context, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(reportsJsonPath, JsonSerializer.Serialize(result.Reports.Select(ToReportEvidence), jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(pollReadsJsonPath, JsonSerializer.Serialize(result.PollReads, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(writeStepsJsonPath, JsonSerializer.Serialize(result.WriteSteps, jsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryMdPath, BuildReportEvidenceMarkdown(context.generatedAt, target, mode, plan, result), System.Text.Encoding.UTF8).ConfigureAwait(false);

        return [summaryJsonPath, reportsJsonPath, pollReadsJsonPath, writeStepsJsonPath, summaryMdPath];
    }

    private static object ToReportEvidence(MmsReportFrame report)
        => new
        {
            report.ReceivedAt,
            report.Message,
            report.RawAccessResultCount,
            report.InclusionBitstringItemIndex,
            report.IncludedDataSetIndexes,
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
            $"- Diagnostics: {diagnostics.Summary}",
            $"- EntryID: {TextOrDash(diagnostics.FirstEntryIdHex)} -> {TextOrDash(diagnostics.LastEntryIdHex)}",
            string.Empty,
            "## Counts",
            string.Empty,
            $"| Metric | Value |",
            $"| --- | ---: |",
            $"| Reports | {diagnostics.ReportCount} |",
            $"| Report values | {diagnostics.ValueCount} |",
            $"| Mapping failures | {diagnostics.MappingFailureCount} |",
            $"| Poll reads OK | {diagnostics.PollReadSuccessCount}/{diagnostics.PollReadCount} |",
            $"| Write failures | {diagnostics.WriteFailureCount} |",
            $"| Duplicate report keys | {diagnostics.DuplicateReportKeyCount} |",
            $"| Sequence gaps | {diagnostics.SequenceGapCount} |",
            $"| Sequence regressions | {diagnostics.SequenceRegressionCount} |",
            $"| EntryID gaps | {diagnostics.EntryIdGapCount} |",
            $"| EntryID regressions | {diagnostics.EntryIdRegressionCount} |",
            $"| Buffer overflow observed | {diagnostics.BufferOverflowObserved.ToString().ToLowerInvariant()} |",
            string.Empty
        };

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
        Console.WriteLine("  generate-pcap <scl-file> <output.pcap> [--source-mac XX:XX:XX:XX:XX:XX] [--sv-frames N] [--goose-frames N]");
        Console.WriteLine("  inspect-pcap <file.pcap>");
        Console.WriteLine("  stream-pcap <file.pcap> [--delay-ms N] [--limit N]");
        Console.WriteLine("  list-adapters");
        Console.WriteLine("  mms-discover <host-or-ip> [--port 102] [--timeout-ms 30000] [--no-report-probe] [--max-report-probes N] [--raw-limit N] [--show-raw]");
        Console.WriteLine("  mms-directory <host-or-ip> [--port 102] [--timeout-ms 30000] [--ln-limit N] [--raw-limit N] [--show-points]");
        Console.WriteLine("  mms-find <host-or-ip> <query> [--port 102] [--timeout-ms 30000] [--fc ST|MX|CO|RP|BR] [--ld LD] [--ln LN] [--raw-limit N]");
        Console.WriteLine("  mms-resolve <host-or-ip> <LD/LN.DO.da> [--port 102] [--timeout-ms 30000] [--raw-limit N]");
        Console.WriteLine("  mms-read-smart <host-or-ip> <LD/LN.DO.da> [--port 102] [--timeout-ms 30000]");
        Console.WriteLine("  mms-report-plan <host-or-ip> [--port 102] [--timeout-ms 60000] [--max-report-probes N] [--only-safe] [--kind ReadyStaticDataSet]");
        Console.WriteLine("  mms-report-static-plan <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--dataset LD/LLN0.DataSet] [--read-values]");
        Console.WriteLine("  mms-report-dynamic-plan <host-or-ip> --points <LD/LN.DO.da,LD/LN.DO.da> [--ld LD] [--rcb LD/LN.RP.name] [--dataset-name AR_DYN_DS01]");
        Console.WriteLine("  mms-rcb-probe <host-or-ip> <LD/LN.BR.name|LD/LN.RP.name> [--port 102] [--timeout-ms 120000]");
        Console.WriteLine("  mms-report-static-live <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--duration-sec 15] [--reserve-sec 30] [--gi true|false] [--evidence out/session] [--yes]");
        Console.WriteLine("  mms-report-monitor <host-or-ip> [--port 102] [--timeout-ms 120000] [--rcb LD/LN.BR.name] [--duration-sec 60] [--gi true|false] [--poll-points LD/LN.DO.da,...] [--poll-interval-ms 1000] [--evidence out/session] [--yes]");
        Console.WriteLine("  mms-report-dynamic-live <host-or-ip> --points <LD/LN.DO.da,LD/LN.DO.da> [--ld LD] [--rcb LD/LN.RP.name] [--dataset-name AR_DYN_DS01] [--duration-sec 15] [--delete-dataset true|false] [--evidence out/session] [--yes]");
        Console.WriteLine("  mms-dataset-directory <host-or-ip> [LD/LLN0.DataSet] [--port 102] [--timeout-ms 60000] [--raw-limit N] [--read-values]");
        Console.WriteLine("  publish-sv-live <scl-file> --adapter <index|name> [--stream-index N] [--source-mac XX:XX:XX:XX:XX:XX] [--frames N] [--duration-sec N] [--continuous] [--status-ms N] [--rate-hz N] [--nominal-hz N] [--dry-run] [--yes]");
        Console.WriteLine("  publish-goose-live <scl-file> --adapter <index|name> [--stream-index N] [--source-mac XX:XX:XX:XX:XX:XX] [--frames N] [--duration-sec N] [--continuous] [--status-ms N] [--min-ms N] [--max-ms N] [--toggle-every-sec N] [--initial-state true|false] [--test] [--nds-com] [--dry-run] [--yes]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl samples/scl/minimal-station.scd");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- generate-pcap samples/scl/minimal-station.scd out/processbus-demo.pcap");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-pcap out/processbus-demo.pcap");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- stream-pcap out/processbus-demo.pcap --delay-ms 50 --limit 12");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- list-adapters");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-discover 192.168.1.10 --port 102 --max-report-probes 16");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-directory 192.168.1.10 --show-points --raw-limit 40");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-find 192.168.1.10 XCBR --fc ST --raw-limit 40");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-resolve 192.168.1.10 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-read-smart 192.168.1.10 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-plan 192.168.1.10 --max-report-probes 64 --only-safe");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-static-plan 192.168.1.10 --read-values");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-dynamic-plan 192.168.1.10 --points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f,OCR7SR12CTRL/BI6GGIO1.Ind1.stVal");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-static-live 192.168.1.10 --duration-sec 15 --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-monitor 192.168.1.10 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --evidence out/report-session01 --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-report-dynamic-live 192.168.1.10 --points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f,OCR7SR12CTRL/BI6GGIO1.Ind1.stVal --yes");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- mms-dataset-directory 192.168.1.10 OCR7SR12PROT/LLN0.DataSet --raw-limit 80");
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
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must start with --.");

            var key = arg[2..];
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Option name cannot be empty.");

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
