using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AR.Iec61850.Simulation;

public sealed record MmsReadOnlyListenerSkeletonOptions
{
    public string Host { get; init; } = IPAddress.Loopback.ToString();
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public int MaxRequestBytes { get; init; } = 1_048_576;
    public string ProtocolName { get; init; } = "ARIEC61850 read-only JSON-line probe protocol";
}

public sealed record MmsReadOnlyListenerSkeletonProfile
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Host { get; init; } = IPAddress.Loopback.ToString();
    public int RequestedPort { get; init; }
    public int BoundPort { get; init; }
    public string ProtocolName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public int AcceptedConnectionCount { get; init; }
    public int RequestCount { get; init; }
    public int SuccessfulResponseCount { get; init; }
    public int FailedResponseCount { get; init; }
    public bool WriteGuardVerified { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<MmsReadOnlyListenerProbeStep> ProbeSteps { get; init; } = Array.Empty<MmsReadOnlyListenerProbeStep>();
    public IReadOnlyList<MmsReadOnlyDiagnostic> Diagnostics { get; init; } = Array.Empty<MmsReadOnlyDiagnostic>();

    public bool IsReady => AcceptedConnectionCount > 0
        && RequestCount > 0
        && SuccessfulResponseCount > 0
        && WriteGuardVerified
        && !Diagnostics.Any(x => string.Equals(x.Severity, "High", StringComparison.OrdinalIgnoreCase));

    public string Summary => $"MMS listener skeleton: ready={IsReady.ToString().ToLowerInvariant()} connections={AcceptedConnectionCount} requests={RequestCount} ok={SuccessfulResponseCount} fail={FailedResponseCount} port={BoundPort}";

    public string ToMarkdown()
    {
        var lines = new List<string>
        {
            "# MMS Listener Skeleton Profile",
            string.Empty,
            "This evidence profile validates the read-only listener skeleton using a local TCP loopback probe. It is intentionally not a full MMS PDU listener yet; it exercises transport lifecycle, request dispatch, read-only server semantics, and write rejection before the live MMS decoder is attached.",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | --- |",
            $"| Ready | {IsReady.ToString().ToLowerInvariant()} |",
            $"| Server | {Escape(ServerName)} |",
            $"| Protocol | {Escape(ProtocolName)} |",
            $"| Host | {Escape(Host)} |",
            $"| Requested port | {RequestedPort.ToString(CultureInfo.InvariantCulture)} |",
            $"| Bound port | {BoundPort.ToString(CultureInfo.InvariantCulture)} |",
            $"| Accepted connections | {AcceptedConnectionCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Requests | {RequestCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Successful responses | {SuccessfulResponseCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Failed responses | {FailedResponseCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Write guard verified | {WriteGuardVerified.ToString().ToLowerInvariant()} |",
            $"| Elapsed ms | {Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} |",
            string.Empty,
            "## Probe Steps",
            string.Empty,
            "| Status | Operation | Target | Server success | Message |",
            "| --- | --- | --- | --- | --- |"
        };

        foreach (var step in ProbeSteps)
            lines.Add($"| {(step.IsTransportSuccess ? "OK" : "FAIL")} | {Escape(step.Operation)} | {Escape(step.Target)} | {step.IsServerSuccess.ToString().ToLowerInvariant()} | {Escape(step.Message)} |");

        lines.Add(string.Empty);
        lines.Add("## Diagnostics");
        lines.Add(string.Empty);
        if (Diagnostics.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            foreach (var diagnostic in Diagnostics)
                lines.Add($"- {Escape(diagnostic.Severity)} {Escape(diagnostic.Code)}: {Escape(diagnostic.Message)}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|");
}

public sealed record MmsReadOnlyListenerProbeStep
{
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool IsTransportSuccess { get; init; }
    public bool IsServerSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReadOnlyListenerSkeleton
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly MmsReadOnlyServerSession _session;

    public MmsReadOnlyListenerSkeleton(MmsReadOnlyServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        _session = new MmsReadOnlyServerSession(profile);
    }

    public MmsReadOnlyServerProfile Profile { get; }

    public async Task<MmsReadOnlyListenerSkeletonProfile> RunSelfProbeAsync(MmsReadOnlyListenerSkeletonOptions? options = null, IReadOnlyList<MmsReadOnlyServerRequest>? requests = null, CancellationToken cancellationToken = default)
    {
        options ??= new MmsReadOnlyListenerSkeletonOptions();
        requests ??= CreateDefaultProbeRequests(Profile);

        var diagnostics = new List<MmsReadOnlyDiagnostic>();
        var probeSteps = new List<MmsReadOnlyListenerProbeStep>();
        var acceptedConnections = 0;
        var successfulResponses = 0;
        var failedResponses = 0;
        var writeGuardVerified = false;
        var timer = Stopwatch.StartNew();

        var host = ParseHost(options.Host);
        var listener = new TcpListener(host, options.Port);
        listener.Start();
        var boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(options.ProbeTimeoutMilliseconds));

        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var serverClient = await listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
                acceptedConnections++;
                await ServeClientAsync(serverClient, options, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                diagnostics.Add(new MmsReadOnlyDiagnostic { Severity = "High", Code = "LISTENER_TIMEOUT", Message = "Listener self-probe timed out while waiting for a client or request." });
            }
            catch (Exception ex)
            {
                diagnostics.Add(new MmsReadOnlyDiagnostic { Severity = "High", Code = "LISTENER_ERROR", Message = ex.Message });
            }
        }, timeoutCts.Token);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, boundPort, timeoutCts.Token).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            foreach (var request in requests)
            {
                var requestJson = JsonSerializer.Serialize(request, JsonOptions);
                await writer.WriteLineAsync(requestJson).ConfigureAwait(false);

                var responseJson = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    failedResponses++;
                    probeSteps.Add(new MmsReadOnlyListenerProbeStep
                    {
                        Operation = request.Operation.ToString(),
                        Target = request.Target,
                        IsTransportSuccess = false,
                        IsServerSuccess = false,
                        Message = "No response received from listener."
                    });
                    continue;
                }

                var response = JsonSerializer.Deserialize<MmsReadOnlyServerResponse>(responseJson, JsonOptions);
                if (response is null)
                {
                    failedResponses++;
                    probeSteps.Add(new MmsReadOnlyListenerProbeStep
                    {
                        Operation = request.Operation.ToString(),
                        Target = request.Target,
                        IsTransportSuccess = false,
                        IsServerSuccess = false,
                        Message = "Response could not be decoded."
                    });
                    continue;
                }

                if (response.IsSuccess)
                    successfulResponses++;
                else
                    failedResponses++;

                if (request.Operation == MmsReadOnlyOperation.Write && !response.IsSuccess && response.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                    writeGuardVerified = true;

                probeSteps.Add(new MmsReadOnlyListenerProbeStep
                {
                    Operation = request.Operation.ToString(),
                    Target = request.Target,
                    IsTransportSuccess = true,
                    IsServerSuccess = response.IsSuccess,
                    Message = response.Message
                });
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new MmsReadOnlyDiagnostic { Severity = "High", Code = "PROBE_TIMEOUT", Message = "Client self-probe timed out." });
        }
        catch (Exception ex)
        {
            diagnostics.Add(new MmsReadOnlyDiagnostic { Severity = "High", Code = "PROBE_ERROR", Message = ex.Message });
        }
        finally
        {
            listener.Stop();
        }

        try
        {
            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The diagnostic was already recorded by the server task or the probe path.
        }

        timer.Stop();

        if (!writeGuardVerified)
            diagnostics.Add(new MmsReadOnlyDiagnostic { Severity = "High", Code = "WRITE_GUARD_NOT_VERIFIED", Message = "The loopback probe did not confirm read-only write rejection." });

        return new MmsReadOnlyListenerSkeletonProfile
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Host = host.ToString(),
            RequestedPort = options.Port,
            BoundPort = boundPort,
            ProtocolName = options.ProtocolName,
            ServerName = Profile.ServerName,
            AcceptedConnectionCount = acceptedConnections,
            RequestCount = requests.Count,
            SuccessfulResponseCount = successfulResponses,
            FailedResponseCount = failedResponses,
            WriteGuardVerified = writeGuardVerified,
            Elapsed = timer.Elapsed,
            ProbeSteps = probeSteps.ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
    }

    private async Task ServeClientAsync(TcpClient client, MmsReadOnlyListenerSkeletonOptions options, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;

            MmsReadOnlyServerResponse response;
            if (line.Length > options.MaxRequestBytes)
            {
                response = new MmsReadOnlyServerResponse
                {
                    IsSuccess = false,
                    Operation = "Decode",
                    Target = string.Empty,
                    Message = "Request rejected because it exceeds the configured maximum request size."
                };
            }
            else
            {
                response = DecodeAndHandle(line);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
        }
    }

    private MmsReadOnlyServerResponse DecodeAndHandle(string json)
    {
        try
        {
            var request = JsonSerializer.Deserialize<MmsReadOnlyServerRequest>(json, JsonOptions);
            return request is null
                ? DecodeFailure("Request JSON decoded to null.")
                : _session.Handle(request);
        }
        catch (JsonException ex)
        {
            return DecodeFailure(ex.Message);
        }
    }

    private static MmsReadOnlyServerResponse DecodeFailure(string message)
        => new()
        {
            IsSuccess = false,
            Operation = "Decode",
            Target = string.Empty,
            Message = $"Invalid listener request: {message}"
        };

    private static IPAddress ParseHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return IPAddress.Loopback;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        return IPAddress.TryParse(host, out var address)
            ? address
            : IPAddress.Loopback;
    }

    private static IReadOnlyList<MmsReadOnlyServerRequest> CreateDefaultProbeRequests(MmsReadOnlyServerProfile profile)
    {
        var firstDevice = profile.LogicalDevices.FirstOrDefault()?.Name ?? string.Empty;
        var firstPoint = profile.Points.FirstOrDefault()?.Reference ?? string.Empty;
        var firstDataSet = profile.DataSets.FirstOrDefault()?.Reference ?? string.Empty;

        var requests = new List<MmsReadOnlyServerRequest>
        {
            new() { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory }
        };

        if (!string.IsNullOrWhiteSpace(firstDevice))
            requests.Add(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalNodeDirectory, Target = firstDevice });
        if (!string.IsNullOrWhiteSpace(firstPoint))
            requests.Add(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = firstPoint });
        if (!string.IsNullOrWhiteSpace(firstDataSet))
            requests.Add(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = firstDataSet });
        if (!string.IsNullOrWhiteSpace(firstPoint))
            requests.Add(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = firstPoint, Value = "test" });

        return requests.ToArray();
    }
}
