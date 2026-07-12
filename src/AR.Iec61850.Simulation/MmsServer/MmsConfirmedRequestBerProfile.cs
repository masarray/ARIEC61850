using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AR.Iec61850.Acse;
using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;
using AR.Iec61850.Mms;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public enum MmsConfirmedBerProbeKind
{
    GetDomainDirectory,
    GetNamedVariableDirectory,
    GetNamedVariableListDirectory,
    GetFileDirectory,
    Read,
    GetVariableAccessAttributes,
    GetNamedVariableListAttributes,
    Write
}

public sealed record MmsConfirmedBerProbe
{
    public MmsConfirmedBerProbeKind Kind { get; init; }
    public int InvokeId { get; init; }
    public string Target { get; init; } = string.Empty;
    public byte[] PresentationPayload { get; init; } = Array.Empty<byte>();
}

public sealed class MmsConfirmedRequestBerOptions
{
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public ushort ServerReference { get; init; } = 0x1001;
    public string AssociationProfileName { get; init; } = "BalancedApTitle";
    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";
    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";
    public int SimulationSteps { get; init; }
}

public sealed class MmsConfirmedRequestBerProfile
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
    public bool NativeBerRequestDecoded { get; init; }
    public bool NativeBerResponseEncoded { get; init; }
    public bool ClientNativeResponseDecoded { get; init; }
    public bool DirectoryDispatchVerified { get; init; }
    public bool ReadDispatchVerified { get; init; }
    public bool DataSetDirectoryDispatchVerified { get; init; }
    public bool WriteGuardVerified { get; init; }
    public int RequestCount { get; init; }
    public int ServerSuccessCount { get; init; }
    public int ServerFailureCount { get; init; }
    public int ClientDecodeSuccessCount { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<MmsConfirmedRequestBerStep> Steps { get; init; } = Array.Empty<MmsConfirmedRequestBerStep>();
    public IReadOnlyList<MmsConfirmedRequestBerProbeResult> ProbeResults { get; init; } = Array.Empty<MmsConfirmedRequestBerProbeResult>();
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public string Summary => $"MMS BER confirmed-request dispatch: ready={IsReady.ToString().ToLowerInvariant()} connections={AcceptedConnectionCount} requests={RequestCount} serverOk={ServerSuccessCount} clientDecoded={ClientDecodeSuccessCount} guarded={WriteGuardVerified.ToString().ToLowerInvariant()} port={BoundPort}";

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MMS Confirmed Request BER Dispatch Profile");
        sb.AppendLine();
        sb.AppendLine("This evidence profile validates the first read-only confirmed-request dispatch path using native MMS BER request payloads carried in COTP Data TPDU frames after a loopback TPKT/COTP/ACSE association response. It exercises GetNameList, Read, GetNamedVariableListAttributes, and Write rejection without claiming a complete MMS server implementation.");
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
        sb.AppendLine($"| Server success | {ServerSuccessCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Server failure | {ServerFailureCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Client decoded responses | {ClientDecodeSuccessCount.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Elapsed ms | {Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} |");
        sb.AppendLine();
        sb.AppendLine("## Gates");
        sb.AppendLine();
        sb.AppendLine($"- TPKT exchange verified: **{TpktExchangeVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- COTP connection confirmed: **{CotpConnectionConfirmed.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Client associate request observed: **{ClientAssociateRequestObserved.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Server associate response sent: **{ServerAssociateResponseSent.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Client accepted associate response: **{ClientAssociateResponseAccepted.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Native BER request decoded: **{NativeBerRequestDecoded.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Native BER response encoded: **{NativeBerResponseEncoded.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Client native response decoded: **{ClientNativeResponseDecoded.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Directory dispatch verified: **{DirectoryDispatchVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Read dispatch verified: **{ReadDispatchVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- DataSet directory dispatch verified: **{DataSetDirectoryDispatchVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Write guard verified: **{WriteGuardVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine();
        sb.AppendLine("## Probe Results");
        sb.AppendLine();
        sb.AppendLine("| Status | Kind | InvokeID | Target | Decoded operation | Server success | Client decode | Message |");
        sb.AppendLine("| --- | --- | ---: | --- | --- | --- | --- | --- |");
        foreach (var result in ProbeResults)
            sb.AppendLine($"| {(result.IsTransportSuccess ? "OK" : "FAIL")} | {Escape(result.Kind)} | {result.InvokeId.ToString(CultureInfo.InvariantCulture)} | {Escape(result.Target)} | {Escape(result.DecodedOperation)} | {result.IsServerSuccess.ToString().ToLowerInvariant()} | {result.IsClientDecodeSuccess.ToString().ToLowerInvariant()} | {Escape(result.Message)} |");
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
            sb.AppendLine("- No blocking finding from the MMS BER confirmed-request dispatch profile.");
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

public sealed class MmsConfirmedRequestBerStep
{
    public int Index { get; init; }
    public string Side { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
    public string HexPreview { get; init; } = string.Empty;
}

public sealed class MmsConfirmedRequestBerProbeResult
{
    public string Kind { get; init; } = string.Empty;
    public int InvokeId { get; init; }
    public string Target { get; init; } = string.Empty;
    public string DecodedOperation { get; init; } = string.Empty;
    public bool IsTransportSuccess { get; init; }
    public bool IsServerSuccess { get; init; }
    public bool IsClientDecodeSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsConfirmedRequestBerProfileBuilder
{
    public async Task<MmsConfirmedRequestBerProfile> RunLoopbackProbeAsync(
        MmsConfirmedRequestBerOptions? options = null,
        IReadOnlyList<MmsConfirmedBerProbe>? probes = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MmsConfirmedRequestBerOptions();
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

        probes ??= CreateDefaultProbes(serverProfile);
        var serverSession = new MmsReadOnlyServerSession(serverProfile);
        var responseProfile = AcseMmsAssociateResponse.Select(options.ResponseProfileName);
        var associationPayload = SelectAssociationPayload(options.AssociationProfileName);

        var steps = new List<MmsConfirmedRequestBerStep>();
        var probeResults = new List<MmsConfirmedRequestBerProbeResult>();
        var findings = new List<string>();
        var sync = new object();
        var stepIndex = 0;
        var acceptedConnections = 0;
        var tpktExchangeVerified = false;
        var cotpConnectionConfirmed = false;
        var clientAssociateRequestObserved = false;
        var serverAssociateResponseSent = false;
        var clientAssociateResponseAccepted = false;
        var nativeBerRequestDecoded = false;
        var nativeBerResponseEncoded = false;
        var clientNativeResponseDecoded = false;
        var directoryDispatchVerified = false;
        var readDispatchVerified = false;
        var dataSetDirectoryDispatchVerified = false;
        var writeGuardVerified = false;
        var serverSuccessCount = 0;
        var serverFailureCount = 0;
        var clientDecodeSuccessCount = 0;
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
                findings.Add($"Loopback MMS BER confirmed-request probe failed: {ex.Message}");
        }
        finally
        {
            timer.Stop();
            listener.Stop();
        }

        if (!writeGuardVerified)
            findings.Add("MMS BER confirmed-request probe did not verify the read-only write guard.");

        var isReady = findings.Count == 0
            && acceptedConnections == 1
            && tpktExchangeVerified
            && cotpConnectionConfirmed
            && clientAssociateRequestObserved
            && serverAssociateResponseSent
            && clientAssociateResponseAccepted
            && nativeBerRequestDecoded
            && nativeBerResponseEncoded
            && clientNativeResponseDecoded
            && directoryDispatchVerified
            && readDispatchVerified
            && dataSetDirectoryDispatchVerified
            && writeGuardVerified
            && serverSuccessCount >= 4
            && serverFailureCount >= 1
            && clientDecodeSuccessCount == probes.Count;

        return new MmsConfirmedRequestBerProfile
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
            NativeBerRequestDecoded = nativeBerRequestDecoded,
            NativeBerResponseEncoded = nativeBerResponseEncoded,
            ClientNativeResponseDecoded = clientNativeResponseDecoded,
            DirectoryDispatchVerified = directoryDispatchVerified,
            ReadDispatchVerified = readDispatchVerified,
            DataSetDirectoryDispatchVerified = dataSetDirectoryDispatchVerified,
            WriteGuardVerified = writeGuardVerified,
            RequestCount = probes.Count,
            ServerSuccessCount = serverSuccessCount,
            ServerFailureCount = serverFailureCount,
            ClientDecodeSuccessCount = clientDecodeSuccessCount,
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

            foreach (var _ in probes)
            {
                var requestFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
                var requestTpkt = TpktFrameCodec.Decode(requestFrame);
                AddStep("server", "TPKT-RECV-MMS-BER-REQUEST", requestTpkt.IsValid, requestTpkt.Message, requestFrame);
                if (!requestTpkt.IsValid)
                {
                    AddFinding($"Server could not decode TPKT MMS BER request frame: {requestTpkt.Message}");
                    return;
                }

                var requestData = CotpFrameCodec.Decode(requestTpkt.Payload);
                var requestDataPass = requestData.IsValid && requestData.Kind == CotpTpduKind.Data && requestData.EndOfTransmission;
                AddStep("server", "COTP-RECV-MMS-BER-REQUEST", requestDataPass, requestData.Message, requestTpkt.Payload);
                if (!requestDataPass)
                {
                    AddFinding($"Server expected COTP Data TPDU carrying MMS BER request: {requestData.Message}");
                    return;
                }

                var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(requestData.UserData, serverSession);
                AddStep("server", "MMS-BER-DISPATCH", dispatch.IsRequestDecoded, dispatch.Message, requestData.UserData);
                if (!dispatch.IsRequestDecoded)
                {
                    AddFinding(dispatch.Message);
                    return;
                }

                nativeBerRequestDecoded = true;
                if (dispatch.Response.IsSuccess)
                    serverSuccessCount++;
                else
                    serverFailureCount++;
                if (dispatch.Request.Operation is MmsReadOnlyOperation.GetLogicalDeviceDirectory or MmsReadOnlyOperation.GetLogicalNodeDirectory or MmsReadOnlyOperation.GetDataSetDirectory)
                    directoryDispatchVerified = true;
                if (dispatch.Request.Operation == MmsReadOnlyOperation.Read && dispatch.Response.IsSuccess)
                    readDispatchVerified = true;
                if (dispatch.Request.Operation == MmsReadOnlyOperation.ReadDataSet && dispatch.Response.IsSuccess)
                    dataSetDirectoryDispatchVerified = true;
                if (dispatch.Request.Operation == MmsReadOnlyOperation.Write && !dispatch.Response.IsSuccess && dispatch.Response.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                    writeGuardVerified = true;

                var responseFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(dispatch.ResponsePresentationPayload));
                await stream.WriteAsync(responseFrame, timeoutSource.Token).ConfigureAwait(false);
                nativeBerResponseEncoded = true;
                AddStep("server", "MMS-BER-SEND-CONFIRMED-RESPONSE", true, dispatch.Response.Summary, responseFrame);
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

            foreach (var probe in probes)
            {
                var requestFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(probe.PresentationPayload));
                await stream.WriteAsync(requestFrame, timeoutSource.Token).ConfigureAwait(false);
                AddStep("client", "MMS-BER-SEND-CONFIRMED-REQUEST", true, $"Sent native MMS BER request {probe.Kind} invoke={probe.InvokeId} target={probe.Target}.", requestFrame);

                var responseFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
                var responseTpkt = TpktFrameCodec.Decode(responseFrame);
                AddStep("client", "TPKT-RECV-MMS-BER-RESPONSE", responseTpkt.IsValid, responseTpkt.Message, responseFrame);
                if (!responseTpkt.IsValid)
                {
                    AddFinding($"Client could not decode TPKT MMS BER response frame: {responseTpkt.Message}");
                    return;
                }

                var responseData = CotpFrameCodec.Decode(responseTpkt.Payload);
                var responseDataPass = responseData.IsValid && responseData.Kind == CotpTpduKind.Data && responseData.EndOfTransmission;
                AddStep("client", "COTP-RECV-MMS-BER-RESPONSE", responseDataPass, responseData.Message, responseTpkt.Payload);
                if (!responseDataPass)
                {
                    AddFinding($"Client expected COTP Data TPDU carrying MMS BER response: {responseData.Message}");
                    return;
                }

                var decoded = DecodeClientResponse(probe, responseData.UserData);
                if (decoded.IsClientDecodeSuccess)
                {
                    clientDecodeSuccessCount++;
                    clientNativeResponseDecoded = true;
                }

                lock (sync)
                    probeResults.Add(decoded);

                AddStep("client", "MMS-BER-DECODE-CONFIRMED-RESPONSE", decoded.IsClientDecodeSuccess, decoded.Message, responseData.UserData);
            }
        }

        void AddStep(string side, string layer, bool pass, string message, byte[] bytes)
        {
            var item = new MmsConfirmedRequestBerStep
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

    public static IReadOnlyList<MmsConfirmedBerProbe> CreateDefaultProbes(MmsReadOnlyServerProfile profile)
    {
        var firstDevice = profile.LogicalDevices.FirstOrDefault()?.Name ?? "IED1LD0";
        var firstPoint = profile.Points.FirstOrDefault()?.Reference ?? "IED1LD0/XCBR1.Pos.stVal";
        var firstDataSet = profile.DataSets.FirstOrDefault()?.Reference ?? "IED1LD0/LLN0.dsStatus";

        return
        [
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.GetDomainDirectory,
                InvokeId = 1,
                Target = string.Empty,
                PresentationPayload = MmsGetNameListRequest.Build(1, MmsGetNameListObjectClass.Domain)
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.GetNamedVariableDirectory,
                InvokeId = 2,
                Target = firstDevice,
                PresentationPayload = MmsGetNameListRequest.Build(2, MmsGetNameListObjectClass.NamedVariable, firstDevice)
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.GetNamedVariableListDirectory,
                InvokeId = 3,
                Target = firstDevice,
                PresentationPayload = MmsGetNameListRequest.Build(3, MmsGetNameListObjectClass.NamedVariableList, firstDevice)
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.Read,
                InvokeId = 4,
                Target = firstPoint,
                PresentationPayload = MmsReadRequest.BuildSingleVariableRead(4, MmsObjectReference.Parse(firstPoint))
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.GetNamedVariableListAttributes,
                InvokeId = 5,
                Target = firstDataSet,
                PresentationPayload = MmsDataSetDirectoryRequest.Build(5, firstDataSet)
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.GetVariableAccessAttributes,
                InvokeId = 6,
                Target = firstPoint,
                PresentationPayload = MmsVariableAccessAttributesRequest.Build(6, MmsObjectReference.Parse(firstPoint))
            },
            new MmsConfirmedBerProbe
            {
                Kind = MmsConfirmedBerProbeKind.Write,
                InvokeId = 7,
                Target = firstPoint,
                PresentationPayload = MmsWriteRequest.BuildSingleVariableWrite(7, MmsObjectReference.Parse(firstPoint), MmsDataValue.VisibleString("open"))
            }
        ];
    }

    private static MmsConfirmedRequestBerProbeResult DecodeClientResponse(MmsConfirmedBerProbe probe, byte[] presentationPayload)
    {
        var serverSuccess = false;
        var clientSuccess = false;
        var message = string.Empty;

        switch (probe.Kind)
        {
            case MmsConfirmedBerProbeKind.GetDomainDirectory:
            case MmsConfirmedBerProbeKind.GetNamedVariableDirectory:
            case MmsConfirmedBerProbeKind.GetNamedVariableListDirectory:
                var names = MmsGetNameListResponseDecoder.Decode(presentationPayload, probe.InvokeId);
                serverSuccess = names.IsSuccess;
                clientSuccess = names.IsSuccess && names.Names.Count > 0;
                message = names.Message;
                break;

            case MmsConfirmedBerProbeKind.Read:
                var read = MmsReadResponseDecoder.DecodeSingleVariable(presentationPayload, probe.InvokeId);
                serverSuccess = read.IsSuccess;
                clientSuccess = read.IsSuccess;
                message = read.Message;
                break;

            case MmsConfirmedBerProbeKind.GetNamedVariableListAttributes:
                var dataSet = MmsDataSetDirectoryResponseDecoder.Decode(presentationPayload, probe.InvokeId, probe.Target);
                serverSuccess = dataSet.IsSuccess;
                clientSuccess = dataSet.IsSuccess && dataSet.Members.Count > 0;
                message = dataSet.Message;
                break;

            case MmsConfirmedBerProbeKind.GetVariableAccessAttributes:
                var attributes = MmsVariableAccessAttributesResponseDecoder.Decode(presentationPayload, probe.InvokeId, MmsObjectReference.Parse(probe.Target));
                serverSuccess = attributes.IsSuccess;
                clientSuccess = attributes.IsSuccess;
                message = attributes.Message;
                break;

            case MmsConfirmedBerProbeKind.Write:
                var write = MmsWriteResponseDecoder.Decode(presentationPayload, probe.InvokeId);
                serverSuccess = write.IsSuccess;
                clientSuccess = !write.IsSuccess && write.AccessResults.Any(x => !x.IsSuccess);
                message = write.Message;
                break;

            default:
                message = "Unsupported probe kind.";
                break;
        }

        return new MmsConfirmedRequestBerProbeResult
        {
            Kind = probe.Kind.ToString(),
            InvokeId = probe.InvokeId,
            Target = probe.Target,
            DecodedOperation = probe.Kind.ToString(),
            IsTransportSuccess = true,
            IsServerSuccess = serverSuccess,
            IsClientDecodeSuccess = clientSuccess,
            Message = message,
            ResponseHexPreview = HexDump.ToCompactString(presentationPayload)
        };
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

public sealed class MmsConfirmedBerDispatchResult
{
    public bool IsRequestDecoded { get; init; }
    public MmsReadOnlyServerRequest Request { get; init; } = new();
    public MmsReadOnlyServerResponse Response { get; init; } = new();
    public byte[] ResponsePresentationPayload { get; init; } = Array.Empty<byte>();
    public string Message { get; init; } = string.Empty;
}

public static class MmsConfirmedRequestBerDispatcher
{
    private const int DataAccessErrorObjectAccessDenied = 3;
    private const int DataAccessErrorTypeInconsistent = 7;
    private const int DataAccessErrorObjectNonExistent = 10;

    public static MmsConfirmedBerDispatchResult Dispatch(
        ReadOnlyMemory<byte> presentationPayload,
        MmsReadOnlyServerSession serverSession,
        int presentationContextId = 3,
        IMmsAssociationRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(serverSession);
        if (presentationContextId <= 0)
            throw new ArgumentOutOfRangeException(nameof(presentationContextId));

        var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
        var pduOffset = 0;
        if (mms.Length > 0 && BerReader.TryReadTlv(mms, ref pduOffset, out var pdu))
        {
            // Conclude-RequestPDU ::= [11] IMPLICIT NULL -> acknowledge with Conclude-ResponsePDU [12]
            // instead of dropping the socket, so clients can close the association gracefully.
            if (pdu.Class == BerClass.ContextSpecific && pdu.TagNumber == 11)
            {
                var concludeResponse = MmsPresentation.WrapIsoPresentationPData(
                    BerWriter.EncodeTlv(0x8C, ReadOnlySpan<byte>.Empty),
                    presentationContextId);
                return new MmsConfirmedBerDispatchResult
                {
                    IsRequestDecoded = true,
                    Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Conclude },
                    Response = new MmsReadOnlyServerResponse
                    {
                        IsSuccess = true,
                        Operation = nameof(MmsReadOnlyOperation.Conclude),
                        Message = "Conclude acknowledged."
                    },
                    ResponsePresentationPayload = concludeResponse,
                    Message = "Acknowledged MMS Conclude-Request."
                };
            }

            // Identify and native multi-variable Read/Write are dispatched here because they need
            // richer semantics than the single-target MmsReadOnlyServerRequest contract.
            if (pdu.EncodedTag == 0xA0 && TryReadInvokeAndService(pdu, out var invoke, out var servicePdu))
            {
                if (servicePdu.Class == BerClass.ContextSpecific && servicePdu.TagNumber == 2 && !servicePdu.Constructed)
                    return DispatchIdentify(invoke, presentationContextId);

                if (servicePdu.EncodedTag == 0xA4)
                    return DispatchRead(invoke, servicePdu, serverSession, presentationContextId, runtime);

                if (servicePdu.EncodedTag == 0xA5)
                    return DispatchWrite(invoke, servicePdu, serverSession, presentationContextId, runtime);
            }
        }

        if (!TryDecodeRequest(presentationPayload, out var invokeId, out var request, out var serviceKind, out var decodeMessage))
        {
            // Keep the association alive: an undecodable or unsupported confirmed request must be
            // answered with a Confirmed-ErrorPDU, not a dropped TCP connection. External IEC 61850
            // engineering clients may probe optional services during discovery and expect a negative response.
            var errorPayload = TryReadInvokeId(presentationPayload, out var errorInvokeId)
                ? EncodeConfirmedError(errorInvokeId, errorClassTag: 12, errorValue: 0, presentationContextId)
                : Array.Empty<byte>();
            var failure = new MmsReadOnlyServerResponse
            {
                IsSuccess = false,
                Operation = "DecodeConfirmedRequest",
                Message = decodeMessage
            };
            return new MmsConfirmedBerDispatchResult
            {
                IsRequestDecoded = false,
                Request = request,
                Response = failure,
                ResponsePresentationPayload = errorPayload,
                Message = errorPayload.Length > 0
                    ? $"{decodeMessage} Answered with Confirmed-Error invokeID={errorInvokeId}; association kept alive."
                    : decodeMessage
            };
        }

        var response = serverSession.Handle(request);
        var encoded = EncodeResponse(invokeId, serviceKind, request, response, presentationContextId);
        return new MmsConfirmedBerDispatchResult
        {
            IsRequestDecoded = true,
            Request = request,
            Response = response,
            ResponsePresentationPayload = encoded,
            Message = $"Decoded MMS BER {serviceKind} invokeID={invokeId} and dispatched {request.Operation} target={request.Target}."
        };
    }

    private static bool TryReadInvokeAndService(BerTlv confirmedRequest, out int invokeId, out BerTlv service)
    {
        invokeId = 0;
        service = default;
        var children = BerReader.ReadChildren(confirmedRequest.Value);
        if (children.Count < 2 || children[0].EncodedTag != 0x02)
            return false;

        var invoke = BerReader.ReadUnsignedInteger(children[0]);
        if (!invoke.HasValue || invoke.Value > int.MaxValue)
            return false;

        invokeId = (int)invoke.Value;
        service = children[1];
        return true;
    }

    private static bool TryReadInvokeId(ReadOnlyMemory<byte> presentationPayload, out int invokeId)
    {
        invokeId = 0;
        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer) || outer.EncodedTag != 0xA0)
                return false;

            return TryReadInvokeAndService(outer, out invokeId, out _);
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static MmsConfirmedBerDispatchResult DispatchIdentify(int invokeId, int presentationContextId)
    {
        // Identify-Response ::= SEQUENCE { vendorName [0], modelName [1], revision [2] }
        var service = BerWriter.EncodeTlv(0xA2, Concat(
            BerWriter.EncodeTlv(0x80, BerWriter.EncodeAscii("ARIEC61850")),
            BerWriter.EncodeTlv(0x81, BerWriter.EncodeAscii("Virtual IED Simulator")),
            BerWriter.EncodeTlv(0x82, BerWriter.EncodeAscii("1.0"))));
        var confirmedResponse = BerWriter.EncodeTlv(0xA1, Concat(Integer(invokeId), service));
        return new MmsConfirmedBerDispatchResult
        {
            IsRequestDecoded = true,
            Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Identify },
            Response = new MmsReadOnlyServerResponse
            {
                IsSuccess = true,
                Operation = nameof(MmsReadOnlyOperation.Identify),
                Message = "Returned vendor/model/revision identification."
            },
            ResponsePresentationPayload = MmsPresentation.WrapIsoPresentationPData(confirmedResponse, presentationContextId),
            Message = $"Decoded MMS BER Identify invokeID={invokeId}."
        };
    }

    private static MmsConfirmedBerDispatchResult DispatchRead(
        int invokeId,
        BerTlv service,
        MmsReadOnlyServerSession serverSession,
        int presentationContextId,
        IMmsAssociationRuntime? runtime)
    {
        if (!TryDecodeReadService(service, out var isVariableList, out var listName, out var targets, out var decodeMessage))
        {
            var errorPayload = EncodeConfirmedError(invokeId, errorClassTag: 7, errorValue: DataAccessErrorObjectNonExistent, presentationContextId);
            return new MmsConfirmedBerDispatchResult
            {
                IsRequestDecoded = false,
                Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read },
                Response = new MmsReadOnlyServerResponse { IsSuccess = false, Operation = nameof(MmsReadOnlyOperation.Read), Message = decodeMessage },
                ResponsePresentationPayload = errorPayload,
                Message = $"{decodeMessage} Answered with Confirmed-Error invokeID={invokeId}."
            };
        }

        if (isVariableList)
        {
            // Read by variableListName: return one AccessResult per DataSet member.
            var dataSetRequest = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = listName };
            var dataSetResponse = serverSession.Handle(dataSetRequest);
            if (!dataSetResponse.IsSuccess)
            {
                var accessErrorPayload = WrapReadResponse(
                    invokeId,
                    EncodeDataAccessError(DataAccessErrorObjectNonExistent),
                    presentationContextId);
                return new MmsConfirmedBerDispatchResult
                {
                    IsRequestDecoded = true,
                    Request = dataSetRequest,
                    Response = dataSetResponse,
                    ResponsePresentationPayload = accessErrorPayload,
                    Message = $"Decoded MMS BER Read(variableListName) invokeID={invokeId}; DataSet '{listName}' returned object-non-existent."
                };
            }

            var memberResults = dataSetResponse.Values.Select(EncodePointValue).ToArray();
            var listPayload = WrapReadResponse(invokeId, Concat(memberResults), presentationContextId);
            return new MmsConfirmedBerDispatchResult
            {
                IsRequestDecoded = true,
                Request = dataSetRequest,
                Response = dataSetResponse,
                ResponsePresentationPayload = listPayload,
                Message = $"Decoded MMS BER Read(variableListName) invokeID={invokeId} and returned {memberResults.Length} DataSet member value(s)."
            };
        }

        // listOfVariable: MMS requires exactly one AccessResult per requested variable, in request
        // order. Collapsing a batch read to a single result desynchronizes the client's decoder and
        // is the classic cause of "unknown error, retry" loops in IED browsers.
        var accessResults = new List<byte[]>(targets.Count);
        var successCount = 0;
        var firstFailureMessage = string.Empty;
        MmsReadOnlyPoint? firstPoint = null;
        foreach (var target in targets)
        {
            if (runtime is not null && runtime.TryReadRcbAttribute(target, out var rcbValue))
            {
                accessResults.Add(MmsDataCodec.Encode(rcbValue));
                successCount++;
                continue;
            }

            var response = serverSession.Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = target });
            if (response.IsSuccess && response.Values.Count > 0)
            {
                accessResults.Add(EncodePointValue(response.Values[0]));
                firstPoint ??= response.Values[0];
                successCount++;
            }
            else
            {
                accessResults.Add(EncodeDataAccessError(DataAccessErrorObjectNonExistent));
                if (firstFailureMessage.Length == 0)
                    firstFailureMessage = $"{target}: {response.Message}";
            }
        }

        var payload = WrapReadResponse(invokeId, Concat(accessResults.ToArray()), presentationContextId);
        var summaryTarget = targets.Count == 0
            ? string.Empty
            : targets.Count == 1 ? targets[0] : $"{targets[0]} (+{targets.Count - 1} more)";
        var isSuccess = successCount == targets.Count && targets.Count > 0;
        var message = targets.Count == 1 && firstPoint is not null
            ? $"Returned value {firstPoint.Value} quality={firstPoint.Quality}."
            : isSuccess
                ? $"Returned {successCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} access result(s)."
                : $"Returned {successCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} access result(s); first failure: {firstFailureMessage}";
        return new MmsConfirmedBerDispatchResult
        {
            IsRequestDecoded = true,
            Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = summaryTarget },
            Response = new MmsReadOnlyServerResponse
            {
                IsSuccess = isSuccess,
                Operation = nameof(MmsReadOnlyOperation.Read),
                Target = summaryTarget,
                Message = message
            },
            ResponsePresentationPayload = payload,
            Message = $"Decoded MMS BER Read invokeID={invokeId} with {targets.Count} variable(s)."
        };
    }

    private static MmsConfirmedBerDispatchResult DispatchWrite(
        int invokeId,
        BerTlv service,
        MmsReadOnlyServerSession serverSession,
        int presentationContextId,
        IMmsAssociationRuntime? runtime)
    {
        if (!TryDecodeWriteService(service, out var targets, out var values, out var decodeMessage))
        {
            var errorPayload = EncodeConfirmedError(invokeId, errorClassTag: 7, errorValue: DataAccessErrorObjectNonExistent, presentationContextId);
            return new MmsConfirmedBerDispatchResult
            {
                IsRequestDecoded = false,
                Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write },
                Response = new MmsReadOnlyServerResponse { IsSuccess = false, Operation = nameof(MmsReadOnlyOperation.Write), Message = decodeMessage },
                ResponsePresentationPayload = errorPayload,
                Message = $"{decodeMessage} Answered with Confirmed-Error invokeID={invokeId}."
            };
        }

        // Write-Response ::= [5] IMPLICIT SEQUENCE OF CHOICE { failure [0] DataAccessError, success [1] NULL }
        // - one entry per written variable, in request order.
        var writeResults = new List<byte[]>(targets.Count);
        var acceptedCount = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var value = index < values.Count ? values[index] : null;
            if (value is null)
            {
                writeResults.Add(BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(DataAccessErrorTypeInconsistent)));
                if (firstFailure.Length == 0)
                    firstFailure = $"{target}: missing Data element";
                continue;
            }

            if (runtime is not null && runtime.TryWriteRcbAttribute(target, value, out var rcbError))
            {
                if (rcbError == 0)
                {
                    writeResults.Add(BerWriter.EncodeTlv(0x81, ReadOnlySpan<byte>.Empty));
                    acceptedCount++;
                }
                else
                {
                    writeResults.Add(BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger((ulong)rcbError)));
                    if (firstFailure.Length == 0)
                        firstFailure = $"{target}: DataAccessError {rcbError}";
                }

                continue;
            }

            writeResults.Add(BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(DataAccessErrorObjectAccessDenied)));
            if (firstFailure.Length == 0)
                firstFailure = $"{target}: write rejected (read-only data model)";
        }

        var responseService = BerWriter.EncodeTlv(0xA5, Concat(writeResults.ToArray()));
        var confirmedResponse = BerWriter.EncodeTlv(0xA1, Concat(Integer(invokeId), responseService));
        var payload = MmsPresentation.WrapIsoPresentationPData(confirmedResponse, presentationContextId);

        var summaryTarget = targets.Count == 0
            ? string.Empty
            : targets.Count == 1 ? targets[0] : $"{targets[0]} (+{targets.Count - 1} more)";
        var isSuccess = acceptedCount == targets.Count && targets.Count > 0;
        return new MmsConfirmedBerDispatchResult
        {
            IsRequestDecoded = true,
            Request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = summaryTarget, Value = "<mms-data>" },
            Response = new MmsReadOnlyServerResponse
            {
                IsSuccess = isSuccess,
                Operation = nameof(MmsReadOnlyOperation.Write),
                Target = summaryTarget,
                Message = isSuccess
                    ? $"Accepted {acceptedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} write(s)."
                    : $"Accepted {acceptedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} write(s); {firstFailure}"
            },
            ResponsePresentationPayload = payload,
            Message = $"Decoded MMS BER Write invokeID={invokeId} with {targets.Count} variable(s)."
        };
    }

    /// <summary>Encodes the AccessResult for a resolved point (Data on success). Public for the reporting runtime.</summary>
    public static byte[] EncodePointAccessResult(MmsReadOnlyPoint point) => EncodePointValue(point);

    private static byte[] EncodeDataAccessError(int code)
        => BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger((ulong)code));

    private static byte[] WrapReadResponse(int invokeId, byte[] accessResults, int presentationContextId)
    {
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, accessResults);
        var responseService = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var confirmedResponse = BerWriter.EncodeTlv(0xA1, Concat(Integer(invokeId), responseService));
        return MmsPresentation.WrapIsoPresentationPData(confirmedResponse, presentationContextId);
    }

    private static byte[] EncodeConfirmedError(int invokeId, int errorClassTag, int errorValue, int presentationContextId)
    {
        // Confirmed-ErrorPDU ::= [2] IMPLICIT SEQUENCE {
        //   invokeID [0] IMPLICIT Unsigned32,
        //   serviceError [2] IMPLICIT ServiceError { errorClass [0] CHOICE { ... [n] IMPLICIT INTEGER } } }
        var invoke = BerWriter.EncodeTlv(0x80, EncodeIntegerContent(invokeId));
        var classChoice = BerWriter.EncodeTlv(BerClass.ContextSpecific, false, errorClassTag, BerWriter.EncodeUnsignedInteger((ulong)errorValue));
        var errorClass = BerWriter.EncodeTlv(0xA0, classChoice);
        var serviceError = BerWriter.EncodeTlv(0xA2, errorClass);
        var pdu = BerWriter.EncodeTlv(0xA2, Concat(invoke, serviceError));
        return MmsPresentation.WrapIsoPresentationPData(pdu, presentationContextId);
    }

    private static bool TryDecodeReadService(
        BerTlv service,
        out bool isVariableList,
        out string listName,
        out List<string> targets,
        out string message)
    {
        isVariableList = false;
        listName = string.Empty;
        targets = new List<string>();
        message = string.Empty;

        try
        {
            BerTlv specification = default;
            foreach (var child in BerReader.ReadChildren(service.Value))
            {
                if (child.Class == BerClass.ContextSpecific && child.TagNumber == 0 && !child.Constructed)
                    continue; // specificationWithResult [0] BOOLEAN

                if (child.Class == BerClass.ContextSpecific && child.TagNumber == 1 && child.Constructed)
                {
                    // variableAccessSpecification [1] explicit wrapper.
                    var inner = BerReader.ReadChildren(child.Value);
                    if (inner.Count > 0)
                        specification = inner[0];
                    break;
                }

                if (child.EncodedTag is 0xA0 && specification.EncodedTag == 0)
                    specification = child; // compatibility: unwrapped VariableAccessSpecification.
            }

            if (specification.EncodedTag == 0)
            {
                message = "MMS Read request carries no VariableAccessSpecification.";
                return false;
            }

            if (specification.Class == BerClass.ContextSpecific && specification.TagNumber == 1)
            {
                // variableListName [1] ObjectName.
                if (!TryDecodeObjectName(specification.Value, out var listDomain, out var listItem))
                {
                    message = "MMS Read variableListName has no decodable ObjectName.";
                    return false;
                }

                isVariableList = true;
                listName = ToIecDataSetReference(listDomain, listItem);
                message = $"Decoded Read variableListName={listName}.";
                return true;
            }

            if (specification.Class == BerClass.ContextSpecific && specification.TagNumber == 0)
            {
                // listOfVariable [0] IMPLICIT SEQUENCE OF SEQUENCE { variableSpecification, alternateAccess? }
                foreach (var entry in BerReader.ReadChildren(specification.Value))
                {
                    if (entry.EncodedTag != 0x30)
                        continue;

                    string domain = string.Empty, item = string.Empty;
                    var resolved = false;
                    foreach (var field in BerReader.ReadChildren(entry.Value))
                    {
                        // VariableSpecification.name [0] ObjectName (explicit).
                        if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0 && field.Constructed)
                        {
                            var nameOffset = 0;
                            if (BerReader.TryReadTlv(field.Value, ref nameOffset, out var objectName) &&
                                TryDecodeObjectName(objectName, out domain, out item))
                            {
                                resolved = true;
                            }

                            break;
                        }
                    }

                    targets.Add(resolved ? ToIecReference(domain, item) : string.Empty);
                }

                // Unresolvable entries stay in the list as empty targets so the response keeps
                // one AccessResult per requested variable (they yield object-non-existent).
                if (targets.Count == 0)
                {
                    message = "MMS Read listOfVariable contains no variable specification.";
                    return false;
                }

                message = $"Decoded Read listOfVariable with {targets.Count} variable(s).";
                return true;
            }

            message = $"Unsupported VariableAccessSpecification tag 0x{specification.EncodedTag:X2} in Read request.";
            return false;
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            message = $"MMS Read decode failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool TryDecodeWriteService(
        BerTlv service,
        out List<string> targets,
        out List<MmsDataValue?> values,
        out string message)
    {
        targets = new List<string>();
        values = new List<MmsDataValue?>();
        message = string.Empty;

        try
        {
            // Write-Request ::= SEQUENCE { variableAccessSpecification VariableAccessSpecification,
            //                              listOfData [0] IMPLICIT SEQUENCE OF Data }
            // Both listOfVariable and listOfData use context tag [0]; the SEQUENCE order disambiguates.
            var children = BerReader.ReadChildren(service.Value);
            if (children.Count < 2)
            {
                message = "MMS Write request does not contain a variable specification and listOfData.";
                return false;
            }

            var specification = children[0];
            var listOfData = children[^1];

            foreach (var data in BerReader.ReadChildren(listOfData.Value))
                values.Add(MmsDataCodec.Decode(data));

            if (specification.Class == BerClass.ContextSpecific && specification.TagNumber == 1)
            {
                if (!TryDecodeObjectName(specification.Value, out var listDomain, out var listItem))
                {
                    message = "MMS Write variableListName has no decodable ObjectName.";
                    return false;
                }

                targets.Add(ToIecDataSetReference(listDomain, listItem));
                message = $"Decoded Write variableListName={targets[0]}.";
                return true;
            }

            foreach (var entry in BerReader.ReadChildren(specification.Value))
            {
                if (entry.EncodedTag != 0x30)
                    continue;

                string domain = string.Empty, item = string.Empty;
                var resolved = false;
                foreach (var field in BerReader.ReadChildren(entry.Value))
                {
                    if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0 && field.Constructed)
                    {
                        var nameOffset = 0;
                        if (BerReader.TryReadTlv(field.Value, ref nameOffset, out var objectName) &&
                            TryDecodeObjectName(objectName, out domain, out item))
                        {
                            resolved = true;
                        }

                        break;
                    }
                }

                targets.Add(resolved ? ToIecReference(domain, item) : string.Empty);
            }

            if (targets.Count == 0)
            {
                message = "MMS Write request contains no variable specification.";
                return false;
            }

            message = $"Decoded Write with {targets.Count} variable(s) and {values.Count} data element(s).";
            return true;
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            message = $"MMS Write decode failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public static bool TryDecodeRequest(
        ReadOnlyMemory<byte> presentationPayload,
        out int invokeId,
        out MmsReadOnlyServerRequest request,
        out MmsConfirmedBerProbeKind serviceKind,
        out string message)
    {
        invokeId = 0;
        request = new MmsReadOnlyServerRequest();
        serviceKind = MmsConfirmedBerProbeKind.Read;

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
            {
                message = "MMS BER confirmed request payload is empty.";
                return false;
            }

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer) || outer.EncodedTag != 0xA0)
            {
                message = $"Expected MMS Confirmed-Request PDU [0] (0xA0), received 0x{(mms.Length > 0 ? mms[0] : 0):X2}.";
                return false;
            }

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count < 2 || children[0].EncodedTag != 0x02)
            {
                message = "MMS Confirmed-Request does not contain invokeID followed by service request.";
                return false;
            }

            var invoke = BerReader.ReadUnsignedInteger(children[0]);
            if (!invoke.HasValue || invoke.Value > int.MaxValue)
            {
                message = "MMS Confirmed-Request invokeID is missing or out of range.";
                return false;
            }

            invokeId = (int)invoke.Value;
            var service = children[1];
            if (service.Class == BerClass.ContextSpecific && service.TagNumber == 77)
            {
                serviceKind = MmsConfirmedBerProbeKind.GetFileDirectory;
                return TryDecodeFileDirectory(service, out request, out message);
            }

            switch (service.EncodedTag)
            {
                case 0xA1:
                    return TryDecodeGetNameList(service, out request, out serviceKind, out message);
                case 0xA4:
                    serviceKind = MmsConfirmedBerProbeKind.Read;
                    return TryDecodeRead(service, out request, out message);
                case 0xA6:
                    serviceKind = MmsConfirmedBerProbeKind.GetVariableAccessAttributes;
                    return TryDecodeGetVariableAccessAttributes(service, out request, out message);
                case 0xAC:
                    serviceKind = MmsConfirmedBerProbeKind.GetNamedVariableListAttributes;
                    return TryDecodeGetNamedVariableListAttributes(service, out request, out message);
                case 0xA5:
                    serviceKind = MmsConfirmedBerProbeKind.Write;
                    return TryDecodeWrite(service, out request, out message);
                default:
                    message = $"Unsupported MMS confirmed service tag 0x{service.EncodedTag:X2}.";
                    return false;
            }
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            message = $"MMS BER confirmed request decode failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool TryDecodeGetNameList(BerTlv service, out MmsReadOnlyServerRequest request, out MmsConfirmedBerProbeKind serviceKind, out string message)
    {
        request = new MmsReadOnlyServerRequest();
        serviceKind = MmsConfirmedBerProbeKind.GetDomainDirectory;
        message = string.Empty;

        var children = BerReader.ReadChildren(service.Value);
        var objectClassField = children.FirstOrDefault(x => x.EncodedTag == 0xA0);
        var objectScopeField = children.FirstOrDefault(x => x.EncodedTag == 0xA1);
        var objectClass = 0;
        if (objectClassField.EncodedTag != 0)
        {
            var objectClassNode = BerReader.ReadChildren(objectClassField.Value).FirstOrDefault();
            ulong? value = objectClassNode.EncodedTag == 0 ? null : BerReader.ReadUnsignedInteger(objectClassNode);
            objectClass = value.HasValue ? (int)value.Value : 0;
        }

        var domain = DecodeObjectScopeDomain(objectScopeField);
        var continueAfterField = children.FirstOrDefault(x => x.Class == BerClass.ContextSpecific && x.TagNumber == 2);
        var continueAfter = continueAfterField.EncodedTag == 0
            ? string.Empty
            : BerReader.ReadAsciiString(continueAfterField);
        if (objectClass == (int)MmsGetNameListObjectClass.Domain)
        {
            serviceKind = MmsConfirmedBerProbeKind.GetDomainDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory };
        }
        else if (objectClass == (int)MmsGetNameListObjectClass.NamedVariableList)
        {
            serviceKind = MmsConfirmedBerProbeKind.GetNamedVariableListDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetDataSetDirectory, Target = domain, ContinueAfter = continueAfter };
        }
        else
        {
            serviceKind = MmsConfirmedBerProbeKind.GetNamedVariableDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetNamedVariableDirectory, Target = domain, ContinueAfter = continueAfter };
        }

        message = $"Decoded GetNameList objectClass={objectClass} domain={domain} continueAfter={continueAfter}.";
        return true;
    }

    private static bool TryDecodeRead(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        if (!TryFindFirstDomainSpecificObjectName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "MMS Read request has no domain-specific variable object name.";
            return false;
        }

        var target = ToIecReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = target };
        message = $"Decoded Read request target={target}.";
        return true;
    }

    private static bool TryDecodeGetVariableAccessAttributes(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        if (!TryDecodeVariableAccessAttributesName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "GetVariableAccessAttributes request has no decodable MMS ObjectName.";
            return false;
        }

        var target = ToIecReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetVariableAccessAttributes, Target = target };
        message = $"Decoded GetVariableAccessAttributes request target={target}.";
        return true;
    }

    private static bool TryDecodeVariableAccessAttributesName(ReadOnlyMemory<byte> buffer, out string domain, out string item)
    {
        domain = string.Empty;
        item = string.Empty;

        var offset = 0;
        if (!BerReader.TryReadTlv(buffer, ref offset, out var requestChoice))
            return false;

        // GetVariableAccessAttributes-Request ::= CHOICE {
        //   name [0] ObjectName,
        //   address [1] Address }
        // Standards-compliant clients use the named-variable branch. Accept a
        // direct ObjectName only as a compatibility fallback for earlier probes.
        if (requestChoice.Class == BerClass.ContextSpecific && requestChoice.TagNumber == 0 && requestChoice.Constructed)
            return TryDecodeObjectName(requestChoice.Value, out domain, out item);

        return TryDecodeObjectName(requestChoice, out domain, out item);
    }

    private static bool TryDecodeObjectName(ReadOnlyMemory<byte> buffer, out string domain, out string item)
    {
        domain = string.Empty;
        item = string.Empty;

        var offset = 0;
        if (!BerReader.TryReadTlv(buffer, ref offset, out var objectName))
            return false;

        return TryDecodeObjectName(objectName, out domain, out item);
    }

    private static bool TryDecodeObjectName(BerTlv objectName, out string domain, out string item)
    {
        domain = string.Empty;
        item = string.Empty;

        if (objectName.Class != BerClass.ContextSpecific)
            return false;

        // ObjectName.domain-specific is a context-specific constructed sequence
        // containing domainId and itemId identifiers.
        if (objectName.TagNumber == 1 && objectName.Constructed)
        {
            var identifiers = BerReader.ReadChildren(objectName.Value)
                .Where(x => x.Class == BerClass.Universal && x.EncodedTag is 0x0C or 0x1A or 0x16)
                .Select(BerReader.ReadAsciiString)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            if (identifiers.Length >= 2)
            {
                domain = identifiers[0];
                item = identifiers[1];
                return true;
            }
        }

        // VMD-specific and AA-specific ObjectName choices carry one identifier.
        // They are valid MMS names even though they do not have an IEC 61850
        // logical-device domain prefix.
        if (objectName.TagNumber is 0 or 2)
        {
            if (objectName.Constructed)
            {
                var identifier = BerReader.ReadChildren(objectName.Value)
                    .FirstOrDefault(x => x.Class == BerClass.Universal && x.EncodedTag is 0x0C or 0x1A or 0x16);
                if (identifier.EncodedTag != 0)
                    item = BerReader.ReadAsciiString(identifier);
            }
            else
            {
                item = BerReader.ReadAsciiString(objectName);
            }

            return !string.IsNullOrWhiteSpace(item);
        }

        return false;
    }

    private static bool TryDecodeGetNamedVariableListAttributes(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        // GetNamedVariableListAttributes-Request ::= ObjectName. Some external
        // clients legitimately probe the VMD-specific form (0x80) for
        // LLN0$DataSet names, while ordinary IEC 61850 DataSets use domain-specific (0xA1).
        if (!TryDecodeObjectName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "GetNamedVariableListAttributes request has no decodable MMS ObjectName.";
            return false;
        }

        var target = ToIecDataSetReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = target };
        message = $"Decoded GetNamedVariableListAttributes request target={target}.";
        return true;
    }

    private static bool TryDecodeFileDirectory(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        var fileSpecification = string.Empty;
        foreach (var field in BerReader.ReadChildren(service.Value))
        {
            if (field.Class != BerClass.ContextSpecific || field.TagNumber != 0)
                continue;

            fileSpecification = ReadNestedAscii(field.Value);
            break;
        }

        request = new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.GetFileDirectory,
            Target = fileSpecification
        };
        message = $"Decoded FileDirectory request fileSpecification='{fileSpecification}'.";
        return true;
    }

    private static string ReadNestedAscii(ReadOnlyMemory<byte> value)
    {
        var offset = 0;
        if (BerReader.TryReadTlv(value, ref offset, out var nested))
            return BerReader.ReadAsciiString(nested);

        return value.Length == 0 ? string.Empty : Encoding.ASCII.GetString(value.Span);
    }

    private static bool TryDecodeWrite(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        if (!TryFindFirstDomainSpecificObjectName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "MMS Write request has no domain-specific variable object name.";
            return false;
        }

        var target = ToIecReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = target, Value = "<mms-data>" };
        message = $"Decoded Write request target={target}.";
        return true;
    }

    private static byte[] EncodeResponse(
        int invokeId,
        MmsConfirmedBerProbeKind serviceKind,
        MmsReadOnlyServerRequest request,
        MmsReadOnlyServerResponse response,
        int presentationContextId)
    {
        var service = serviceKind switch
        {
            MmsConfirmedBerProbeKind.GetDomainDirectory or
            MmsConfirmedBerProbeKind.GetNamedVariableDirectory or
            MmsConfirmedBerProbeKind.GetNamedVariableListDirectory => EncodeGetNameListResponse(response),
            MmsConfirmedBerProbeKind.GetFileDirectory => EncodeFileDirectoryResponse(),
            MmsConfirmedBerProbeKind.Read => EncodeReadResponse(response),
            MmsConfirmedBerProbeKind.GetVariableAccessAttributes => EncodeVariableAccessAttributesResponse(response),
            MmsConfirmedBerProbeKind.GetNamedVariableListAttributes => EncodeDataSetDirectoryResponse(response),
            MmsConfirmedBerProbeKind.Write => EncodeWriteResponse(response),
            _ => EncodeReadResponse(response)
        };

        var confirmedResponse = BerWriter.EncodeTlv(0xA1, Concat(Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedResponse, presentationContextId);
    }

    private static byte[] EncodeGetNameListResponse(MmsReadOnlyServerResponse response)
    {
        var names = response.Items.Select(ToMmsNameForDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var encodedNames = Concat(names.Select(VisibleString).ToArray());
        var listOfIdentifier = BerWriter.EncodeTlv(0xA0, encodedNames);
        var moreFollows = BerWriter.EncodeTlv(0x81, new byte[] { response.MoreFollows ? (byte)0xFF : (byte)0x00 });
        return BerWriter.EncodeTlv(0xA1, Concat(listOfIdentifier, moreFollows));
    }

    private static byte[] EncodeReadResponse(MmsReadOnlyServerResponse response)
    {
        var access = response.IsSuccess && response.Values.Count > 0
            ? EncodePointValue(response.Values[0])
            : BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(10)); // object-non-existent
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, access);
        return BerWriter.EncodeTlv(0xA4, listOfAccessResult);
    }

    private static byte[] EncodeDataSetDirectoryResponse(MmsReadOnlyServerResponse response)
    {
        var variableDefinitions = response.IsSuccess
            ? Concat(response.Items.Select(EncodeDataSetVariableDefinition).ToArray())
            : Array.Empty<byte>();
        var deletable = BerWriter.EncodeTlv(0x80, new byte[] { 0x00 });
        // GetNamedVariableListAttributes-Response.listOfVariable is [1]
        // IMPLICIT SEQUENCE OF VariableSpecification. Each member is a
        // SEQUENCE containing VariableSpecification.name [0] ObjectName.
        var listOfVariable = BerWriter.EncodeTlv(0xA1, variableDefinitions);
        return BerWriter.EncodeTlv(0xAC, Concat(deletable, listOfVariable));
    }

    private static byte[] EncodeFileDirectoryResponse()
    {
        // FileDirectory-Response ::= SEQUENCE {
        //   listOfDirectoryEntry [0] SEQUENCE OF DirectoryEntry,
        //   moreFollows [1] BOOLEAN DEFAULT FALSE
        // }
        var entries = BerWriter.EncodeTlv(0xA0, ReadOnlySpan<byte>.Empty);
        var moreFollows = BerWriter.EncodeTlv(0x81, BerWriter.EncodeBoolean(false));
        return BerWriter.EncodeTlv(BerClass.ContextSpecific, true, 77, Concat(entries, moreFollows));
    }

    private static byte[] EncodeDataSetVariableDefinition(string reference)
    {
        var objectName = EncodeDomainSpecificObjectNameFromReference(reference);
        var variableSpecificationName = BerWriter.EncodeTlv(0xA0, objectName);
        return BerWriter.EncodeTlv(0x30, variableSpecificationName);
    }

    private static byte[] EncodeVariableAccessAttributesResponse(MmsReadOnlyServerResponse response)
    {
        var deletable = BerWriter.EncodeTlv(0x80, new byte[] { 0x00 });
        var typeDescription = response.IsSuccess && response.Values.Count > 0
            ? EncodeTypeSpecification(response.Values[0])
            : BerWriter.EncodeTlv(0x8A, BerWriter.EncodeUnsignedInteger(255));

        // The response contains typeDescription [2] TypeDescription. The [2]
        // field is explicit, so the actual TypeDescription is nested inside it.
        var typeDescriptionField = BerWriter.EncodeTlv(0xA2, typeDescription);

        return BerWriter.EncodeTlv(0xA6, Concat(deletable, typeDescriptionField));
    }

    private static byte[] EncodeWriteResponse(MmsReadOnlyServerResponse response)
    {
        if (response.IsSuccess)
            return BerWriter.EncodeTlv(0xA5, BerWriter.EncodeTlv(0x81, ReadOnlySpan<byte>.Empty));

        var failure = BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(3));
        return BerWriter.EncodeTlv(0xA5, failure);
    }

    private static byte[] EncodePointValue(MmsReadOnlyPoint point)
    {
        if (point.Children.Count > 0)
            return MmsDataCodec.Encode(MmsDataValue.Structure(point.Children.Select(DecodePointValue)));

        var sclValue = TryEncodeSclPointValue(point);
        if (sclValue is not null)
            return MmsDataCodec.Encode(sclValue);

        if (point.Reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
            point.Kind.Equals("quality", StringComparison.OrdinalIgnoreCase))
            return MmsDataCodec.Encode(MmsDataValue.BitString(3, [0x00, 0x00]));

        if (point.Reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) &&
            point.Value.Equals("closed", StringComparison.OrdinalIgnoreCase))
            return MmsDataCodec.Encode(MmsDataValue.Integer(2));

        if (point.Reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) &&
            point.Value.Equals("open", StringComparison.OrdinalIgnoreCase))
            return MmsDataCodec.Encode(MmsDataValue.Integer(1));

        if (point.Kind.Equals("measurement", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(point.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return MmsDataCodec.Encode(MmsDataValue.FloatingPoint(number));

        if (bool.TryParse(point.Value, out var boolean))
            return MmsDataCodec.Encode(MmsDataValue.Boolean(boolean));

        return MmsDataCodec.Encode(MmsDataValue.VisibleString(point.Value));
    }

    private static MmsDataValue DecodePointValue(MmsReadOnlyPoint point)
    {
        var encoded = EncodePointValue(point);
        var offset = 0;
        if (!BerReader.TryReadTlv(encoded, ref offset, out var value))
            return MmsDataValue.VisibleString(point.Value);

        return MmsDataCodec.Decode(value);
    }

    private static byte[] EncodeTypeSpecification(MmsReadOnlyPoint point)
    {
        if (point.Kind.Equals("structure", StringComparison.OrdinalIgnoreCase) || point.Children.Count > 0)
        {
            var components = Concat(point.Children.Select(EncodeStructureComponent).ToArray());
            return BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0xA1, components));
        }

        var sclType = TryEncodeSclTypeSpecification(point.SclBType);
        if (sclType is not null)
            return sclType;

        if (point.Reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
            point.Kind.Equals("quality", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x84, new byte[] { 13 });

        if (point.Reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase) ||
            point.Kind.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x91, ReadOnlySpan<byte>.Empty);

        if (point.Kind.Equals("measurement", StringComparison.OrdinalIgnoreCase))
            return EncodeFloatingPointTypeSpecification(formatWidth: 32, exponentWidth: 8);

        if (point.Reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x85, new byte[] { 32 });

        if (bool.TryParse(point.Value, out _))
            return BerWriter.EncodeTlv(0x83, ReadOnlySpan<byte>.Empty);

        return BerWriter.EncodeTlv(0x8A, BerWriter.EncodeUnsignedInteger(255));
    }

    private static MmsDataValue? TryEncodeSclPointValue(MmsReadOnlyPoint point)
    {
        var bType = NormalizeSclBType(point.SclBType);
        if (bType.Length == 0 || bType == "STRUCT")
            return null;

        if (bType is "QUALITY" or "CHECK")
            return MmsDataValue.BitString(bType == "QUALITY" ? (byte)3 : (byte)6, [0x00, 0x00]);

        if (bType == "OPTFLDS")
            return MmsDataValue.BitString(6, MmsReportControlBlockLayout.ParseOptionalFields(point.Value));

        if (bType == "TRGOPS")
            return MmsDataValue.BitString(2, [MmsReportControlBlockLayout.ParseTriggerOptions(point.Value)]);

        if (bType == "ENTRYID")
            return MmsDataValue.OctetString(new byte[8]);

        if (bType == "ENTRYTIME")
            return MmsDataValue.BinaryTime(MmsReportControlBlockLayout.ToBinaryTime6(point.TimestampUtc));

        if (bType == "TIMESTAMP")
            return MmsDataValue.UtcTime(new Iec61850UtcTime(point.TimestampUtc, Quality: 0));

        if (bType is "BOOLEAN" or "BOOL")
            return MmsDataValue.Boolean(bool.TryParse(point.Value, out var boolean) && boolean);

        if (bType is "DBPOS" or "TCMD")
            return MmsDataValue.Integer(TryMapStatusToInteger(point.Value, out var mappedStatus) ? mappedStatus : 0);

        if (IsSignedSclInteger(bType))
            return MmsDataValue.Integer(long.TryParse(point.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed) ? signed : 0);

        if (IsUnsignedSclInteger(bType))
            return MmsDataValue.Unsigned(ulong.TryParse(point.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned) ? unsigned : 0);

        if (bType.StartsWith("FLOAT", StringComparison.Ordinal))
            return MmsDataValue.FloatingPoint(double.TryParse(point.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) ? floating : 0d);

        if (bType.StartsWith("OCTET", StringComparison.Ordinal))
            return MmsDataValue.OctetString(Encoding.ASCII.GetBytes(point.Value));

        if (bType.StartsWith("UNICODE", StringComparison.Ordinal) || bType.StartsWith("MMSSTRING", StringComparison.Ordinal))
            return MmsDataValue.MmsString(point.Value);

        if (bType.StartsWith("VISSTRING", StringComparison.Ordinal) || bType == "OBJREF")
            return MmsDataValue.VisibleString(point.Value);

        return null;
    }

    private static byte[]? TryEncodeSclTypeSpecification(string sclBType)
    {
        var bType = NormalizeSclBType(sclBType);
        if (bType.Length == 0 || bType == "STRUCT")
            return null;

        if (bType is "BOOLEAN" or "BOOL")
            return BerWriter.EncodeTlv(0x83, ReadOnlySpan<byte>.Empty);

        if (bType == "QUALITY")
            return BerWriter.EncodeTlv(0x84, new byte[] { 13 });

        if (bType == "CHECK")
            return BerWriter.EncodeTlv(0x84, new byte[] { 2 });

        if (bType == "OPTFLDS")
            return BerWriter.EncodeTlv(0x84, new byte[] { 10 });

        if (bType == "TRGOPS")
            return BerWriter.EncodeTlv(0x84, new byte[] { 6 });

        if (bType == "ENTRYID")
            return BerWriter.EncodeTlv(0x89, BerWriter.EncodeUnsignedInteger(8));

        if (bType == "ENTRYTIME")
            return BerWriter.EncodeTlv(0x8C, new byte[] { 0xFF }); // binary-time [12], time-of-day = 6 bytes

        if (IsSignedSclInteger(bType) || bType is "DBPOS" or "TCMD")
            return BerWriter.EncodeTlv(0x85, BerWriter.EncodeUnsignedInteger((ulong)SclIntegerWidth(bType, 32)));

        if (IsUnsignedSclInteger(bType))
            return BerWriter.EncodeTlv(0x86, BerWriter.EncodeUnsignedInteger((ulong)SclIntegerWidth(bType, 32)));

        if (bType == "FLOAT32")
            return EncodeFloatingPointTypeSpecification(formatWidth: 32, exponentWidth: 8);

        if (bType == "FLOAT64")
            return EncodeFloatingPointTypeSpecification(formatWidth: 64, exponentWidth: 11);

        if (bType.StartsWith("OCTET", StringComparison.Ordinal))
            return BerWriter.EncodeTlv(0x89, BerWriter.EncodeUnsignedInteger((ulong)SclStringLength(bType, 64)));

        if (bType.StartsWith("VISSTRING", StringComparison.Ordinal) || bType == "OBJREF")
            return BerWriter.EncodeTlv(0x8A, BerWriter.EncodeUnsignedInteger((ulong)SclStringLength(bType, 255)));

        if (bType.StartsWith("UNICODE", StringComparison.Ordinal) || bType.StartsWith("MMSSTRING", StringComparison.Ordinal))
            return BerWriter.EncodeTlv(0x90, BerWriter.EncodeUnsignedInteger((ulong)SclStringLength(bType, 255)));

        if (bType == "TIMESTAMP")
            return BerWriter.EncodeTlv(0x91, ReadOnlySpan<byte>.Empty);

        return null;
    }

    private static byte[] EncodeFloatingPointTypeSpecification(byte formatWidth, byte exponentWidth)
        => BerWriter.EncodeTlv(
            0xA7,
            Concat(
                BerWriter.EncodeTlv(0x02, [formatWidth]),
                BerWriter.EncodeTlv(0x02, [exponentWidth])));

    private static string NormalizeSclBType(string value)
        => (value ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static bool IsSignedSclInteger(string bType)
        => bType.StartsWith("INT", StringComparison.Ordinal) && !bType.EndsWith("U", StringComparison.Ordinal) || bType == "ENUM";

    private static bool IsUnsignedSclInteger(string bType)
        => bType.StartsWith("INT", StringComparison.Ordinal) && bType.EndsWith("U", StringComparison.Ordinal);

    private static int SclIntegerWidth(string bType, int fallback)
    {
        var digits = new string(bType.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width > 0 ? width : fallback;
    }

    private static int SclStringLength(string bType, int fallback)
    {
        var digits = new string(bType.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var length) && length > 0 ? length : fallback;
    }

    private static bool TryMapStatusToInteger(string value, out long result)
    {
        result = value.Equals("closed", StringComparison.OrdinalIgnoreCase) ? 2 :
            value.Equals("open", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return value.Equals("closed", StringComparison.OrdinalIgnoreCase) || value.Equals("open", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] EncodeStructureComponent(MmsReadOnlyPoint point)
    {
        var name = string.IsNullOrWhiteSpace(point.Name)
            ? LastMmsSegment(point.Reference)
            : point.Name;
        var componentName = BerWriter.EncodeTlv(0x80, BerWriter.EncodeAscii(name));
        var componentType = BerWriter.EncodeTlv(0xA1, EncodeTypeSpecification(point));
        return BerWriter.EncodeTlv(0x30, Concat(componentName, componentType));
    }

    private static string LastMmsSegment(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var separator = Math.Max(reference.LastIndexOf('$'), reference.LastIndexOf('.'));
        return separator >= 0 && separator + 1 < reference.Length ? reference[(separator + 1)..] : reference;
    }

    private static string DecodeObjectScopeDomain(BerTlv objectScopeField)
    {
        if (objectScopeField.EncodedTag == 0)
            return string.Empty;

        foreach (var child in BerReader.ReadChildren(objectScopeField.Value))
        {
            if (child.EncodedTag is 0x81 or 0x82 or 0x1A or 0x16)
                return BerReader.ReadAsciiString(child);
        }

        return string.Empty;
    }

    private static bool TryFindFirstDomainSpecificObjectName(ReadOnlyMemory<byte> buffer, out string domain, out string item)
    {
        domain = string.Empty;
        item = string.Empty;

        foreach (var child in BerReader.ReadChildren(buffer))
        {
            if (TryDecodeDomainSpecificObjectName(child.Value, out domain, out item))
                return true;

            if (child.Constructed && TryFindFirstDomainSpecificObjectName(child.Value, out domain, out item))
                return true;
        }

        return false;
    }

    private static bool TryDecodeDomainSpecificObjectName(ReadOnlyMemory<byte> buffer, out string domain, out string item)
    {
        domain = string.Empty;
        item = string.Empty;

        var offset = 0;
        if (!BerReader.TryReadTlv(buffer, ref offset, out var objectName))
            return false;

        if (objectName.EncodedTag != 0xA1)
            return false;

        var ids = BerReader.ReadChildren(objectName.Value)
            .Where(x => x.EncodedTag is 0x0C or 0x1A or 0x16)
            .Select(BerReader.ReadAsciiString)
            .ToArray();

        if (ids.Length < 2)
            return false;

        domain = ids[0];
        item = ids[1];
        return !string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(item);
    }

    private static string ToIecReference(string domain, string item)
    {
        var path = item.Replace('$', '.');
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The second MMS segment is normally the IEC 61850 data FC. Report
        // control block names also use the same position for RP/BR, which are
        // not FC values and must remain part of the object name.
        if (parts.Length >= 3 && IsDataFunctionalConstraint(parts[1]))
            path = string.Join('.', parts.Take(1).Concat(parts.Skip(2)));
        return string.IsNullOrWhiteSpace(domain) ? path : $"{domain}/{path}";
    }

    private static bool IsDataFunctionalConstraint(string value)
        => value.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("MX", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("SP", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("SV", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("CF", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("DC", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("SG", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("SE", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("EX", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("CO", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("SR", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("BL", StringComparison.OrdinalIgnoreCase);

    private static string ToIecDataSetReference(string domain, string item)
    {
        var path = item.Replace('$', '.');
        return string.IsNullOrWhiteSpace(domain) ? path : $"{domain}/{path}";
    }

    private static string ToMmsNameForDirectory(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return string.Empty;

        var normalized = item.Trim();
        var slash = normalized.IndexOf('/');
        if (slash >= 0 && slash < normalized.Length - 1)
            normalized = normalized[(slash + 1)..];

        return normalized.Replace('.', '$');
    }

    private static byte[] EncodeDomainSpecificObjectNameFromReference(string reference)
    {
        var normalized = reference ?? string.Empty;
        var slash = normalized.IndexOf('/');
        var domain = slash > 0 ? normalized[..slash] : string.Empty;
        var item = slash >= 0 && slash < normalized.Length - 1 ? normalized[(slash + 1)..] : normalized;
        return BerWriter.EncodeTlv(0xA1, Concat(VisibleString(domain), VisibleString(item.Replace('.', '$'))));
    }

    private static byte[] Integer(int value)
        => BerWriter.EncodeTlv(0x02, EncodeIntegerContent(value));

    private static byte[] EncodeIntegerContent(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        if (value <= 0x7F)
            return [(byte)value];

        if (value <= 0xFF)
            return [0x00, (byte)value];

        if (value <= 0x7FFF)
            return [(byte)(value >> 8), (byte)value];

        if (value <= 0xFFFF)
            return [0x00, (byte)(value >> 8), (byte)value];

        return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    private static byte[] VisibleString(string text)
        => BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(text));

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
            length += part.Length;

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
