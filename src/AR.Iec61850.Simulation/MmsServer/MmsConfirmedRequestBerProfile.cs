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
    public static MmsConfirmedBerDispatchResult Dispatch(
        ReadOnlyMemory<byte> presentationPayload,
        MmsReadOnlyServerSession serverSession,
        int presentationContextId = 3)
    {
        ArgumentNullException.ThrowIfNull(serverSession);
        if (presentationContextId <= 0)
            throw new ArgumentOutOfRangeException(nameof(presentationContextId));

        if (!TryDecodeRequest(presentationPayload, out var invokeId, out var request, out var serviceKind, out var decodeMessage))
        {
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
                Message = decodeMessage
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
        if (objectClass == (int)MmsGetNameListObjectClass.Domain)
        {
            serviceKind = MmsConfirmedBerProbeKind.GetDomainDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory };
        }
        else if (objectClass == (int)MmsGetNameListObjectClass.NamedVariableList)
        {
            serviceKind = MmsConfirmedBerProbeKind.GetNamedVariableListDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetDataSetDirectory, Target = domain };
        }
        else
        {
            serviceKind = MmsConfirmedBerProbeKind.GetNamedVariableDirectory;
            request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetNamedVariableDirectory, Target = domain };
        }

        message = $"Decoded GetNameList objectClass={objectClass} domain={domain}.";
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
        if (!TryDecodeDomainSpecificObjectName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "GetVariableAccessAttributes request has no domain-specific object name.";
            return false;
        }

        var target = ToIecReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetVariableAccessAttributes, Target = target };
        message = $"Decoded GetVariableAccessAttributes request target={target}.";
        return true;
    }

    private static bool TryDecodeGetNamedVariableListAttributes(BerTlv service, out MmsReadOnlyServerRequest request, out string message)
    {
        if (!TryDecodeDomainSpecificObjectName(service.Value, out var domain, out var item))
        {
            request = new MmsReadOnlyServerRequest();
            message = "GetNamedVariableListAttributes request has no domain-specific object name.";
            return false;
        }

        var target = ToIecDataSetReference(domain, item);
        request = new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = target };
        message = $"Decoded GetNamedVariableListAttributes request target={target}.";
        return true;
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
        var moreFollows = BerWriter.EncodeTlv(0x81, new byte[] { 0x00 });
        return BerWriter.EncodeTlv(0xA1, Concat(listOfIdentifier, moreFollows));
    }

    private static byte[] EncodeReadResponse(MmsReadOnlyServerResponse response)
    {
        var access = response.IsSuccess && response.Values.Count > 0
            ? EncodePointValue(response.Values[0])
            : BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(4));
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, access);
        return BerWriter.EncodeTlv(0xA4, listOfAccessResult);
    }

    private static byte[] EncodeDataSetDirectoryResponse(MmsReadOnlyServerResponse response)
    {
        var objectNames = response.IsSuccess
            ? Concat(response.Items.Select(EncodeDomainSpecificObjectNameFromReference).ToArray())
            : Array.Empty<byte>();
        var deletable = BerWriter.EncodeTlv(0x80, new byte[] { 0x00 });
        var listOfVariable = BerWriter.EncodeTlv(0xA0, objectNames);
        return BerWriter.EncodeTlv(0xAC, Concat(deletable, listOfVariable));
    }

    private static byte[] EncodeVariableAccessAttributesResponse(MmsReadOnlyServerResponse response)
    {
        var deletable = BerWriter.EncodeTlv(0x80, new byte[] { 0x00 });
        var typeSpecification = response.IsSuccess && response.Values.Count > 0
            ? EncodeTypeSpecification(response.Values[0])
            : BerWriter.EncodeTlv(0x8A, BerWriter.EncodeUnsignedInteger(255));

        return BerWriter.EncodeTlv(0xA6, Concat(deletable, typeSpecification));
    }

    private static byte[] EncodeWriteResponse(MmsReadOnlyServerResponse response)
    {
        if (response.IsSuccess)
            return BerWriter.EncodeTlv(0xA5, ReadOnlySpan<byte>.Empty);

        var failure = BerWriter.EncodeTlv(0x80, BerWriter.EncodeUnsignedInteger(3));
        return BerWriter.EncodeTlv(0xA5, failure);
    }

    private static byte[] EncodePointValue(MmsReadOnlyPoint point)
    {
        if (point.Kind.Equals("measurement", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(point.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return MmsDataCodec.Encode(MmsDataValue.FloatingPoint(number));

        if (bool.TryParse(point.Value, out var boolean))
            return MmsDataCodec.Encode(MmsDataValue.Boolean(boolean));

        return MmsDataCodec.Encode(MmsDataValue.VisibleString(point.Value));
    }

    private static byte[] EncodeTypeSpecification(MmsReadOnlyPoint point)
    {
        if (point.Kind.Equals("structure", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0xA2, ReadOnlySpan<byte>.Empty);

        if (point.Reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
            point.Kind.Equals("quality", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x84, new byte[] { 13 });

        if (point.Reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase) ||
            point.Kind.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x91, ReadOnlySpan<byte>.Empty);

        if (point.Kind.Equals("measurement", StringComparison.OrdinalIgnoreCase))
            return BerWriter.EncodeTlv(0x87, BerWriter.EncodeUnsignedInteger(32));

        if (bool.TryParse(point.Value, out _))
            return BerWriter.EncodeTlv(0x83, ReadOnlySpan<byte>.Empty);

        return BerWriter.EncodeTlv(0x8A, BerWriter.EncodeUnsignedInteger(255));
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
            .Where(x => x.EncodedTag is 0x1A or 0x16)
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
        if (parts.Length >= 3 && parts[1].Length == 2 && parts[1].All(char.IsUpper))
            path = string.Join('.', parts.Take(1).Concat(parts.Skip(2)));
        return string.IsNullOrWhiteSpace(domain) ? path : $"{domain}/{path}";
    }

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
