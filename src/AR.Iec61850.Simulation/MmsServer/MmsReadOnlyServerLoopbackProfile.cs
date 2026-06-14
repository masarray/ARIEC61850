using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AR.Iec61850.Simulation;

public sealed class MmsReadOnlyServerLoopbackOptions
{
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public string AssociationProfileName { get; init; } = "BalancedApTitle";
    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";
    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";
    public int SimulationSteps { get; init; }
}

public sealed class MmsReadOnlyServerLoopbackProfile
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ServerName { get; init; } = string.Empty;
    public bool IsReady { get; init; }
    public int BoundPort { get; init; }
    public string AssociationProfileName { get; init; } = string.Empty;
    public string ResponseProfileName { get; init; } = string.Empty;
    public bool ModelReady { get; init; }
    public bool AssociationReady { get; init; }
    public bool NativeBerDispatchReady { get; init; }
    public bool ReadOnlyGuardReady { get; init; }
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int PointCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlBlockCount { get; init; }
    public int RequestCount { get; init; }
    public int ServerSuccessCount { get; init; }
    public int ServerFailureCount { get; init; }
    public int ClientDecodeSuccessCount { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<MmsReadOnlyServerLoopbackGate> Gates { get; init; } = Array.Empty<MmsReadOnlyServerLoopbackGate>();
    public IReadOnlyList<MmsReadOnlyServerLoopbackOperation> Operations { get; init; } = Array.Empty<MmsReadOnlyServerLoopbackOperation>();
    public IReadOnlyList<MmsReadOnlyServerLoopbackProbeResult> ProbeResults { get; init; } = Array.Empty<MmsReadOnlyServerLoopbackProbeResult>();
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public string Summary => $"read-only MMS server loopback alpha: ready={IsReady.ToString().ToLowerInvariant()} LD={LogicalDeviceCount} LN={LogicalNodeCount} points={PointCount} DataSets={DataSetCount} requests={RequestCount} guarded={ReadOnlyGuardReady.ToString().ToLowerInvariant()} port={BoundPort}";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MMS Read-Only Server Loopback Alpha Profile");
        sb.AppendLine();
        sb.AppendLine("This evidence profile combines the virtual IED model, TPKT/COTP association path, ACSE AARE/MMS InitiateResponse profile, and native MMS BER confirmed-request dispatch into one read-only loopback server alpha readiness gate. It remains intentionally read-only and does not claim full server conformance.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Ready | {IsReady.ToString().ToLowerInvariant()} |");
        sb.AppendLine($"| Server | {Escape(ServerName)} |");
        sb.AppendLine($"| Bound port | {BoundPort.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Association profile | {Escape(AssociationProfileName)} |");
        sb.AppendLine($"| Response profile | {Escape(ResponseProfileName)} |");
        sb.AppendLine($"| Logical devices | {LogicalDeviceCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Logical nodes | {LogicalNodeCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Points | {PointCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| DataSets | {DataSetCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Report control blocks | {ReportControlBlockCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Requests | {RequestCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Server success | {ServerSuccessCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Server failure | {ServerFailureCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Client decoded responses | {ClientDecodeSuccessCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Elapsed ms | {Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} |");
        sb.AppendLine();
        sb.AppendLine("## Readiness Gates");
        sb.AppendLine();
        sb.AppendLine("| Gate | Status | Message |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var gate in Gates)
            sb.AppendLine($"| {Escape(gate.Name)} | {(gate.IsPass ? "PASS" : "FAIL")} | {Escape(gate.Message)} |");
        sb.AppendLine();
        sb.AppendLine("## Service Operations");
        sb.AppendLine();
        sb.AppendLine("| Operation | Access | Status | Notes |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var operation in Operations)
            sb.AppendLine($"| {Escape(operation.Name)} | {Escape(operation.Access)} | {(operation.IsReady ? "READY" : "BLOCKED")} | {Escape(operation.Notes)} |");
        sb.AppendLine();
        sb.AppendLine("## Probe Results");
        sb.AppendLine();
        sb.AppendLine("| Status | Kind | InvokeID | Target | Server success | Client decode | Message |");
        sb.AppendLine("| --- | --- | ---: | --- | --- | --- | --- |");
        foreach (var result in ProbeResults)
            sb.AppendLine($"| {(result.IsTransportSuccess ? "OK" : "FAIL")} | {Escape(result.Kind)} | {result.InvokeId.ToString(CultureInfo.InvariantCulture)} | {Escape(result.Target)} | {result.IsServerSuccess.ToString().ToLowerInvariant()} | {result.IsClientDecodeSuccess.ToString().ToLowerInvariant()} | {Escape(result.Message)} |");
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (Findings.Count == 0)
        {
            sb.AppendLine("- No blocking finding from the read-only MMS server loopback alpha profile.");
        }
        else
        {
            foreach (var finding in Findings)
                sb.AppendLine($"- {Escape(finding)}");
        }

        return sb.ToString();
    }

    public string ToJson(JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(this, options ?? new JsonSerializerOptions { WriteIndented = true });

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}

public sealed record MmsReadOnlyServerLoopbackGate
{
    public string Name { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record MmsReadOnlyServerLoopbackOperation
{
    public string Name { get; init; } = string.Empty;
    public string Access { get; init; } = "read-only";
    public bool IsReady { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record MmsReadOnlyServerLoopbackProbeResult
{
    public string Kind { get; init; } = string.Empty;
    public int InvokeId { get; init; }
    public string Target { get; init; } = string.Empty;
    public bool IsTransportSuccess { get; init; }
    public bool IsServerSuccess { get; init; }
    public bool IsClientDecodeSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReadOnlyServerLoopbackProfileBuilder
{
    public async Task<MmsReadOnlyServerLoopbackProfile> RunAsync(
        MmsReadOnlyServerLoopbackOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MmsReadOnlyServerLoopbackOptions();
        if (options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "TCP port must be 0..65535.");

        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var engine = new IedSimulatorEngine(simulatorProfile);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < options.SimulationSteps; i++)
            engine.Step(now.AddMilliseconds(i * 250));

        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(simulatorProfile, engine.CreateSnapshot(now), new MmsReadOnlyServerProfileOptions
        {
            ServerName = string.IsNullOrWhiteSpace(options.ServerName) ? "ARIEC61850 Virtual IED" : options.ServerName,
            Port = options.Port == 0 ? 102 : options.Port,
            IncludeSelfTest = true
        });

        var dispatchProfile = await new MmsConfirmedRequestBerProfileBuilder().RunLoopbackProbeAsync(
            new MmsConfirmedRequestBerOptions
            {
                Port = options.Port,
                ProbeTimeoutMilliseconds = options.ProbeTimeoutMilliseconds,
                AssociationProfileName = options.AssociationProfileName,
                ResponseProfileName = options.ResponseProfileName,
                ServerName = options.ServerName,
                SimulationSteps = options.SimulationSteps
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var gates = BuildGates(serverProfile, dispatchProfile).ToArray();
        var operations = BuildOperations(serverProfile, dispatchProfile).ToArray();
        var findings = new List<string>();
        findings.AddRange(serverProfile.Diagnostics.Where(x => string.Equals(x.Severity, "High", StringComparison.OrdinalIgnoreCase)).Select(x => $"Server model: {x.Code} - {x.Message}"));
        findings.AddRange(dispatchProfile.Findings.Select(x => $"Loopback dispatch: {x}"));
        foreach (var gate in gates.Where(x => !x.IsPass))
            findings.Add($"Gate blocked: {gate.Name} - {gate.Message}");

        var isReady = serverProfile.IsReady && dispatchProfile.IsReady && gates.All(x => x.IsPass) && operations.All(x => x.IsReady);
        return new MmsReadOnlyServerLoopbackProfile
        {
            ServerName = serverProfile.ServerName,
            IsReady = isReady,
            BoundPort = dispatchProfile.BoundPort,
            AssociationProfileName = dispatchProfile.AssociationProfileName,
            ResponseProfileName = dispatchProfile.ResponseProfileName,
            ModelReady = serverProfile.IsReady,
            AssociationReady = dispatchProfile.ClientAssociateResponseAccepted,
            NativeBerDispatchReady = dispatchProfile.NativeBerRequestDecoded && dispatchProfile.NativeBerResponseEncoded && dispatchProfile.ClientNativeResponseDecoded,
            ReadOnlyGuardReady = dispatchProfile.WriteGuardVerified,
            LogicalDeviceCount = serverProfile.LogicalDeviceCount,
            LogicalNodeCount = serverProfile.LogicalNodeCount,
            PointCount = serverProfile.PointCount,
            DataSetCount = serverProfile.DataSetCount,
            ReportControlBlockCount = serverProfile.ReportControlBlockCount,
            RequestCount = dispatchProfile.RequestCount,
            ServerSuccessCount = dispatchProfile.ServerSuccessCount,
            ServerFailureCount = dispatchProfile.ServerFailureCount,
            ClientDecodeSuccessCount = dispatchProfile.ClientDecodeSuccessCount,
            Elapsed = dispatchProfile.Elapsed,
            Gates = gates,
            Operations = operations,
            ProbeResults = dispatchProfile.ProbeResults.Select(x => new MmsReadOnlyServerLoopbackProbeResult
            {
                Kind = x.Kind,
                InvokeId = x.InvokeId,
                Target = x.Target,
                IsTransportSuccess = x.IsTransportSuccess,
                IsServerSuccess = x.IsServerSuccess,
                IsClientDecodeSuccess = x.IsClientDecodeSuccess,
                Message = x.Message
            }).ToArray(),
            Findings = findings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IEnumerable<MmsReadOnlyServerLoopbackGate> BuildGates(MmsReadOnlyServerProfile serverProfile, MmsConfirmedRequestBerProfile dispatchProfile)
    {
        yield return Gate("virtual-model", serverProfile.IsReady, $"LD={serverProfile.LogicalDeviceCount} LN={serverProfile.LogicalNodeCount} points={serverProfile.PointCount} DataSets={serverProfile.DataSetCount} RCB={serverProfile.ReportControlBlockCount}");
        yield return Gate("tpkt-cotp", dispatchProfile.TpktExchangeVerified && dispatchProfile.CotpConnectionConfirmed, "TPKT exchange and COTP connection confirmation must pass.");
        yield return Gate("association", dispatchProfile.ClientAssociateRequestObserved && dispatchProfile.ServerAssociateResponseSent && dispatchProfile.ClientAssociateResponseAccepted, "AARQ must be observed and AARE/MMS InitiateResponse profile must be accepted by the probe client.");
        yield return Gate("native-ber-request", dispatchProfile.NativeBerRequestDecoded, "Native MMS BER confirmed-request payloads must be decoded.");
        yield return Gate("native-ber-response", dispatchProfile.NativeBerResponseEncoded && dispatchProfile.ClientNativeResponseDecoded, "Native MMS BER confirmed-response payloads must be encoded and decoded by the probe client.");
        yield return Gate("directory-dispatch", dispatchProfile.DirectoryDispatchVerified, "Logical device/logical node/DataSet directory services must dispatch successfully.");
        yield return Gate("read-dispatch", dispatchProfile.ReadDispatchVerified, "Point read service must dispatch successfully.");
        yield return Gate("dataset-directory-dispatch", dispatchProfile.DataSetDirectoryDispatchVerified, "DataSet member directory service must dispatch successfully.");
        yield return Gate("write-guard", dispatchProfile.WriteGuardVerified, "Write operation must be rejected by the read-only guard.");
    }

    private static IEnumerable<MmsReadOnlyServerLoopbackOperation> BuildOperations(MmsReadOnlyServerProfile serverProfile, MmsConfirmedRequestBerProfile dispatchProfile)
    {
        yield return Operation(nameof(MmsReadOnlyOperation.GetLogicalDeviceDirectory), "read", serverProfile.LogicalDeviceCount > 0 && dispatchProfile.DirectoryDispatchVerified, "Returns logical device names from the virtual IED model.");
        yield return Operation(nameof(MmsReadOnlyOperation.GetLogicalNodeDirectory), "read", serverProfile.LogicalNodeCount > 0 && dispatchProfile.DirectoryDispatchVerified, "Returns logical node names for a selected logical device.");
        yield return Operation(nameof(MmsReadOnlyOperation.GetDataSetDirectory), "read", serverProfile.DataSetCount > 0 && dispatchProfile.DirectoryDispatchVerified, "Returns named DataSet references.");
        yield return Operation(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), "read", serverProfile.PointCount > 0, "Returns synthetic variable access attribute summary from the server model.");
        yield return Operation(nameof(MmsReadOnlyOperation.Read), "read", serverProfile.PointCount > 0 && dispatchProfile.ReadDispatchVerified, "Returns point values using native MMS BER read response encoding.");
        yield return Operation(nameof(MmsReadOnlyOperation.ReadDataSet), "read", serverProfile.DataSetCount > 0 && dispatchProfile.DataSetDirectoryDispatchVerified, "Returns DataSet member directory and model-backed member values.");
        yield return Operation(nameof(MmsReadOnlyOperation.Write), "blocked", dispatchProfile.WriteGuardVerified, "Must remain rejected until an explicit safe control/write milestone is implemented.");
    }

    private static MmsReadOnlyServerLoopbackGate Gate(string name, bool isPass, string message)
        => new() { Name = name, IsPass = isPass, Message = message };

    private static MmsReadOnlyServerLoopbackOperation Operation(string name, string access, bool isReady, string notes)
        => new() { Name = name, Access = access, IsReady = isReady, Notes = notes };
}
