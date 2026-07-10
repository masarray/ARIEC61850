using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AR.Iec61850.Acse;
using AR.Iec61850.Diagnostics;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public sealed class MmsConfirmedRequestSkeletonOptions
{
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public ushort ServerReference { get; init; } = 0x1001;
    public string AssociationProfileName { get; init; } = "BalancedApTitle";
    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";
    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";
    public int SimulationSteps { get; init; }
}

public sealed class MmsConfirmedRequestSkeletonProfile
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsReady { get; init; }
    public int BoundPort { get; init; }
    public int AcceptedConnectionCount { get; init; }
    public string AssociationProfileName { get; init; } = string.Empty;
    public string ResponseProfileName { get; init; } = string.Empty;
    public bool TpktExchangeVerified { get; init; }
    public bool CotpConnectionConfirmed { get; init; }
    public bool ClientAssociateRequestObserved { get; init; }
    public bool ServerAssociateResponseSent { get; init; }
    public bool ClientAssociateResponseAccepted { get; init; }
    public bool ConfirmedRequestObserved { get; init; }
    public bool ConfirmedResponseSent { get; init; }
    public bool ConfirmedResponseAccepted { get; init; }
    public bool ReadOnlyDispatchVerified { get; init; }
    public bool WriteGuardVerified { get; init; }
    public int RequestCount { get; init; }
    public int SuccessfulResponseCount { get; init; }
    public int FailedResponseCount { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<MmsConfirmedRequestSkeletonStep> Steps { get; init; } = Array.Empty<MmsConfirmedRequestSkeletonStep>();
    public IReadOnlyList<MmsConfirmedRequestProbeResult> ProbeResults { get; init; } = Array.Empty<MmsConfirmedRequestProbeResult>();
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public string Summary => $"MMS confirmed-request skeleton: ready={IsReady.ToString().ToLowerInvariant()} connections={AcceptedConnectionCount} requests={RequestCount} ok={SuccessfulResponseCount} guarded={WriteGuardVerified.ToString().ToLowerInvariant()} port={BoundPort}";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MMS Confirmed Request Skeleton Profile");
        sb.AppendLine();
        sb.AppendLine("This evidence profile validates the first live read-only confirmed-request path over TCP, TPKT, COTP, and an ACSE/MMS association response. The confirmed requests are clean-room skeleton envelopes carried in COTP Data TPDU frames; this milestone proves session lifecycle and service dispatch before full MMS ConfirmedRequest BER decoding is attached.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Ready | {IsReady.ToString().ToLowerInvariant()} |");
        sb.AppendLine($"| Bound port | {BoundPort.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Accepted connections | {AcceptedConnectionCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Association profile | {Escape(AssociationProfileName)} |");
        sb.AppendLine($"| Response profile | {Escape(ResponseProfileName)} |");
        sb.AppendLine($"| Requests | {RequestCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Successful responses | {SuccessfulResponseCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Failed responses | {FailedResponseCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Write guard verified | {WriteGuardVerified.ToString().ToLowerInvariant()} |");
        sb.AppendLine($"| Elapsed ms | {Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} |");
        sb.AppendLine();
        sb.AppendLine("## Gates");
        sb.AppendLine();
        sb.AppendLine($"- TPKT exchange verified: **{TpktExchangeVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- COTP connection confirmed: **{CotpConnectionConfirmed.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Client associate request observed: **{ClientAssociateRequestObserved.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Server associate response sent: **{ServerAssociateResponseSent.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Client accepted associate response: **{ClientAssociateResponseAccepted.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Confirmed request observed: **{ConfirmedRequestObserved.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Confirmed response sent: **{ConfirmedResponseSent.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Confirmed response accepted: **{ConfirmedResponseAccepted.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Read-only dispatch verified: **{ReadOnlyDispatchVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Write guard verified: **{WriteGuardVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine();
        sb.AppendLine("## Probe Results");
        sb.AppendLine();
        sb.AppendLine("| Status | Operation | Target | Server success | Message |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var result in ProbeResults)
            sb.AppendLine($"| {(result.IsTransportSuccess ? "OK" : "FAIL")} | {Escape(result.Operation)} | {Escape(result.Target)} | {result.IsServerSuccess.ToString().ToLowerInvariant()} | {Escape(result.Message)} |");
        sb.AppendLine();
        sb.AppendLine("## Loopback Steps");
        sb.AppendLine();
        sb.AppendLine("| Step | Side | Layer | Result | Message |");
        sb.AppendLine("| ---: | --- | --- | --- | --- |");
        foreach (var step in Steps.OrderBy(x => x.Index))
            sb.AppendLine($"| {step.Index} | {Escape(step.Side)} | {Escape(step.Layer)} | {(step.IsPass ? "PASS" : "FAIL")} | {Escape(step.Message)} |");
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (Findings.Count == 0)
        {
            sb.AppendLine("- No blocking finding from the confirmed-request skeleton profile.");
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

public sealed class MmsConfirmedRequestSkeletonStep
{
    public int Index { get; init; }
    public string Side { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
    public string HexPreview { get; init; } = string.Empty;
}

public sealed class MmsConfirmedRequestProbeResult
{
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool IsTransportSuccess { get; init; }
    public bool IsServerSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsConfirmedRequestSkeletonProfileBuilder
{
    public async Task<MmsConfirmedRequestSkeletonProfile> RunLoopbackProbeAsync(
        MmsConfirmedRequestSkeletonOptions? options = null,
        IReadOnlyList<MmsReadOnlyServerRequest>? requests = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MmsConfirmedRequestSkeletonOptions();
        if (options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "TCP port must be 0..65535.");

        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var engine = new IedSimulatorEngine(simulatorProfile);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < Math.Max(0, options.SimulationSteps); i++)
            engine.Step(now.AddMilliseconds(i * 20));

        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(
            simulatorProfile,
            engine.CreateSnapshot(DateTimeOffset.UtcNow),
            new MmsReadOnlyServerProfileOptions
            {
                ServerName = options.ServerName,
                Port = options.Port == 0 ? 102 : options.Port,
                IncludeSelfTest = true
            });

        requests ??= CreateDefaultProbeRequests(serverProfile);
        var serverSession = new MmsReadOnlyServerSession(serverProfile);
        var responseProfile = AcseMmsAssociateResponse.Select(options.ResponseProfileName);
        var associationPayload = SelectAssociationPayload(options.AssociationProfileName);

        var steps = new List<MmsConfirmedRequestSkeletonStep>();
        var probeResults = new List<MmsConfirmedRequestProbeResult>();
        var findings = new List<string>();
        var sync = new object();
        var stepIndex = 0;
        var acceptedConnections = 0;
        var tpktExchangeVerified = false;
        var cotpConnectionConfirmed = false;
        var clientAssociateRequestObserved = false;
        var serverAssociateResponseSent = false;
        var clientAssociateResponseAccepted = false;
        var confirmedRequestObserved = false;
        var confirmedResponseSent = false;
        var confirmedResponseAccepted = false;
        var readOnlyDispatchVerified = false;
        var writeGuardVerified = false;
        var successfulResponses = 0;
        var failedResponses = 0;
        var timer = Stopwatch.StartNew();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.ProbeTimeoutMilliseconds <= 0 ? 5000 : options.ProbeTimeoutMilliseconds);

        var listener = new TcpListener(IPAddress.Loopback, options.Port);
        listener.Start(backlog: 1);
        var boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = RunServerAsync();
            var clientTask = RunClientAsync();
            await Task.WhenAll(serverTask, clientTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (sync)
                findings.Add($"Loopback confirmed-request skeleton probe failed: {ex.Message}");
        }
        finally
        {
            timer.Stop();
            listener.Stop();
        }

        if (!writeGuardVerified)
            findings.Add("Confirmed-request probe did not verify the read-only write guard.");

        var isReady = findings.Count == 0
            && acceptedConnections == 1
            && tpktExchangeVerified
            && cotpConnectionConfirmed
            && clientAssociateRequestObserved
            && serverAssociateResponseSent
            && clientAssociateResponseAccepted
            && confirmedRequestObserved
            && confirmedResponseSent
            && confirmedResponseAccepted
            && readOnlyDispatchVerified
            && writeGuardVerified
            && successfulResponses >= 4
            && failedResponses >= 1;

        return new MmsConfirmedRequestSkeletonProfile
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsReady = isReady,
            BoundPort = boundPort,
            AcceptedConnectionCount = acceptedConnections,
            AssociationProfileName = associationPayload.Name,
            ResponseProfileName = responseProfile.Name,
            TpktExchangeVerified = tpktExchangeVerified,
            CotpConnectionConfirmed = cotpConnectionConfirmed,
            ClientAssociateRequestObserved = clientAssociateRequestObserved,
            ServerAssociateResponseSent = serverAssociateResponseSent,
            ClientAssociateResponseAccepted = clientAssociateResponseAccepted,
            ConfirmedRequestObserved = confirmedRequestObserved,
            ConfirmedResponseSent = confirmedResponseSent,
            ConfirmedResponseAccepted = confirmedResponseAccepted,
            ReadOnlyDispatchVerified = readOnlyDispatchVerified,
            WriteGuardVerified = writeGuardVerified,
            RequestCount = requests.Count,
            SuccessfulResponseCount = successfulResponses,
            FailedResponseCount = failedResponses,
            Elapsed = timer.Elapsed,
            Steps = steps.OrderBy(x => x.Index).ToArray(),
            ProbeResults = probeResults.ToArray(),
            Findings = findings.ToArray()
        };

        async Task RunServerAsync()
        {
            using var tcpClient = await listener.AcceptTcpClientAsync(timeoutSource.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref acceptedConnections, 1);
            await using var stream = tcpClient.GetStream();

            var crFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var crTpkt = TpktFrameCodec.Decode(crFrame);
            AddStep("server", "TPKT-RECV-CR", crTpkt.IsValid, crTpkt.Message, crFrame);
            if (!crTpkt.IsValid)
            {
                AddFinding($"Server could not decode client TPKT connect frame: {crTpkt.Message}");
                return;
            }

            var cr = CotpFrameCodec.Decode(crTpkt.Payload);
            var crPass = cr.IsValid && cr.Kind == CotpTpduKind.ConnectionRequest;
            AddStep("server", "COTP-RECV-CR", crPass, cr.Message, crTpkt.Payload);
            if (!crPass)
            {
                AddFinding($"Server expected COTP CR but received {cr.Kind}: {cr.Message}");
                return;
            }

            var ccPayload = CotpFrameCodec.EncodeConnectionConfirm(cr, options.ServerReference);
            var ccFrame = TpktFrameCodec.Encode(ccPayload);
            await stream.WriteAsync(ccFrame, timeoutSource.Token).ConfigureAwait(false);
            AddStep("server", "COTP-SEND-CC", true, $"Sent COTP Connection Confirm dstRef=0x{cr.SourceReference:X4} srcRef=0x{options.ServerReference:X4}.", ccFrame);

            var aarqFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var aarqTpkt = TpktFrameCodec.Decode(aarqFrame);
            AddStep("server", "TPKT-RECV-AARQ", aarqTpkt.IsValid, aarqTpkt.Message, aarqFrame);
            if (!aarqTpkt.IsValid)
            {
                AddFinding($"Server could not decode client TPKT AARQ frame: {aarqTpkt.Message}");
                return;
            }

            var aarqData = CotpFrameCodec.Decode(aarqTpkt.Payload);
            var aarqDataPass = aarqData.IsValid && aarqData.Kind == CotpTpduKind.Data && aarqData.EndOfTransmission;
            AddStep("server", "COTP-RECV-AARQ", aarqDataPass, aarqData.Message, aarqTpkt.Payload);
            if (!aarqDataPass)
            {
                AddFinding($"Server expected COTP Data TPDU carrying AARQ payload: {aarqData.Message}");
                return;
            }

            var requestInspection = AcseAssociationPayloadInspector.Inspect(aarqData.UserData);
            var requestPass = requestInspection.LooksLikeClientAssociateRequest && requestInspection.HasUserInformation;
            if (requestPass)
                clientAssociateRequestObserved = true;
            AddStep("server", "ACSE-INSPECT-AARQ", requestPass, requestInspection.Message, aarqData.UserData);
            if (!requestPass)
            {
                AddFinding("Server received COTP Data TPDU, but the payload does not look like an ACSE AARQ associate request.");
                return;
            }

            var responseDataPayload = CotpFrameCodec.EncodeData(responseProfile.Payload);
            var responseDataFrame = TpktFrameCodec.Encode(responseDataPayload);
            await stream.WriteAsync(responseDataFrame, timeoutSource.Token).ConfigureAwait(false);
            serverAssociateResponseSent = true;
            AddStep("server", "ACSE-SEND-AARE", true, $"Sent {responseProfile.Name} response profile wrapped in COTP Data TPDU.", responseDataFrame);

            foreach (var _ in requests)
            {
                var requestFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
                var requestTpkt = TpktFrameCodec.Decode(requestFrame);
                AddStep("server", "TPKT-RECV-CONFIRMED-REQUEST", requestTpkt.IsValid, requestTpkt.Message, requestFrame);
                if (!requestTpkt.IsValid)
                {
                    AddFinding($"Server could not decode TPKT confirmed-request frame: {requestTpkt.Message}");
                    return;
                }

                var requestData = CotpFrameCodec.Decode(requestTpkt.Payload);
                var requestDataPass = requestData.IsValid && requestData.Kind == CotpTpduKind.Data && requestData.EndOfTransmission;
                AddStep("server", "COTP-RECV-CONFIRMED-REQUEST", requestDataPass, requestData.Message, requestTpkt.Payload);
                if (!requestDataPass)
                {
                    AddFinding($"Server expected COTP Data TPDU carrying confirmed-request skeleton: {requestData.Message}");
                    return;
                }

                if (!MmsConfirmedRequestEnvelopeCodec.TryDecodeRequest(requestData.UserData, out var request, out var decodeMessage))
                {
                    AddStep("server", "MMS-DECODE-CONFIRMED-REQUEST", false, decodeMessage, requestData.UserData);
                    AddFinding(decodeMessage);
                    return;
                }

                confirmedRequestObserved = true;
                AddStep("server", "MMS-DECODE-CONFIRMED-REQUEST", true, $"Decoded skeleton confirmed request {request.Operation} target={request.Target}.", requestData.UserData);

                var serverResponse = serverSession.Handle(request);
                if (serverResponse.IsSuccess)
                    readOnlyDispatchVerified = true;
                if (request.Operation == MmsReadOnlyOperation.Write && !serverResponse.IsSuccess && serverResponse.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                    writeGuardVerified = true;

                var encodedResponse = MmsConfirmedRequestEnvelopeCodec.EncodeResponse(serverResponse);
                var responseFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(encodedResponse));
                await stream.WriteAsync(responseFrame, timeoutSource.Token).ConfigureAwait(false);
                confirmedResponseSent = true;
                AddStep("server", "MMS-SEND-CONFIRMED-RESPONSE", true, serverResponse.Summary, responseFrame);
            }
        }

        async Task RunClientAsync()
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, boundPort, timeoutSource.Token).ConfigureAwait(false);
            await using var stream = tcpClient.GetStream();

            var crFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeDefaultConnectRequest());
            await stream.WriteAsync(crFrame, timeoutSource.Token).ConfigureAwait(false);
            AddStep("client", "COTP-SEND-CR", true, "Sent default COTP Connection Request wrapped in TPKT.", crFrame);

            var ccFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var ccTpkt = TpktFrameCodec.Decode(ccFrame);
            AddStep("client", "TPKT-RECV-CC", ccTpkt.IsValid, ccTpkt.Message, ccFrame);
            if (!ccTpkt.IsValid)
            {
                AddFinding($"Client could not decode server TPKT connection confirm: {ccTpkt.Message}");
                return;
            }

            var cc = CotpFrameCodec.Decode(ccTpkt.Payload);
            var ccPass = cc.IsValid && cc.Kind == CotpTpduKind.ConnectionConfirm;
            if (ccPass)
            {
                tpktExchangeVerified = true;
                cotpConnectionConfirmed = true;
            }
            AddStep("client", "COTP-RECV-CC", ccPass, cc.Message, ccTpkt.Payload);
            if (!ccPass)
            {
                AddFinding($"Client expected COTP CC but received {cc.Kind}: {cc.Message}");
                return;
            }

            var aarqFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(associationPayload.Payload));
            await stream.WriteAsync(aarqFrame, timeoutSource.Token).ConfigureAwait(false);
            AddStep("client", "ACSE-SEND-AARQ", true, $"Sent {associationPayload.Name} association payload wrapped in COTP Data TPDU.", aarqFrame);

            var aareFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var aareTpkt = TpktFrameCodec.Decode(aareFrame);
            AddStep("client", "TPKT-RECV-AARE", aareTpkt.IsValid, aareTpkt.Message, aareFrame);
            if (!aareTpkt.IsValid)
            {
                AddFinding($"Client could not decode server TPKT AARE frame: {aareTpkt.Message}");
                return;
            }

            var aareData = CotpFrameCodec.Decode(aareTpkt.Payload);
            var aareDataPass = aareData.IsValid && aareData.Kind == CotpTpduKind.Data && aareData.EndOfTransmission;
            AddStep("client", "COTP-RECV-AARE", aareDataPass, aareData.Message, aareTpkt.Payload);
            if (!aareDataPass)
            {
                AddFinding($"Client expected COTP Data TPDU with server AARE: {aareData.Message}");
                return;
            }

            var responseInspection = AcseAssociationPayloadInspector.Inspect(aareData.UserData);
            var responsePass = responseInspection.LooksLikeServerAssociateResponse && responseInspection.HasUserInformation && responseInspection.HasMmsInitiateResponseMarker;
            if (responsePass)
                clientAssociateResponseAccepted = true;
            AddStep("client", "ACSE-INSPECT-AARE", responsePass, responseInspection.Message, aareData.UserData);
            if (!responsePass)
            {
                AddFinding("Client received response data but payload does not look like an ACSE AARE + MMS InitiateResponse.");
                return;
            }

            foreach (var request in requests)
            {
                var encodedRequest = MmsConfirmedRequestEnvelopeCodec.EncodeRequest(request);
                var requestFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(encodedRequest));
                await stream.WriteAsync(requestFrame, timeoutSource.Token).ConfigureAwait(false);
                AddStep("client", "MMS-SEND-CONFIRMED-REQUEST", true, $"Sent skeleton confirmed request {request.Operation} target={request.Target}.", requestFrame);

                var responseFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
                var responseTpkt = TpktFrameCodec.Decode(responseFrame);
                AddStep("client", "TPKT-RECV-CONFIRMED-RESPONSE", responseTpkt.IsValid, responseTpkt.Message, responseFrame);
                if (!responseTpkt.IsValid)
                {
                    AddFinding($"Client could not decode TPKT confirmed-response frame: {responseTpkt.Message}");
                    return;
                }

                var responseData = CotpFrameCodec.Decode(responseTpkt.Payload);
                var responseDataPass = responseData.IsValid && responseData.Kind == CotpTpduKind.Data && responseData.EndOfTransmission;
                AddStep("client", "COTP-RECV-CONFIRMED-RESPONSE", responseDataPass, responseData.Message, responseTpkt.Payload);
                if (!responseDataPass)
                {
                    AddFinding($"Client expected COTP Data TPDU carrying confirmed-response skeleton: {responseData.Message}");
                    return;
                }

                if (!MmsConfirmedRequestEnvelopeCodec.TryDecodeResponse(responseData.UserData, out var serverResponse, out var decodeMessage))
                {
                    AddStep("client", "MMS-DECODE-CONFIRMED-RESPONSE", false, decodeMessage, responseData.UserData);
                    AddFinding(decodeMessage);
                    return;
                }

                confirmedResponseAccepted = true;
                if (serverResponse.IsSuccess)
                    successfulResponses++;
                else
                    failedResponses++;

                lock (sync)
                {
                    probeResults.Add(new MmsConfirmedRequestProbeResult
                    {
                        Operation = serverResponse.Operation,
                        Target = serverResponse.Target,
                        IsTransportSuccess = true,
                        IsServerSuccess = serverResponse.IsSuccess,
                        Message = serverResponse.Message
                    });
                }

                AddStep("client", "MMS-DECODE-CONFIRMED-RESPONSE", true, serverResponse.Summary, responseData.UserData);
            }
        }

        void AddStep(string side, string layer, bool pass, string message, byte[] bytes)
        {
            var item = new MmsConfirmedRequestSkeletonStep
            {
                Index = Interlocked.Increment(ref stepIndex),
                Side = side,
                Layer = layer,
                IsPass = pass,
                Message = message,
                HexPreview = HexDump.ToCompactString(bytes)
            };
            lock (sync)
                steps.Add(item);
        }

        void AddFinding(string finding)
        {
            lock (sync)
                findings.Add(finding);
        }
    }

    private static IReadOnlyList<MmsReadOnlyServerRequest> CreateDefaultProbeRequests(MmsReadOnlyServerProfile profile)
    {
        var firstDevice = profile.LogicalDevices.FirstOrDefault()?.Name ?? "IED1LD0";
        var firstPoint = profile.Points.FirstOrDefault()?.Reference ?? "IED1LD0/XCBR1.Pos.stVal";
        var firstDataSet = profile.DataSets.FirstOrDefault()?.Reference ?? "IED1LD0/LLN0.dsStatus";

        return
        [
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalNodeDirectory, Target = firstDevice },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = firstPoint },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = firstDataSet },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = firstPoint, Value = "closed" }
        ];
    }

    private static AcseAssociationProfile SelectAssociationPayload(string? name)
    {
        var profiles = AcseMmsInitiateRequest.BuildAssociationProfiles();
        if (string.IsNullOrWhiteSpace(name))
            return profiles[0];

        return profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? profiles[0];
    }

    private static async Task<byte[]> ReadTpktFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false);
        var declaredLength = (header[2] << 8) | header[3];
        if (declaredLength < TpktFrameCodec.HeaderLength)
            throw new InvalidDataException($"Invalid TPKT declared length {declaredLength}.");

        var payload = await ReadExactAsync(stream, declaredLength - TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false);
        var frame = new byte[declaredLength];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Remote OSI peer closed the TCP connection.");
            offset += read;
        }

        return buffer;
    }
}

public static class MmsConfirmedRequestEnvelopeCodec
{
    private const string RequestMarker = "ARIEC61850-MMS-CONFIRMED-REQUEST/1";
    private const string ResponseMarker = "ARIEC61850-MMS-CONFIRMED-RESPONSE/1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static byte[] EncodeRequest(MmsReadOnlyServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encode(RequestMarker, JsonSerializer.Serialize(request, JsonOptions));
    }

    public static byte[] EncodeResponse(MmsReadOnlyServerResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Encode(ResponseMarker, JsonSerializer.Serialize(response, JsonOptions));
    }

    public static bool TryDecodeRequest(ReadOnlySpan<byte> payload, out MmsReadOnlyServerRequest request, out string message)
    {
        request = new MmsReadOnlyServerRequest();
        if (!TryDecode(payload, RequestMarker, out var json, out message))
            return false;

        try
        {
            request = JsonSerializer.Deserialize<MmsReadOnlyServerRequest>(json, JsonOptions) ?? new MmsReadOnlyServerRequest();
            message = $"Decoded skeleton confirmed request {request.Operation} target={request.Target}.";
            return true;
        }
        catch (JsonException ex)
        {
            message = $"Confirmed-request JSON payload could not be decoded: {ex.Message}";
            return false;
        }
    }

    public static bool TryDecodeResponse(ReadOnlySpan<byte> payload, out MmsReadOnlyServerResponse response, out string message)
    {
        response = new MmsReadOnlyServerResponse();
        if (!TryDecode(payload, ResponseMarker, out var json, out message))
            return false;

        try
        {
            response = JsonSerializer.Deserialize<MmsReadOnlyServerResponse>(json, JsonOptions) ?? new MmsReadOnlyServerResponse();
            message = $"Decoded skeleton confirmed response {response.Operation} target={response.Target} success={response.IsSuccess}.";
            return true;
        }
        catch (JsonException ex)
        {
            message = $"Confirmed-response JSON payload could not be decoded: {ex.Message}";
            return false;
        }
    }

    private static byte[] Encode(string marker, string json)
        => Encoding.UTF8.GetBytes(marker + "\n" + json);

    private static bool TryDecode(ReadOnlySpan<byte> payload, string expectedMarker, out string json, out string message)
    {
        json = string.Empty;
        var text = Encoding.UTF8.GetString(payload);
        var lineBreak = text.IndexOf('\n', StringComparison.Ordinal);
        if (lineBreak <= 0)
        {
            message = "Skeleton confirmed-request payload is missing the envelope marker line.";
            return false;
        }

        var marker = text[..lineBreak].Trim();
        if (!string.Equals(marker, expectedMarker, StringComparison.Ordinal))
        {
            message = $"Unexpected skeleton envelope marker '{marker}'. Expected '{expectedMarker}'.";
            return false;
        }

        json = text[(lineBreak + 1)..];
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "Skeleton envelope JSON payload is empty.";
            return false;
        }

        message = "Skeleton envelope decoded.";
        return true;
    }
}
