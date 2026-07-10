using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AR.Iec61850.Acse;
using AR.Iec61850.Diagnostics;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public sealed class MmsHandshakeListenerOptions
{
    public int Port { get; init; }
    public int ProbeTimeoutMilliseconds { get; init; } = 5000;
    public ushort ServerReference { get; init; } = 0x1001;
    public string AssociationProfileName { get; init; } = "standard";
}

public sealed class MmsHandshakeListenerProfile
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsReady { get; init; }
    public int BoundPort { get; init; }
    public int AcceptedConnectionCount { get; init; }
    public bool TpktExchangeVerified { get; init; }
    public bool CotpConnectionConfirmed { get; init; }
    public bool CotpDataObserved { get; init; }
    public bool AssociationPayloadObserved { get; init; }
    public IReadOnlyList<MmsHandshakeListenerStep> Steps { get; init; } = Array.Empty<MmsHandshakeListenerStep>();
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MMS Handshake Listener Profile");
        sb.AppendLine();
        sb.AppendLine($"Created UTC: `{CreatedAtUtc:O}`");
        sb.AppendLine($"Listener readiness: **{(IsReady ? "READY" : "BLOCKED")}**");
        sb.AppendLine($"Bound port: `{BoundPort}`");
        sb.AppendLine($"Accepted connections: `{AcceptedConnectionCount}`");
        sb.AppendLine();
        sb.AppendLine("## Transport gates");
        sb.AppendLine();
        sb.AppendLine($"- TPKT exchange verified: **{TpktExchangeVerified.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- COTP connection confirmed: **{CotpConnectionConfirmed.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- COTP data observed: **{CotpDataObserved.ToString().ToLowerInvariant()}**");
        sb.AppendLine($"- Association payload observed: **{AssociationPayloadObserved.ToString().ToLowerInvariant()}**");
        sb.AppendLine();
        sb.AppendLine("## Loopback handshake steps");
        sb.AppendLine();
        sb.AppendLine("| Step | Side | Layer | Result | Message |");
        sb.AppendLine("|---:|---|---|---|---|");
        foreach (var step in Steps.OrderBy(x => x.Index))
            sb.AppendLine($"| {step.Index} | {Escape(step.Side)} | {Escape(step.Layer)} | {(step.IsPass ? "PASS" : "FAIL")} | {Escape(step.Message)} |");

        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (Findings.Count == 0)
        {
            sb.AppendLine("- No blocking finding from the loopback OSI handshake listener profile.");
        }
        else
        {
            foreach (var finding in Findings)
                sb.AppendLine($"- {finding}");
        }

        return sb.ToString();
    }

    public string ToJson(JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(this, options ?? new JsonSerializerOptions { WriteIndented = true });

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}

public sealed class MmsHandshakeListenerStep
{
    public int Index { get; init; }
    public string Side { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
    public string HexPreview { get; init; } = string.Empty;
}

public sealed class MmsHandshakeListenerProfileBuilder
{
    public async Task<MmsHandshakeListenerProfile> RunLoopbackProbeAsync(
        MmsHandshakeListenerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MmsHandshakeListenerOptions();
        if (options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "TCP port must be 0..65535.");

        var steps = new List<MmsHandshakeListenerStep>();
        var findings = new List<string>();
        var sync = new object();
        var stepIndex = 0;
        var acceptedConnections = 0;
        var tpktExchangeVerified = false;
        var cotpConnectionConfirmed = false;
        var cotpDataObserved = false;
        var associationPayloadObserved = false;

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
                findings.Add($"Loopback OSI handshake listener failed: {ex.Message}");
        }
        finally
        {
            listener.Stop();
        }

        var isReady = findings.Count == 0
            && acceptedConnections == 1
            && tpktExchangeVerified
            && cotpConnectionConfirmed
            && cotpDataObserved
            && associationPayloadObserved;

        return new MmsHandshakeListenerProfile
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsReady = isReady,
            BoundPort = boundPort,
            AcceptedConnectionCount = acceptedConnections,
            TpktExchangeVerified = tpktExchangeVerified,
            CotpConnectionConfirmed = cotpConnectionConfirmed,
            CotpDataObserved = cotpDataObserved,
            AssociationPayloadObserved = associationPayloadObserved,
            Steps = steps.OrderBy(x => x.Index).ToArray(),
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

            var dataFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var dataTpkt = TpktFrameCodec.Decode(dataFrame);
            var dataTpktPass = dataTpkt.IsValid;
            AddStep("server", "TPKT-RECV-DATA", dataTpktPass, dataTpkt.Message, dataFrame);
            if (!dataTpktPass)
            {
                AddFinding($"Server could not decode client TPKT data frame: {dataTpkt.Message}");
                return;
            }

            var data = CotpFrameCodec.Decode(dataTpkt.Payload);
            var dataPass = data.IsValid && data.Kind == CotpTpduKind.Data && data.EndOfTransmission;
            if (dataPass)
                cotpDataObserved = true;
            AddStep("server", "COTP-RECV-DATA", dataPass, data.Message, dataTpkt.Payload);
            if (!dataPass)
            {
                AddFinding($"Server expected COTP Data TPDU with EOT: {data.Message}");
                return;
            }

            var inspection = AcseAssociationPayloadInspector.Inspect(data.UserData);
            if (inspection.LooksLikeClientAssociateRequest)
                associationPayloadObserved = true;
            AddStep("server", "ACSE-INSPECT", inspection.LooksLikeClientAssociateRequest, inspection.Message, data.UserData);
            if (!inspection.LooksLikeClientAssociateRequest)
                AddFinding("Server received COTP Data TPDU but payload does not look like a complete IEC 61850 MMS associate request.");
        }

        async Task RunClientAsync()
        {
            using var tcpClient = new TcpClient { NoDelay = true };
            await tcpClient.ConnectAsync(IPAddress.Loopback, boundPort, timeoutSource.Token).ConfigureAwait(false);
            await using var stream = tcpClient.GetStream();

            var crPayload = CotpFrameCodec.EncodeDefaultConnectRequest();
            var crFrame = TpktFrameCodec.Encode(crPayload);
            await stream.WriteAsync(crFrame, timeoutSource.Token).ConfigureAwait(false);
            AddStep("client", "COTP-SEND-CR", true, "Sent default COTP Connection Request wrapped in TPKT.", crFrame);

            var ccFrame = await ReadTpktFrameAsync(stream, timeoutSource.Token).ConfigureAwait(false);
            var ccTpkt = TpktFrameCodec.Decode(ccFrame);
            var ccTpktPass = ccTpkt.IsValid;
            AddStep("client", "TPKT-RECV-CC", ccTpktPass, ccTpkt.Message, ccFrame);
            if (!ccTpktPass)
            {
                AddFinding($"Client could not decode server TPKT connection confirm: {ccTpkt.Message}");
                return;
            }

            var cc = CotpFrameCodec.Decode(ccTpkt.Payload);
            var ccPass = cc.IsValid && cc.Kind == CotpTpduKind.ConnectionConfirm;
            if (ccPass)
            {
                cotpConnectionConfirmed = true;
                tpktExchangeVerified = true;
            }
            AddStep("client", "COTP-RECV-CC", ccPass, cc.Message, ccTpkt.Payload);
            if (!ccPass)
            {
                AddFinding($"Client expected COTP CC but received {cc.Kind}: {cc.Message}");
                return;
            }

            var associationPayload = SelectAssociationPayload(options.AssociationProfileName);
            var dataPayload = CotpFrameCodec.EncodeData(associationPayload.Payload);
            var dataFrame = TpktFrameCodec.Encode(dataPayload);
            await stream.WriteAsync(dataFrame, timeoutSource.Token).ConfigureAwait(false);
            AddStep("client", "COTP-SEND-DATA", true, $"Sent {associationPayload.Name} association payload wrapped in COTP Data TPDU.", dataFrame);
        }

        void AddStep(string side, string layer, bool pass, string message, byte[] bytes)
        {
            var item = new MmsHandshakeListenerStep
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
