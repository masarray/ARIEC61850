using System.Text.Json;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

if (args.Length < 2 || args.Any(x => x is "-h" or "--help" or "help"))
{
    WriteUsage();
    return args.Length < 2 ? 1 : 0;
}

var sclPath = args[0];
var host = args[1];
var options = Options.Parse(args[2..]);
var reportSelector = options.GetRequired("report");
var kind = options.Get("kind", string.Empty).Trim();
var port = options.GetInt("port", 102);
var timeoutMs = options.GetInt("timeout-ms", 120000);
var maxReportProbes = options.GetInt("max-report-probes", 286);
var initialTimeoutSec = options.GetInt("initial-timeout-sec", 10);
var durationSec = options.GetInt("duration-sec", 30);
var evidencePath = options.Get("evidence", string.Empty);
var confirmed = options.GetBool("yes", false);

if (!File.Exists(sclPath))
    throw new FileNotFoundException("SCL file was not found.", sclPath);
if (port is < 1 or > 65535)
    throw new ArgumentException("--port must be 1..65535.");
if (timeoutMs < 1)
    throw new ArgumentException("--timeout-ms must be at least 1.");
if (initialTimeoutSec < 1)
    throw new ArgumentException("--initial-timeout-sec must be at least 1.");
if (durationSec < initialTimeoutSec)
    throw new ArgumentException("--duration-sec must be greater than or equal to --initial-timeout-sec.");
if (!string.IsNullOrWhiteSpace(kind) &&
    !kind.Equals("BRCB", StringComparison.OrdinalIgnoreCase) &&
    !kind.Equals("URCB", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("--kind must be BRCB or URCB when supplied.");

var document = new SclParser().Load(sclPath);
var reportMatches = document.ReportControls
    .Where(x => x.Name.Equals(reportSelector, StringComparison.Ordinal) ||
                x.ControlBlockReference.Equals(reportSelector, StringComparison.Ordinal))
    .Where(x => string.IsNullOrWhiteSpace(kind) ||
                (kind.Equals("BRCB", StringComparison.OrdinalIgnoreCase) ? x.Buffered : !x.Buffered))
    .ToArray();

if (reportMatches.Length != 1)
{
    Console.Error.WriteLine($"ERROR: expected exactly one SCL ReportControl for selector '{reportSelector}' kind='{(string.IsNullOrWhiteSpace(kind) ? "any" : kind)}', found {reportMatches.Length}.");
    foreach (var report in document.ReportControls)
        Console.Error.WriteLine($"  {(report.Buffered ? "BRCB" : "URCB")} {report.ControlBlockReference} name={report.Name}");
    return 2;
}

var sclReport = reportMatches[0];
using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
await using var session = new MmsClientSession();

Console.WriteLine("ARIEC61850 SCL initial-GI report trial");
Console.WriteLine($"SCL: {Path.GetFullPath(sclPath)} ({document.Edition})");
Console.WriteLine($"Target: {host}:{port}");
Console.WriteLine($"Declared report family: {(sclReport.Buffered ? "BRCB" : "URCB")} {sclReport.ControlBlockReference}");
Console.WriteLine("Contract: discover live -> reconcile SCL family -> safe concrete RCB -> RptEna -> register routing -> one-shot GI -> initial values -> event-driven reports.");

await session.ConnectAsync(host, port, TimeSpan.FromMilliseconds(timeoutMs), timeout.Token).ConfigureAwait(false);
Console.WriteLine($"Association: {session.State}");
Console.WriteLine($"  {session.LastHandshakeMessage}");
Console.WriteLine($"Receive pump: {(session.IsReceivePumpRunning ? "running" : "stopped")}");

var discovery = await session.DiscoverAsync(
    probeReportAttributes: true,
    maxReportAttributeProbes: maxReportProbes,
    cancellationToken: timeout.Token).ConfigureAwait(false);

Console.WriteLine(discovery.IedDirectory.Summary);
Console.WriteLine(discovery.ReportInventory.Summary);

var liveDataSetReferences = discovery.ReportInventory.ReportControls
    .Where(x => !string.IsNullOrWhiteSpace(x.DataSetReference))
    .Select(x => x.DataSetReference)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var dataSetDirectories = await session.GetDataSetDirectoriesAsync(
    liveDataSetReferences,
    discovery.IedDirectory,
    timeout.Token).ConfigureAwait(false);

var preflight = MmsSclReportSubscriptionPlanner.BuildStaticPlan(
    discovery.ReportInventory,
    dataSetDirectories,
    sclReport,
    allowUrCbFallback: false,
    allowPollingFallback: false);

Console.WriteLine();
Console.WriteLine("SCL/live preflight:");
Console.WriteLine($"  {preflight.RcbResolution.Message}");
Console.WriteLine($"  {preflight.Plan.Summary}");
if (preflight.Plan.ReportControl != null)
{
    var selected = preflight.Plan.ReportControl;
    Console.WriteLine($"  selected={selected.Reference} mode={selected.Mode} dataset={selected.DataSetReference} rptEna={selected.EnabledState} resv={(selected.Buffered ? selected.ReservationTimeSeconds : selected.ReservationState)} attrs={string.Join(',', selected.Attributes)}");
}
foreach (var blocker in preflight.Plan.Blockers)
    Console.WriteLine($"  BLOCKER {blocker}");
foreach (var warning in preflight.Plan.Warnings)
    Console.WriteLine($"  WARNING {warning}");

if (!preflight.IsReady)
{
    Console.WriteLine("Trial blocked before any report-control write.");
    await WriteEvidenceAsync(evidencePath, document, sclReport, discovery, preflight, null, null, null).ConfigureAwait(false);
    return 3;
}

if (!confirmed)
{
    Console.WriteLine();
    Console.WriteLine("Dry-run complete. No report-control write was performed. Re-run with --yes on an authorized test IED to execute the initial-GI trial.");
    await WriteEvidenceAsync(evidencePath, document, sclReport, discovery, preflight, null, null, null).ConfigureAwait(false);
    return 0;
}

Console.WriteLine();
Console.WriteLine("Executing guarded SCL initial-GI bootstrap...");
var bootstrap = await session.StartSclPersistentReportMonitorWithInitialGiAsync(
    sclReport,
    discovery.ReportInventory,
    dataSetDirectories,
    TimeSpan.FromSeconds(initialTimeoutSec),
    allowUrCbFallback: false,
    allowPollingFallback: false,
    deleteDynamicDataSetOnStop: true,
    directory: discovery.IedDirectory,
    cancellationToken: timeout.Token).ConfigureAwait(false);

Console.WriteLine($"Bootstrap state: {bootstrap.State}");
Console.WriteLine($"  {bootstrap.Message}");
Console.WriteLine($"  monitoring={bootstrap.IsMonitoring} initialValues={bootstrap.HasInitialValues}");
if (bootstrap.Bootstrap != null)
{
    Console.WriteLine($"  GI capability={bootstrap.Bootstrap.GiCapabilityObserved} attempted={bootstrap.Bootstrap.GiAttempted} success={bootstrap.Bootstrap.GiSucceeded}");
    foreach (var step in bootstrap.Bootstrap.Start.WriteSteps)
        Console.WriteLine($"  START {(step.IsSuccess ? "OK" : "FAIL")} {step.Attribute} {step.Reference}: {step.Message}");
    foreach (var step in bootstrap.Bootstrap.InitialReceive.WriteSteps)
        Console.WriteLine($"  BOOT {(step.IsSuccess ? "OK" : "FAIL")} {step.Attribute} {step.Reference}: {step.Message}");
    WriteReports("Initial reports", bootstrap.Bootstrap.InitialReports);
}

MmsPersistentReportMonitorReceiveResult? steadyState = null;
MmsPersistentReportMonitorStopResult? stop = null;
var monitorSession = bootstrap.Bootstrap?.Session;
try
{
    if (monitorSession != null && !monitorSession.IsStopped)
    {
        var remaining = TimeSpan.FromSeconds(Math.Max(0, durationSec - initialTimeoutSec));
        if (remaining > TimeSpan.Zero)
        {
            Console.WriteLine();
            Console.WriteLine($"Listening event-driven for {remaining.TotalSeconds:0}s with no further GI and no polling...");
            steadyState = await session.ReceivePersistentReportMonitorSliceAsync(
                monitorSession,
                remaining,
                pollDirectory: null,
                pollReferences: null,
                pollInterval: null,
                triggerGeneralInterrogation: false,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            WriteReports("Event-driven reports", steadyState.Reports);
        }
    }
}
finally
{
    if (monitorSession != null && !monitorSession.IsStopped)
    {
        stop = await session.StopPersistentReportMonitorAsync(monitorSession, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Cleanup: {(stop.IsSuccess ? "OK" : "CHECK")} {stop.Message}");
        foreach (var step in stop.WriteSteps)
            Console.WriteLine($"  {(step.IsSuccess ? "OK" : "FAIL")} {step.Attribute} {step.Reference}: {step.Message}");
    }
}

await WriteEvidenceAsync(evidencePath, document, sclReport, discovery, preflight, bootstrap, steadyState, stop).ConfigureAwait(false);

var fieldPass = bootstrap.Bootstrap is
{
    State: MmsInitialReportBootstrapState.InitialValuesReceived,
    GiAttempted: true,
    GiSucceeded: true,
    HasInitialValues: true
} && stop?.IsSuccess == true;

Console.WriteLine();
Console.WriteLine(fieldPass
    ? "TRIAL PASS: initial values arrived through the registered-monitor one-shot GI path and cleanup succeeded."
    : "TRIAL CHECK: review bootstrap/initial report/cleanup evidence before merge.");
return fieldPass ? 0 : 4;

static void WriteReports(string title, IReadOnlyList<MmsReportFrame> reports)
{
    Console.WriteLine($"{title}: {reports.Count}");
    foreach (var report in reports.Take(20))
    {
        Console.WriteLine($"  {report.ReceivedAt:yyyy-MM-dd HH:mm:ss.fff}Z {report.Header.Summary} values={report.Values.Count}");
        foreach (var value in report.Values.Take(12))
            Console.WriteLine($"    [{value.Index}] {value.MemberReference} = {value.DisplayValue} reason={value.ReasonSummary}");
        if (report.Values.Count > 12)
            Console.WriteLine($"    ... +{report.Values.Count - 12} more value(s)");
    }
    if (reports.Count > 20)
        Console.WriteLine($"  ... +{reports.Count - 20} more report(s)");
}

static async Task WriteEvidenceAsync(
    string evidencePath,
    SclDocument document,
    SclReportControl sclReport,
    MmsDiscoveryResult discovery,
    MmsSclReportSubscriptionPlanResult preflight,
    MmsSclInitialReportBootstrapResult? bootstrap,
    MmsPersistentReportMonitorReceiveResult? steadyState,
    MmsPersistentReportMonitorStopResult? stop)
{
    if (string.IsNullOrWhiteSpace(evidencePath))
        return;

    var path = Path.GetFullPath(evidencePath);
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    var selected = preflight.Plan.ReportControl;
    var evidence = new
    {
        generatedAtUtc = DateTimeOffset.UtcNow,
        scl = new
        {
            document.SourceName,
            Edition = document.Edition.ToString(),
            report = new
            {
                sclReport.Name,
                sclReport.ControlBlockReference,
                sclReport.Buffered,
                sclReport.Indexed,
                sclReport.DataSetReference,
                sclReport.ReportId,
                sclReport.ConfigurationRevision
            }
        },
        live = new
        {
            discovery.IedDirectory.Summary,
            ReportInventorySummary = discovery.ReportInventory.Summary,
            selectedRcb = selected == null ? null : new
            {
                selected.Reference,
                selected.Mode,
                selected.DataSetReference,
                selected.ReportId,
                selected.ConfRev,
                selected.EnabledState,
                selected.ReservationState,
                selected.ReservationTimeSeconds,
                selected.Attributes
            }
        },
        familyResolution = new
        {
            preflight.RcbResolution.Kind,
            preflight.RcbResolution.Message,
            preflight.RcbResolution.MatchedReferences
        },
        plan = new
        {
            preflight.Plan.Summary,
            preflight.Plan.IsReady,
            preflight.Plan.Blockers,
            preflight.Plan.Warnings
        },
        bootstrap = bootstrap == null ? null : new
        {
            bootstrap.State,
            bootstrap.Message,
            bootstrap.IsMonitoring,
            bootstrap.HasInitialValues,
            inner = bootstrap.Bootstrap == null ? null : new
            {
                bootstrap.Bootstrap.State,
                bootstrap.Bootstrap.Message,
                bootstrap.Bootstrap.GiCapabilityObserved,
                bootstrap.Bootstrap.GiAttempted,
                bootstrap.Bootstrap.GiSucceeded,
                InitialReportCount = bootstrap.Bootstrap.InitialReports.Count,
                InitialValueCount = bootstrap.Bootstrap.InitialReports.Sum(x => x.Values.Count),
                StartWrites = bootstrap.Bootstrap.Start.WriteSteps,
                BootstrapWrites = bootstrap.Bootstrap.InitialReceive.WriteSteps,
                InitialReports = bootstrap.Bootstrap.InitialReports.Select(ToReportEvidence).ToArray()
            }
        },
        steadyState = steadyState == null ? null : new
        {
            ReportCount = steadyState.Reports.Count,
            PollReadCount = steadyState.PollReads.Count,
            GiWriteCount = steadyState.WriteSteps.Count(x => x.Attribute.Equals("GI", StringComparison.OrdinalIgnoreCase)),
            Reports = steadyState.Reports.Select(ToReportEvidence).ToArray()
        },
        cleanup = stop == null ? null : new
        {
            stop.IsSuccess,
            stop.Message,
            stop.WriteSteps
        }
    };

    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    Console.WriteLine($"Evidence: {path}");
}

static object ToReportEvidence(MmsReportFrame report)
    => new
    {
        report.ReceivedAt,
        header = new
        {
            report.Header.ReportId,
            report.Header.DataSetReference,
            report.Header.ConfRev,
            report.Header.SequenceNumber,
            report.Header.TimeOfEntry,
            report.Header.BufferOverflow,
            report.Header.EntryIdHex
        },
        report.IncludedDataSetIndexes,
        values = report.Values.Select(value => new
        {
            value.Index,
            value.MemberReference,
            value.DataReference,
            value.DisplayValue,
            value.ReasonForInclusion
        }).ToArray()
    };

static void WriteUsage()
{
    Console.WriteLine("AR.Iec61850.ReportTrial");
    Console.WriteLine("Purpose: qualify SCL-family resolution and registered-monitor one-shot GI startup against an authorized test IED.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project apps/AR.Iec61850.ReportTrial -- <scl-file> <host-or-ip> --report <SCL ReportControl name|full reference> [--kind BRCB|URCB] [--port 102] [--timeout-ms 120000] [--max-report-probes 286] [--initial-timeout-sec 10] [--duration-sec 30] [--evidence .artifacts/trial/result.json] [--yes]");
    Console.WriteLine();
    Console.WriteLine("Without --yes the command performs read-only discovery, SCL/live family reconciliation and safe planning only.");
}

sealed class Options
{
    private readonly Dictionary<string, string> _values;
    private Options(Dictionary<string, string> values) => _values = values;

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must start with --.");

            var key = arg[2..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Option name cannot be empty.");

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                values[key] = args[++i];
            else
                values[key] = bool.TrueString;
        }
        return new Options(values);
    }

    public string Get(string key, string fallback) => _values.TryGetValue(key, out var value) ? value : fallback;
    public string GetRequired(string key) => _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"--{key} is required.");
    public int GetInt(string key, int fallback) => !_values.TryGetValue(key, out var value)
        ? fallback
        : int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"--{key} must be a non-negative integer.");
    public bool GetBool(string key, bool fallback) => !_values.TryGetValue(key, out var value)
        ? fallback
        : bool.TryParse(value, out var parsed)
            ? parsed
            : value.Equals("1", StringComparison.Ordinal) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                ? true
                : value.Equals("0", StringComparison.Ordinal) || value.Equals("no", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : throw new ArgumentException($"--{key} must be true or false.");
}
