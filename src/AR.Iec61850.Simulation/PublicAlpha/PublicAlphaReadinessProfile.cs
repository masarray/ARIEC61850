using System.Globalization;
using System.Text;
using System.Text.Json;
using AR.Iec61850.Diagnostics.Binding;
using AR.Iec61850.Diagnostics.Goose;
using AR.Iec61850.Diagnostics.SampledValues;
using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Simulation;

public sealed class PublicAlphaReadinessOptions
{
    public string SclPath { get; init; } = Path.Combine("samples", "scl", "minimal-station.scd");
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public int SimulationSteps { get; init; } = 6;
    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";
    public string AssociationProfileName { get; init; } = "BalancedApTitle";
    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";
}

public sealed class PublicAlphaReadinessProfile
{
    public string Version { get; init; } = "N5.37";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SourceName { get; init; } = string.Empty;
    public bool IsReady { get; init; }
    public int GateCount => Gates.Count;
    public int PassedGateCount => Gates.Count(g => g.IsPass);
    public int BlockingFindingCount => Findings.Count(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase));
    public int WarningFindingCount => Findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
    public SclEngineeringProfile SclEngineering { get; init; } = new();
    public ExpectedObservedBindingProfile ProcessBusBinding { get; init; } = new();
    public GooseDiagnosticsProfile GooseDiagnostics { get; init; } = new();
    public SampledValuesDiagnosticsProfile SampledValuesDiagnostics { get; init; } = new();
    public MmsReadOnlyServerLoopbackProfile ReadOnlyMmsLoopback { get; init; } = new();
    public IReadOnlyList<PublicAlphaReadinessGate> Gates { get; init; } = Array.Empty<PublicAlphaReadinessGate>();
    public IReadOnlyList<PublicAlphaReadinessFinding> Findings { get; init; } = Array.Empty<PublicAlphaReadinessFinding>();

    public string Summary => $"public alpha readiness: ready={IsReady.ToString().ToLowerInvariant()} gates={PassedGateCount}/{GateCount} blocking={BlockingFindingCount} warnings={WarningFindingCount} source={SourceName}";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Public Alpha Readiness Profile");
        sb.AppendLine();
        sb.AppendLine("This profile is an engine-only public-alpha gate. It combines static SCL engineering, synthetic healthy process-bus binding, GOOSE/SV diagnostics, and the read-only MMS loopback server alpha into one repeatable evidence artifact. It does not claim full IEC 61850 conformance or production readiness.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| Version | {Escape(Version)} |");
        sb.AppendLine($"| Ready | {IsReady.ToString().ToLowerInvariant()} |");
        sb.AppendLine($"| Source | {Escape(SourceName)} |");
        sb.AppendLine($"| Gates passed | {PassedGateCount.ToString(CultureInfo.InvariantCulture)}/{GateCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Blocking findings | {BlockingFindingCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Warning findings | {WarningFindingCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| IEDs | {SclEngineering.Ieds.Count.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Logical devices | {SclEngineering.LogicalDevices.Count.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Logical nodes | {SclEngineering.LogicalNodes.Count.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| DataSets | {SclEngineering.DataSetCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Reports | {SclEngineering.ReportControlCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| GOOSE streams | {SclEngineering.GooseStreamCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| SV streams | {SclEngineering.SampledValuesStreamCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| MMS loopback requests | {ReadOnlyMmsLoopback.RequestCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine();

        sb.AppendLine("## Public Alpha Gates");
        sb.AppendLine();
        sb.AppendLine("| Gate | Status | Message |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var gate in Gates)
            sb.AppendLine($"| {Escape(gate.Name)} | {(gate.IsPass ? "PASS" : "FAIL")} | {Escape(gate.Message)} |");
        sb.AppendLine();

        sb.AppendLine("## Capability Snapshot");
        sb.AppendLine();
        sb.AppendLine("| Capability | Status |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| SCL server model | {YesNo(SclEngineering.Capabilities.HasServerModel)} |");
        sb.AppendLine($"| DataSet engineering | {YesNo(SclEngineering.Capabilities.HasDataSets)} |");
        sb.AppendLine($"| Report engineering | {YesNo(SclEngineering.Capabilities.HasReports)} |");
        sb.AppendLine($"| GOOSE engineering | {YesNo(SclEngineering.Capabilities.HasGoose)} |");
        sb.AppendLine($"| Sampled Values engineering | {YesNo(SclEngineering.Capabilities.HasSampledValues)} |");
        sb.AppendLine($"| Process-bus expected/observed binding | {YesNo(ProcessBusBinding.IsReady)} |");
        sb.AppendLine($"| GOOSE diagnostics | {YesNo(GooseDiagnostics.IsHealthy)} |");
        sb.AppendLine($"| SV diagnostics | {YesNo(SampledValuesDiagnostics.IsHealthy)} |");
        sb.AppendLine($"| Read-only MMS loopback | {YesNo(ReadOnlyMmsLoopback.IsReady)} |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| Severity | Code | Area | Message | Recommendation |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var finding in Findings)
            sb.AppendLine($"| {Escape(finding.Severity)} | `{Escape(finding.Code)}` | {Escape(finding.Area)} | {Escape(finding.Message)} | {Escape(finding.Recommendation)} |");
        if (Findings.Count == 0)
            sb.AppendLine("| Info | `PUBLIC_ALPHA_READY` | alpha | All public-alpha readiness gates passed. | Keep CI/build/test/source-clean gates green before tagging. |");
        sb.AppendLine();

        sb.AppendLine("## Scope Boundary");
        sb.AppendLine();
        sb.AppendLine("- This is a developer-preview readiness gate, not a conformance certificate.");
        sb.AppendLine("- MMS server path is read-only loopback alpha; external full server operation remains a future milestone.");
        sb.AppendLine("- GOOSE/SV observations use deterministic synthetic healthy summaries generated from SCL to validate diagnostic engines without field hardware.");
        sb.AppendLine("- Product applications should consume the engine APIs and evidence schema rather than duplicating protocol logic.");

        return sb.ToString();
    }

    public string ToJson(JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(this, options ?? new JsonSerializerOptions { WriteIndented = true });

    private static string YesNo(bool value) => value ? "yes" : "no";
    private static string Escape(string? value) => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}

public sealed record PublicAlphaReadinessGate
{
    public string Name { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record PublicAlphaReadinessFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class PublicAlphaReadinessProfileBuilder
{
    public async Task<PublicAlphaReadinessProfile> RunAsync(
        PublicAlphaReadinessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PublicAlphaReadinessOptions();
        if (string.IsNullOrWhiteSpace(options.SclPath))
            throw new ArgumentException("SCL path is required.", nameof(options));
        if (!File.Exists(options.SclPath))
            throw new FileNotFoundException("SCL sample file was not found.", options.SclPath);

        var sclEngineering = new SclEngineeringProfileBuilder().Load(options.SclPath);
        var observed = BuildHealthyObservedSummaries(sclEngineering).ToArray();
        var sourceName = Path.GetFileName(options.SclPath);
        var binding = new ExpectedObservedBindingProfileBuilder().Build(sclEngineering, observed, sourceName);
        var goose = new GooseDiagnosticsProfileBuilder().Build(sclEngineering, observed, sourceName);
        var sv = new SampledValuesDiagnosticsProfileBuilder().Build(sclEngineering, observed, sourceName);
        var mms = await new MmsReadOnlyServerLoopbackProfileBuilder().RunAsync(
            new MmsReadOnlyServerLoopbackOptions
            {
                Port = options.Port,
                ProbeTimeoutMilliseconds = options.ProbeTimeoutMilliseconds,
                SimulationSteps = options.SimulationSteps,
                ServerName = options.ServerName,
                AssociationProfileName = options.AssociationProfileName,
                ResponseProfileName = options.ResponseProfileName
            },
            cancellationToken).ConfigureAwait(false);

        var gates = BuildGates(sclEngineering, binding, goose, sv, mms).ToArray();
        var findings = BuildFindings(sclEngineering, binding, goose, sv, mms, gates).ToArray();
        var isReady = gates.All(g => g.IsPass) && !findings.Any(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase));

        return new PublicAlphaReadinessProfile
        {
            SourceName = sourceName,
            IsReady = isReady,
            SclEngineering = sclEngineering,
            ProcessBusBinding = binding,
            GooseDiagnostics = goose,
            SampledValuesDiagnostics = sv,
            ReadOnlyMmsLoopback = mms,
            Gates = gates,
            Findings = findings
        };
    }

    private static IEnumerable<ProcessBusStreamSummary> BuildHealthyObservedSummaries(SclEngineeringProfile profile)
    {
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var goose in profile.ProcessBus.GooseStreams)
        {
            var values = goose.Entries.Count == 0
                ? new[] { "Boolean=False" }
                : goose.Entries.Select((entry, index) => $"[{index}] {entry.SignalReference}=OK").ToArray();
            var summary = new ProcessBusStreamSummary
            {
                Kind = ProcessBusEventKind.Goose,
                AppId = goose.Address.AppId ?? 0,
                Source = "02:00:00:00:AA:01",
                Destination = NormalizeMac(goose.Address.DestinationMacText),
                VlanId = goose.Address.VlanId,
                VlanPriority = goose.Address.VlanPriority,
                StreamId = goose.ControlBlockReference,
                ConfigurationRevision = goose.ConfigurationRevision
            };
            summary.RecordGoose(timestamp, 1, 0, goose.MaxTimeMilliseconds == 0 ? 1000u : goose.MaxTimeMilliseconds, values, Array.Empty<string>(), out _, out _);
            var minDelayMilliseconds = goose.MinTimeMilliseconds == 0 ? 1d : goose.MinTimeMilliseconds;
            summary.RecordGoose(timestamp.AddMilliseconds(minDelayMilliseconds), 1, 1, goose.MaxTimeMilliseconds == 0 ? 1000u : goose.MaxTimeMilliseconds, values, Array.Empty<string>(), out _, out _);
            yield return summary;
        }

        foreach (var sv in profile.ProcessBus.SampledValuesStreams)
        {
            var payloadBytes = SampledValuesPayloadLayout.FromDataSet(sv.Entries).PayloadByteLength;
            var summary = new ProcessBusStreamSummary
            {
                Kind = ProcessBusEventKind.SampledValues,
                AppId = sv.Address.AppId ?? 0,
                Source = "02:00:00:00:AA:02",
                Destination = NormalizeMac(sv.Address.DestinationMacText),
                VlanId = sv.Address.VlanId,
                VlanPriority = sv.Address.VlanPriority,
                StreamId = string.IsNullOrWhiteSpace(sv.SvId) ? sv.ControlBlockReference : sv.SvId,
                ConfigurationRevision = sv.ConfigurationRevision
            };
            var sampleMode = MapSampleMode(sv.SampleMode);
            var noAsdu = sv.NoAsdu == 0 ? 1 : sv.NoAsdu;
            for (ushort i = 0; i < 4; i++)
                summary.RecordSample(i, null, sv.Entries.Count, Array.Empty<string>(), payloadBytes, sv.SampleRate == 0 ? null : sv.SampleRate, sampleMode, 2, noAsdu);
            yield return summary;
        }
    }

    private static IEnumerable<PublicAlphaReadinessGate> BuildGates(
        SclEngineeringProfile scl,
        ExpectedObservedBindingProfile binding,
        GooseDiagnosticsProfile goose,
        SampledValuesDiagnosticsProfile sv,
        MmsReadOnlyServerLoopbackProfile mms)
    {
        yield return Gate("scl-engineering-profile", scl.Ieds.Count > 0 && scl.LogicalDevices.Count > 0 && scl.DataSetCount > 0,
            $"IEDs={scl.Ieds.Count} LD={scl.LogicalDevices.Count} DataSets={scl.DataSetCount} reports={scl.ReportControlCount} GOOSE={scl.GooseStreamCount} SV={scl.SampledValuesStreamCount}");
        yield return Gate("report-engineering", scl.ReportControlCount > 0 && scl.Capabilities.HasReports,
            "SCL sample must expose at least one expected report control for report-readiness workflows.");
        yield return Gate("process-bus-binding", binding.IsReady && binding.BoundGooseCount == binding.ExpectedGooseCount && binding.BoundSampledValuesCount == binding.ExpectedSampledValuesCount,
            $"GOOSE bound={binding.BoundGooseCount}/{binding.ExpectedGooseCount}, SV bound={binding.BoundSampledValuesCount}/{binding.ExpectedSampledValuesCount}, findings={binding.Findings.Count}");
        yield return Gate("goose-diagnostics", goose.IsHealthy && goose.BoundStreamCount == goose.ExpectedStreamCount,
            $"healthy={goose.HealthyStreamCount}/{goose.ExpectedStreamCount}, high={goose.HighCount}, warning={goose.WarningCount}");
        yield return Gate("sampled-values-diagnostics", sv.IsHealthy && sv.BoundStreamCount == sv.ExpectedStreamCount,
            $"healthy={sv.HealthyStreamCount}/{sv.ExpectedStreamCount}, high={sv.HighCount}, warning={sv.WarningCount}");
        yield return Gate("mms-readonly-loopback", mms.IsReady,
            $"requests={mms.RequestCount}, success={mms.ServerSuccessCount}, failure={mms.ServerFailureCount}, writeGuard={mms.ReadOnlyGuardReady.ToString().ToLowerInvariant()}");
        yield return Gate("public-alpha-scope", true,
            "Scope is developer preview / engine alpha with evidence-first, read-only-safe defaults and explicit non-conformance boundary.");
    }

    private static IEnumerable<PublicAlphaReadinessFinding> BuildFindings(
        SclEngineeringProfile scl,
        ExpectedObservedBindingProfile binding,
        GooseDiagnosticsProfile goose,
        SampledValuesDiagnosticsProfile sv,
        MmsReadOnlyServerLoopbackProfile mms,
        IReadOnlyList<PublicAlphaReadinessGate> gates)
    {
        foreach (var gate in gates.Where(g => !g.IsPass))
            yield return Finding("High", "PUBLIC_ALPHA_GATE_FAILED", "alpha", $"Gate failed: {gate.Name}. {gate.Message}", "Fix the failed gate before tagging a public alpha release.");

        foreach (var finding in scl.Findings.Where(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            yield return Finding("High", finding.Code, "scl", finding.Message, "Fix the static SCL engineering issue or use a different release sample.");
        foreach (var finding in binding.Findings.Where(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            yield return Finding("High", finding.Code, "process-bus", finding.Message, "Expected and observed process-bus evidence must match for the alpha readiness baseline.");
        foreach (var finding in goose.Findings.Where(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            yield return Finding("High", finding.Code, "goose", finding.Message, finding.Recommendation);
        foreach (var finding in sv.Findings.Where(f => string.Equals(f.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            yield return Finding("High", finding.Code, "sv", finding.Message, finding.Recommendation);
        foreach (var finding in mms.Findings)
            yield return Finding("Warning", "MMS_LOOPBACK_FINDING", "mms", finding, "Review the MMS loopback readiness profile and keep read-only guard enabled.");
    }

    private static PublicAlphaReadinessGate Gate(string name, bool isPass, string message)
        => new() { Name = name, IsPass = isPass, Message = message };

    private static PublicAlphaReadinessFinding Finding(string severity, string code, string area, string message, string recommendation)
        => new() { Severity = severity, Code = code, Area = area, Message = message, Recommendation = recommendation };

    private static string NormalizeMac(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Replace('-', ':').ToUpperInvariant();
    }

    private static ushort? MapSampleMode(string sampleMode)
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
}
