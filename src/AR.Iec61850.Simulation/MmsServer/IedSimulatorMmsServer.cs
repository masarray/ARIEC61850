using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AR.Iec61850.Acse;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public sealed class IedSimulatorMmsServerOptions
{
    /// <summary>Bind address. Use "0.0.0.0" to accept clients from the lab network, "127.0.0.1" for local only.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP port. IEC 61850 MMS uses 102; binding it usually requires administrator/root privileges.</summary>
    public int Port { get; init; } = 102;

    public ushort ServerReference { get; init; } = 0x1001;

    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";

    public string ServerName { get; init; } = "ARIEC61850 Virtual IED";

    /// <summary>Maximum number of recent activity records kept in memory for monitoring.</summary>
    public int ActivityHistoryLimit { get; init; } = 500;
}

public enum IedSimulatorServerActivityKind
{
    ServerStarted,
    ServerStopped,
    ClientConnected,
    ClientDisconnected,
    HandshakeReceived,
    HandshakeSent,
    ClientClosed,
    RequestServed,
    AssociationRejected,
    Error
}

public sealed record IedSimulatorServerActivity
{
    public DateTimeOffset TimeUtc { get; init; } = DateTimeOffset.UtcNow;
    public IedSimulatorServerActivityKind Kind { get; init; }
    public string RemoteEndPoint { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool Success { get; init; } = true;
    public string Message { get; init; } = string.Empty;
    public string RequestMmsPayloadHex { get; init; } = string.Empty;
    public string ResponseMmsPayloadHex { get; init; } = string.Empty;
    public int RequestMmsPayloadBytes { get; init; }
    public int ResponseMmsPayloadBytes { get; init; }
    public int ResponseCotpSegmentCount { get; init; }

    public string Summary =>
        $"{TimeUtc:HH:mm:ss.fff} {Kind} {RemoteEndPoint} {Operation} {Target} {(Success ? "ok" : "fail")} {Message}".Trim();
}

/// <summary>
/// A runnable, persistent, read-only IEC 61850 MMS server for the IED simulator. It binds a TCP
/// listener, accepts external clients (for example IED Discovery or another MMS browser), runs the
/// TPKT/COTP/ACSE association, and answers native MMS BER confirmed requests from a live snapshot of
/// the simulator model. Writes and controls are rejected by the underlying read-only session guard.
///
/// This is the "Open SCL → Run" capability: combined with <see cref="IedSimulatorProfileBuilder"/> a
/// caller can load any SCL model and serve it. All protocol encode/decode is delegated to the existing
/// tested codecs (<c>TpktFrameCodec</c>, <c>CotpFrameCodec</c>, <c>AcseMmsAssociateResponse</c>) and
/// the <c>MmsConfirmedRequestBerDispatcher</c>; this class only owns the socket lifecycle and the
/// per-association loop.
///
/// Scope: read-only confirmed services (GetNameList, Read, GetNamedVariableListAttributes, Write
/// rejection). Reports, GOOSE/SV publishing, and control remain future milestones.
/// </summary>
public sealed class IedSimulatorMmsServer : IAsyncDisposable
{
    private sealed record MmsAssociation(int PresentationContextId, byte TpduSizeCode);

    private readonly Func<MmsReadOnlyServerSession> _sessionFactory;
    private readonly IedSimulatorMmsServerOptions _options;
    private readonly ConcurrentQueue<IedSimulatorServerActivity> _activity = new();
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _connectionSequence;
    private long _acceptedConnections;
    private long _servedRequests;
    private long _rejectedWrites;

    public IedSimulatorMmsServer(Func<MmsReadOnlyServerSession> sessionFactory, IedSimulatorMmsServerOptions? options = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _options = options ?? new IedSimulatorMmsServerOptions();
    }

    /// <summary>Convenience factory: serve a live snapshot of the running simulator engine.</summary>
    public static IedSimulatorMmsServer Create(IedSimulatorEngine engine, IedSimulatorMmsServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        options ??= new IedSimulatorMmsServerOptions();

        var modelBuilder = new MmsReadOnlyServerModelBuilder();
        var serverOptions = new MmsReadOnlyServerProfileOptions
        {
            ServerName = options.ServerName,
            Port = options.Port,
            IncludeSelfTest = false
        };

        MmsReadOnlyServerSession Factory()
        {
            var snapshot = engine.CreateSnapshot(DateTimeOffset.UtcNow);
            var profile = modelBuilder.Build(engine.Profile, snapshot, serverOptions);
            return new MmsReadOnlyServerSession(profile);
        }

        return new IedSimulatorMmsServer(Factory, options);
    }

    public bool IsRunning { get; private set; }
    public int BoundPort { get; private set; }
    public int ActiveConnectionCount => _clients.Count;
    public long AcceptedConnectionCount => Interlocked.Read(ref _acceptedConnections);
    public long ServedRequestCount => Interlocked.Read(ref _servedRequests);
    public long RejectedWriteCount => Interlocked.Read(ref _rejectedWrites);

    public event EventHandler<IedSimulatorServerActivity>? Activity;

    public IReadOnlyList<IedSimulatorServerActivity> RecentActivity() => _activity.ToArray();

    /// <summary>Bind the listener and start accepting clients in the background. Returns once bound.</summary>
    public void Start()
    {
        if (IsRunning)
            throw new InvalidOperationException("The server is already running.");

        var address = ParseHost(_options.Host);
        _listener = new TcpListener(address, _options.Port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        IsRunning = true;

        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.ServerStarted,
            Message = $"Listening on {_options.Host}:{BoundPort} as '{_options.ServerName}'."
        });

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
            // Listener already torn down.
        }

        foreach (var client in _clients.Values)
        {
            try { client.Close(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        }

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _clients.Clear();
        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;

        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.ServerStopped,
            Message = "Server stopped."
        });
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener!;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Record(new IedSimulatorServerActivity
                {
                    Kind = IedSimulatorServerActivityKind.Error,
                    Success = false,
                    Message = $"Accept failed: {ex.Message}"
                });
                continue;
            }

            var connectionId = Interlocked.Increment(ref _connectionSequence);
            _clients[connectionId] = client;
            _ = Task.Run(() => HandleConnectionAsync(connectionId, client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(int connectionId, TcpClient client, CancellationToken cancellationToken)
    {
        var remote = SafeRemote(client);
        var activeOperation = string.Empty;
        var activeTarget = string.Empty;
        var activeRequestPayload = ReadOnlyMemory<byte>.Empty;
        var activeResponsePayload = ReadOnlyMemory<byte>.Empty;
        var activeResponseCotpSegmentCount = 0;
        Interlocked.Increment(ref _acceptedConnections);
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.ClientConnected,
            RemoteEndPoint = remote,
            Message = "Client connected."
        });

        try
        {
            await using var stream = client.GetStream();

            var association = await NegotiateAssociationAsync(stream, remote, cancellationToken).ConfigureAwait(false);
            if (association is null)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                var requestPayload = await ReadCotpDataPayloadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (requestPayload is null)
                    break; // client closed the association.

                activeOperation = string.Empty;
                activeTarget = string.Empty;
                activeRequestPayload = requestPayload;
                activeResponsePayload = ReadOnlyMemory<byte>.Empty;
                activeResponseCotpSegmentCount = 0;

                var session = _sessionFactory();
                var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(requestPayload, session, association.PresentationContextId);
                if (!dispatch.IsRequestDecoded)
                {
                    Record(new IedSimulatorServerActivity
                    {
                        Kind = IedSimulatorServerActivityKind.RequestServed,
                        RemoteEndPoint = remote,
                        Operation = "DecodeConfirmedRequest",
                        Success = false,
                        Message = $"{dispatch.Message} MMS={FormatMmsPayload(requestPayload)}",
                        RequestMmsPayloadHex = FormatMmsPayload(requestPayload),
                        RequestMmsPayloadBytes = requestPayload.Length
                    });
                    break;
                }

                activeOperation = dispatch.Request.Operation.ToString();
                activeTarget = dispatch.Request.Target;
                activeResponsePayload = dispatch.ResponsePresentationPayload;
                activeResponseCotpSegmentCount = CountCotpDataSegments(activeResponsePayload.Length, association.TpduSizeCode);
                var responseSegments = await WriteCotpDataPayloadAsync(
                    stream,
                    activeResponsePayload,
                    association.TpduSizeCode,
                    cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref _servedRequests);
                if (dispatch.Request.Operation == MmsReadOnlyOperation.Write && !dispatch.Response.IsSuccess)
                    Interlocked.Increment(ref _rejectedWrites);

                Record(new IedSimulatorServerActivity
                {
                    Kind = IedSimulatorServerActivityKind.RequestServed,
                    RemoteEndPoint = remote,
                    Operation = dispatch.Request.Operation.ToString(),
                    Target = dispatch.Request.Target,
                    Success = dispatch.Response.IsSuccess,
                    Message = responseSegments == 1
                        ? dispatch.Response.Message
                        : $"{dispatch.Response.Message} COTP segments={responseSegments.ToString(System.Globalization.CultureInfo.InvariantCulture)}.",
                    RequestMmsPayloadHex = FormatMmsPayload(activeRequestPayload),
                    ResponseMmsPayloadHex = FormatMmsPayload(activeResponsePayload),
                    RequestMmsPayloadBytes = activeRequestPayload.Length,
                    ResponseMmsPayloadBytes = activeResponsePayload.Length,
                    ResponseCotpSegmentCount = responseSegments
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Server stopping.
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
        {
            Record(new IedSimulatorServerActivity
            {
                Kind = IedSimulatorServerActivityKind.Error,
                RemoteEndPoint = remote,
                Operation = activeOperation,
                Target = activeTarget,
                Success = false,
                Message = BuildTransportErrorMessage(ex, activeOperation, activeTarget, activeRequestPayload, activeResponsePayload, activeResponseCotpSegmentCount),
                RequestMmsPayloadHex = FormatMmsPayload(activeRequestPayload, maxBytes: 4096),
                ResponseMmsPayloadHex = FormatMmsPayload(activeResponsePayload, maxBytes: 4096),
                RequestMmsPayloadBytes = activeRequestPayload.Length,
                ResponseMmsPayloadBytes = activeResponsePayload.Length,
                ResponseCotpSegmentCount = activeResponseCotpSegmentCount
            });
        }
        finally
        {
            _clients.TryRemove(connectionId, out _);
            try { client.Close(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }

            Record(new IedSimulatorServerActivity
            {
                Kind = IedSimulatorServerActivityKind.ClientDisconnected,
                RemoteEndPoint = remote,
                Message = "Client disconnected."
            });
        }
    }

    private async Task<MmsAssociation?> NegotiateAssociationAsync(NetworkStream stream, string remote, CancellationToken cancellationToken)
    {
        // 1. COTP connection request -> connection confirm.
        var crFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (crFrame is null)
            return CloseHandshake(remote, "COTP CR", "Client closed before sending a COTP Connection Request.");

        var crTpkt = TpktFrameCodec.Decode(crFrame);
        if (!crTpkt.IsValid)
            return Reject(remote, $"Invalid TPKT connect frame: {crTpkt.Message}");

        var cr = CotpFrameCodec.Decode(crTpkt.Payload);
        if (!cr.IsValid || cr.Kind != CotpTpduKind.ConnectionRequest)
            return Reject(remote, $"Expected COTP CR, received {cr.Kind}: {cr.Message}");

        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.HandshakeReceived,
            RemoteEndPoint = remote,
            Operation = "COTP CR",
            Target = FormatCotpReferences(cr),
            Message = DescribeCotpParameters(cr)
        });

        var ccPayload = CotpFrameCodec.EncodeConnectionConfirm(cr, _options.ServerReference);
        var cc = CotpFrameCodec.Decode(ccPayload);
        var ccFrame = TpktFrameCodec.Encode(ccPayload);
        await stream.WriteAsync(ccFrame, cancellationToken).ConfigureAwait(false);
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.HandshakeSent,
            RemoteEndPoint = remote,
            Operation = "COTP CC",
            Target = FormatCotpReferences(cc),
            Message = DescribeCotpParameters(cc)
        });

        // 2. ACSE AARQ (in COTP Data) -> AARE + MMS InitiateResponse.
        var aarqPayload = await ReadCotpDataPayloadAsync(stream, cancellationToken).ConfigureAwait(false);
        if (aarqPayload is null)
            return CloseHandshake(remote, "ACSE AARQ", "Client closed after COTP CC before sending ACSE AARQ.");

        var inspection = AcseAssociationPayloadInspector.Inspect(aarqPayload);
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.HandshakeReceived,
            RemoteEndPoint = remote,
            Operation = "ACSE AARQ",
            Target = inspection.Kind.ToString(),
            Success = inspection.HasAcseAarq && inspection.HasUserInformation,
            Message = inspection.Message
        });

        if (!inspection.HasAcseAarq || !inspection.HasUserInformation)
            return Reject(remote, $"Payload does not look like an ACSE associate request. {inspection.Message}");

        var responseProfile = AcseMmsAssociateResponse.SelectForRequest(_options.ResponseProfileName, aarqPayload);
        var tpduSizeCode = ReadTpduSizeCode(cc.Parameters);
        var aareSegments = await WriteCotpDataPayloadAsync(stream, responseProfile.Payload, tpduSizeCode, cancellationToken).ConfigureAwait(false);
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.HandshakeSent,
            RemoteEndPoint = remote,
            Operation = "ACSE AARE",
            Target = responseProfile.Name,
            Message = $"Sent {responseProfile.Payload.Length} byte ACSE AARE + MMS InitiateResponse payload in {aareSegments.ToString(System.Globalization.CultureInfo.InvariantCulture)} COTP segment(s); MMS presentation context id={responseProfile.MmsPresentationContextId}."
        });
        return new MmsAssociation(responseProfile.MmsPresentationContextId, tpduSizeCode);
    }

    private MmsAssociation? CloseHandshake(string remote, string operation, string message)
    {
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.ClientClosed,
            RemoteEndPoint = remote,
            Operation = operation,
            Success = false,
            Message = message
        });
        return null;
    }

    private MmsAssociation? Reject(string remote, string message)
    {
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.AssociationRejected,
            RemoteEndPoint = remote,
            Success = false,
            Message = message
        });
        return null;
    }

    private static async Task<byte[]?> ReadTpktFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactOrNullAsync(stream, TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false);
        if (header is null)
            return null;

        var declaredLength = (header[2] << 8) | header[3];
        if (declaredLength < TpktFrameCodec.HeaderLength)
            throw new InvalidDataException($"Invalid TPKT declared length {declaredLength}.");

        var body = await ReadExactOrNullAsync(stream, declaredLength - TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidDataException("TPKT frame truncated before the declared length.");

        var frame = new byte[declaredLength];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
        return frame;
    }

    private static async Task<byte[]?> ReadCotpDataPayloadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        List<byte[]>? segments = null;
        while (true)
        {
            var frame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                if (segments is null)
                    return null;

                throw new InvalidDataException("COTP Data sequence ended before its final EOT segment.");
            }

            var tpkt = TpktFrameCodec.Decode(frame);
            if (!tpkt.IsValid)
                throw new InvalidDataException($"Invalid TPKT Data frame: {tpkt.Message}");

            var data = CotpFrameCodec.Decode(tpkt.Payload);
            if (!data.IsValid || data.Kind != CotpTpduKind.Data)
                throw new InvalidDataException($"Expected COTP Data TPDU, received {data.Kind}: {data.Message}");

            segments ??= new List<byte[]>();
            segments.Add(data.UserData);
            if (data.EndOfTransmission)
                break;
        }

        if (segments.Count == 1)
            return segments[0];

        var totalLength = segments.Sum(segment => segment.Length);
        var payload = new byte[totalLength];
        var offset = 0;
        foreach (var segment in segments)
        {
            Buffer.BlockCopy(segment, 0, payload, offset, segment.Length);
            offset += segment.Length;
        }

        return payload;
    }

    private static async Task<int> WriteCotpDataPayloadAsync(
        NetworkStream stream,
        ReadOnlyMemory<byte> payload,
        byte tpduSizeCode,
        CancellationToken cancellationToken)
    {
        var segments = CotpFrameCodec.EncodeDataSegments(payload.Span, tpduSizeCode);
        for (var index = 0; index < segments.Count; index++)
        {
            try
            {
                var frame = TpktFrameCodec.Encode(segments[index]);
                await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw new IOException(
                    $"COTP Data write failed at segment {(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} of {segments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} for {payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} MMS byte(s).",
                    ex);
            }
        }

        return segments.Count;
    }

    private static int CountCotpDataSegments(int payloadLength, byte tpduSizeCode)
    {
        var maxUserDataBytes = CotpFrameCodec.GetTpduSizeBytes(tpduSizeCode) - 3;
        return payloadLength == 0 ? 1 : (payloadLength + maxUserDataBytes - 1) / maxUserDataBytes;
    }

    private static byte ReadTpduSizeCode(ReadOnlySpan<byte> parameters)
    {
        const byte defaultTpduSizeCode = 0x0A;
        var offset = 0;
        while (offset + 2 <= parameters.Length)
        {
            var code = parameters[offset];
            var length = parameters[offset + 1];
            var next = offset + 2 + length;
            if (next > parameters.Length)
                break;

            if (code == 0xC0 && length == 1)
            {
                var tpduSizeCode = parameters[offset + 2];
                return tpduSizeCode is >= 7 and <= 15 ? tpduSizeCode : defaultTpduSizeCode;
            }

            offset = next;
        }

        return defaultTpduSizeCode;
    }

    private static async Task<byte[]?> ReadExactOrNullAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
            return Array.Empty<byte>();

        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset == 0 ? null : throw new InvalidDataException("TCP stream closed mid-frame.");
            offset += read;
        }

        return buffer;
    }

    private static IPAddress ParseHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host.Equals("any", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Any;

        return IPAddress.TryParse(host, out var address) ? address : IPAddress.Loopback;
    }

    private static string SafeRemote(TcpClient client)
    {
        try { return client.Client.RemoteEndPoint?.ToString() ?? "-"; }
        catch (SocketException) { return "-"; }
        catch (ObjectDisposedException) { return "-"; }
    }

    private static string FormatMmsPayload(ReadOnlyMemory<byte> payload, int maxBytes = 96)
    {
        var bytes = payload.Span;
        var shown = bytes[..Math.Min(bytes.Length, Math.Max(0, maxBytes))];
        var hex = Convert.ToHexString(shown);
        return bytes.Length <= maxBytes ? hex : $"{hex}...({bytes.Length} bytes)";
    }

    private static string BuildTransportErrorMessage(
        Exception exception,
        string operation,
        string target,
        ReadOnlyMemory<byte> requestPayload,
        ReadOnlyMemory<byte> responsePayload,
        int responseCotpSegmentCount)
    {
        var context = string.IsNullOrWhiteSpace(operation)
            ? "No confirmed MMS request was active."
            : $"Active request={operation} target={target}; requestBytes={requestPayload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}; responseBytes={responsePayload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}; plannedCotpSegments={responseCotpSegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
        return $"{exception.Message} {context}";
    }

    private static string FormatCotpReferences(CotpTpdu tpdu)
        => $"dst=0x{tpdu.DestinationReference:X4} src=0x{tpdu.SourceReference:X4}";

    private static string DescribeCotpParameters(CotpTpdu tpdu)
    {
        var parts = new List<string>();
        var offset = 0;
        while (offset + 2 <= tpdu.Parameters.Length)
        {
            var code = tpdu.Parameters[offset];
            var length = tpdu.Parameters[offset + 1];
            var next = offset + 2 + length;
            if (next > tpdu.Parameters.Length)
                break;

            var value = tpdu.Parameters.AsSpan(offset + 2, length);
            var rendered = string.Join("", value.ToArray().Select(b => b.ToString("X2")));
            switch (code)
            {
                case 0xC0 when length == 1:
                    parts.Add($"tpduSize=0x{value[0]:X2}");
                    break;
                case 0xC1:
                    parts.Add($"callingTsap={rendered}");
                    break;
                case 0xC2:
                    parts.Add($"calledTsap={rendered}");
                    break;
            }

            offset = next;
        }

        return parts.Count == 0
            ? tpdu.Message
            : $"{tpdu.Message} {string.Join("; ", parts)}.";
    }

    private void Record(IedSimulatorServerActivity activity)
    {
        _activity.Enqueue(activity);
        while (_activity.Count > _options.ActivityHistoryLimit && _activity.TryDequeue(out _))
        {
            // Trim history ring.
        }

        Activity?.Invoke(this, activity);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
