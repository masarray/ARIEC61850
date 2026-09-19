using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AR.Iec61850.Mms;

public sealed class MmsSmartDiscoveryKpiPhaseSnapshot
{
    public string Phase { get; init; } = string.Empty;
    public int Requests { get; init; }
    public int SuccessfulRequests { get; init; }
    public int FailedRequests { get; init; }
    public int DuplicateRequests { get; init; }
    public double TotalLatencyMs { get; init; }
    public double AverageLatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
}

/// <summary>
/// Zero-traffic observability snapshot for one smart-discovery generation.
/// Request counts come only from requests that ARSAS was already going to send.
/// Timing is measured around the existing confirmed-service call sites; no probe,
/// ping, Read, GVA, or GetNameList is added for KPI collection.
/// </summary>
public sealed class MmsSmartDiscoveryKpiSnapshot
{
    public long Generation { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public double ElapsedMs { get; init; }
    public int TotalRequests { get; init; }
    public int SuccessfulRequests { get; init; }
    public int FailedRequests { get; init; }
    public int DuplicateRequests { get; init; }
    public int PeakOutstandingRequests { get; init; }
    public bool WireAccountingComplete { get; init; } = true;
    public IReadOnlyList<string> AccountingNotes { get; init; } = Array.Empty<string>();
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int RawVariableCount { get; init; }
    public int FcPointCount { get; init; }
    public int DataSetCount { get; init; }
    public int DataSetDirectoryCount { get; init; }
    public int DataSetMemberCount { get; init; }
    public int ReportControlCount { get; init; }
    public int BufferedReportControlCount { get; init; }
    public int UnbufferedReportControlCount { get; init; }
    public IReadOnlyList<MmsSmartDiscoveryKpiPhaseSnapshot> Phases { get; init; } = Array.Empty<MmsSmartDiscoveryKpiPhaseSnapshot>();
    public string DeterministicSignature { get; init; } = string.Empty;

    public string Summary =>
        $"Smart discovery KPI: generation={Generation}, requests={TotalRequests}, success={SuccessfulRequests}, " +
        $"failed={FailedRequests}, duplicates={DuplicateRequests}, peakOutstanding={PeakOutstandingRequests}, " +
        $"wireAccounting={(WireAccountingComplete ? "complete" : "partial")}, elapsed={ElapsedMs:0.0} ms, " +
        $"LD={LogicalDeviceCount}, LN={LogicalNodeCount}, raw={RawVariableCount}, FC-points={FcPointCount}, " +
        $"datasets={DataSetCount}, datasetDirectories={DataSetDirectoryCount}, datasetMembers={DataSetMemberCount}, " +
        $"RCB={ReportControlCount}, signature={DeterministicSignature}.";
}

public sealed partial class MmsClientSession
{
    private static long _nextSmartDiscoveryKpiGeneration;
    private MmsSmartDiscoveryKpiRecorder? _smartDiscoveryKpiRecorder;

    /// <summary>
    /// Most recent point-in-time KPI snapshot. The deterministic signature excludes
    /// wall-clock timing and worker completion order so equivalent discovery evidence
    /// produces the same signature across repeat runs.
    /// </summary>
    public MmsSmartDiscoveryKpiSnapshot? LastSmartDiscoveryKpi
        => Volatile.Read(ref _smartDiscoveryKpiRecorder)?.Snapshot();

    private void BeginSmartDiscoveryKpiGeneration()
    {
        var generation = Interlocked.Increment(ref _nextSmartDiscoveryKpiGeneration);
        Volatile.Write(ref _smartDiscoveryKpiRecorder, new MmsSmartDiscoveryKpiRecorder(generation));
    }

    private MmsSmartDiscoveryRequestObservation ObserveSmartDiscoveryRequest(
        string phase,
        string service,
        string logicalKey)
        => Volatile.Read(ref _smartDiscoveryKpiRecorder)?.BeginRequest(phase, service, logicalKey)
           ?? MmsSmartDiscoveryRequestObservation.Noop;

    private void MarkSmartDiscoveryKpiAccountingPartial(string note)
        => Volatile.Read(ref _smartDiscoveryKpiRecorder)?.MarkAccountingPartial(note);

    private void UpdateSmartDiscoveryCompleteness(
        MmsDiscoverySnapshot snapshot,
        MmsIedModelDirectory directory,
        MmsReportInventory inventory,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories)
    {
        var recorder = Volatile.Read(ref _smartDiscoveryKpiRecorder);
        if (recorder is null)
            return;

        recorder.UpdateCompleteness(
            directory.LogicalDeviceCount,
            directory.LogicalNodeCount,
            snapshot.RawVariableCount,
            directory.PointCount,
            inventory.DataSets.Count,
            dataSetDirectories.Count(result => result.IsSuccess),
            dataSetDirectories.Where(result => result.IsSuccess).Sum(result => result.Members.Count),
            inventory.ReportControls.Count,
            inventory.BufferedCount,
            inventory.UnbufferedCount);
    }

    /// <summary>
    /// Refreshes model completeness after later semantic materialization has enriched
    /// the same live directory. This is local bookkeeping only and sends no MMS traffic.
    /// </summary>
    public void RefreshSmartDiscoveryModelKpi(MmsIedModelDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Volatile.Read(ref _smartDiscoveryKpiRecorder)?.UpdateModelCompleteness(
            directory.LogicalDeviceCount,
            directory.LogicalNodeCount,
            directory.PointCount);
    }

    private async Task<IReadOnlyList<MmsDataSetDirectoryResult>> GetObservedSmartDataSetDirectoriesAsync(
        IReadOnlyList<string> dataSetReferences,
        MmsIedModelDirectory directory,
        CancellationToken cancellationToken)
    {
        if (dataSetReferences.Count == 0)
            return Array.Empty<MmsDataSetDirectoryResult>();

        var results = new List<MmsDataSetDirectoryResult>(dataSetReferences.Count);
        foreach (var reference in dataSetReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var observation = ObserveSmartDiscoveryRequest(
                "dataset-directory",
                "GetNamedVariableListAttributes",
                reference);

            var result = await GetDataSetDirectoryAsync(reference, directory, cancellationToken)
                .ConfigureAwait(false);
            observation.Complete(result.IsSuccess);
            results.Add(result);

            if (!IsMmsInitiated)
                break;
        }

        return results;
    }
}

internal sealed class MmsSmartDiscoveryKpiRecorder
{
    private readonly object _gate = new();
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, MutableRequestStats> _requestStats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutablePhaseStats> _phaseStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _accountingNotes = new(StringComparer.Ordinal);
    private int _totalRequests;
    private int _successfulRequests;
    private int _failedRequests;
    private int _duplicateRequests;
    private int _outstandingRequests;
    private int _peakOutstandingRequests;
    private bool _wireAccountingComplete = true;
    private int _logicalDeviceCount;
    private int _logicalNodeCount;
    private int _rawVariableCount;
    private int _fcPointCount;
    private int _dataSetCount;
    private int _dataSetDirectoryCount;
    private int _dataSetMemberCount;
    private int _reportControlCount;
    private int _bufferedReportControlCount;
    private int _unbufferedReportControlCount;

    public MmsSmartDiscoveryKpiRecorder(long generation)
    {
        Generation = generation;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public long Generation { get; }
    public DateTimeOffset StartedAtUtc { get; }

    public MmsSmartDiscoveryRequestObservation BeginRequest(string phase, string service, string logicalKey)
    {
        var normalizedPhase = Normalize(phase, "unknown");
        var normalizedService = Normalize(service, "unknown");
        var normalizedKey = Normalize(logicalKey, "<none>");
        var descriptor = $"{normalizedPhase}|{normalizedService}|{normalizedKey}";

        lock (_gate)
        {
            _totalRequests++;
            _outstandingRequests++;
            _peakOutstandingRequests = Math.Max(_peakOutstandingRequests, _outstandingRequests);

            if (!_phaseStats.TryGetValue(normalizedPhase, out var phaseStats))
            {
                phaseStats = new MutablePhaseStats(normalizedPhase);
                _phaseStats.Add(normalizedPhase, phaseStats);
            }
            phaseStats.Requests++;

            if (!_requestStats.TryGetValue(descriptor, out var requestStats))
            {
                requestStats = new MutableRequestStats(descriptor);
                _requestStats.Add(descriptor, requestStats);
            }
            else
            {
                _duplicateRequests++;
                phaseStats.DuplicateRequests++;
            }
            requestStats.Attempts++;
        }

        return new MmsSmartDiscoveryRequestObservation(this, normalizedPhase, descriptor, Stopwatch.GetTimestamp());
    }

    public void MarkAccountingPartial(string note)
    {
        lock (_gate)
        {
            _wireAccountingComplete = false;
            var normalized = Normalize(note, "unspecified unobserved request path");
            _accountingNotes.Add(normalized);
        }
    }

    public void UpdateCompleteness(
        int logicalDeviceCount,
        int logicalNodeCount,
        int rawVariableCount,
        int fcPointCount,
        int dataSetCount,
        int dataSetDirectoryCount,
        int dataSetMemberCount,
        int reportControlCount,
        int bufferedReportControlCount,
        int unbufferedReportControlCount)
    {
        lock (_gate)
        {
            _logicalDeviceCount = Math.Max(0, logicalDeviceCount);
            _logicalNodeCount = Math.Max(0, logicalNodeCount);
            _rawVariableCount = Math.Max(0, rawVariableCount);
            _fcPointCount = Math.Max(0, fcPointCount);
            _dataSetCount = Math.Max(0, dataSetCount);
            _dataSetDirectoryCount = Math.Max(0, dataSetDirectoryCount);
            _dataSetMemberCount = Math.Max(0, dataSetMemberCount);
            _reportControlCount = Math.Max(0, reportControlCount);
            _bufferedReportControlCount = Math.Max(0, bufferedReportControlCount);
            _unbufferedReportControlCount = Math.Max(0, unbufferedReportControlCount);
        }
    }

    public void UpdateModelCompleteness(int logicalDeviceCount, int logicalNodeCount, int fcPointCount)
    {
        lock (_gate)
        {
            _logicalDeviceCount = Math.Max(0, logicalDeviceCount);
            _logicalNodeCount = Math.Max(0, logicalNodeCount);
            _fcPointCount = Math.Max(0, fcPointCount);
        }
    }

    public MmsSmartDiscoveryKpiSnapshot Snapshot()
    {
        lock (_gate)
        {
            var phases = _phaseStats.Values
                .OrderBy(phase => phase.Phase, StringComparer.OrdinalIgnoreCase)
                .Select(phase => new MmsSmartDiscoveryKpiPhaseSnapshot
                {
                    Phase = phase.Phase,
                    Requests = phase.Requests,
                    SuccessfulRequests = phase.SuccessfulRequests,
                    FailedRequests = phase.FailedRequests,
                    DuplicateRequests = phase.DuplicateRequests,
                    TotalLatencyMs = phase.TotalLatencyMs,
                    AverageLatencyMs = phase.Requests == 0 ? 0 : phase.TotalLatencyMs / phase.Requests,
                    MaxLatencyMs = phase.MaxLatencyMs
                })
                .ToArray();

            return new MmsSmartDiscoveryKpiSnapshot
            {
                Generation = Generation,
                StartedAtUtc = StartedAtUtc,
                ElapsedMs = Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds,
                TotalRequests = _totalRequests,
                SuccessfulRequests = _successfulRequests,
                FailedRequests = _failedRequests,
                DuplicateRequests = _duplicateRequests,
                PeakOutstandingRequests = _peakOutstandingRequests,
                WireAccountingComplete = _wireAccountingComplete,
                AccountingNotes = _accountingNotes.OrderBy(note => note, StringComparer.Ordinal).ToArray(),
                LogicalDeviceCount = _logicalDeviceCount,
                LogicalNodeCount = _logicalNodeCount,
                RawVariableCount = _rawVariableCount,
                FcPointCount = _fcPointCount,
                DataSetCount = _dataSetCount,
                DataSetDirectoryCount = _dataSetDirectoryCount,
                DataSetMemberCount = _dataSetMemberCount,
                ReportControlCount = _reportControlCount,
                BufferedReportControlCount = _bufferedReportControlCount,
                UnbufferedReportControlCount = _unbufferedReportControlCount,
                Phases = phases,
                DeterministicSignature = BuildDeterministicSignature()
            };
        }
    }

    internal void CompleteRequest(string phase, string descriptor, long startedTimestamp, bool success)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        lock (_gate)
        {
            _outstandingRequests = Math.Max(0, _outstandingRequests - 1);
            if (success)
                _successfulRequests++;
            else
                _failedRequests++;

            if (_requestStats.TryGetValue(descriptor, out var requestStats))
            {
                if (success)
                    requestStats.SuccessfulAttempts++;
                else
                    requestStats.FailedAttempts++;
            }

            if (_phaseStats.TryGetValue(phase, out var phaseStats))
            {
                if (success)
                    phaseStats.SuccessfulRequests++;
                else
                    phaseStats.FailedRequests++;
                phaseStats.TotalLatencyMs += elapsedMs;
                phaseStats.MaxLatencyMs = Math.Max(phaseStats.MaxLatencyMs, elapsedMs);
            }
        }
    }

    private string BuildDeterministicSignature()
    {
        var builder = new StringBuilder();
        foreach (var request in _requestStats.Values.OrderBy(item => item.Descriptor, StringComparer.Ordinal))
        {
            builder.Append(request.Descriptor)
                .Append("|attempts=").Append(request.Attempts)
                .Append("|ok=").Append(request.SuccessfulAttempts)
                .Append("|fail=").Append(request.FailedAttempts)
                .Append('\n');
        }

        builder.Append("accounting|complete=").Append(_wireAccountingComplete);
        foreach (var note in _accountingNotes.OrderBy(note => note, StringComparer.Ordinal))
            builder.Append("|note=").Append(note);
        builder.Append('\n');

        builder.Append("model|ld=").Append(_logicalDeviceCount)
            .Append("|ln=").Append(_logicalNodeCount)
            .Append("|raw=").Append(_rawVariableCount)
            .Append("|fc=").Append(_fcPointCount)
            .Append("|ds=").Append(_dataSetCount)
            .Append("|dsdir=").Append(_dataSetDirectoryCount)
            .Append("|dsmembers=").Append(_dataSetMemberCount)
            .Append("|rcb=").Append(_reportControlCount)
            .Append("|brcb=").Append(_bufferedReportControlCount)
            .Append("|urcb=").Append(_unbufferedReportControlCount);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().Replace('\r', ' ').Replace('\n', ' ').ToLowerInvariant();

    private sealed class MutableRequestStats
    {
        public MutableRequestStats(string descriptor) => Descriptor = descriptor;
        public string Descriptor { get; }
        public int Attempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public int FailedAttempts { get; set; }
    }

    private sealed class MutablePhaseStats
    {
        public MutablePhaseStats(string phase) => Phase = phase;
        public string Phase { get; }
        public int Requests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int DuplicateRequests { get; set; }
        public double TotalLatencyMs { get; set; }
        public double MaxLatencyMs { get; set; }
    }
}

internal sealed class MmsSmartDiscoveryRequestObservation : IDisposable
{
    public static MmsSmartDiscoveryRequestObservation Noop { get; } = new(null, string.Empty, string.Empty, 0);

    private readonly MmsSmartDiscoveryKpiRecorder? _recorder;
    private readonly string _phase;
    private readonly string _descriptor;
    private readonly long _startedTimestamp;
    private int _completed;

    public MmsSmartDiscoveryRequestObservation(
        MmsSmartDiscoveryKpiRecorder? recorder,
        string phase,
        string descriptor,
        long startedTimestamp)
    {
        _recorder = recorder;
        _phase = phase;
        _descriptor = descriptor;
        _startedTimestamp = startedTimestamp;
    }

    public void Complete(bool success)
    {
        if (_recorder is null || Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        _recorder.CompleteRequest(_phase, _descriptor, _startedTimestamp, success);
    }

    public void Dispose() => Complete(success: false);
}
