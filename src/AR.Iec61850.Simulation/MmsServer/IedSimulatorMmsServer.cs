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

            var presentationContextId = await NegotiateAssociationAsync(stream, remote, cancellationToken).ConfigureAwait(false);
            if (!presentationContextId.HasValue)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                var requestFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (requestFrame is null)
                    break; // client closed the association.

                var requestTpkt = TpktFrameCodec.Decode(requestFrame);
                if (!requestTpkt.IsValid)
                    break;

                var requestData = CotpFrameCodec.Decode(requestTpkt.Payload);
                if (!requestData.IsValid || requestData.Kind != CotpTpduKind.Data)
                    break;

                var session = _sessionFactory();
                var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(requestData.UserData, session, presentationContextId.Value);
                if (!dispatch.IsRequestDecoded)
                {
                    Record(new IedSimulatorServerActivity
                    {
                        Kind = IedSimulatorServerActivityKind.RequestServed,
                        RemoteEndPoint = remote,
                        Operation = "DecodeConfirmedRequest",
                        Success = false,
                        Message = dispatch.Message
                    });
                    break;
                }

                var responseFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(dispatch.ResponsePresentationPayload));
                await stream.WriteAsync(responseFrame, cancellationToken).ConfigureAwait(false);

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
                    Message = dispatch.Response.Message
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
                Success = false,
                Message = ex.Message
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

    private async Task<int?> NegotiateAssociationAsync(NetworkStream stream, string remote, CancellationToken cancellationToken)
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
        var aarqFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (aarqFrame is null)
            return CloseHandshake(remote, "ACSE AARQ", "Client closed after COTP CC before sending ACSE AARQ.");

        var aarqTpkt = TpktFrameCodec.Decode(aarqFrame);
        if (!aarqTpkt.IsValid)
            return Reject(remote, $"Invalid TPKT AARQ frame: {aarqTpkt.Message}");

        var aarqData = CotpFrameCodec.Decode(aarqTpkt.Payload);
        if (!aarqData.IsValid || aarqData.Kind != CotpTpduKind.Data)
            return Reject(remote, $"Expected COTP Data carrying AARQ, received {aarqData.Kind}: {aarqData.Message}");

        var inspection = AcseAssociationPayloadInspector.Inspect(aarqData.UserData);
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

        var responseProfile = AcseMmsAssociateResponse.SelectForRequest(_options.ResponseProfileName, aarqData.UserData);
        var aareFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(responseProfile.Payload));
        await stream.WriteAsync(aareFrame, cancellationToken).ConfigureAwait(false);
        Record(new IedSimulatorServerActivity
        {
            Kind = IedSimulatorServerActivityKind.HandshakeSent,
            RemoteEndPoint = remote,
            Operation = "ACSE AARE",
            Target = responseProfile.Name,
            Message = $"Sent {responseProfile.Payload.Length} byte ACSE AARE + MMS InitiateResponse payload; MMS presentation context id={responseProfile.MmsPresentationContextId}."
        });
        return responseProfile.MmsPresentationContextId;
    }

    private int? CloseHandshake(string remote, string operation, string message)
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

    private int? Reject(string remote, string message)
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
