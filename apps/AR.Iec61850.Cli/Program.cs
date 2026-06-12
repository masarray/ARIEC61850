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
                "publish-sv-live" => await PublishSvLiveAsync(args[1..]).ConfigureAwait(false),
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
        Console.WriteLine("Use the adapter index with publish-sv-live --adapter <index>.");
        return 0;
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
        if (!frameLimit.HasValue)
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

    private static string FormatFrameLimit(long? frameLimit)
        => frameLimit.HasValue ? $"frames={frameLimit.Value}" : "frames=continuous";

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
        Console.WriteLine("  publish-sv-live <scl-file> --adapter <index|name> [--stream-index N] [--source-mac XX:XX:XX:XX:XX:XX] [--frames N] [--duration-sec N] [--continuous] [--status-ms N] [--rate-hz N] [--nominal-hz N] [--dry-run] [--yes]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl samples/scl/minimal-station.scd");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- generate-pcap samples/scl/minimal-station.scd out/processbus-demo.pcap");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- inspect-pcap out/processbus-demo.pcap");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- stream-pcap out/processbus-demo.pcap --delay-ms 50 --limit 12");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- list-adapters");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- publish-sv-live \"samples/scl/01_SV_Stream_4I+4V_(9-2LE).scd\" --adapter 1 --stream-index 1 --frames 4000 --dry-run");
        Console.WriteLine("  dotnet run --project apps/AR.Iec61850.Cli -- publish-sv-live \"samples/scl/01_SV_Stream_4I+4V_(9-2LE).scd\" --adapter 1 --stream-index 1 --continuous --yes");
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

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
